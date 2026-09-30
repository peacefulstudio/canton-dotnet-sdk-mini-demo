// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using AwesomeAssertions;
using Daml.Runtime.Outcomes;
using Daml.Ledger.Abstractions;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace MiniDemo.Tests;

public class LocalnetPreflightTests
{
    [Fact]
    public void WriteTargetSummary_lists_local_localnet_defaults_when_no_env_is_set()
    {
        var env = new Dictionary<string, string?>();
        var writer = new StringWriter();

        LocalnetPreflight.WriteTargetSummary(env, writer);

        var summary = writer.ToString();
        summary.Should().Contain("Targeting Canton LocalNet");
        summary.Should().Contain(EndpointDiscovery.JsonApiUrlEnv);
        summary.Should().Contain("http://localhost:11975");
        summary.Should().Contain(LedgerEndpoint.GrpcAddressEnv);
        summary.Should().Contain(LedgerEndpoint.DefaultGrpcAddress);
        summary.Should().Contain(EndpointDiscovery.TokenUrlEnv);
        summary.Should().Contain(EndpointDiscovery.ClientIdEnv);
    }

    [Fact]
    public void WriteTargetSummary_reflects_json_api_url_override_from_env()
    {
        var env = new Dictionary<string, string?> { [EndpointDiscovery.JsonApiUrlEnv] = "http://localhost:19999" };
        var writer = new StringWriter();

        LocalnetPreflight.WriteTargetSummary(env, writer);

        writer.ToString().Should().Contain("http://localhost:19999");
    }

    [Fact]
    public void WriteTargetSummary_reflects_grpc_address_override_from_env()
    {
        var env = new Dictionary<string, string?> { [LedgerEndpoint.GrpcAddressEnv] = "http://localhost:18901" };
        var writer = new StringWriter();

        LocalnetPreflight.WriteTargetSummary(env, writer);

        writer.ToString().Should().Contain("http://localhost:18901");
    }

    [Theory]
    [InlineData(SocketError.ConnectionRefused, true)]
    [InlineData(SocketError.TimedOut, true)]
    [InlineData(SocketError.HostNotFound, true)]
    [InlineData(SocketError.HostUnreachable, true)]
    [InlineData(SocketError.NetworkUnreachable, true)]
    [InlineData(SocketError.TryAgain, true)]
    [InlineData(SocketError.ConnectionReset, false)]
    [InlineData(SocketError.AccessDenied, false)]
    public void IsLocalnetUnreachable_classifies_wrapped_socket_errors_by_the_unreachable_allowlist(
        SocketError code, bool expectedUnreachable)
    {
        var wrapped = new HttpRequestException("transport failure", new SocketException((int)code));

        LocalnetPreflight.IsLocalnetUnreachable(wrapped).Should().Be(expectedUnreachable);
    }

    [Fact]
    public void IsLocalnetUnreachable_walks_a_deeply_nested_inner_exception_chain()
    {
        var refused = new SocketException((int)SocketError.ConnectionRefused);
        var deeplyNested = new InvalidOperationException(
            "outer", new HttpRequestException("transport", new IOException("io", refused)));

        LocalnetPreflight.IsLocalnetUnreachable(deeplyNested).Should().BeTrue();
    }

    [Fact]
    public void IsLocalnetUnreachable_is_true_for_a_bare_unwrapped_socket_exception()
    {
        LocalnetPreflight.IsLocalnetUnreachable(new SocketException((int)SocketError.ConnectionRefused))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(GrpcStatusCode.Unavailable, true)]
    [InlineData(GrpcStatusCode.DeadlineExceeded, false)]
    [InlineData(GrpcStatusCode.PermissionDenied, false)]
    [InlineData(GrpcStatusCode.NotFound, false)]
    [InlineData(GrpcStatusCode.Internal, false)]
    public void IsLocalnetUnreachable_treats_only_grpc_Unavailable_as_unreachable(
        GrpcStatusCode code, bool expectedUnreachable)
    {
        var infra = new LedgerOperationException("Create failed", new TransportStatus.Grpc(code), category: null);

        LocalnetPreflight.IsLocalnetUnreachable(infra).Should().Be(expectedUnreachable);
    }

