// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace MiniDemo.Tests;

public class PqsConnectionStringTests
{
    [Fact]
    public void configured_value_is_returned_when_present()
    {
        var env = new Dictionary<string, string?>
        {
            [PqsConnectionString.ConnectionStringEnv] = "Host=db;Port=5433;Database=custom;Username=u;Password=p",
        };

        PqsConnectionString.Resolve(env).Should().Be("Host=db;Port=5433;Database=custom;Username=u;Password=p");
    }

    [Fact]
    public void default_connection_string_is_returned_when_key_is_missing()
    {
        var env = new Dictionary<string, string?>();

        PqsConnectionString.Resolve(env).Should().Be(PqsConnectionString.DefaultConnectionString);
    }

    [Fact]
    public void default_connection_string_is_returned_when_value_is_whitespace()
    {
        var env = new Dictionary<string, string?> { [PqsConnectionString.ConnectionStringEnv] = "   " };

        PqsConnectionString.Resolve(env).Should().Be(PqsConnectionString.DefaultConnectionString);
    }
}
