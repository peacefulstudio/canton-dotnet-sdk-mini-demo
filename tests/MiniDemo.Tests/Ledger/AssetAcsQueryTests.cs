// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Sockets;
using AwesomeAssertions;
using Canton.Ledger.Testing;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Daml.Runtime.Streams;
using MiniDemo;
using Peaceful.Canton.Localnet.Testing;
using Xunit;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo.Tests;

public class AssetAcsQueryTests
{
    private static readonly ContractKey GoldKey = new(
        DemoAsset.Key.KeyEncoder(new Tuple2<Party, string>(new Party("issuer"), "GOLD")), DemoAsset.TemplateId);

    [Fact]
    public async Task created_events_are_mapped_to_asset_snapshots()
    {
        var owner = new Party("bob");
        var asset = new DemoAsset(Issuer: new Party("issuer"), Owner: owner, Name: "GOLD", Amount: 42m, Lock: null);
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid1"),
                    asset,
                    GoldKey,
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [owner]),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2)))
            .Build();

        var result = await AssetAcsQuery.QueryForPartyAsync(client, owner, CancellationToken.None);

        result.Should().ContainSingle();
        result[0].ContractId.Value.Should().Be("cid1");
        result[0].Owner.Should().Be("bob");
        result[0].Name.Should().Be("GOLD");
        result[0].Amount.Should().Be(42m);
        result[0].Key.Should().Be(new AssetKey("issuer", "GOLD"));
    }

    [Fact]
    public async Task a_created_event_without_a_contract_key_throws_because_every_asset_is_keyed()
    {
        var owner = new Party("alice");
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid1"),
                    new DemoAsset(Issuer: new Party("issuer"), Owner: owner, Name: "GOLD", Amount: 42m, Lock: null),
                    key: null,
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [owner]),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2)))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, owner, CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("cid1").And.Contain("without its contract key");
    }

    [Fact]
    public async Task a_created_event_whose_key_is_not_an_issuer_name_pair_throws_a_verification_failure()
    {
        var owner = new Party("alice");
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid1"),
                    new DemoAsset(Issuer: new Party("issuer"), Owner: owner, Name: "GOLD", Amount: 42m, Lock: null),
                    new ContractKey(new DamlText("GOLD"), DemoAsset.TemplateId),
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [owner]),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2)))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, owner, CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("cid1").And.Contain("does not decode as (issuer, name)");
    }

    [Fact]
    public async Task unclassified_events_throw_instead_of_being_silently_dropped()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Unclassified<DemoAsset>(LedgerOffset.At(7), UnclassifiedKind.Unknown, "unmapped-template"),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(8)))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("offset 7").And.Contain("unmapped-template");
    }

    [Fact]
    public async Task an_unclassified_event_without_an_offset_reports_an_unknown_offset_and_no_wire_kind()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Unclassified<DemoAsset>(offset: null, UnclassifiedKind.DecodeFailure, rawKind: null),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(1)))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("offset unknown")
            .And.Contain("DecodeFailure")
            .And.NotContain("wire kind");
    }

    [Fact]
    public async Task stream_error_events_throw_a_LedgerOperationException_with_status_and_message()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(LedgerEvents.StreamError<DemoAsset>(GrpcStatus.Unavailable, "snapshot transport failed"))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Message.Should().Contain("status Grpc { StatusCode = Unavailable }").And.Contain("snapshot transport failed");
        thrown.Status.Should().Be(GrpcStatus.Unavailable);
    }

    [Fact]
    public async Task a_stream_error_carrying_a_category_reports_it_alongside_the_status()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.StreamError<DemoAsset>(
                    GrpcStatus.Unavailable, "snapshot transport failed", DamlErrorCategory.TransientServerFailure))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.Message.Should().Contain("status Grpc { StatusCode = Unavailable }")
            .And.Contain("category TransientServerFailure")
            .And.Contain("snapshot transport failed");
        thrown.Category.Should().Be(DamlErrorCategory.TransientServerFailure);
    }

    [Fact]
    public async Task a_stream_error_carrying_a_source_exception_preserves_it_for_transport_classification()
    {
        var transportFailure = new SocketException((int)SocketError.ConnectionRefused);
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.StreamError<DemoAsset>(
                    GrpcStatus.Internal, "snapshot transport failed", DamlErrorCategory.TransientServerFailure,
                    sourceException: transportFailure))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        thrown.InnerException.Should().BeSameAs(transportFailure);
        LocalnetPreflight.IsLocalnetUnreachable(thrown).Should().BeTrue();
    }

    [Fact]
    public async Task a_grpc_unavailable_stream_error_is_classified_as_localnet_unreachable_not_a_verification_failure()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.StreamError<DemoAsset>(GrpcStatus.Unavailable, "failed to connect to all addresses"))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<LedgerOperationException>()).Which;
        LocalnetPreflight.IsLocalnetUnreachable(thrown).Should().BeTrue();
    }

    [Fact]
    public async Task an_unclassified_event_of_a_transport_kind_conditions_the_mapping_cause_on_DecodeFailure()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Unclassified<DemoAsset>(
                    LedgerOffset.At(4), UnclassifiedKind.MissingSynchronizerId, rawKind: null),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(5)))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("MissingSynchronizerId")
            .And.Contain("DecodeFailure");
    }

    [Fact]
    public async Task every_created_event_before_the_terminal_checkpoint_is_accumulated_in_order()
    {
        var owner = new Party("bob");
        var asset = new DemoAsset(Issuer: new Party("issuer"), Owner: owner, Name: "GOLD", Amount: 42m, Lock: null);
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid1"),
                    asset,
                    GoldKey,
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [owner]),
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid2"),
                    asset,
                    GoldKey,
                    LedgerOffset.At(2),
                    (SynchronizerId)"sync1",
                    [owner]),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(3)))
            .Build();

        var result = await AssetAcsQuery.QueryForPartyAsync(client, owner, CancellationToken.None);

        result.Select(a => a.ContractId.Value).Should().Equal("cid1", "cid2");
    }

    [Fact]
    public async Task a_created_event_after_the_terminal_checkpoint_is_not_accumulated()
    {
        var owner = new Party("bob");
        var asset = new DemoAsset(Issuer: new Party("issuer"), Owner: owner, Name: "GOLD", Amount: 42m, Lock: null);
        var client = FakeLedgerClient.Create()
            .WithMalformedActiveContracts(
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid1"),
                    asset,
                    GoldKey,
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [owner]),
                LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2)),
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid3"),
                    asset,
                    GoldKey,
                    LedgerOffset.At(3),
                    (SynchronizerId)"sync1",
                    [owner]))
            .Build();

        var result = await AssetAcsQuery.QueryForPartyAsync(client, owner, CancellationToken.None);

        result.Select(a => a.ContractId.Value).Should().Equal("cid1");
    }

    [Fact]
    public async Task a_created_event_does_not_suppress_the_throw_from_a_terminal_stream_error()
    {
        var owner = new Party("bob");
        var asset = new DemoAsset(Issuer: new Party("issuer"), Owner: owner, Name: "GOLD", Amount: 42m, Lock: null);
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid1"),
                    asset,
                    GoldKey,
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [owner]),
                LedgerEvents.StreamError<DemoAsset>(GrpcStatus.Unavailable, "snapshot transport failed"))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, owner, CancellationToken.None);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Message.Should().Contain("status Grpc { StatusCode = Unavailable }").And.Contain("snapshot transport failed");
    }

    [Fact]
    public async Task a_checkpoint_only_snapshot_returns_no_assets()
    {
        var client = FakeLedgerClient.Create()
            .WithActiveContracts(LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(1)))
            .Build();

        var result = await AssetAcsQuery.QueryForPartyAsync(client, new Party("bob"), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task a_stream_ending_without_a_terminal_marker_throws_instead_of_returning_a_truncated_snapshot()
    {
        var owner = new Party("bob");
        var asset = new DemoAsset(Issuer: new Party("issuer"), Owner: owner, Name: "GOLD", Amount: 42m, Lock: null);
        var client = FakeLedgerClient.Create()
            .WithMalformedActiveContracts(
                LedgerEvents.Created(
                    new ContractId<DemoAsset>("cid1"),
                    asset,
                    GoldKey,
                    LedgerOffset.At(1),
                    (SynchronizerId)"sync1",
                    [owner]))
            .Build();

        var act = () => AssetAcsQuery.QueryForPartyAsync(client, owner, CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("truncated");
    }
}
