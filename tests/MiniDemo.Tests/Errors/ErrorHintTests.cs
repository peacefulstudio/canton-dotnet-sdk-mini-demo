// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace MiniDemo.Tests;

public class ErrorHintTests
{
    [Theory]
    [InlineData("DAML_FAILURE", "A Daml `require`, `assert` or `abort` rejected the command. The ledger is reachable.")]
    [InlineData("CONTRACT_NOT_FOUND", "The contract may not be visible to the submitter. Attach it as a disclosed contract.")]
    [InlineData("DUPLICATE_COMMAND", "This command id was already used inside its deduplication window. Submit with a fresh command id.")]
    [InlineData("DAML_AUTHORIZATION_ERROR", "A required authorizer did not authorize the command. Submit as, or with, the missing party.")]
    public void For_returns_the_one_line_hint_of_a_known_error_code(string errorId, string expectedHint)
    {
        ErrorHint.For(errorId).Should().Be(expectedHint);
    }

    [Theory]
    [InlineData("SOMETHING_UNHEARD_OF")]
    [InlineData("contract_not_found")]
    [InlineData("")]
    [InlineData(null)]
    public void For_returns_nothing_for_an_unknown_or_missing_error_code(string? errorId)
    {
        ErrorHint.For(errorId).Should().BeNull();
    }
}
