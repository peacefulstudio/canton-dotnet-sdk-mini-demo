// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Peaceful.Canton.Localnet.Testing;

namespace MiniDemo;

internal static class LocalnetConfiguration
{
    public static string? FindProblem(IReadOnlyDictionary<string, string?> env)
    {
        LocalnetEndpoints endpoints;
        try
        {
            endpoints = EndpointDiscovery.Resolve(EndpointDiscovery.ResolveProfile(env), env);
        }
        catch (InvalidOperationException incompleteConfiguration)
        {
            return $"{incompleteConfiguration.Message}\nFix {EndpointDiscovery.ProfileEnv} or the CANTON_LOCALNET_* variables the message names, then re-run.";
        }

        return string.IsNullOrWhiteSpace(endpoints.ValidatorUserId)
            ? $"{EndpointDiscovery.ValidatorUserIdEnv} is set but blank.\nUnset it to use the slot's default validator user id, or set it to a real user id."
            : null;
    }
}
