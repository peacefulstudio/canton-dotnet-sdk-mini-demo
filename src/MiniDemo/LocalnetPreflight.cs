// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Net.Http;
using System.Net.Sockets;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Peaceful.Canton.Localnet.Testing;

namespace MiniDemo;

internal static class LocalnetPreflight
{
    private static readonly HashSet<SocketError> UnreachableSocketErrors =
    [
        SocketError.ConnectionRefused,
        SocketError.TimedOut,
        SocketError.HostNotFound,
        SocketError.HostUnreachable,
        SocketError.NetworkUnreachable,
        SocketError.TryAgain,
    ];

    public static IReadOnlyDictionary<string, string?> ReadEnvironment()
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var env = new Dictionary<string, string?>(comparer);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            env[(string)entry.Key] = entry.Value as string;
        return env;
    }

    public static void WriteTargetSummary(IReadOnlyDictionary<string, string?> env, TextWriter writer)
    {
        var profile = EndpointDiscovery.ResolveProfile(env);
        var endpoints = EndpointDiscovery.Resolve(profile, env);
        var grpcAddress = LedgerEndpoint.Resolve(env);
        writer.WriteLine(
            $"Targeting Canton LocalNet ({profile}). Values default to a local LocalNet; override any via CANTON_LOCALNET_* env vars:\n" +
            $"  {EndpointDiscovery.JsonApiUrlEnv,-30} {endpoints.JsonLedgerApi}\n" +
            $"  {LedgerEndpoint.GrpcAddressEnv,-30} {grpcAddress}\n" +
            $"  {EndpointDiscovery.TokenUrlEnv,-30} {endpoints.TokenEndpoint}\n" +
            $"  {EndpointDiscovery.ClientIdEnv,-30} {endpoints.ClientId}");
    }

    public static bool IsLocalnetUnreachable(Exception exception) =>
        ExceptionChain.Flatten(exception).Any(current =>
            IsUnreachableSocketError(current) || IsGrpcTransportFailure(current) || IsRestTransportFailure(current));

    private static bool IsUnreachableSocketError(Exception exception) =>
        exception is SocketException socket && UnreachableSocketErrors.Contains(socket.SocketErrorCode);

    // Workaround: Grpc.Net.Client maps every transport-level SocketException to UNAVAILABLE
    // without preserving SocketErrorCode, so IsUnreachableSocketError above never fires for
    // gRPC failures — this check is gRPC-specific, not a duplicate of it.
    private static bool IsGrpcTransportFailure(Exception exception) =>
        exception is LedgerOperationException { Status: TransportStatus.Grpc { StatusCode: GrpcStatusCode.Unavailable } };

    private static bool IsRestTransportFailure(Exception exception) =>
        exception is LedgerOperationException { Status: TransportStatus.NoResponse, InnerException: HttpRequestException };

    public static bool IsLocalnetUnresponsive(Exception exception) =>
        ExceptionChain.Flatten(exception).Any(current => current is TimeoutException);

    public static void WriteCancelledNotice(TextWriter writer) =>
        writer.WriteLine("\nDemo cancelled before it finished.");

    public static void WriteVerificationReport(Exception exception, TextWriter writer) =>
        writer.WriteLine(
            "\nDemo verification failed \u2014 the ledger did not match what the demo asserted.\n\n" +
            $"{exception.Message}\n\n" +
            "This is the demo working as intended: it detected a real disagreement rather than\n" +
            "reporting success.");

    public static void WriteUnreachableHelp(Exception exception, TextWriter writer) =>
        writer.WriteLine(
            $"\nCanton LocalNet is not reachable, or is not ready yet ({exception.Message}).\n" +
            "If you just started it, wait a few seconds and retry. Otherwise start a LocalNet, or set the\n" +
            "CANTON_LOCALNET_* env vars to point at a running one (see README \u2192 Quickstart).");

    public static void WriteUnresponsiveHelp(Exception exception, TextWriter writer) =>
        writer.WriteLine(
            $"\nCanton LocalNet accepted the connection but did not respond in time ({exception.Message}).\n" +
            "It is running but not healthy \u2014 check its logs, or restart it (see README \u2192 Quickstart).");

    public static void WritePqsRequirementFailure(PqsRequirementNotMetException exception, TextWriter writer) =>
        writer.WriteLine(
            "\n--require-pqs was set, and PQS did not reach full projection within the bounded wait.\n\n" +
            $"{exception.Message}\n\n" +
            "Sections 1-5 already passed, so the ledger is fine — this is the read model lagging,\n" +
            "stopped, or not yet caught up.");

    public static void WriteLedgerOperationFailure(LedgerOperationException exception, TextWriter writer) =>
        writer.WriteLine(
            $"\nThe ledger reported an operation failure that is not a known LocalNet reachability\n" +
            $"issue: {exception.Message}\n\n" +
            "This is not the demo's own content check failing \u2014 it is the ledger reporting a fault\n" +
            "the demo cannot retry past. Check the LocalNet logs for the underlying cause, or file an\n" +
            "issue if the status/category above looks like it should be classified as reachability instead.");
}
