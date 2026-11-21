// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace MiniDemo;

internal static class ErrorHint
{
    private static readonly IReadOnlyDictionary<string, string> HintsByErrorId = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DAML_FAILURE"] = "A Daml `require`, `assert` or `abort` rejected the command. The ledger is reachable.",
        ["CONTRACT_NOT_FOUND"] = "The contract may not be visible to the submitter. Attach it as a disclosed contract.",
        ["DUPLICATE_COMMAND"] = "This command id was already used inside its deduplication window. Submit with a fresh command id.",
        ["DAML_AUTHORIZATION_ERROR"] = "A required authorizer did not authorize the command. Submit as, or with, the missing party.",
    };

    public static string? For(string? errorId) =>
        errorId is not null && HintsByErrorId.TryGetValue(errorId, out var hint) ? hint : null;
}
