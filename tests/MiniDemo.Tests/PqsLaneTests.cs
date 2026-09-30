// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Net.Sockets;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Splice.Api.Token.HoldingV2;
using Splice.Api.Token.MetadataV1;
using Xunit;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo.Tests;

public class PqsLaneTests
{
    private static readonly Party Issuer = new("issuer");
    private static readonly Party Alice = new("alice");

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ShortPollInterval = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan LongProgressAfter = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RunAsync_reports_nothing_to_project_when_no_asset_was_written()
    {
        var writer = new StringWriter();
        var neverCalled = new StubPqsClient();

        await PqsLane.RunAsync(
            5, neverCalled, PqsConnectionString.DefaultConnectionString, Array.Empty<WrittenAsset>(), Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, LongProgressAfter, false, CancellationToken.None);

        writer.ToString().Should().Contain("nothing for PQS to project");
    }

    [Fact]
    public async Task RunAsync_reports_the_projected_count_once_the_probe_contract_is_found()
    {
        var asset = new DemoAsset(Issuer: Issuer, Owner: Alice, Name: "GOLD", Amount: 42m, Lock: null);
        var staged = new Contract<DemoAsset>(new ContractId<DemoAsset>("asset-2"), asset);
        var pqsClient = FakePqsClient.Create()
            .WithQueryResults(staged)
            .Build();
        var writtenAssets = new[] { new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")) };
        var writer = new StringWriter();

        await PqsLane.RunAsync(
            5, pqsClient, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, LongProgressAfter, false, CancellationToken.None, FakeHoldingsQuery());

        var report = writer.ToString();
        report.Should().Contain("PQS projected 1 of 1 Asset contract(s) owned by alice");
        report.Should().Contain("asset-2");
        report.Should().NotContain("still catching up");
    }

    [Fact]
    public async Task RunAsync_reports_the_written_assets_projected_as_IHolding_views()
    {
        var asset = new DemoAsset(Issuer: Issuer, Owner: Alice, Name: "GOLD", Amount: 42m, Lock: null);
        var staged = new Contract<DemoAsset>(new ContractId<DemoAsset>("asset-2"), asset);
        var holding = new InterfaceContract<IHolding, HoldingView>(
            new ContractId<IHolding>("asset-2"),
            new HoldingView(
                new Account(Alice, Provider: null, Id: ""),
                new InstrumentId(Issuer, "GOLD"),
                42m,
                Lock: null,
                new Metadata(new Dictionary<string, string>())));
        var unrelated = holding with { Id = new ContractId<IHolding>("other-holding") };
        var pqsClient = FakePqsClient.Create()
            .WithQueryResults(staged)
            .Build();
        var writtenAssets = new[] { new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")) };
        var writer = new StringWriter();

        await PqsLane.RunAsync(
            5, pqsClient, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, LongProgressAfter, false, CancellationToken.None,
            FakeHoldingsQuery(holding, unrelated));

        var report = writer.ToString();
        report.Should().Contain("PQS projected 1 of 1 of them as IHolding views");
        report.Should().Contain("asset-2 owner=alice, admin=issuer, instrument=GOLD, amount=42");
        report.Should().NotContain("other-holding");
    }

    [Fact]
    public async Task RunAsync_reports_the_read_model_still_catching_up_when_not_every_asset_is_projected_yet()
    {
        var asset = new DemoAsset(Issuer: Issuer, Owner: Alice, Name: "GOLD", Amount: 42m, Lock: null);
        var staged = new Contract<DemoAsset>(new ContractId<DemoAsset>("asset-2"), asset);
        var pqsClient = FakePqsClient.Create()
            .WithQueryResults(staged)
            .Build();
        var writtenAssets = new[]
        {
            new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")),
            new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-3"), new AssetKey("issuer", "GOLD")),
        };
        var writer = new StringWriter();

        await PqsLane.RunAsync(
            5, pqsClient, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, LongProgressAfter, false, CancellationToken.None, FakeHoldingsQuery());

        var report = writer.ToString();
        report.Should().Contain("PQS projected 1 of 2 Asset contract(s) owned by alice");
        report.Should().Contain("still catching up");
    }

    [Fact]
    public async Task RunAsync_reports_a_hint_and_skips_the_query_when_pqs_is_unavailable()
    {
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => throw new SocketException((int)SocketError.ConnectionRefused),
        };
        var writtenAssets = new[] { new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")) };
        var writer = new StringWriter();

        await PqsLane.RunAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, LongProgressAfter, false, CancellationToken.None);

        var report = writer.ToString();
        report.Should().Contain("PQS is not available");
        report.Should().Contain("make up PQS=true");
    }

    [Fact]
    public async Task RunAsync_reports_a_troubleshooting_hint_when_the_projection_never_lands_within_the_budget()
    {
        var stub = new StubPqsClient { FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(null) };
        var writtenAssets = new[] { new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")) };
        var writer = new StringWriter();

        await PqsLane.RunAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, TimeSpan.Zero, false, CancellationToken.None);

        var report = writer.ToString();
        report.Should().Contain("did not project the Asset contract");
        report.Should().Contain("asset-2");
        report.Should().Contain("unknown Daml package");
        report.Should().Contain("__watermark");
        report.Should().Contain("host=localhost");
        report.Should().Contain("dbname=pqs-a-validator-1");
        report.Should().NotContain("CANTON_LOCALNET");
        report.Should().Contain("Sections 1-4 already passed");
        report.Should().Contain("Waiting for PQS to project Asset asset-2");
    }

    [Fact]
    public async Task RunAsync_reports_the_troubleshooting_hint_against_an_overridden_connection_string()
    {
        var stub = new StubPqsClient { FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(null) };
        var writtenAssets = new[] { new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")) };
        var writer = new StringWriter();
        const string overriddenConnectionString =
            "Host=pqs.example.internal;Port=6543;Database=other-db;Username=cnadmin;Password=supersafe";

        await PqsLane.RunAsync(
            5, stub, overriddenConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, TimeSpan.Zero, false, CancellationToken.None);

        var report = writer.ToString();
        report.Should().Contain("host=pqs.example.internal");
        report.Should().Contain("port=6543");
        report.Should().Contain("dbname=other-db");
        report.Should().NotContain("host=localhost");
        report.Should().NotContain("dbname=pqs-a-validator-1");
    }

    [Fact]
    public async Task RunAsync_throws_a_PqsRequirementNotMetException_when_pqs_is_unavailable_and_require_pqs_is_set()
    {
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => throw new SocketException((int)SocketError.ConnectionRefused),
        };
        var writtenAssets = new[] { new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")) };
        var writer = new StringWriter();

        var act = () => PqsLane.RunAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, LongProgressAfter, true, CancellationToken.None);

        await act.Should().ThrowAsync<PqsRequirementNotMetException>();
    }

    [Fact]
    public async Task RunAsync_throws_a_PqsRequirementNotMetException_when_nothing_projects_within_the_budget_and_require_pqs_is_set()
    {
        var stub = new StubPqsClient { FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(null) };
        var writtenAssets = new[] { new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")) };
        var writer = new StringWriter();

        var act = () => PqsLane.RunAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, TimeSpan.Zero, true, CancellationToken.None);

        await act.Should().ThrowAsync<PqsRequirementNotMetException>();
    }

    [Fact]
    public async Task RunAsync_does_not_throw_when_every_asset_and_holding_is_projected_and_require_pqs_is_set()
    {
        var asset = new DemoAsset(Issuer: Issuer, Owner: Alice, Name: "GOLD", Amount: 42m, Lock: null);
        var staged = new Contract<DemoAsset>(new ContractId<DemoAsset>("asset-2"), asset);
        var holding = new InterfaceContract<IHolding, HoldingView>(
            new ContractId<IHolding>("asset-2"),
            new HoldingView(
                new Account(Alice, Provider: null, Id: ""),
                new InstrumentId(Issuer, "GOLD"),
                42m,
                Lock: null,
                new Metadata(new Dictionary<string, string>())));
        var pqsClient = FakePqsClient.Create()
            .WithQueryResults(staged)
            .Build();
        var writtenAssets = new[] { new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")) };
        var writer = new StringWriter();

        await PqsLane.RunAsync(
            5, pqsClient, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, LongProgressAfter, true, CancellationToken.None,
            FakeHoldingsQuery(holding));

        var report = writer.ToString();
        report.Should().Contain("PQS projected 1 of 1 Asset contract(s) owned by alice");
        report.Should().Contain("PQS projected 1 of 1 of them as IHolding views");
    }

    [Fact]
    public async Task RunAsync_polls_until_complete_and_does_not_throw_when_projection_catches_up_within_the_budget()
    {
        var assetTwo = new Contract<DemoAsset>(
            new ContractId<DemoAsset>("asset-2"),
            new DemoAsset(Issuer: Issuer, Owner: Alice, Name: "GOLD", Amount: 42m, Lock: null));
        var assetThree = new Contract<DemoAsset>(
            new ContractId<DemoAsset>("asset-3"),
            new DemoAsset(Issuer: Issuer, Owner: Alice, Name: "GOLD", Amount: 7m, Lock: null));
        var holdingTwo = new InterfaceContract<IHolding, HoldingView>(
            new ContractId<IHolding>("asset-2"),
            new HoldingView(
                new Account(Alice, Provider: null, Id: ""),
                new InstrumentId(Issuer, "GOLD"),
                42m,
                Lock: null,
                new Metadata(new Dictionary<string, string>())));
        var holdingThree = holdingTwo with { Id = new ContractId<IHolding>("asset-3") };

        var attempt = 0;
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(assetTwo),
            QueryBehavior = _ =>
            {
                attempt++;
                IReadOnlyList<Contract<DemoAsset>> owned = attempt == 1
                    ? new[] { assetTwo }
                    : new[] { assetTwo, assetThree };
                return Task.FromResult(owned);
            },
        };
        var holdingsFilterQuery = FakeHoldingsQuery(contractIds =>
        {
            IReadOnlyList<InterfaceContract<IHolding, HoldingView>> holdings = attempt == 1
                ? new[] { holdingTwo }
                : new[] { holdingTwo, holdingThree };
            return holdings.Where(holding => contractIds.Contains(holding.Id.Value)).ToList();
        });
        var writtenAssets = new[]
        {
            new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")),
            new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-3"), new AssetKey("issuer", "GOLD")),
        };
        var writer = new StringWriter();

        await PqsLane.RunAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, LongProgressAfter, true, CancellationToken.None, holdingsFilterQuery);

        var report = writer.ToString();
        report.Should().Contain("PQS projected 2 of 2 Asset contract(s) owned by alice");
        report.Should().Contain("PQS projected 2 of 2 of them as IHolding views");
        attempt.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task RunAsync_throws_a_PqsRequirementNotMetException_when_projection_stays_partial_for_the_whole_budget()
    {
        var assetTwo = new Contract<DemoAsset>(
            new ContractId<DemoAsset>("asset-2"),
            new DemoAsset(Issuer: Issuer, Owner: Alice, Name: "GOLD", Amount: 42m, Lock: null));
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(assetTwo),
            QueryBehavior = _ => Task.FromResult<IReadOnlyList<Contract<DemoAsset>>>(new[] { assetTwo }),
        };
        var writtenAssets = new[]
        {
            new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")),
            new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-3"), new AssetKey("issuer", "GOLD")),
        };
        var writer = new StringWriter();

        var act = () => PqsLane.RunAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, LongProgressAfter, true, CancellationToken.None, FakeHoldingsQuery());

