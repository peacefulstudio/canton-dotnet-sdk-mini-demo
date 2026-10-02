// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Canton.Ledger.Testing;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using Xunit;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo.Tests;

public class UpdateStreamObserverTests
{
    private static readonly Party Bob = new("bob");

    private static readonly ContractId<DemoAsset> Accepted = new("accepted-1");

    private static readonly LedgerOffset Start = LedgerOffset.At(10);

    private static readonly LedgerOffset End = LedgerOffset.At(20);

    private static readonly TimeSpan GenerousTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task ObserveCreatedAsync_reports_the_created_event_it_reads_inside_the_window()
    {
        var client = ClientStreaming(CreatedAt(Accepted, 15));
        var output = new StringWriter();

        await UpdateStreamObserver.ObserveCreatedAsync(
            client, "gRPC", Bob, Accepted, Start, End, GenerousTimeout, output, CancellationToken.None);

        output.ToString().Should()
            .Contain("gRPC update stream (10, 20]")
            .And.Contain("Created accepted-1")
            .And.Contain("offset 15");
    }

    [Fact]
    public async Task ObserveCreatedAsync_ignores_unrelated_events_before_the_expected_one()
    {
        var client = ClientStreaming(CreatedAt(new ContractId<DemoAsset>("other"), 12), CreatedAt(Accepted, 18));
        var output = new StringWriter();

        await UpdateStreamObserver.ObserveCreatedAsync(
            client, "REST", Bob, Accepted, Start, End, GenerousTimeout, output, CancellationToken.None);

        output.ToString().Should().Contain("REST update stream").And.Contain("(2 event(s) read)");
    }

    [Fact]
    public async Task ObserveCreatedAsync_throws_when_the_bounded_stream_completes_without_the_expected_event()
    {
        var client = ClientStreaming(CreatedAt(new ContractId<DemoAsset>("other"), 12));

        var act = () => UpdateStreamObserver.ObserveCreatedAsync(
            client, "REST", Bob, Accepted, Start, End, GenerousTimeout, new StringWriter(), CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("REST").And.Contain("(10, 20]").And.Contain("without a Created accepted-1");
    }

    [Fact]
    public async Task ObserveCreatedAsync_does_not_deliver_events_outside_the_half_open_window()
    {
        var client = ClientStreaming(CreatedAt(Accepted, 10), CreatedAt(Accepted, 21));

        var act = () => UpdateStreamObserver.ObserveCreatedAsync(
            client, "gRPC", Bob, Accepted, Start, End, GenerousTimeout, new StringWriter(), CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("without a Created accepted-1");
    }

    [Fact]
    public async Task ObserveCreatedAsync_throws_a_verification_failure_when_a_stream_never_completes_within_the_timeout()
    {
        var act = () => UpdateStreamObserver.ObserveCreatedAsync(
            NeverCompletes, "gRPC", Accepted, Start, End, TimeSpan.FromMilliseconds(50), new StringWriter(), CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("gRPC").And.Contain("within 0 s").And.Contain("(10, 20]");
    }

    [Fact]
    public async Task ObserveCreatedAsync_propagates_cancellation_requested_by_the_caller()
    {
        var client = ClientStreaming(CreatedAt(Accepted, 15));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => UpdateStreamObserver.ObserveCreatedAsync(
            client, "gRPC", Bob, Accepted, Start, End, GenerousTimeout, new StringWriter(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ObserveCreatedAsync_throws_when_the_stream_carries_a_fault()
    {
        var client = FakeLedgerClient.Create()
            .WithContractEvents(new ContractStreamEvent<DemoAsset>.StreamError(
                new TransportStatus.Http(System.Net.HttpStatusCode.ServiceUnavailable), "boom", null, null, null))
            .Build();

        var act = () => UpdateStreamObserver.ObserveCreatedAsync(
            client, "REST", Bob, Accepted, Start, End, GenerousTimeout, new StringWriter(), CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("REST").And.Contain("boom");
    }

    [Fact]
    public async Task ObserveTransferOnEveryTransportAsync_observes_the_transfer_on_each_transport()
    {
        var grpc = new LedgerTransport("gRPC", "http://localhost:11901", ClientStreaming(CreatedAt(Accepted, 15)));
        var rest = new LedgerTransport("REST", "http://localhost:11975", ClientStreaming(CreatedAt(Accepted, 16)));
        var output = new StringWriter();

        await MiniDemoRunner.ObserveTransferOnEveryTransportAsync(
            [grpc, rest], Bob, Accepted, Start, End, GenerousTimeout, output, CancellationToken.None);

        output.ToString().Should().Contain("gRPC update stream").And.Contain("REST update stream");
    }

    [Fact]
    public async Task ObserveTransferOnEveryTransportAsync_fails_when_one_transport_never_delivers_the_transfer()
    {
        var grpc = new LedgerTransport("gRPC", "http://localhost:11901", ClientStreaming(CreatedAt(Accepted, 15)));
        var rest = new LedgerTransport("REST", "http://localhost:11975", ClientStreaming());

        var act = () => MiniDemoRunner.ObserveTransferOnEveryTransportAsync(
            [grpc, rest], Bob, Accepted, Start, End, GenerousTimeout, new StringWriter(), CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>()).Which.Message.Should().Contain("REST");
    }

    private static async IAsyncEnumerable<ContractStreamEvent<DemoAsset>> NeverCompletes(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        yield break;
    }

    private static Canton.Ledger.Abstractions.ICantonLedgerClient ClientStreaming(
        params ContractStreamEvent<DemoAsset>[] events) =>
        FakeLedgerClient.Create().WithContractEvents(events).Build();

    private static ContractStreamEvent<DemoAsset> CreatedAt(ContractId<DemoAsset> contractId, long offset) =>
        new ContractStreamEvent<DemoAsset>.Created(
            contractId,
            new DemoAsset(new Party("issuer"), Bob, "GOLD", 42m, Lock: null),
            null,
            LedgerOffset.At(offset),
            (SynchronizerId)"sync1",
            [Bob]);
}
