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
        var created = new AcsSnapshotEntry<DemoAsset>.Created(
            new ContractId<DemoAsset>("cid1"),
            asset.ToRecord(),
            Offset: LedgerOffset.At(1),
            SynchronizerId: (SynchronizerId)"sync1",
            WitnessParties: new[] { owner });
        var client = new FakeLedgerClient(new AcsSnapshotEntry<DemoAsset>[] { created });

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
        var unclassified = new AcsSnapshotEntry<DemoAsset>.Unclassified(Offset: LedgerOffset.At(7), Kind: "unmapped-template");
        var client = new FakeLedgerClient(new AcsSnapshotEntry<DemoAsset>[] { unclassified });

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("offset 7").And.Contain("unmapped-template");
    }

    [Fact]
    public async Task created_events_are_mapped_even_when_followed_by_a_terminal_checkpoint()
    {
        var owner = new Party("bob");
        var asset = new DemoAsset(Issuer: new Party("issuer"), Owner: owner, Name: "GOLD", Amount: 42m);
        var created = new AcsSnapshotEntry<DemoAsset>.Created(
            new ContractId<DemoAsset>("cid1"),
            asset.ToRecord(),
            Offset: LedgerOffset.At(1),
            SynchronizerId: (SynchronizerId)"sync1",
            WitnessParties: new[] { owner });
        var checkpoint = new AcsSnapshotEntry<DemoAsset>.Checkpoint(Offset: LedgerOffset.At(2));
        var client = new FakeLedgerClient(new AcsSnapshotEntry<DemoAsset>[] { created, checkpoint });

        var result = await AssetAcsQuery.QueryForPartyAsync(client, owner, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].ContractId.Should().Be("cid1");
    }

    [Fact]
    public async Task stream_error_events_throw_with_status_and_message()
    {
        var streamError = new AcsSnapshotEntry<DemoAsset>.StreamError(StatusCode: 14, Message: "snapshot transport failed");
        var client = new FakeLedgerClient(new AcsSnapshotEntry<DemoAsset>[] { streamError });

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("14").And.Contain("snapshot transport failed");
    }

    private sealed class FakeLedgerClient : ILedgerClient
    {
        private readonly IReadOnlyList<AcsSnapshotEntry<DemoAsset>> _events;

        public FakeLedgerClient(IReadOnlyList<AcsSnapshotEntry<DemoAsset>> events) => _events = events;

        public IAsyncEnumerable<AcsSnapshotEntry<T>> SubscribeActiveAsync<T>(SubmitterInfo submitter, LedgerOffset? activeAtOffset = null, CancellationToken cancellationToken = default) where T : IDamlType
        {
            if (typeof(T) != typeof(DemoAsset))
                throw new NotSupportedException($"{nameof(FakeLedgerClient)} only supports {nameof(DemoAsset)}.");
            return (IAsyncEnumerable<AcsSnapshotEntry<T>>)(object)Stream();
        }

        private async IAsyncEnumerable<AcsSnapshotEntry<DemoAsset>> Stream()
        {
            foreach (var evt in _events)
                yield return evt;
            await Task.CompletedTask;
        }

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeAsync<T>(SubmitterInfo submitter, LedgerOffset? fromOffset = null, LedgerOffset? toOffset = null, CancellationToken cancellationToken = default) where T : IDamlType =>
            throw new NotImplementedException();

        public IAsyncEnumerable<ContractStreamEvent<T>> SubscribeLedgerEffectsAsync<T>(SubmitterInfo submitter, LedgerOffset? fromOffset = null, LedgerOffset? toOffset = null, CancellationToken cancellationToken = default) where T : IDamlType =>
            throw new NotImplementedException();

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(ExerciseCommand command, SubmitterInfo submitter, string? workflowId = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<SubmitAndWaitResult> SubmitAndWaitAsync(CommandsSubmission submission, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(CommandsSubmission submission, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(TTemplate payload, SubmitterInfo submitter, string? workflowId = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) where TTemplate : ITemplate =>
            throw new NotImplementedException();

        public Task<LedgerOffset> GetLedgerEndAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
