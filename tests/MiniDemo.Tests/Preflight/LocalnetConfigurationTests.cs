// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace MiniDemo.Tests;

public class LocalnetConfigurationTests
{
    [Fact]
    public void FindProblem_accepts_the_default_slot_without_any_override()
    {
        LocalnetConfiguration.FindProblem(new Dictionary<string, string?>()).Should().BeNull();
    }

    [Theory]
    [InlineData("b-validator-1")]
    [InlineData("c-validator-1")]
    [InlineData("d-validator-1")]
    public void FindProblem_accepts_a_non_default_slot_without_any_user_id_override(string slot)
    {
        var env = new Dictionary<string, string?> { [EndpointDiscovery.ProfileEnv] = slot };

        LocalnetConfiguration.FindProblem(env).Should().BeNull();
    }

    [Fact]
    public void FindProblem_rejects_a_whitespace_validator_user_id_override()
    {
        var env = new Dictionary<string, string?> { [EndpointDiscovery.ValidatorUserIdEnv] = "   " };

        LocalnetConfiguration.FindProblem(env).Should().NotBeNull();
    }

    [Fact]
    public void FindProblem_lists_the_known_slots_for_an_unknown_profile()
    {
        var env = new Dictionary<string, string?> { [EndpointDiscovery.ProfileEnv] = "x-validator-9" };

        var problem = LocalnetConfiguration.FindProblem(env);

        problem.Should().Contain("x-validator-9");
        problem.Should().Contain("c-validator-1");
        problem.Should().Contain(EndpointDiscovery.ProfileEnv);
    }

    [Fact]
    public void FindProblem_reports_a_slot_that_has_no_default_endpoints_instead_of_throwing()
    {
        var env = new Dictionary<string, string?> { [EndpointDiscovery.ProfileEnv] = "sv-validator-1" };

        var problem = LocalnetConfiguration.FindProblem(env);

        problem.Should().NotBeNullOrWhiteSpace();
        problem.Should().Contain("CANTON_LOCALNET");
    }

    [Fact]
    public void FindProblem_rejects_a_pqs_port_that_is_not_a_tcp_port()
    {
        var env = new Dictionary<string, string?> { ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT"] = "5433:5432" };

        LocalnetConfiguration.FindProblem(env).Should().Contain("CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT");
    }

    [Fact]
    public void FindProblem_checks_the_pqs_port_of_the_selected_slot_only()
    {
        var env = new Dictionary<string, string?>
        {
            [EndpointDiscovery.ProfileEnv] = "c-validator-1",
            ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT"] = "abc",
        };

        LocalnetConfiguration.FindProblem(env).Should().BeNull();
    }

    [Fact]
    public void FindProblem_accepts_a_valid_pqs_port()
    {
        var env = new Dictionary<string, string?> { ["CANTON_LOCALNET_A_VALIDATOR_1_PQS_PORT"] = "5433" };

        LocalnetConfiguration.FindProblem(env).Should().BeNull();
    }

    [Fact]
    public void WriteConfigurationProblem_prints_the_problem_to_the_writer()
    {
        var writer = new StringWriter();

        LocalnetPreflight.WriteConfigurationProblem("set the thing", writer);

        writer.ToString().Should().Contain("set the thing");
    }
}
