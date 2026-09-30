// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Stdlib;
using Daml.Runtime.Streams;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Peaceful.Canton.Localnet.Testing;
using Splice.Api.Token.HoldingV2;
using Splice.Api.Token.MetadataV1;
using Xunit;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo.Tests;

public class MiniDemoRunnerTests
{
    private const int CrossTransportSection = 4;

    private static readonly DemoParties Parties =
        new(new Party("issuer"), new Party("alice"), new Party("bob"));

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_passes_when_every_transport_sees_every_asset_key_and_view()
    {
        var grpc = Transport("gRPC", "http://localhost:11901", "asset-grpc", "asset-rest");
        var rest = Transport("REST", "http://localhost:11975", "asset-grpc", "asset-rest");

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc, rest },
            new[] { Written(grpc, "asset-grpc"), Written(rest, "asset-rest") },
            Parties.Alice,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_throws_when_one_transport_misses_what_another_wrote()
    {
        var grpc = Transport("gRPC", "http://localhost:11901", "asset-grpc", "asset-rest");
        var rest = Transport("REST", "http://localhost:11975", "asset-rest");

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc, rest },
            new[] { Written(grpc, "asset-grpc"), Written(rest, "asset-rest") },
            Parties.Alice,
            CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("Cross-transport check failed")
            .And.Contain("asset-grpc")
            .And.Contain("gRPC")
            .And.Contain("REST")
            .And.Contain("1 contract(s) visible");
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_throws_when_transports_report_different_payloads_for_the_same_contract_id()
    {
        var grpc = TransportSeeing("gRPC", "http://localhost:11901", new Row("asset-1", Parties.Alice));
        var rest = TransportSeeing("REST", "http://localhost:11975", new Row("asset-1", Parties.Alice, Amount: 99m));

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc, rest },
            new[] { Written(grpc, "asset-1") },
            Parties.Alice,
            CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("Cross-transport check failed")
            .And.Contain("asset-1")
            .And.Contain("gRPC")
            .And.Contain("REST")
            .And.Contain("amount=42")
            .And.Contain("amount=99");
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_throws_when_the_name_disagrees_while_owner_key_and_amount_match()
    {
        var grpc = TransportSeeing("gRPC", "http://localhost:11901", new Row("asset-1", Parties.Alice));
        var rest = TransportSeeing(
            "REST", "http://localhost:11975", new Row("asset-1", Parties.Alice, "SILVER") { KeyName = "GOLD" });

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc, rest },
            new[] { Written(grpc, "asset-1") },
            Parties.Alice,
            CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("Cross-transport check failed")
            .And.Contain("asset-1")
            .And.Contain("name=GOLD")
            .And.Contain("name=SILVER");
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_throws_when_a_transport_reports_another_contract_key_for_the_same_contract_id()
    {
        var grpc = TransportSeeing("gRPC", "http://localhost:11901", new Row("asset-1", Parties.Alice));
        var rest = TransportSeeing(
            "REST", "http://localhost:11975", new Row("asset-1", Parties.Alice) { KeyName = "SILVER" });

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc, rest },
            new[] { Written(grpc, "asset-1") },
            Parties.Alice,
            CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("Cross-transport check failed")
            .And.Contain("asset-1")
            .And.Contain("REST")
            .And.Contain("key (issuer, SILVER)")
            .And.Contain("under key (issuer, GOLD)");
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_throws_when_transports_report_different_holding_views_for_the_same_contract_id()
    {
        var grpc = TransportSeeing("gRPC", "http://localhost:11901", new Row("asset-1", Parties.Alice));
        var rest = TransportSeeing(
            "REST", "http://localhost:11975", new Row("asset-1", Parties.Alice) { ViewAmount = 99m });

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc, rest },
            new[] { Written(grpc, "asset-1") },
            Parties.Alice,
            CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("Cross-transport check failed")
            .And.Contain("IHolding view asset-1")
            .And.Contain("gRPC")
            .And.Contain("REST")
            .And.Contain("amount=42")
            .And.Contain("amount=99");
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_throws_when_the_holding_view_owner_diverges()
    {
        var grpc = TransportSeeing("gRPC", "http://localhost:11901", new Row("asset-1", Parties.Alice));
        var rest = TransportSeeing(
            "REST", "http://localhost:11975", new Row("asset-1", Parties.Alice) { ViewOwner = Parties.Bob });

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc, rest },
            new[] { Written(grpc, "asset-1") },
            Parties.Alice,
            CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("IHolding view asset-1")
            .And.Contain("owner=alice")
            .And.Contain("owner=bob");
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_throws_when_one_transport_returns_the_asset_but_not_its_holding_view()
    {
        var grpc = TransportSeeing("gRPC", "http://localhost:11901", new Row("asset-1", Parties.Alice));
        var rest = TransportSeeing(
            "REST", "http://localhost:11975", new Row("asset-1", Parties.Alice) { HasView = false });

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc, rest },
            new[] { Written(grpc, "asset-1") },
            Parties.Alice,
            CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("Cross-transport check failed")
            .And.Contain("asset-1")
            .And.Contain("not visible over REST")
            .And.Contain("IHolding views")
            .And.Contain("0 contract(s) visible");
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_throws_when_a_third_transport_diverges_from_two_that_agree()
    {
        var grpc = TransportSeeing("gRPC", "http://localhost:11901", new Row("asset-1", Parties.Alice));
        var rest = TransportSeeing("REST", "http://localhost:11975", new Row("asset-1", Parties.Alice));
        var mirror = TransportSeeing("Mirror", "http://localhost:11999", new Row("asset-1", Parties.Alice, Amount: 99m));

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc, rest, mirror },
            new[] { Written(grpc, "asset-1") },
            Parties.Alice,
            CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("Cross-transport check failed")
            .And.Contain("asset-1")
            .And.Contain("gRPC")
            .And.Contain("Mirror")
            .And.Contain("amount=42")
            .And.Contain("amount=99");
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_throws_for_the_mismatching_asset_only_when_another_asset_matches()
    {
        var grpc = TransportSeeing(
            "gRPC", "http://localhost:11901",
            new Row("asset-ok", Parties.Alice), new Row("asset-bad", Parties.Alice));
        var rest = TransportSeeing(
            "REST", "http://localhost:11975",
            new Row("asset-ok", Parties.Alice), new Row("asset-bad", Parties.Alice, Amount: 99m));

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc, rest },
            new[] { Written(grpc, "asset-ok"), Written(grpc, "asset-bad") },
            Parties.Alice,
            CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("Cross-transport check failed")
            .And.Contain("asset-bad")
            .And.NotContain("asset-ok");
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_throws_when_one_transport_returns_a_duplicate_contract_id()
    {
        var grpc = TransportSeeing(
            "gRPC", "http://localhost:11901",
            new Row("asset-1", Parties.Alice), new Row("asset-1", Parties.Alice));

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc },
            new[] { Written(grpc, "asset-1") },
            Parties.Alice,
            CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("gRPC")
            .And.Contain("asset-1")
            .And.Contain("twice in one ACS snapshot");
    }

    [Fact]
    public async Task VerifyEveryTransportSeesEveryAssetAsync_passes_for_a_lone_transport_reading_back_its_own_asset()
    {
        var grpc = Transport("gRPC", "http://localhost:11901", "asset-grpc");

        var act = () => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
            CrossTransportSection,
            new[] { grpc },
            new[] { Written(grpc, "asset-grpc") },
            Parties.Alice,
            CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task a_cross_transport_key_divergence_exits_the_demo_with_65()
    {
        var grpc = TransportSeeing("gRPC", "http://localhost:11901", new Row("asset-1", Parties.Alice));
        var rest = TransportSeeing(
            "REST", "http://localhost:11975", new Row("asset-1", Parties.Alice) { KeyName = "SILVER" });
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            ct => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
                CrossTransportSection, new[] { grpc, rest }, new[] { Written(grpc, "asset-1") }, Parties.Alice, ct),
            error,
            CancellationToken.None);

        exitCode.Should().Be(65);
        error.ToString().Should().Contain("(issuer, SILVER)");
    }

    [Fact]
    public async Task a_cross_transport_holding_view_divergence_exits_the_demo_with_65()
    {
        var grpc = TransportSeeing("gRPC", "http://localhost:11901", new Row("asset-1", Parties.Alice));
        var rest = TransportSeeing(
            "REST", "http://localhost:11975", new Row("asset-1", Parties.Alice) { ViewAmount = 99m });
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            ct => MiniDemoRunner.VerifyEveryTransportSeesEveryAssetAsync(
                CrossTransportSection, new[] { grpc, rest }, new[] { Written(grpc, "asset-1") }, Parties.Alice, ct),
            error,
            CancellationToken.None);

        exitCode.Should().Be(65);
        error.ToString().Should().Contain("amount=99");
    }

    [Fact]
    public async Task MiniDemoRunner_rejects_an_empty_transport_list()
    {
        await using var fixture = LocalnetFixture.FromEnvironment(NullLoggerFactory.Instance);

        var act = () => new MiniDemoRunner(fixture, Array.Empty<LedgerTransport>());

        act.Should().Throw<ArgumentException>().WithParameterName("transports");
    }

    [Fact]
    public void MiniDemoRunner_rejects_a_null_fixture()
    {
        var act = () => new MiniDemoRunner(null!, new[] { Transport("gRPC", "http://localhost:11901") });

        act.Should().Throw<ArgumentNullException>().WithParameterName("fixture");
    }

    [Fact]
    public async Task MiniDemoRunner_rejects_a_null_transport_list()
    {
        await using var fixture = LocalnetFixture.FromEnvironment(NullLoggerFactory.Instance);

        var act = () => new MiniDemoRunner(fixture, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("transports");
    }

    [Fact]
    public async Task RunPqsLaneAsync_no_ops_when_there_is_no_pqs_client()
    {
        var writtenAssets = new[] { Written(Transport("gRPC", "http://localhost:11901"), "asset-2") };
        var writer = new StringWriter();

        var act = () => MiniDemoRunner.RunPqsLaneAsync(
            5, pqsClient: null, PqsConnectionString.DefaultConnectionString, writtenAssets, Parties.Alice, "alice", writer,
            requirePqs: false, CancellationToken.None);

        await act.Should().NotThrowAsync();
        writer.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task RunPqsLaneAsync_reports_and_swallows_a_query_failure_after_the_probe_is_found()
    {
        var found = new Contract<DemoAsset>(
            new ContractId<DemoAsset>("asset-2"),
            new DemoAsset(Issuer: Parties.Issuer, Owner: Parties.Alice, Name: "GOLD", Amount: 42m, Lock: null));
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(found),
            QueryBehavior = _ => throw new PostgresException("password authentication failed", "FATAL", "FATAL", "28P01"),
        };
        var writtenAssets = new[] { Written(Transport("gRPC", "http://localhost:11901"), "asset-2") };
        var writer = new StringWriter();

        var act = () => MiniDemoRunner.RunPqsLaneAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Parties.Alice, "alice", writer,
            requirePqs: false, CancellationToken.None);

        await act.Should().NotThrowAsync();
        var report = writer.ToString();
        report.Should().Contain("PQS lane failed");
        report.Should().Contain("password authentication failed");
        report.Should().Contain("the ledger sections above already passed");
    }

    [Fact]
    public async Task RunPqsLaneAsync_prints_the_asset_report_before_the_failure_line_when_the_holding_query_fails()
    {
        var found = new Contract<DemoAsset>(
            new ContractId<DemoAsset>("asset-2"),
            new DemoAsset(Issuer: Parties.Issuer, Owner: Parties.Alice, Name: "GOLD", Amount: 42m, Lock: null));
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(found),
            QueryBehavior = _ => Task.FromResult<IReadOnlyList<Contract<DemoAsset>>>(new[] { found }),
        };
        PqsLane.HoldingsFilterQuery holdingsFilterQuery = (_, _, _) =>
            throw new PostgresException("password authentication failed", "FATAL", "FATAL", "28P01");
        var writtenAssets = new[] { Written(Transport("gRPC", "http://localhost:11901"), "asset-2") };
        var writer = new StringWriter();

        var act = () => MiniDemoRunner.RunPqsLaneAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Parties.Alice, "alice", writer,
            requirePqs: false, CancellationToken.None, holdingsFilterQuery);

        await act.Should().NotThrowAsync();
        var report = writer.ToString();
        var assetReportIndex = report.IndexOf("PQS projected 1 of 1 Asset contract(s)", StringComparison.Ordinal);
        var failureIndex = report.IndexOf("PQS lane failed", StringComparison.Ordinal);
        assetReportIndex.Should().BeGreaterThanOrEqualTo(0);
        failureIndex.Should().BeGreaterThan(assetReportIndex);
    }

    [Fact]
    public async Task RunPqsLaneAsync_wraps_and_rethrows_a_query_failure_as_a_PqsRequirementNotMetException_when_strict()
    {
        var found = new Contract<DemoAsset>(
            new ContractId<DemoAsset>("asset-2"),
            new DemoAsset(Issuer: Parties.Issuer, Owner: Parties.Alice, Name: "GOLD", Amount: 42m, Lock: null));
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(found),
            QueryBehavior = _ => throw new PostgresException("password authentication failed", "FATAL", "FATAL", "28P01"),
        };
        var writtenAssets = new[] { Written(Transport("gRPC", "http://localhost:11901"), "asset-2") };
        var writer = new StringWriter();

        var act = () => MiniDemoRunner.RunPqsLaneAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Parties.Alice, "alice", writer,
            requirePqs: true, CancellationToken.None);

        (await act.Should().ThrowAsync<PqsRequirementNotMetException>())
            .Which.Message.Should().Contain("password authentication failed");
    }

    [Fact]
    public async Task RunPqsLaneAsync_lets_cancellation_propagate_instead_of_reporting_it_as_a_failure()
    {
        var stub = new StubPqsClient { FetchByIdBehavior = _ => throw new OperationCanceledException() };
        var writtenAssets = new[] { Written(Transport("gRPC", "http://localhost:11901"), "asset-2") };
        var writer = new StringWriter();

        var act = () => MiniDemoRunner.RunPqsLaneAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Parties.Alice, "alice", writer,
            requirePqs: false, CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        writer.ToString().Should().NotContain("PQS lane failed");
    }

    private static AssetKey Key(string name) => new("issuer", name);

    private static WrittenAsset Written(LedgerTransport transport, string contractId) =>
        new(transport, new ContractId<DemoAsset>(contractId), Key("GOLD"));

    private static LedgerTransport Transport(string name, string endpoint, params string[] visibleContractIds) =>
        TransportSeeing(name, endpoint, visibleContractIds
            .Select(contractId => new Row(contractId, Parties.Alice))
            .ToArray());

    private static LedgerTransport TransportSeeing(string name, string endpoint, params Row[] rows) =>
        new(name, endpoint, FakeLedgerClient.Create()
            .WithActiveContracts(rows.Select(ActiveAsset).Append(SnapshotCheckpoint()).ToArray())
            .WithActiveInterfaceContracts(rows
                .Where(row => row.HasView)
                .Select(ActiveHolding)
                .Append(new InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Checkpoint(
                    new StakeholderResume(LedgerOffset.At(2))))
                .ToArray())
            .Build());

    private static AcsSnapshotEntry<DemoAsset> ActiveAsset(Row row) =>
        LedgerEvents.Created(
            new ContractId<DemoAsset>(row.ContractId),
            new DemoAsset(Issuer: Parties.Issuer, Owner: row.Owner, Name: row.Name, Amount: row.Amount, Lock: null),
            new ContractKey(
                DemoAsset.Key.KeyEncoder(new Tuple2<Party, string>(Parties.Issuer, row.KeyName)),
                DemoAsset.TemplateId),
            LedgerOffset.At(1),
            (SynchronizerId)"sync1",
            [row.Owner]);

    private static InterfaceAcsSnapshotEntry<IHolding, HoldingView> ActiveHolding(Row row) =>
        new InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Created(
            new ContractId<IHolding>(row.ContractId),
            new HoldingView(
                new Account(row.ViewOwner ?? row.Owner, Provider: null, Id: ""),
                new InstrumentId(Parties.Issuer, row.Name),
                row.ViewAmount ?? row.Amount,
                Lock: null,
                new Metadata(new Dictionary<string, string>())),
            Key: null,
            LedgerOffset.At(1),
            (SynchronizerId)"sync1",
            EquatableArray.Create<Party>([row.Owner]));

    private static AcsSnapshotEntry<DemoAsset> SnapshotCheckpoint() =>
        LedgerEvents.Checkpoint<DemoAsset>(LedgerOffset.At(2));

    private sealed record Row(string ContractId, Party Owner, string Name = "GOLD", decimal Amount = 42m)
    {
        public string KeyName { get; init; } = Name;

        public bool HasView { get; init; } = true;

        public decimal? ViewAmount { get; init; }

        public Party? ViewOwner { get; init; }
    }
}