    [Fact]
    public void IsLocalnetUnreachable_is_false_for_a_LedgerOperationException_carrying_no_status()
    {
        LocalnetPreflight.IsLocalnetUnreachable(new LedgerOperationException("no transport status"))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public void IsLocalnetUnreachable_is_false_for_a_rest_http_answer_whatever_its_cause(HttpStatusCode code)
    {
        var infra = new LedgerOperationException(
            "Create failed", new TransportStatus.Http(code), category: null,
            innerException: new HttpRequestException("transport failure"));

        LocalnetPreflight.IsLocalnetUnreachable(infra).Should().BeFalse();
    }

    [Fact]
    public void IsLocalnetUnreachable_is_true_for_a_rest_NoResponse_with_a_transport_cause()
    {
        var infra = new LedgerOperationException(
            "Create failed", new TransportStatus.NoResponse(), category: null,
            innerException: new HttpRequestException("transport failure"));

        LocalnetPreflight.IsLocalnetUnreachable(infra).Should().BeTrue();
    }

    [Fact]
    public void IsLocalnetUnreachable_is_false_for_a_rest_NoResponse_with_no_transport_cause()
    {
        var infra = new LedgerOperationException("Create failed", new TransportStatus.NoResponse(), category: null);

        LocalnetPreflight.IsLocalnetUnreachable(infra).Should().BeFalse();
    }

    [Fact]
    public void IsLocalnetUnreachable_is_false_for_a_rest_NoResponse_with_a_non_transport_cause()
    {
        var infra = new LedgerOperationException(
            "Create failed", new TransportStatus.NoResponse(), category: null,
            innerException: new InvalidOperationException("unrelated"));

        LocalnetPreflight.IsLocalnetUnreachable(infra).Should().BeFalse();
    }

    [Fact]
    public void IsLocalnetUnreachable_finds_a_wrapped_rest_NoResponse_LedgerOperationException()
    {
        var wrapped = new InvalidOperationException(
            "outer",
            new LedgerOperationException(
                "transport failure", new TransportStatus.NoResponse(), category: null,
                innerException: new HttpRequestException("Connection refused")));

        LocalnetPreflight.IsLocalnetUnreachable(wrapped).Should().BeTrue();
    }

    [Fact]
    public void IsLocalnetUnreachable_routes_a_rest_NoResponse_Unwrap_failure_to_the_help_text()
    {
        var outcome = new ExerciseOutcome<int>.InfraError(
            new TransportStatus.NoResponse(), "Connection refused (localhost:8082)",
            SourceException: new HttpRequestException("Connection refused (localhost:8082)"));
        var writer = new StringWriter();

        try
        {
            outcome.Unwrap("CreateAssetAsync");
        }
        catch (Exception ex) when (LocalnetPreflight.IsLocalnetUnreachable(ex))
        {
            LocalnetPreflight.WriteUnreachableHelp(ex, writer);
        }

        var help = writer.ToString();
        help.Should().Contain("Canton LocalNet is not reachable");
        help.Should().Contain("Connection refused (localhost:8082)");
        help.Should().Contain("start a LocalNet");
    }

    [Fact]
    public void IsLocalnetUnreachable_finds_a_wrapped_LedgerOperationException()
    {
        var wrapped = new InvalidOperationException(
            "outer", new LedgerOperationException("Create failed", GrpcStatus.Unavailable, category: null));

        LocalnetPreflight.IsLocalnetUnreachable(wrapped).Should().BeTrue();
    }

    [Fact]
    public void IsLocalnetUnreachable_searches_every_branch_of_an_AggregateException()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("first"),
            new LedgerOperationException("Create failed", GrpcStatus.Unavailable, category: null));

