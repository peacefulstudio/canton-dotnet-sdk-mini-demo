// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using MiniDemo;
using Xunit;
using DemoAsset = Canton.Mini.Demo.Asset;

namespace MiniDemo.Tests;

public class AssetAcsQueryTests
{
    [Fact]
    public async Task created_events_are_mapped_to_asset_snapshots()
    {
        var owner = new Party("bob");
        var asset = new DemoAsset(Issuer: new Party("issuer"), Owner: owner, Name: "GOLD", Amount: 42m);
        var created = new ContractStreamEvent<DemoAsset>.Created(
            new ContractId<DemoAsset>("cid1"),
            asset.ToRecord(),
            Offset: 1,
            SynchronizerId: (SynchronizerId)"sync1",
            WitnessParties: new[] { owner });
        var client = new FakeLedgerClient(new ContractStreamEvent<DemoAsset>[] { created });

        var result = await AssetAcsQuery.QueryForPartyAsync(client, owner, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].ContractId.Should().Be("cid1");
        result[0].Owner.Should().Be("bob");
        result[0].Name.Should().Be("GOLD");
        result[0].Amount.Should().Be(42m);
    }

    [Fact]
    public async Task unclassified_events_throw_instead_of_being_silently_dropped()
    {
        var unclassified = new ContractStreamEvent<DemoAsset>.Unclassified(Offset: 7, Kind: "unmapped-template");
        var client = new FakeLedgerClient(new ContractStreamEvent<DemoAsset>[] { unclassified });

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("offset 7").And.Contain("unmapped-template");
    }

    private sealed class FakeLedgerClient : ILedgerClient
    {
        private readonly IReadOnlyList<ContractStreamEvent<DemoAsset>> _events;

        public FakeLedgerClient(IReadOnlyList<ContractStreamEvent<DemoAsset>> events) => _events = events;

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeActiveAsync<T>(Party actAs, CancellationToken cancellationToken) where T : IDamlType
        {
            if (typeof(T) != typeof(DemoAsset))
                throw new NotSupportedException($"{nameof(FakeLedgerClient)} only supports {nameof(DemoAsset)}.");
            return (IAsyncEnumerable<ContractStreamEvent<T>>)(object)Stream();
        }

        private async IAsyncEnumerable<ContractStreamEvent<DemoAsset>> Stream()
        {
            foreach (var evt in _events)
                yield return evt;
            await Task.CompletedTask;
        }

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeActiveAsync<T>(SubmitterInfo submitter, CancellationToken cancellationToken) where T : IDamlType =>
            throw new NotImplementedException();

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(Party actAs, long? fromOffset, CancellationToken cancellationToken) where T : IDamlType =>
            throw new NotImplementedException();

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(SubmitterInfo submitter, long? fromOffset, CancellationToken cancellationToken) where T : IDamlType =>
            throw new NotImplementedException();

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(ExerciseCommand command, Party actAs, string? workflowId = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<SubmitAndWaitResult> SubmitAndWaitAsync(CommandsSubmission submission, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(CommandsSubmission submission, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(TTemplate payload, SubmitterInfo submitter, string? workflowId = null, CancellationToken cancellationToken = default) where TTemplate : ITemplate =>
            throw new NotImplementedException();

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(TTemplate payload, Party actAs, string? workflowId = null, CancellationToken cancellationToken = default) where TTemplate : ITemplate =>
            throw new NotImplementedException();

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null, CancellationToken cancellationToken = default) where TTemplate : IDamlType =>
            throw new NotImplementedException();

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryExerciseForCreatedAsync<TTemplate>(ExerciseCommand command, Party actAs, string? workflowId = null, CancellationToken cancellationToken = default) where TTemplate : IDamlType =>
            throw new NotImplementedException();

        public Task<long> GetLedgerEndAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public void Dispose()
        {
        }
    }
}
