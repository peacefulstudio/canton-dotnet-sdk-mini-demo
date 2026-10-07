// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Peaceful.Canton.Localnet.Testing;

namespace MiniDemo;

internal static class LocalnetConfiguration
{
    public static string? FindProblem(IReadOnlyDictionary<string, string?> env)
    {
        LocalnetProfile profile;
        LocalnetEndpoints endpoints;
        try
        {
            profile = EndpointDiscovery.ResolveProfile(env);
            endpoints = EndpointDiscovery.Resolve(profile, env);
        }
        catch (InvalidOperationException incompleteConfiguration)
        {
            return $"{incompleteConfiguration.Message}\nFix {EndpointDiscovery.ProfileEnv} or the CANTON_LOCALNET_* variables the message names, then re-run.";
        }

        return string.IsNullOrWhiteSpace(endpoints.ValidatorUserId)
            ? MissingValidatorUserId(profile)
            : null;
    }

    private static string MissingValidatorUserId(LocalnetProfile profile)
    {
        var slot = LocalnetSlot.Name(profile);
        var message =
            $"No validator ledger user id is configured for {slot}, so the demo cannot lease act-as rights.\n" +
            $"Set {EndpointDiscovery.ValidatorUserIdEnv} to that slot's validator user id.";
        return profile == LocalnetProfile.SvValidator1
            ? message
            : message +
              $"\ncanton-localnet keeps it as AUTH_{LocalnetSlot.EnvKey(profile)}_VALIDATOR_USER_ID in " +
              $"compose/modules/keycloak/env/{slot}/on/oauth2.env.";
    }
}
