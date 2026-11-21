// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace MiniDemo;

internal static partial class DamlFailureMessage
{
    [GeneratedRegex(@"\(error category \d+\): (?<message>\S.*)$", RegexOptions.Singleline)]
    private static partial Regex AfterErrorCategory();

    public static string Summarize(string rawMessage) =>
        AfterErrorCategory().Match(rawMessage) is { Success: true } match
            ? match.Groups["message"].Value.TrimEnd()
            : rawMessage;
}