        LocalnetPreflight.IsLocalnetUnreachable(aggregate).Should().BeTrue();
    }

    [Fact]
    public void IsLocalnetUnreachable_routes_a_grpc_Unavailable_Unwrap_failure_to_the_help_text()
    {
        var outcome = new ExerciseOutcome<int>.InfraError(
            GrpcStatus.Unavailable, "failed to connect to all addresses");
        var writer = new StringWriter();

        try
        {
            outcome.Unwrap("CreateAssetAsync");
        }
        catch (Exception ex) when (LocalnetPreflight.IsLocalnetUnreachable(ex))
        {
            LocalnetPreflight.WriteUnreachableHelp(ex, writer);
        }

        var help = writer.ToString();
        help.Should().Contain("Canton LocalNet is not reachable");
        help.Should().Contain("failed to connect to all addresses");
        help.Should().Contain("start a LocalNet");
    }

    [Fact]
    public void IsLocalnetUnreachable_is_false_for_a_non_Unavailable_Unwrap_failure()
    {
        var outcome = new ExerciseOutcome<int>.InfraError(GrpcStatus.Unauthenticated, "validator token expired");

        var act = () => outcome.Unwrap("CreateAssetAsync");

        var thrown = act.Should().Throw<LedgerOperationException>().Which;
        thrown.Status.Should().Be(GrpcStatus.Unauthenticated);
        LocalnetPreflight.IsLocalnetUnreachable(thrown).Should().BeFalse();
    }

    [Fact]
    public void IsLocalnetUnresponsive_is_true_for_a_wrapped_TimeoutException()
    {
        var hung = new TaskCanceledException("the request timed out", new TimeoutException("HttpClient timeout"));

        LocalnetPreflight.IsLocalnetUnresponsive(hung).Should().BeTrue();
    }

    [Fact]
    public void IsLocalnetUnresponsive_is_true_for_a_bare_unwrapped_TimeoutException()
    {
        LocalnetPreflight.IsLocalnetUnresponsive(new TimeoutException("HttpClient timeout"))
            .Should().BeTrue();
    }

    [Fact]
    public void IsLocalnetUnresponsive_is_false_for_an_unreachable_socket_error()
    {
        LocalnetPreflight.IsLocalnetUnresponsive(new SocketException((int)SocketError.ConnectionRefused))
            .Should().BeFalse();
    }

    [Fact]
    public void WriteUnresponsiveHelp_distinguishes_a_hung_localnet_from_a_missing_one()
    {
        var writer = new StringWriter();

        LocalnetPreflight.WriteUnresponsiveHelp(new TimeoutException("HttpClient timeout"), writer);

        var help = writer.ToString();
        help.Should().Contain("did not respond");
        help.Should().Contain("HttpClient timeout");
        help.Should().NotContain("start a LocalNet");
    }

    [Fact]
    public void IsLocalnetUnreachable_is_false_for_an_unrelated_exception()
    {
        LocalnetPreflight.IsLocalnetUnreachable(new InvalidOperationException("boom")).Should().BeFalse();
    }

    [Fact]
    public void IsLocalnetUnreachable_is_false_for_a_cancelled_run()
    {
        LocalnetPreflight.IsLocalnetUnreachable(new OperationCanceledException()).Should().BeFalse();
    }

    [Fact]
    public void WriteCancelledNotice_says_the_demo_was_cancelled()
    {
        var writer = new StringWriter();

        LocalnetPreflight.WriteCancelledNotice(writer);

        writer.ToString().Should().Contain("cancelled");
    }

    [Fact]
    public void WriteUnreachableHelp_includes_the_underlying_error_and_a_recovery_hint()
    {
        var writer = new StringWriter();

        LocalnetPreflight.WriteUnreachableHelp(
            new HttpRequestException("Connection refused (localhost:8082)"), writer);

        var help = writer.ToString();
        help.Should().Contain("Connection refused (localhost:8082)");
        help.Should().Contain("is not ready yet");
        help.Should().Contain("start a LocalNet");
    }
}
