// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace MiniDemo;

internal static class DarLocator
{
    private const string DarPathEnv = "MINI_DEMO_DAR";

    public static string Resolve()
    {
        var fromEnv = Environment.GetEnvironmentVariable(DarPathEnv);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var resolved = Path.GetFullPath(fromEnv);
            if (!File.Exists(resolved))
                throw new FileNotFoundException($"{DarPathEnv} points to '{resolved}', which does not exist.", resolved);
            return resolved;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var dist = Path.Combine(dir.FullName, "daml", ".daml", "dist");
            if (!Directory.Exists(dist))
                continue;

            var dar = Directory.EnumerateFiles(dist, "*.dar")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (dar is not null)
                return dar;
        }

        throw new FileNotFoundException(
            "Could not locate a built .dar. Run ./scripts/codegen.sh (or `dpm build` in daml/) first, " +
            $"or set {DarPathEnv} to the .dar path.");
    }
}
