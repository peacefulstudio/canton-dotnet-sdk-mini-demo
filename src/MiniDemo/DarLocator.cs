// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace MiniDemo;

internal readonly record struct DarLookup(string? Path, string? Problem);

internal static class DarLocator
{
    public const string DarPathEnv = "MINI_DEMO_DAR";

    public static string Resolve()
    {
        var lookup = Locate(Environment.GetEnvironmentVariable(DarPathEnv), AppContext.BaseDirectory);
        return lookup.Path ?? throw new FileNotFoundException(lookup.Problem);
    }

    public static DarLookup Locate(string? configuredPath, string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var resolved = System.IO.Path.GetFullPath(configuredPath);
            return File.Exists(resolved)
                ? new DarLookup(resolved, null)
                : new DarLookup(null, $"{DarPathEnv} points to '{resolved}', which does not exist.");
        }

        var beside = Directory.Exists(baseDirectory)
            ? Directory.EnumerateFiles(baseDirectory, "*.dar").OrderBy(path => path, StringComparer.Ordinal).FirstOrDefault()
            : null;
        if (beside is not null)
            return new DarLookup(beside, null);

        return new DarLookup(
            null,
            "Could not locate a built .dar beside the demo assembly. Run `dotnet build` first, " +
            $"or set {DarPathEnv} to the .dar path.");
    }
}
