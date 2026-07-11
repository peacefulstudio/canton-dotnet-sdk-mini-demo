// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using MiniDemo;
using Xunit;

namespace MiniDemo.Tests;

public class UnwrapTests
{
    [Fact]
    public void one_outcome_returns_the_unwrapped_result()
    {
        var outcome = new ExerciseOutcome<int>.One(42);

        var result = outcome.Unwrap("Transfer");

        result.Should().Be(42);
    }

    [Fact]
    public void daml_error_outcome_throws_with_operation_and_error_id()
    {
        var outcome = new ExerciseOutcome<int>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceMissing,
            "CONTRACT_NOT_FOUND",
            "the contract was archived",
            new Dictionary<string, string>());

        var act = () => outcome.Unwrap("Transfer");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Transfer")
            .And.Contain("CONTRACT_NOT_FOUND")
            .And.Contain("InvalidGivenCurrentSystemStateResourceMissing")
            .And.Contain("the contract was archived");
    }

    [Fact]
    public void infra_error_outcome_throws_with_status_code_and_message()
    {
        var outcome = new ExerciseOutcome<int>.InfraError(14, "transport unavailable");

        var act = () => outcome.Unwrap("Create");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Create")
            .And.Contain("14")
            .And.Contain("transport unavailable");
    }

    [Fact]
    public void none_outcome_throws_with_no_contract_message()
    {
        var outcome = new ExerciseOutcome<int>.None();

        var act = () => outcome.Unwrap("Query");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Query*produced no expected contract*");
    }

    [Fact]
    public void many_outcome_throws_with_operation_and_contract_count()
    {
        var outcome = new ExerciseOutcome<int>.Many(3, new[] { "cid1", "cid2", "cid3" });

        var act = () => outcome.Unwrap("Query");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Query*produced 3 contracts*expected one*");
    }
}
