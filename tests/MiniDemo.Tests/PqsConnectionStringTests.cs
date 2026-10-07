// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace MiniDemo.Tests;

public class PqsConnectionStringTests
{
    private const string Custom = "Host=db;Port=5433;Database=custom;Username=u;Password=p";

    [Fact]
    public void a_validator_1_keeps_its_env_var_name_and_default()
    {
        PqsConnectionString.EnvFor(LocalnetProfile.AValidator1)
            .Should().Be("CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING");
        PqsConnectionString.Resolve(LocalnetProfile.AValidator1, new Dictionary<string, string?>())
            .Should().Be(PqsConnectionString.DefaultConnectionString);
    }

    [Fact]
    public void configured_value_is_returned_when_present()
    {
        var env = new Dictionary<string, string?> { [PqsConnectionString.EnvFor(LocalnetProfile.AValidator1)] = Custom };

        PqsConnectionString.Resolve(LocalnetProfile.AValidator1, env).Should().Be(Custom);
    }

    [Fact]
    public void default_connection_string_is_returned_when_value_is_whitespace()
    {
        var env = new Dictionary<string, string?> { [PqsConnectionString.EnvFor(LocalnetProfile.AValidator1)] = "   " };

        PqsConnectionString.Resolve(LocalnetProfile.AValidator1, env).Should().Be(PqsConnectionString.DefaultConnectionString);
    }

    [Fact]
    public void c_validator_1_defaults_to_its_own_database_on_the_same_host_and_credentials()
    {
        PqsConnectionString.Resolve(LocalnetProfile.CValidator1, new Dictionary<string, string?>())
            .Should().Be("Host=localhost;Port=5432;Database=pqs-c-validator-1;Username=cnadmin;Password=supersafe");
    }

    [Fact]
    public void c_validator_1_reads_its_own_env_var_and_ignores_the_a_validator_1_one()
    {
        var env = new Dictionary<string, string?>
        {
            ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING"] = "Host=a;Database=pqs-a",
        };
        PqsConnectionString.Resolve(LocalnetProfile.CValidator1, env).Should().Contain("Database=pqs-c-validator-1");

        env["CANTON_LOCALNET_C_VALIDATOR_1_PQS_CONNECTION_STRING"] = Custom;
        PqsConnectionString.Resolve(LocalnetProfile.CValidator1, env).Should().Be(Custom);
    }

    [Theory]
    [InlineData("c-validator-1")]
    [InlineData("C-Validator-1")]
    public void profile_spellings_resolve_to_the_same_slot_database(string spelling)
    {
        var env = new Dictionary<string, string?> { [EndpointDiscovery.ProfileEnv] = spelling };

        var profile = EndpointDiscovery.ResolveProfile(env);

        PqsConnectionString.Resolve(profile, env).Should().Contain("Database=pqs-c-validator-1");
    }

    [Theory]
    [InlineData("sv-validator-1")]
    [InlineData("supervalidator")]
    [InlineData("super-validator")]
    public void sv_slot_aliases_resolve_to_the_sv_database(string spelling)
    {
        var env = new Dictionary<string, string?> { [EndpointDiscovery.ProfileEnv] = spelling };

        var profile = EndpointDiscovery.ResolveProfile(env);

        PqsConnectionString.Resolve(profile, env).Should().Contain("Database=pqs-sv-validator-1");
    }

    [Fact]
    public void container_name_is_the_database_of_the_connection_string()
    {
        PqsConnectionString.ContainerName(PqsConnectionString.DefaultFor(LocalnetProfile.CValidator1))
            .Should().Be("pqs-c-validator-1");
    }
}
