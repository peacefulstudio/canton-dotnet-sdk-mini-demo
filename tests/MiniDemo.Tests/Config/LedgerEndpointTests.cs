// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using MiniDemo;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace MiniDemo.Tests;

public class LedgerEndpointTests
{
    [Fact]
    public void configured_value_is_returned_when_present()
    {
        var env = new Dictionary<string, string?> { [LedgerEndpoint.GrpcAddressEnv] = "http://localhost:9999" };

        LedgerEndpoint.Resolve(env).Should().Be("http://localhost:9999");
    }

    [Fact]
    public void default_address_is_returned_when_key_is_missing()
    {
        var env = new Dictionary<string, string?>();

        LedgerEndpoint.Resolve(env).Should().Be(LedgerEndpoint.DefaultGrpcAddress);
    }

    [Fact]
    public void default_address_is_returned_when_value_is_whitespace()
    {
        var env = new Dictionary<string, string?> { [LedgerEndpoint.GrpcAddressEnv] = "   " };

        LedgerEndpoint.Resolve(env).Should().Be(LedgerEndpoint.DefaultGrpcAddress);
    }

    [Fact]
    public void json_api_default_address_is_returned_when_key_is_missing()
    {
        var env = new Dictionary<string, string?>();

        LedgerEndpoint.ResolveJsonApi(env).Should().Be("http://localhost:11975/");
    }

    [Fact]
    public void json_api_configured_value_is_returned_when_present()
    {
        var env = new Dictionary<string, string?> { [EndpointDiscovery.JsonApiUrlEnv] = "http://localhost:19999" };

        LedgerEndpoint.ResolveJsonApi(env).Should().Be("http://localhost:19999/");
    }
}
