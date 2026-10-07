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

    [Fact]
    public void FindProblem_names_the_env_var_and_the_slot_user_id_source_for_a_non_default_slot_without_a_user_id()
    {
        var env = new Dictionary<string, string?> { [EndpointDiscovery.ProfileEnv] = "c-validator-1" };

        var problem = LocalnetConfiguration.FindProblem(env);

        problem.Should().Contain(EndpointDiscovery.ValidatorUserIdEnv);
        problem.Should().Contain("AUTH_C_VALIDATOR_1_VALIDATOR_USER_ID");
        problem.Should().Contain("compose/modules/keycloak/env/c-validator-1/on/oauth2.env");
    }

    [Fact]
    public void FindProblem_accepts_a_non_default_slot_once_the_user_id_is_set()
    {
        var env = new Dictionary<string, string?>
        {
            [EndpointDiscovery.ProfileEnv] = "c-validator-1",
            [EndpointDiscovery.ValidatorUserIdEnv] = "some-user-id",
        };

        LocalnetConfiguration.FindProblem(env).Should().BeNull();
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
    public void WriteConfigurationProblem_prints_the_problem_to_the_writer()
    {
        var writer = new StringWriter();

        LocalnetPreflight.WriteConfigurationProblem("set the thing", writer);

        writer.ToString().Should().Contain("set the thing");
    }
}
