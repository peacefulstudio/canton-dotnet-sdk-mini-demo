// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http;
using System.Net.Sockets;
using AwesomeAssertions;
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

    [Fact]
    public void IsLocalnetUnreachable_is_false_for_an_unrelated_exception()
    {
        LocalnetPreflight.IsLocalnetUnreachable(new InvalidOperationException("boom")).Should().BeFalse();
    }

    [Fact]
    public void WriteUnreachableHelp_includes_the_underlying_error_and_a_recovery_hint()
    {
        var writer = new StringWriter();

        LocalnetPreflight.WriteUnreachableHelp(
            new HttpRequestException("Connection refused (localhost:8082)"), writer);

        var help = writer.ToString();
        help.Should().Contain("Connection refused (localhost:8082)");
        help.Should().Contain("Start a LocalNet");
    }
}
