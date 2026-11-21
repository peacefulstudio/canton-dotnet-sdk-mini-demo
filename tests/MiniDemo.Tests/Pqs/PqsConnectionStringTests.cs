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
    public void psql_conninfo_names_the_connection_without_the_password()
    {
        PqsConnectionString.ToPsqlConnInfo(Custom).Should().Be("host=db port=5433 dbname=custom user=u");
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
    public void port_env_var_follows_the_slot()
    {
        PqsConnectionString.PortEnvFor(LocalnetProfile.AValidator1).Should().Be("CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT");
        PqsConnectionString.PortEnvFor(LocalnetProfile.CValidator1).Should().Be("CANTON_LOCALNET_C_VALIDATOR_1_PQS_PORT");
    }

    [Fact]
    public void port_env_var_replaces_only_the_port_of_the_default_connection_string()
    {
        var env = new Dictionary<string, string?> { ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT"] = "5433" };

        PqsConnectionString.Resolve(LocalnetProfile.AValidator1, env)
            .Should().Be("Host=localhost;Port=5433;Database=pqs-a-validator-1;Username=cnadmin;Password=supersafe");
    }

    [Fact]
    public void port_env_var_of_another_slot_is_ignored()
    {
        var env = new Dictionary<string, string?> { ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT"] = "5433" };

        PqsConnectionString.Resolve(LocalnetProfile.CValidator1, env).Should().Contain("Port=5432");
    }

    [Fact]
    public void full_connection_string_wins_over_the_port_env_var()
    {
        var env = new Dictionary<string, string?>
        {
            [PqsConnectionString.EnvFor(LocalnetProfile.AValidator1)] = Custom,
            ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT"] = "6000",
        };

        PqsConnectionString.Resolve(LocalnetProfile.AValidator1, env).Should().Be(Custom);
    }

    [Fact]
    public void blank_port_env_var_keeps_the_default_port()
    {
        var env = new Dictionary<string, string?> { ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT"] = "  " };

        PqsConnectionString.Resolve(LocalnetProfile.AValidator1, env).Should().Contain("Port=5432");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("5433:5432")]
    public void FindPortProblem_rejects_a_port_that_is_not_a_tcp_port(string value)
    {
        var env = new Dictionary<string, string?> { ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT"] = value };

        var problem = PqsConnectionString.FindPortProblem(LocalnetProfile.AValidator1, env);

        problem.Should().Contain("CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT");
        problem.Should().Contain($"\"{value}\"");
    }

    [Theory]
    [InlineData("5433")]
    [InlineData("1")]
    [InlineData("65535")]
    [InlineData("")]
    public void FindPortProblem_accepts_a_tcp_port_or_nothing(string value)
    {
        var env = new Dictionary<string, string?> { ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT"] = value };

        PqsConnectionString.FindPortProblem(LocalnetProfile.AValidator1, env).Should().BeNull();
    }

    [Fact]
    public void FindPortProblem_ignores_an_invalid_port_when_the_full_connection_string_is_set()
    {
        var env = new Dictionary<string, string?>
        {
            [PqsConnectionString.EnvFor(LocalnetProfile.AValidator1)] = Custom,
            ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT"] = "abc",
        };

        PqsConnectionString.FindPortProblem(LocalnetProfile.AValidator1, env).Should().BeNull();
    }

    [Fact]
    public void container_name_is_the_database_of_the_connection_string()
    {
        PqsConnectionString.ContainerName(PqsConnectionString.DefaultFor(LocalnetProfile.CValidator1))
            .Should().Be("pqs-c-validator-1");
    }
}
