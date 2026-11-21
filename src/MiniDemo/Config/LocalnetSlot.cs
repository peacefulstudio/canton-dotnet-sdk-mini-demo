// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Peaceful.Canton.Localnet.Testing;

namespace MiniDemo;

internal static class LocalnetSlot
{
    public static string Name(LocalnetProfile profile)
    {
        var pascal = profile.ToString();
        var name = new StringBuilder();
        for (var index = 0; index < pascal.Length; index++)
        {
            var current = pascal[index];
            var startsNewWord = index > 0 && (char.IsUpper(current) || (char.IsDigit(current) && !char.IsDigit(pascal[index - 1])));
            if (startsNewWord)
                name.Append('-');
            name.Append(char.ToLowerInvariant(current));
        }

        return name.ToString();
    }

    public static string EnvKey(LocalnetProfile profile) =>
        Name(profile).Replace('-', '_').ToUpperInvariant();
}
