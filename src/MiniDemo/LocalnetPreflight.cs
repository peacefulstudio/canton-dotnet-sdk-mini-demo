// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Net.Sockets;
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

    public static bool IsLocalnetUnreachable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
            if (current is SocketException socket && UnreachableSocketErrors.Contains(socket.SocketErrorCode))
                return true;
        return false;
    }

    public static void WriteUnreachableHelp(Exception exception, TextWriter writer) =>
        writer.WriteLine(
            $"\nCanton LocalNet is not reachable ({exception.Message}).\n" +
            "Start a LocalNet, or set the CANTON_LOCALNET_* env vars to point at a running one (see README → Quickstart).");
}
