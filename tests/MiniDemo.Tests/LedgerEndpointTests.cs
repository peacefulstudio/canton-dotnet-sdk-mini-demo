// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using MiniDemo;
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
}
