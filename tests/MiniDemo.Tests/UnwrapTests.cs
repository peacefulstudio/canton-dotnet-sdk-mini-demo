// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http;
using Daml.Runtime.Outcomes;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
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
        var outcome = new ExerciseOutcome<int>.InfraError(GrpcStatus.Unavailable, "transport unavailable");

        var act = () => outcome.Unwrap("Create");

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Create")
            .And.Contain("Unavailable")
            .And.Contain("transport unavailable");
    }

    [Fact]
    public void committed_undecodable_outcome_throws_naming_the_committed_update_and_keeping_the_cause()
    {
        var decodeFailure = new FormatException("unexpected token");
        var outcome = new ExerciseOutcome<int>.CommittedUndecodable("update-7", "result did not decode", decodeFailure);

        var act = () => outcome.Unwrap("Transfer");

        var thrown = act.Should().Throw<InvalidOperationException>().Which;
        thrown.Message.Should().Contain("Transfer")
            .And.Contain("committed")
            .And.Contain("update-7")
            .And.Contain("result did not decode");
        thrown.InnerException.Should().BeSameAs(decodeFailure);
    }

    [Theory]
    [InlineData(GrpcStatusCode.Unavailable)]
    [InlineData(GrpcStatusCode.PermissionDenied)]
    [InlineData(GrpcStatusCode.Unauthenticated)]
    public void infra_error_outcome_throws_an_exception_carrying_the_transport_status(GrpcStatusCode code)
    {
        var status = new TransportStatus.Grpc(code);
        var outcome = new ExerciseOutcome<int>.InfraError(status, "transport failure");

        var act = () => outcome.Unwrap("Create");

        act.Should().Throw<LedgerOperationException>().Which.Status.Should().Be(status);
    }

    [Fact]
    public void infra_error_outcome_forwards_a_directly_constructed_SourceException_as_the_inner_exception()
    {
        var transport = new HttpRequestException("connection refused");
        var outcome = new ExerciseOutcome<int>.InfraError(
            GrpcStatus.Unavailable, "transport failure", Category: null, transport);

        var act = () => outcome.Unwrap("Create");

        act.Should().Throw<LedgerOperationException>().WithInnerException<HttpRequestException>();
    }

    [Fact]
    public void infra_error_outcome_forwards_the_transport_classification_onto_the_thrown_exception()
    {
        var outcome = new ExerciseOutcome<int>.InfraError(
            GrpcStatus.Unavailable, "transport failure", DamlErrorCategory.TransientServerFailure);

        var act = () => outcome.Unwrap("Create");

        var thrown = act.Should().Throw<LedgerOperationException>().Which;
        thrown.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
        thrown.Message.Should().Contain("category TransientServerFailure");
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
        var outcome = new ExerciseOutcome<int>.Many(["cid1", "cid2", "cid3"]);

        var act = () => outcome.Unwrap("Query");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Query*produced 3 contracts*expected one*");
    }
}
