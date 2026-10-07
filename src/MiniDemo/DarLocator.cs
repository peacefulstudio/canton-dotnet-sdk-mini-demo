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

        for (var dir = new DirectoryInfo(baseDirectory); dir is not null; dir = dir.Parent)
        {
            var dist = System.IO.Path.Combine(dir.FullName, "daml", ".daml", "dist");
            if (!Directory.Exists(dist))
                continue;

            var dar = Directory.EnumerateFiles(dist, "*.dar")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (dar is not null)
                return new DarLookup(dar, null);
        }

        return new DarLookup(
            null,
            "Could not locate a built .dar. Run ./scripts/codegen.sh (or `dpm build` in daml/) first, " +
            $"or set {DarPathEnv} to the .dar path.");
    }
}
