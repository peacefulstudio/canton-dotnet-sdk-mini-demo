// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Peaceful.Canton.Localnet.Testing;
using Xunit;

namespace MiniDemo.Tests;

public class LocalnetSlotTests
{
    [Theory]
    [InlineData(LocalnetProfile.AValidator1, "a-validator-1", "A_VALIDATOR_1")]
    [InlineData(LocalnetProfile.CValidator1, "c-validator-1", "C_VALIDATOR_1")]
    [InlineData(LocalnetProfile.SvValidator1, "sv-validator-1", "SV_VALIDATOR_1")]
    public void names_a_profile_the_way_canton_localnet_does(LocalnetProfile profile, string name, string envKey)
    {
        LocalnetSlot.Name(profile).Should().Be(name);
        LocalnetSlot.EnvKey(profile).Should().Be(envKey);
    }
}

public class SvSlotConfigurationTests
{
    private static Dictionary<string, string?> SvEnvWithCredentials(string profileSpelling) => new()
    {
        [EndpointDiscovery.ProfileEnv] = profileSpelling,
        [EndpointDiscovery.TokenUrlEnv] = "http://localhost:8082/realms/Sv/protocol/openid-connect/token",
        [EndpointDiscovery.ClientIdEnv] = "sv-client",
        [EndpointDiscovery.ClientSecretEnv] = "sv-secret",
    };

    [Theory]
    [InlineData("sv-validator-1")]
    [InlineData("supervalidator")]
    [InlineData("super-validator")]
    public void FindProblem_accepts_the_sv_slot_once_it_has_credentials(string spelling)
    {
        LocalnetConfiguration.FindProblem(SvEnvWithCredentials(spelling)).Should().BeNull();
    }
}

public class DarLocatorTests
{
    [Fact]
    public void Locate_reports_a_missing_configured_dar_with_its_full_path()
    {
        var missingDar = System.IO.Path.GetFullPath(System.IO.Path.Combine("nonexistent", "mini-demo.dar"));

        var lookup = DarLocator.Locate(missingDar, AppContext.BaseDirectory);

        lookup.Path.Should().BeNull();
        lookup.Problem.Should().Contain("MINI_DEMO_DAR").And.Contain(missingDar);
    }

    [Fact]
    public void Locate_reports_a_missing_build_when_nothing_is_configured_and_no_dar_sits_beside_the_assembly()
    {
        var emptyRoot = Directory.CreateTempSubdirectory().FullName;

        var lookup = DarLocator.Locate(null, emptyRoot);

        lookup.Path.Should().BeNull();
        lookup.Problem.Should().Contain("Could not locate a built .dar")
            .And.Contain("dotnet build")
            .And.NotContain("codegen.sh");
    }

    [Fact]
    public void Locate_accepts_an_existing_configured_dar()
    {
        var dar = System.IO.Path.Combine(Directory.CreateTempSubdirectory().FullName, "demo.dar");
        File.WriteAllText(dar, string.Empty);

        var lookup = DarLocator.Locate(dar, AppContext.BaseDirectory);

        lookup.Problem.Should().BeNull();
        lookup.Path.Should().Be(dar);
    }

    [Fact]
    public void Locate_finds_the_dar_beside_the_assembly()
    {
        var baseDirectory = Directory.CreateTempSubdirectory().FullName;
        var dar = System.IO.Path.Combine(baseDirectory, "daml.dar");
        File.WriteAllText(dar, string.Empty);

        DarLocator.Locate(null, baseDirectory).Path.Should().Be(dar);
    }

    [Fact]
    public void Locate_prefers_the_configured_dar_over_the_one_beside_the_assembly()
    {
        var baseDirectory = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(System.IO.Path.Combine(baseDirectory, "daml.dar"), string.Empty);
        var configured = System.IO.Path.Combine(Directory.CreateTempSubdirectory().FullName, "other.dar");
        File.WriteAllText(configured, string.Empty);

        DarLocator.Locate(configured, baseDirectory).Path.Should().Be(configured);
    }

    [Fact]
    public void The_built_demo_carries_the_dar_the_msbuild_codegen_produced()
    {
        DarLocator.Locate(null, AppContext.BaseDirectory).Path.Should().EndWith("daml.dar");
    }
}