        (await act.Should().ThrowAsync<PqsRequirementNotMetException>())
            .Which.Message.Should().Contain("PQS projected 1 of 2 Asset contract(s) and 0 of 2 IHolding view(s)");
    }

    [Fact]
    public async Task RunAsync_throws_when_every_asset_is_projected_but_holding_views_stay_incomplete()
    {
        var assetTwo = new Contract<DemoAsset>(
            new ContractId<DemoAsset>("asset-2"),
            new DemoAsset(Issuer: Issuer, Owner: Alice, Name: "GOLD", Amount: 42m, Lock: null));
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(assetTwo),
            QueryBehavior = _ => Task.FromResult<IReadOnlyList<Contract<DemoAsset>>>(new[] { assetTwo }),
        };
        var writtenAssets = new[] { new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")) };
        var writer = new StringWriter();

        var act = () => PqsLane.RunAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            ShortTimeout, ShortPollInterval, LongProgressAfter, true, CancellationToken.None, FakeHoldingsQuery());

        (await act.Should().ThrowAsync<PqsRequirementNotMetException>())
            .Which.Message.Should().Contain("PQS projected 1 of 1 Asset contract(s) and 0 of 1 IHolding view(s)");
    }

    [Fact]
    public async Task RunAsync_shares_a_single_budget_across_the_probe_wait_and_the_polling_phase()
    {
        var budget = TimeSpan.FromMilliseconds(300);
        var pollInterval = TimeSpan.FromMilliseconds(10);
        var asset = new Contract<DemoAsset>(
            new ContractId<DemoAsset>("asset-2"),
            new DemoAsset(Issuer: Issuer, Owner: Alice, Name: "GOLD", Amount: 42m, Lock: null));
        var sinceStart = Stopwatch.StartNew();
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => Task.FromResult(
                sinceStart.Elapsed >= TimeSpan.FromMilliseconds(200) ? asset : (Contract<DemoAsset>?)null),
            QueryBehavior = _ => Task.FromResult<IReadOnlyList<Contract<DemoAsset>>>(new[] { asset }),
        };
        var writtenAssets = new[] { new WrittenAsset(Transport(), new ContractId<DemoAsset>("asset-2"), new AssetKey("issuer", "GOLD")) };
        var writer = new StringWriter();
        var wallClock = Stopwatch.StartNew();

        var act = () => PqsLane.RunAsync(
            5, stub, PqsConnectionString.DefaultConnectionString, writtenAssets, Alice, "alice", writer,
            budget, pollInterval, TimeSpan.Zero, true, CancellationToken.None, FakeHoldingsQuery());

        await act.Should().ThrowAsync<PqsRequirementNotMetException>();
        wallClock.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(450));
    }

    [Fact]
    public async Task WaitForFirstProjectionAsync_returns_Found_once_the_probe_contract_appears()
    {
        var asset = new DemoAsset(Issuer: Issuer, Owner: Alice, Name: "GOLD", Amount: 42m, Lock: null);
        var contractId = new ContractId<DemoAsset>("asset-1");
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(new Contract<DemoAsset>(contractId, asset)),
        };

        var result = await PqsLane.WaitForFirstProjectionAsync(
            stub, contractId, TextWriter.Null, Stopwatch.StartNew(), ShortTimeout, ShortPollInterval, LongProgressAfter,
            CancellationToken.None);

        result.Should().BeOfType<PqsWaitResult.Found>();
    }

    [Fact]
    public async Task WaitForFirstProjectionAsync_returns_TimedOut_when_the_contract_never_appears()
    {
        var stub = new StubPqsClient { FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(null) };

        var result = await PqsLane.WaitForFirstProjectionAsync(
            stub, new ContractId<DemoAsset>("asset-1"), TextWriter.Null, Stopwatch.StartNew(), ShortTimeout,
            ShortPollInterval, LongProgressAfter, CancellationToken.None);

        result.Should().BeOfType<PqsWaitResult.TimedOut>();
    }

    [Fact]
    public async Task WaitForFirstProjectionAsync_returns_Unavailable_when_pqs_is_unreachable()
    {
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => throw new SocketException((int)SocketError.ConnectionRefused),
        };

        var result = await PqsLane.WaitForFirstProjectionAsync(
            stub, new ContractId<DemoAsset>("asset-1"), TextWriter.Null, Stopwatch.StartNew(), ShortTimeout,
            ShortPollInterval, LongProgressAfter, CancellationToken.None);

        result.Should().BeOfType<PqsWaitResult.Unavailable>()
            .Which.Exception.Should().BeOfType<SocketException>();
    }

    [Fact]
    public async Task WaitForFirstProjectionAsync_writes_a_progress_line_once_the_configured_delay_elapses()
    {
        var stub = new StubPqsClient { FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(null) };
        var writer = new StringWriter();

        await PqsLane.WaitForFirstProjectionAsync(
            stub, new ContractId<DemoAsset>("asset-1"), writer, Stopwatch.StartNew(), ShortTimeout, ShortPollInterval,
            TimeSpan.Zero, CancellationToken.None);

        writer.ToString().Should().Contain("Waiting for PQS to project Asset asset-1");
    }

    private static PqsLane.HoldingsFilterQuery FakeHoldingsQuery(
        Func<IReadOnlyCollection<string>, IReadOnlyList<InterfaceContract<IHolding, HoldingView>>> resolve) =>
        (_, contractIds, _) => Task.FromResult(resolve(contractIds));

    private static PqsLane.HoldingsFilterQuery FakeHoldingsQuery(
        params InterfaceContract<IHolding, HoldingView>[] holdings) =>
        FakeHoldingsQuery(contractIds => holdings.Where(holding => contractIds.Contains(holding.Id.Value)).ToList());

    private static LedgerTransport Transport() =>
        new("gRPC", "http://localhost:11901", FakeLedgerClient.Create().Build());
}
