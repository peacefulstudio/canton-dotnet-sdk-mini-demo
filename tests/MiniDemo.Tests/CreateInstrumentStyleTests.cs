// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Testing;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;
using MiniDemo.Asset;

namespace MiniDemo.Tests;

public class CreateInstrumentStyleTests
{
    private static readonly Instrument Gold = new(new Party("issuer"), "GOLD");

    public static TheoryData<ResultStyle> BothStyles => new() { ResultStyle.OutcomePatternMatch, ResultStyle.OrThrow };

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task CreateInstrumentAsync_returns_the_created_contract_id_in_either_style(ResultStyle style)
    {
        var client = FakeLedgerClient.Create()
            .WithCreateResult(LedgerOutcomes.One(new ContractId<Instrument>("instrument-1")))
            .Build();

        var createdCid = await MiniDemoRunner.CreateInstrumentAsync(client, Gold, "gRPC", style, CancellationToken.None);

        (createdCid == new ContractId<Instrument>("instrument-1")).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task CreateInstrumentAsync_surfaces_an_infra_fault_as_a_ledger_operation_exception_in_either_style(
        ResultStyle style)
    {
        var client = FakeLedgerClient.Create()
            .WithCreateResult(LedgerOutcomes.InfraError<ContractId<Instrument>>(GrpcStatus.Unavailable, "connection refused"))
            .Build();

        var act = () => MiniDemoRunner.CreateInstrumentAsync(client, Gold, "gRPC", style, CancellationToken.None);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Status.Should().Be(GrpcStatus.Unavailable);
    }

    [Fact]
    public async Task CreateInstrumentAsync_pattern_match_style_throws_InvalidOperationException_for_a_daml_error()
    {
        var client = FakeLedgerClient.Create()
            .WithCreateResult(new ExerciseOutcome<ContractId<Instrument>>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
                "DUPLICATE_CONTRACT_KEY",
                "key exists",
                new Dictionary<string, string>()))
            .Build();

        var act = () => MiniDemoRunner.CreateInstrumentAsync(client, Gold, "gRPC", ResultStyle.OutcomePatternMatch, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("DUPLICATE_CONTRACT_KEY");
    }

    [Fact]
    public async Task CreateInstrumentAsync_or_throw_style_throws_LedgerOperationException_for_a_daml_error()
    {
        var client = FakeLedgerClient.Create()
            .WithCreateResult(new ExerciseOutcome<ContractId<Instrument>>.DamlError(
                DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
                "DUPLICATE_CONTRACT_KEY",
                "key exists",
                new Dictionary<string, string>()))
            .Build();

        var act = () => MiniDemoRunner.CreateInstrumentAsync(client, Gold, "gRPC", ResultStyle.OrThrow, CancellationToken.None);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.ErrorId.Should().Be("DUPLICATE_CONTRACT_KEY");
    }

    [Fact]
    public async Task CreateInstrumentAsync_pattern_match_style_rejects_a_committed_but_undecodable_create()
    {
        var client = FakeLedgerClient.Create()
            .WithCreateResult(new ExerciseOutcome<ContractId<Instrument>>.CommittedUndecodable(
                "update-1", "no contract id in response", new FormatException("bad")))
            .Build();

        var act = () => MiniDemoRunner.CreateInstrumentAsync(client, Gold, "gRPC", ResultStyle.OutcomePatternMatch, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("do not resubmit");
    }
}
