// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Sockets;
using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Testing;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Daml.Runtime.Streams;
using MiniDemo.Asset;
using Splice.Api.Token.HoldingV2;
using Splice.Api.Token.MetadataV1;
using Splice.Api.Token.TransferInstructionV2;
using Xunit;
using DemoAsset = MiniDemo.Asset.Asset;
using HoldingLock = Splice.Api.Token.HoldingV2.Lock;

namespace MiniDemo.Tests;

public class TwoStepTransferTests
{
    private static readonly DemoParties Parties =
        new(new Party("issuer"), new Party("alice"), new Party("bob"));

    private static readonly AssetKey Gold = new("issuer", "GOLD");

    private static readonly DateTimeOffset Now = new(2026, 9, 29, 10, 0, 0, 123, TimeSpan.Zero);

    private static readonly DateTimeOffset ExecuteBefore = new(2026, 9, 29, 11, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan ShortPollInterval = TimeSpan.FromMilliseconds(5);

    private static readonly TimeSpan LongProgressAfter = TimeSpan.FromSeconds(10);

    [Fact]
    public void The_demo_pins_its_amounts_and_transfer_window_by_literal()
    {
        MiniDemoRunner.DemoInstrument.Should().Be("GOLD");
        MiniDemoRunner.AliceMintAmount.Should().Be(42m);
        MiniDemoRunner.BobMintAmount.Should().Be(10m);
        MiniDemoRunner.ExpectedTotalSupply.Should().Be(52m);
        MiniDemoRunner.ClockSkewAllowance.Should().Be(TimeSpan.FromMinutes(1));
        MiniDemoRunner.AcceptanceWindow.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task CreateInstrumentAsync_submits_the_instrument_acting_as_the_issuer_alone()
    {
        var writer = new RecordingLedgerWriter(FakeLedgerClient.Create()
            .WithCreateResult(LedgerOutcomes.One(new ContractId<Instrument>("instrument-1")))
            .Build());

        await MiniDemoRunner.CreateInstrumentAsync(
            writer, new Instrument(Parties.Issuer, "GOLD"), "gRPC", ResultStyle.OutcomePatternMatch, CancellationToken.None);

        var create = writer.Creates.Should().ContainSingle().Subject;
        create.Payload.Should().Be(new Instrument(new Party("issuer"), "GOLD"));
        create.Submitter.ActAs.Should().Equal(new Party("issuer"));
        create.Submitter.ReadAs.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateTransferFactoryAsync_lists_alice_and_bob_as_users_and_returns_the_factory_as_a_TransferFactory()
    {
        var writer = new RecordingLedgerWriter(FakeLedgerClient.Create()
            .WithCreateResult(LedgerOutcomes.One(new ContractId<AssetTransferFactory>("factory-1")))
            .Build());

        var factoryCid = await MiniDemoRunner.CreateTransferFactoryAsync(writer, Parties, "gRPC", CancellationToken.None);

        factoryCid.Value.Should().Be("factory-1");
        var create = writer.Creates.Should().ContainSingle().Subject;
        var factory = create.Payload.Should().BeOfType<AssetTransferFactory>().Subject;
        factory.Issuer.Should().Be(new Party("issuer"));
        factory.Users.Should().Equal(new Party("alice"), new Party("bob"));
        create.Submitter.ActAs.Should().Equal(new Party("issuer"));
    }

    [Fact]
    public async Task MintByKeyAsync_exercises_Mint_by_the_instrument_key_acting_as_the_issuer()
    {
        var writer = new RecordingLedgerWriter(FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.One(Transaction(created: [CreatedAsset("asset-bob")])))
            .Build());

        var mintedCid = await MiniDemoRunner.MintByKeyAsync(
            writer, Gold, Parties.Issuer, Parties.Bob, "bob", 10m, "gRPC", CancellationToken.None);

        mintedCid.Value.Should().Be("asset-bob");
        var submission = writer.Submissions.Should().ContainSingle().Subject;
        submission.Submitter.ActAs.Should().Equal(new Party("issuer"));
        submission.Submission.WorkflowId.Should().Be(new WorkflowId("mini-demo"));
        var command = submission.Submission.Commands.Should().ContainSingle()
            .Subject.Should().BeOfType<ExerciseByKeyCommand>().Subject;
        command.TemplateId.Should().Be(Instrument.TemplateId);
        command.Choice.Should().Be(new ChoiceName("Mint"));
        var key = Instrument.Key.KeyDecoder(command.ContractKey);
        key._1.Should().Be(new Party("issuer"));
        key._2.Should().Be("GOLD");
        Instrument.Mint.FromRecord((DamlRecord)command.ChoiceArgument)
            .Should().Be(new Instrument.Mint(new Party("bob"), 10m));
    }

    [Fact]
    public async Task MintByKeyAsync_throws_when_the_mint_creates_no_asset()
    {
        var client = FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.One(Transaction()))
            .Build();

        var act = () => MiniDemoRunner.MintByKeyAsync(
            client, Gold, Parties.Issuer, Parties.Alice, "alice", 42m, "gRPC", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("no contracts of type Asset");
    }

    [Fact]
    public void ProposalFor_moves_alices_whole_holding_to_bob_inside_a_one_hour_window()
    {
        var proposal = MiniDemoRunner.ProposalFor(Parties, Gold, new ContractId<DemoAsset>("asset-alice"), Now);

        var transfer = proposal.Transfer;
        transfer.Sender.Should().Be(new Account(new Party("alice"), Provider: null, Id: ""));
        transfer.Receiver.Should().Be(new Account(new Party("bob"), Provider: null, Id: ""));
        transfer.Amount.Should().Be(42m);
        transfer.InstrumentId.Should().Be(new InstrumentId(new Party("issuer"), "GOLD"));
        transfer.RequestedAt.Should().Be(new DateTimeOffset(2026, 9, 29, 9, 59, 0, TimeSpan.Zero));
        transfer.ExecuteBefore.Should().Be(ExecuteBefore);
        transfer.InputHoldingCids.Should().Equal(new ContractId<IHolding>("asset-alice"));
        transfer.Meta.Values.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeAsync_exercises_TransferFactory_Transfer_as_alice_and_returns_the_pending_instruction()
    {
        var writer = new RecordingLedgerWriter(FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.One(Transaction(exercised: [TransferExercised(PendingResult("instruction-1"))])))
            .Build());

        var instructionCid = await MiniDemoRunner.ProposeAsync(
            writer, new ContractId<ITransferFactory>("factory-1"), Proposal(), "gRPC", CancellationToken.None);

        instructionCid.Value.Should().Be("instruction-1");
        var submission = writer.Submissions.Should().ContainSingle().Subject;
        submission.Submitter.ActAs.Should().Equal(new Party("alice"));
        var command = submission.Submission.Commands.Should().ContainSingle()
            .Subject.Should().BeOfType<ExerciseCommand>().Subject;
        command.ContractId.Value.Should().Be("factory-1");
        command.Choice.Should().Be(new ChoiceName("TransferFactory_Transfer"));
        var argument = TransferFactory_Transfer.FromRecord((DamlRecord)command.ChoiceArgument);
        argument.Actors.Should().Equal(new Party("alice"));
        argument.Transfer.Should().Be(Proposal().Transfer);
    }

    [Fact]
    public async Task ProposeAsync_rejects_a_transfer_that_does_not_leave_a_pending_instruction()
    {
        var client = FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.One(Transaction(exercised: [TransferExercised(CompletedResult("asset-bob"))])))
            .Build();

        var act = () => MiniDemoRunner.ProposeAsync(
            client, new ContractId<ITransferFactory>("factory-1"), Proposal(), "gRPC", CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("TransferInstructionResult_Completed")
            .And.Contain("pending transfer instruction");
    }

    [Fact]
    public async Task ReportPendingHoldingAsync_returns_alices_holding_locked_by_the_issuer_until_the_deadline()
    {
        var client = ClientHolding(
            Holding("asset-locked", Parties.Alice, 42m, TransferLock()),
            Holding("asset-other", Parties.Bob, 10m, holdingLock: null));

        var locked = await MiniDemoRunner.ReportPendingHoldingAsync(client, Proposal(), "gRPC", CancellationToken.None);

        locked.ContractId.Should().Be("asset-locked");
        locked.Headline("alice").Should().Be("alice: 42 GOLD 🔒");
        locked.Lock!.Describe().Should().Be("locked by issuer until 2026-09-29 11:00:00Z (transfer to bob)");
    }

    [Fact]
    public async Task ReadDisclosureAsync_returns_the_typed_disclosure_of_the_requested_holding()
    {
        var client = ClientHoldingEntries(
            ActiveHolding(Holding("asset-locked", Parties.Alice, 42m, TransferLock()))
                with { Disclosure = LockedHoldingDisclosure() },
            ActiveHolding(Holding("asset-other", Parties.Bob, 10m, holdingLock: null)));

        var disclosed = await HoldingQuery.ReadDisclosureAsync(client, Parties.Issuer, "asset-locked", CancellationToken.None);

        disclosed.ContractId.Should().Be("asset-locked");
        disclosed.CreatedEventBlob.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task ReadDisclosureAsync_throws_when_the_holding_is_not_active()
    {
        var client = ClientHolding(Holding("asset-other", Parties.Bob, 10m, holdingLock: null));

        var act = () => HoldingQuery.ReadDisclosureAsync(client, Parties.Issuer, "asset-locked", CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("no active holding asset-locked");
    }

    [Fact]
    public async Task ReadDisclosureAsync_throws_when_the_holding_carries_no_disclosure()
    {
        var client = ClientHolding(Holding("asset-locked", Parties.Alice, 42m, TransferLock()));

        var act = () => HoldingQuery.ReadDisclosureAsync(client, Parties.Issuer, "asset-locked", CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("asset-locked without its disclosure");
    }

    [Fact]
    public async Task ReportPendingHoldingAsync_throws_when_alices_holding_is_still_unlocked()
    {
        var client = ClientHolding(Holding("asset-alice", Parties.Alice, 42m, holdingLock: null));

        var act = () => MiniDemoRunner.ReportPendingHoldingAsync(client, Proposal(), "gRPC", CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("Pending-transfer check failed")
            .And.Contain("the gRPC ACS shows 0 locked holding(s) owned by alice (1 holding(s) returned)");
    }

    [Theory]
    [InlineData("issuer", "transfer to carol", 42)]
    [InlineData("alice", "transfer to bob", 42)]
    [InlineData("issuer", "transfer to bob", 41)]
    public void LockedHoldingOrThrow_rejects_a_lock_that_does_not_match_the_proposal(
        string holder, string context, int amount)
    {
        var holding = HoldingSnapshot.From(Holding(
            "asset-locked", Parties.Alice, amount, new HoldingLock([new Party(holder)], ExecuteBefore, null, context)));

        var act = () => MiniDemoRunner.LockedHoldingOrThrow([holding], Proposal(), "PQS");

        act.Should().Throw<DemoVerificationException>()
            .Which.Message.Should().Contain("PQS shows the locked holding asset-locked")
            .And.Contain("expected 42 GOLD locked by issuer until 2026-09-29 11:00:00Z (transfer to bob)");
    }

    [Fact]
    public void LockedHoldingOrThrow_rejects_a_lock_that_expires_at_another_instant()
    {
        var holding = HoldingSnapshot.From(Holding(
            "asset-locked", Parties.Alice, 42m,
            new HoldingLock([Parties.Issuer], ExecuteBefore.AddSeconds(1), null, "transfer to bob")));

        var act = () => MiniDemoRunner.LockedHoldingOrThrow([holding], Proposal(), "PQS");

        act.Should().Throw<DemoVerificationException>().Which.Message.Should().Contain("11:00:01Z");
    }

    [Fact]
    public void HoldingSnapshot_headline_drops_trailing_zeros_and_marks_only_locked_holdings()
    {
        var unlocked = HoldingSnapshot.From(Holding("asset-bob", Parties.Bob, 10.0000000000m, holdingLock: null));

        unlocked.Headline("bob").Should().Be("bob: 10 GOLD");
        unlocked.Locked.Should().BeFalse();
    }

    [Fact]
    public async Task AcceptWithDisclosureAsync_attaches_the_locked_holding_as_a_disclosed_contract_and_acts_as_bob()
    {
        var disclosed = LockedHoldingDisclosure();
        var writer = new RecordingLedgerWriter(FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.One(Transaction(exercised: [AcceptExercised(CompletedResult("asset-bob"))])))
            .Build());

        var receivedCid = await MiniDemoRunner.AcceptWithDisclosureAsync(
            writer, new ContractId<ITransferInstruction>("instruction-1"), disclosed, Parties.Bob, "REST",
            CancellationToken.None);

        receivedCid.Value.Should().Be("asset-bob");
        var submission = writer.Submissions.Should().ContainSingle().Subject;
        submission.Submitter.ActAs.Should().Equal(new Party("bob"));
        submission.Submission.DisclosedContracts.Should().ContainSingle().Which.Should().Be(LockedHoldingDisclosure());
        submission.Submission.WorkflowId.Should().Be(new WorkflowId("mini-demo"));
        var command = submission.Submission.Commands.Should().ContainSingle()
            .Subject.Should().BeOfType<ExerciseCommand>().Subject;
        command.ContractId.Value.Should().Be("instruction-1");
        command.Choice.Should().Be(new ChoiceName("TransferInstruction_Accept"));
        TransferInstruction_Accept.FromRecord((DamlRecord)command.ChoiceArgument).Actors.Should().Equal(new Party("bob"));
    }

    [Fact]
    public async Task AcceptWithDisclosureAsync_throws_when_the_accept_does_not_complete_the_transfer()
    {
        var client = FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.One(Transaction(exercised: [AcceptExercised(FailedResult())])))
            .Build();

        var act = () => MiniDemoRunner.AcceptWithDisclosureAsync(
            client, new ContractId<ITransferInstruction>("instruction-1"), LockedHoldingDisclosure(), Parties.Bob,
            "REST", CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("TransferInstructionResult_Failed");
    }

    [Fact]
    public async Task VerifyTotalSupplyAsync_exercises_TotalSupply_by_key_and_accepts_52()
    {
        var writer = new RecordingLedgerWriter(FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.One(Transaction(exercised: [TotalSupplyExercised(52m)])))
            .Build());

        var act = () => MiniDemoRunner.VerifyTotalSupplyAsync(writer, Gold, Parties.Issuer, "gRPC", CancellationToken.None);

        await act.Should().NotThrowAsync();
        var submission = writer.Submissions.Should().ContainSingle().Subject;
        submission.Submitter.ActAs.Should().Equal(new Party("issuer"));
        var command = submission.Submission.Commands.Should().ContainSingle()
            .Subject.Should().BeOfType<ExerciseByKeyCommand>().Subject;
        command.TemplateId.Should().Be(Instrument.TemplateId);
        command.Choice.Should().Be(new ChoiceName("TotalSupply"));
        Instrument.Key.KeyDecoder(command.ContractKey)._2.Should().Be("GOLD");
    }

    [Fact]
    public async Task VerifyTotalSupplyAsync_throws_when_the_supply_is_not_52()
    {
        var client = FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.One(Transaction(exercised: [TotalSupplyExercised(42m)])))
            .Build();

        var act = () => MiniDemoRunner.VerifyTotalSupplyAsync(client, Gold, Parties.Issuer, "gRPC", CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("returned 42; expected 52");
    }

    [Fact]
    public async Task a_total_supply_mismatch_exits_the_demo_with_65()
    {
        var client = FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.One(Transaction(exercised: [TotalSupplyExercised(42m)])))
            .Build();
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            ct => MiniDemoRunner.VerifyTotalSupplyAsync(client, Gold, Parties.Issuer, "gRPC", ct),
            error,
            CancellationToken.None);

        exitCode.Should().Be(65);
    }

    [Fact]
    public async Task ReadPendingHoldingFromPqsAsync_no_ops_when_there_is_no_pqs_client()
    {
        var output = new StringWriter();

        await MiniDemoRunner.ReadPendingHoldingFromPqsAsync(
            null, PqsConnectionString.DefaultConnectionString, Proposal(), new ContractId<DemoAsset>("asset-locked"),
            output, requirePqs: true, CancellationToken.None);

        output.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task ReadLockedHoldingAsync_returns_the_projected_lock_once_pqs_holds_the_holding_view()
    {
        var output = new StringWriter();

        var projected = await PqsLane.ReadLockedHoldingAsync(
            ProjectedPqs(), PqsConnectionString.DefaultConnectionString, new ContractId<DemoAsset>("asset-locked"),
            output, ShortTimeout, ShortPollInterval, LongProgressAfter, requirePqs: true, CancellationToken.None,
            HoldingsQuery(Holding("asset-locked", Parties.Alice, 42m, TransferLock())));

        projected.Should().NotBeNull();
        projected!.Headline("alice").Should().Be("alice: 42 GOLD 🔒");
        projected.Lock.Should().Be(new LockSnapshot("issuer", ExecuteBefore, "transfer to bob"));
    }

    [Fact]
    public async Task ReadLockedHoldingAsync_throws_when_strict_and_the_holding_view_never_projects()
    {
        var act = () => PqsLane.ReadLockedHoldingAsync(
            ProjectedPqs(), PqsConnectionString.DefaultConnectionString, new ContractId<DemoAsset>("asset-locked"),
            new StringWriter(), ShortTimeout, ShortPollInterval, LongProgressAfter, requirePqs: true,
            CancellationToken.None, HoldingsQuery());

        (await act.Should().ThrowAsync<PqsRequirementNotMetException>())
            .Which.Message.Should().Contain("0 IHolding view(s) of the locked holding asset-locked");
    }

    [Fact]
    public async Task ReadLockedHoldingAsync_reports_the_timeout_against_the_committed_proposal_when_lenient()
    {
        var stub = new StubPqsClient { FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(null) };
        var output = new StringWriter();

        var projected = await PqsLane.ReadLockedHoldingAsync(
            stub, PqsConnectionString.DefaultConnectionString, new ContractId<DemoAsset>("asset-locked"),
            output, ShortTimeout, ShortPollInterval, LongProgressAfter, requirePqs: false, CancellationToken.None);

        projected.Should().BeNull();
        output.ToString().Should().Contain("PQS did not project the Asset contract asset-locked within 0s")
            .And.Contain("The transfer proposal already committed, so the ledger is fine");
    }

    [Fact]
    public async Task ReadPendingHoldingFromPqsAsync_prints_the_locked_holding_read_from_pqs()
    {
        var output = new StringWriter();

        await MiniDemoRunner.ReadPendingHoldingFromPqsAsync(
            ProjectedPqs(), PqsConnectionString.DefaultConnectionString, Proposal(),
            new ContractId<DemoAsset>("asset-locked"), output, requirePqs: true, CancellationToken.None,
            HoldingsQuery(Holding("asset-locked", Parties.Alice, 42m, TransferLock())));

        output.ToString().Should().Contain(
            "  pqs      alice: 42 GOLD 🔒  locked by issuer until 2026-09-29 11:00:00Z (transfer to bob)");
    }

    [Fact]
    public async Task ReadPendingHoldingFromPqsAsync_reports_a_lock_mismatch_as_a_pqs_requirement_failure_when_strict()
    {
        var wrongLock = new HoldingLock([Parties.Issuer], ExecuteBefore, null, "transfer to carol");

        var act = () => MiniDemoRunner.ReadPendingHoldingFromPqsAsync(
            ProjectedPqs(), PqsConnectionString.DefaultConnectionString, Proposal(),
            new ContractId<DemoAsset>("asset-locked"), new StringWriter(), requirePqs: true, CancellationToken.None,
            HoldingsQuery(Holding("asset-locked", Parties.Alice, 42m, wrongLock)));

        (await act.Should().ThrowAsync<PqsRequirementNotMetException>())
            .Which.Message.Should().Contain("PQS pending-holding read failed")
            .And.Contain("transfer to carol");
    }

    [Fact]
    public async Task ReadPendingHoldingFromPqsAsync_reports_an_unavailable_pqs_and_carries_on_when_lenient()
    {
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => throw new SocketException((int)SocketError.ConnectionRefused),
        };
        var output = new StringWriter();

        var act = () => MiniDemoRunner.ReadPendingHoldingFromPqsAsync(
            stub, PqsConnectionString.DefaultConnectionString, Proposal(), new ContractId<DemoAsset>("asset-locked"),
            output, requirePqs: false, CancellationToken.None);

        await act.Should().NotThrowAsync();
        output.ToString().Should().Contain("PQS is not available");
    }

    [Fact]
    public async Task ReadPendingHoldingFromPqsAsync_fails_the_pqs_requirement_when_strict_and_pqs_is_unavailable()
    {
        var stub = new StubPqsClient
        {
            FetchByIdBehavior = _ => throw new SocketException((int)SocketError.ConnectionRefused),
        };

        var act = () => MiniDemoRunner.ReadPendingHoldingFromPqsAsync(
            stub, PqsConnectionString.DefaultConnectionString, Proposal(), new ContractId<DemoAsset>("asset-locked"),
            new StringWriter(), requirePqs: true, CancellationToken.None);

        await act.Should().ThrowAsync<PqsRequirementNotMetException>();
    }

    private static TransferProposal Proposal() =>
        MiniDemoRunner.ProposalFor(Parties, Gold, new ContractId<DemoAsset>("asset-alice"), Now);

    private static HoldingLock TransferLock() =>
        new([new Party("issuer")], ExecuteBefore, ExpiresAfter: null, "transfer to bob");

    private static DisclosedContract LockedHoldingDisclosure() =>
        new("asset-locked", DemoAsset.TemplateId, new byte[] { 1, 2, 3 });

    private static StubPqsClient ProjectedPqs()
    {
        var locked = new Contract<DemoAsset>(
            new ContractId<DemoAsset>("asset-locked"),
            new DemoAsset(Parties.Issuer, Parties.Alice, "GOLD", 42m, TransferLock()));
        return new StubPqsClient { FetchByIdBehavior = _ => Task.FromResult<Contract<DemoAsset>?>(locked) };
    }

    private static PqsLane.HoldingsFilterQuery HoldingsQuery(params InterfaceContract<IHolding, HoldingView>[] holdings) =>
        (_, contractIds, _) => Task.FromResult<IReadOnlyList<InterfaceContract<IHolding, HoldingView>>>(
            holdings.Where(holding => contractIds.Contains(holding.Id.Value)).ToList());

    private static Canton.Ledger.Abstractions.ICantonLedgerClient ClientHolding(
        params InterfaceContract<IHolding, HoldingView>[] holdings) =>
        ClientHoldingEntries(holdings.Select(ActiveHolding).ToArray());

    private static Canton.Ledger.Abstractions.ICantonLedgerClient ClientHoldingEntries(
        params InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Created[] holdings) =>
        FakeLedgerClient.Create()
            .WithActiveInterfaceContracts(holdings
                .Cast<InterfaceAcsSnapshotEntry<IHolding, HoldingView>>()
                .Append(new InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Checkpoint(
                    new StakeholderResume(LedgerOffset.At(2))))
                .ToArray())
            .Build();

    private static InterfaceContract<IHolding, HoldingView> Holding(
        string contractId, Party owner, decimal amount, HoldingLock? holdingLock) =>
        new(
            new ContractId<IHolding>(contractId),
            new HoldingView(
                new Account(owner, Provider: null, Id: ""),
                new InstrumentId(Parties.Issuer, "GOLD"),
                amount,
                holdingLock,
                new Metadata(new Dictionary<string, string>())));

    private static InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Created ActiveHolding(
        InterfaceContract<IHolding, HoldingView> holding) =>
        new InterfaceAcsSnapshotEntry<IHolding, HoldingView>.Created(
            holding.Id,
            holding.View,
            Key: null,
            LedgerOffset.At(1),
            (SynchronizerId)"sync1",
            EquatableArray.Create<Party>([holding.View.Account.Owner ?? Parties.Alice]));

    private static CreatedContract CreatedAsset(string contractId) =>
        new(
            "event-1",
            contractId,
            DemoAsset.TemplateId,
            DamlRecord.Create(),
            WitnessParties: [Parties.Issuer],
            Signatories: [Parties.Issuer],
            Observers: [Parties.Bob],
            ContractKey: null);

    private static TransactionResult Transaction(
        CreatedContract[]? created = null, ExercisedEvent[]? exercised = null) =>
        LedgerResults.Transaction("update-1", LedgerOffset.At(2), EquatableArray.Create(created ?? []), [], new CommandId("command-1"))
            with { ExercisedEvents = EquatableArray.Create(exercised ?? []) };

    private static ExercisedEvent TransferExercised(TransferInstructionResult result) =>
        Exercised("factory-1", AssetTransferFactory.TemplateId, ITransferFactory.InterfaceId, "TransferFactory_Transfer", result.ToRecord());

    private static ExercisedEvent AcceptExercised(TransferInstructionResult result) =>
        Exercised("instruction-1", AssetTransferInstruction.TemplateId, ITransferInstruction.InterfaceId, "TransferInstruction_Accept", result.ToRecord());

    private static ExercisedEvent TotalSupplyExercised(decimal totalSupply) =>
        Exercised("instrument-1", Instrument.TemplateId, null, "TotalSupply", new DamlNumeric(totalSupply, 10));

    private static ExercisedEvent Exercised(
        string contractId, Identifier templateId, Identifier? interfaceId, string choice, DamlValue result) =>
        new(
            contractId,
            templateId,
            interfaceId,
            new ChoiceName(choice),
            DamlRecord.Create(),
            result,
            false,
            EquatableArray.Create<Party>([Parties.Issuer]),
            EquatableArray.Create<Party>([Parties.Issuer]));

    private static TransferInstructionResult PendingResult(string instructionCid) =>
        Result(new TransferInstructionResult_Output.TransferInstructionResult_Pending(
            new TransferInstructionResult_Output_TransferInstructionResult_Pending(
                new ContractId<ITransferInstruction>(instructionCid))));

    private static TransferInstructionResult CompletedResult(string receiverHoldingCid) =>
        Result(new TransferInstructionResult_Output.TransferInstructionResult_Completed(
            new TransferInstructionResult_Output_TransferInstructionResult_Completed(
                [new ContractId<IHolding>(receiverHoldingCid)])));

    private static TransferInstructionResult FailedResult() =>
        Result(new TransferInstructionResult_Output.TransferInstructionResult_Failed());

    private static TransferInstructionResult Result(TransferInstructionResult_Output output) =>
        new(output, [], new Metadata(new Dictionary<string, string>()));
}
