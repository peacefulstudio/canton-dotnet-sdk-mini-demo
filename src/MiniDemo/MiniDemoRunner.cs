// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using MiniDemo.Asset;
using Peaceful.Canton.Localnet.Testing;
using Splice.Api.Token.HoldingV2;
using Splice.Api.Token.MetadataV1;
using Splice.Api.Token.TransferInstructionV2;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo;

internal sealed class MiniDemoRunner
{
    private const int BootstrapSection = 1;
    private const int IssuanceSection = BootstrapSection + 1;
    private const int TransferSection = IssuanceSection + 1;
    private const int CrossTransportSection = TransferSection + 1;
    private const int FailureLaneSection = CrossTransportSection + 1;
    private const int PqsLaneSection = FailureLaneSection + 1;

    internal const string WorkflowId = "mini-demo";
    internal const string DemoInstrument = "GOLD";
    internal const string ShowcaseInstrument = "SILVER";
    internal const decimal AliceMintAmount = 42m;
    internal const decimal BobMintAmount = 10m;
    internal const decimal ExpectedTotalSupply = AliceMintAmount + BobMintAmount;
    internal static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan AcceptanceWindow = TimeSpan.FromHours(1);

    private static readonly Metadata EmptyMetadata = new(new Dictionary<string, string>());
    private static readonly ExtraArgs NoExtraArgs = new(new ChoiceContext(new Dictionary<string, AnyValue>()), EmptyMetadata);

    private readonly LocalnetFixture _fixture;
    private readonly IReadOnlyList<LedgerTransport> _transports;
    private readonly IPqsClient? _pqsClient;
    private readonly string _pqsConnectionString;
    private readonly bool _requirePqs;

    public MiniDemoRunner(
        LocalnetFixture fixture,
        IReadOnlyList<LedgerTransport> transports,
        IPqsClient? pqsClient = null,
        string pqsConnectionString = PqsConnectionString.DefaultConnectionString,
        bool requirePqs = false)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(transports);
        if (transports.Count == 0)
            throw new ArgumentException("At least one ledger transport is required.", nameof(transports));
        _fixture = fixture;
        _transports = transports;
        _pqsClient = pqsClient;
        _pqsConnectionString = pqsConnectionString;
        _requirePqs = requirePqs;
    }

    private LedgerTransport IssuerAndAliceTransport => _transports[0];

    private LedgerTransport BobTransport => _transports[^1];

    public async Task RunAsync(CancellationToken ct)
    {
        var (parties, rightsLease) = await BootstrapAsync(ct);
        await using var lease = rightsLease;

        var instrumentKey = new AssetKey(parties.Issuer.Value, DemoInstrument);
        var issuance = await IssueAsync(parties, instrumentKey, ct);
        var bobHoldings = await TransferTwoStepAsync(parties, instrumentKey, issuance, ct);

        await VerifyEveryTransportSeesEveryAssetAsync(CrossTransportSection, _transports, bobHoldings, parties.Bob, ct);
        await FailureLane.RunAsync(FailureLaneSection, _transports, parties, Console.Out, ct);
        await RunPqsLaneAsync(
            PqsLaneSection, _pqsClient, _pqsConnectionString, bobHoldings, parties.Bob, "bob", Console.Out, _requirePqs, ct);

        Console.WriteLine(
            $"\nDone — one set of generated bindings drove {_transports.Count} transports " +
            $"({string.Join(" and ", _transports.Select(transport => transport.Name))}) through a Token Standard V2 " +
            "two-step transfer against one ledger.");
    }

    private async Task<(DemoParties Parties, UserRightsLease RightsLease)> BootstrapAsync(CancellationToken ct)
    {
        Console.WriteLine($"== {BootstrapSection}. Bootstrap ==");
        var darPath = DarLocator.Resolve();
        Console.WriteLine($"Uploading DAR: {darPath}");
        var uploadOutcome = await _fixture.UploadDarAsync(darPath, ct);
        Console.WriteLine($"DAR upload outcome: {uploadOutcome}");

        var issuer = await _fixture.AllocatePartyAsync("issuer", "Issuer", ct);
        var alice = await _fixture.AllocatePartyAsync("alice", "Alice", ct);
        var bob = await _fixture.AllocatePartyAsync("bob", "Bob", ct);
        Console.WriteLine($"issuer = {issuer.PartyId}");
        Console.WriteLine($"alice  = {alice.PartyId}");
        Console.WriteLine($"bob    = {bob.PartyId}");

        var rightsLease = await _fixture.GrantUserRightsLeaseAsync(
            _fixture.ValidatorUserId,
            actAs: new[] { issuer.PartyId, alice.PartyId, bob.PartyId },
            cancellationToken: ct);
        Console.WriteLine(
            $"Granted act-as (issuer/alice/bob) to ledger user {_fixture.ValidatorUserId} " +
            "(leased — revoked when the run completes or fails)");

        var parties = new DemoParties(
            new Party(issuer.PartyId),
            new Party(alice.PartyId),
            new Party(bob.PartyId));
        return (parties, rightsLease);
    }

    private async Task<Issuance> IssueAsync(DemoParties parties, AssetKey instrumentKey, CancellationToken ct)
    {
        Console.WriteLine($"\n== {IssuanceSection}. Issuance — instruments, a transfer factory and a mint by key ==");
        for (var index = 0; index < _transports.Count; index++)
        {
            var transport = _transports[index];
            var name = index == 0 ? DemoInstrument : ShowcaseInstrument;
            await CreateInstrumentAsync(transport.Client, new Instrument(parties.Issuer, name), transport.Name, transport.ResultStyle, ct);
        }

        var writer = IssuerAndAliceTransport;
        var factoryCid = await CreateTransferFactoryAsync(writer.Client, parties, writer.Name, ct);
        var aliceHoldingCid = await MintByKeyAsync(
            writer.Client, instrumentKey, parties.Issuer, parties.Alice, "alice", AliceMintAmount, writer.Name, ct);
        return new Issuance(factoryCid, aliceHoldingCid);
    }

    private async Task<IReadOnlyList<WrittenAsset>> TransferTwoStepAsync(
        DemoParties parties, AssetKey instrumentKey, Issuance issuance, CancellationToken ct)
    {
        Console.WriteLine($"\n== {TransferSection}. Token Standard V2 two-step transfer — alice -> bob ==");
        var writer = IssuerAndAliceTransport;
        var acceptor = BobTransport;

        var windowStart = await writer.Client.GetLedgerEndAsync(cancellationToken: ct);
        var proposal = ProposalFor(parties, instrumentKey, issuance.AliceHoldingCid, DateTimeOffset.UtcNow);
        var instructionCid = await ProposeAsync(writer.Client, issuance.FactoryCid, proposal, writer.Name, ct);

        var lockedHolding = await ReportPendingHoldingAsync(writer.Client, proposal, writer.Name, ct);
        await ReadPendingHoldingFromPqsAsync(
            _pqsClient, _pqsConnectionString, proposal, new ContractId<DemoAsset>(lockedHolding.ContractId),
            Console.Out, _requirePqs, ct);

        var disclosed = await HoldingQuery.ReadDisclosureAsync(writer.Client, parties.Issuer, lockedHolding.ContractId, ct);
        Console.WriteLine(
            $"  disclose issuer reads the locked holding's typed Disclosure from its {writer.Name} ACS query " +
            $"({disclosed.CreatedEventBlob.Length} bytes) and hands it to bob off-ledger");

        var acceptedCid = await AcceptWithDisclosureAsync(acceptor.Client, instructionCid, disclosed, parties.Bob, acceptor.Name, ct);
        var windowEnd = await writer.Client.GetLedgerEndAsync(cancellationToken: ct);
        await ObserveTransferOnEveryTransportAsync(
            _transports, parties.Bob, acceptedCid, windowStart, windowEnd, UpdateStreamObserver.DefaultTimeout, Console.Out, ct);
        var mintedCid = await MintByKeyAsync(
            writer.Client, instrumentKey, parties.Issuer, parties.Bob, "bob", BobMintAmount, writer.Name, ct);
        await VerifyTotalSupplyAsync(writer.Client, instrumentKey, parties.Issuer, writer.Name, ct);

        return
        [
            new WrittenAsset(acceptor, acceptedCid, instrumentKey),
            new WrittenAsset(writer, mintedCid, instrumentKey),
        ];
    }

    internal static async Task ObserveTransferOnEveryTransportAsync(
        IReadOnlyList<LedgerTransport> transports,
        Party observer,
        ContractId<DemoAsset> acceptedCid,
        LedgerOffset windowStart,
        LedgerOffset windowEnd,
        TimeSpan timeout,
        TextWriter output,
        CancellationToken ct)
    {
        foreach (var transport in transports)
            await UpdateStreamObserver.ObserveCreatedAsync(
                transport.Client, transport.Name, observer, acceptedCid, windowStart, windowEnd, timeout, output, ct);
    }

    internal static async Task<ContractId<Instrument>> CreateInstrumentAsync(
        ILedgerWriter ledgerClient, Instrument instrument, string transportName, ResultStyle style, CancellationToken ct)
    {
        var createdCid = style switch
        {
            ResultStyle.OutcomePatternMatch => MatchCreated(await ledgerClient.TryCreateAsync(instrument, cancellationToken: ct)),
            ResultStyle.OrThrow => await ledgerClient
                .TryCreateAsync(instrument, cancellationToken: ct)
                .OneOrThrowAsync(nameof(CreateInstrumentAsync)),
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown result style."),
        };
        Console.WriteLine($"  create   Instrument(issuer, {instrument.Name}) over {transportName} -> {createdCid.Value}");
        Console.WriteLine($"             ({style.Describe()})");
        return createdCid;
    }

    private static ContractId<Instrument> MatchCreated(ExerciseOutcome<ContractId<Instrument>> outcome) => outcome switch
    {
        ExerciseOutcome<ContractId<Instrument>>.One created => created.Result,
        _ => outcome.Unwrap(nameof(CreateInstrumentAsync)),
    };

    internal static async Task<ContractId<ITransferFactory>> CreateTransferFactoryAsync(
        ILedgerWriter ledgerClient, DemoParties parties, string transportName, CancellationToken ct)
    {
        var factory = new AssetTransferFactory(parties.Issuer, [parties.Alice, parties.Bob]);
        var factoryCid = (await ledgerClient.TryCreateAsync(factory, cancellationToken: ct))
            .Unwrap(nameof(CreateTransferFactoryAsync));
        Console.WriteLine(
            $"  create   AssetTransferFactory(issuer, users = alice, bob) over {transportName} -> {factoryCid.Value}");
        return new ContractId<ITransferFactory>(factoryCid.Value);
    }

    internal static async Task<ContractId<DemoAsset>> MintByKeyAsync(
        ILedgerWriter ledgerClient,
        AssetKey instrumentKey,
        Party issuer,
        Party owner,
        string ownerLabel,
        decimal amount,
        string transportName,
        CancellationToken ct)
    {
        var outcome = await ledgerClient.TrySubmitSingleAsync(
            Instrument.MintByKeyCommand(instrumentKey.ToDaml(), new Instrument.Mint(owner, amount)),
            new SubmitterInfo(issuer),
            workflowId: WorkflowId,
            cancellationToken: ct);
        var transaction = outcome.Unwrap(nameof(MintByKeyAsync));
        var minted = transaction.Single<DemoAsset>();
        Console.WriteLine(
            $"  mint     {AmountFormat.Display(amount)} {instrumentKey.Name} to {ownerLabel}, by key {instrumentKey} " +
            $"over {transportName} -> {minted.Value}");
        return minted;
    }

    internal static TransferProposal ProposalFor(
        DemoParties parties, AssetKey instrumentKey, ContractId<DemoAsset> aliceHoldingCid, DateTimeOffset now)
    {
        var wholeSecondNow = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
        var transfer = new Transfer(
            Sender: OwnerAccount(parties.Alice),
            Receiver: OwnerAccount(parties.Bob),
            Amount: AliceMintAmount,
            InstrumentId: new InstrumentId(parties.Issuer, instrumentKey.Name),
            RequestedAt: wholeSecondNow - ClockSkewAllowance,
            ExecuteBefore: wholeSecondNow + AcceptanceWindow,
            InputHoldingCids: [new ContractId<IHolding>(aliceHoldingCid.Value)],
            Meta: EmptyMetadata);
        return new TransferProposal(parties, transfer);
    }

    private static Account OwnerAccount(Party owner) => new(owner, Provider: null, Id: "");

    internal static async Task<ContractId<ITransferInstruction>> ProposeAsync(
        ILedgerWriter ledgerClient,
        ContractId<ITransferFactory> factoryCid,
        TransferProposal proposal,
        string transportName,
        CancellationToken ct)
    {
        var outcome = await factoryCid.TryTransferFactory_TransferAsync(
            ledgerClient,
            new TransferFactory_Transfer(proposal.Transfer, [proposal.Parties.Alice], NoExtraArgs),
            new SubmitterInfo(proposal.Parties.Alice),
            workflowId: WorkflowId,
            cancellationToken: ct);
        var result = outcome.Unwrap(nameof(ProposeAsync));
        if (result.Output is not TransferInstructionResult_Output.TransferInstructionResult_Pending pending)
            throw new DemoVerificationException(
                $"TransferFactory_Transfer returned {result.Output.GetType().Name}; a two-step transfer must " +
                "leave a pending transfer instruction for the receiver to accept.");

        var instructionCid = pending.Value.TransferInstructionCid;
        Console.WriteLine(
            $"  propose  alice -> bob, {AmountFormat.Display(proposal.Transfer.Amount)} {proposal.Transfer.InstrumentId.Id} " +
            $"via TransferFactory_Transfer over {transportName} -> pending {instructionCid.Value}");
        Console.WriteLine(
            $"             (requestedAt {Utc(proposal.Transfer.RequestedAt)} <= now < executeBefore " +
            $"{Utc(proposal.Transfer.ExecuteBefore)})");
        return instructionCid;
    }

    internal static async Task<HoldingSnapshot> ReportPendingHoldingAsync(
        ICantonLedgerClient ledgerClient, TransferProposal proposal, string transportName, CancellationToken ct)
    {
        var holdings = await HoldingQuery.QueryForPartyAsync(ledgerClient, proposal.Parties.Alice, ct);
        var locked = LockedHoldingOrThrow(holdings, proposal, $"the {transportName} ACS");
        Console.WriteLine($"  acs      {locked.Headline("alice")}  {locked.Lock!.Describe()}");
        Console.WriteLine($"             ({locked.ContractId}, read as an IHolding view over {transportName})");
        return locked;
    }

    internal static HoldingSnapshot LockedHoldingOrThrow(
        IReadOnlyList<HoldingSnapshot> holdings, TransferProposal proposal, string source)
    {
        var alice = proposal.Parties.Alice.Value;
        var locked = holdings.Where(holding => holding.Owner == alice && holding.Locked).ToList();
        if (locked.Count != 1)
            throw new DemoVerificationException(
                $"Pending-transfer check failed: {source} shows {locked.Count} locked holding(s) owned by alice " +
                $"({holdings.Count} holding(s) returned); the proposal locks exactly one.");

        var holding = locked[0];
        var expected = new LockSnapshot(
            proposal.Parties.Issuer.Value, proposal.Transfer.ExecuteBefore, $"transfer to {proposal.Parties.Bob.Value}");
        if (holding.Amount != proposal.Transfer.Amount
            || holding.InstrumentId != proposal.Transfer.InstrumentId.Id
            || holding.Admin != proposal.Parties.Issuer.Value
            || holding.Lock != expected)
            throw new DemoVerificationException(
                $"Pending-transfer check failed: {source} shows the locked holding {holding.ContractId} as " +
                $"({holding.Describe()}); expected {AmountFormat.Display(proposal.Transfer.Amount)} " +
                $"{proposal.Transfer.InstrumentId.Id} {expected.Describe()}.");
        return holding;
    }

    internal static async Task ReadPendingHoldingFromPqsAsync(
        IPqsClient? pqsClient,
        string connectionString,
        TransferProposal proposal,
        ContractId<DemoAsset> lockedHoldingCid,
        TextWriter output,
        bool requirePqs,
        CancellationToken ct,
        PqsLane.HoldingsFilterQuery? holdingsFilterQuery = null)
    {
        if (pqsClient is null)
            return;

        await GuardPqsAsync("PQS pending-holding read", PqsRequirementStage.PendingHoldingRead, output, requirePqs, async () =>
        {
            var projected = await PqsLane.ReadLockedHoldingAsync(
                pqsClient,
                connectionString,
                lockedHoldingCid,
                output,
                PqsLane.DefaultProjectionTimeout,
                PqsLane.DefaultPollInterval,
                PqsLane.DefaultProgressAfter,
                requirePqs,
                ct,
                holdingsFilterQuery);
            if (projected is null)
                return;

            var locked = LockedHoldingOrThrow([projected], proposal, "PQS");
            output.WriteLine($"  pqs      {locked.Headline("alice")}  {locked.Lock!.Describe()}");
            output.WriteLine("             (the same pending holding, read from the Postgres read model)");
        });
    }

    internal static async Task<ContractId<DemoAsset>> AcceptWithDisclosureAsync(
        ILedgerWriter ledgerClient,
        ContractId<ITransferInstruction> instructionCid,
        DisclosedContract lockedHolding,
        Party bob,
        string transportName,
        CancellationToken ct)
    {
        var result = await instructionCid
            .TryTransferInstruction_AcceptAsync(
                ledgerClient,
                new TransferInstruction_Accept([bob], NoExtraArgs),
                new SubmitterInfo(bob),
                workflowId: WorkflowId,
                configure: submission => submission.WithDisclosedContracts(lockedHolding),
                cancellationToken: ct)
            .OneOrThrowAsync(nameof(AcceptWithDisclosureAsync));
        if (result.Output is not TransferInstructionResult_Output.TransferInstructionResult_Completed completed
            || completed.Value.ReceiverHoldingCids.Count != 1)
            throw new DemoVerificationException(
                $"TransferInstruction_Accept returned {result.Output.GetType().Name}; " +
                "accepting must complete the transfer into exactly one holding for bob.");

        var receivedCid = new ContractId<DemoAsset>(completed.Value.ReceiverHoldingCids[0].Value);
        Console.WriteLine(
            $"  accept   bob accepts over {transportName}, the locked holding attached as a disclosed contract " +
            $"-> completed, bob holds {receivedCid.Value}");
        return receivedCid;
    }

    internal static async Task VerifyTotalSupplyAsync(
        ILedgerWriter ledgerClient, AssetKey instrumentKey, Party issuer, string transportName, CancellationToken ct)
    {
        var outcome = await ledgerClient.TrySubmitSingleAsync(
            Instrument.TotalSupplyByKeyCommand(instrumentKey.ToDaml(), new Instrument.TotalSupply()),
            new SubmitterInfo(issuer),
            workflowId: WorkflowId,
            cancellationToken: ct);
        var transaction = outcome.Unwrap(nameof(VerifyTotalSupplyAsync));
        var totalSupply = transaction.ExerciseResult<decimal>(Instrument.ChoiceTotalSupply.Name);
        Console.WriteLine(
            $"  supply   TotalSupply by key {instrumentKey} over {transportName} = {AmountFormat.Display(totalSupply)} " +
            $"({AmountFormat.Display(AliceMintAmount)} accepted by bob + {AmountFormat.Display(BobMintAmount)} minted to bob)");
        if (totalSupply != ExpectedTotalSupply)
            throw new DemoVerificationException(
                $"Total-supply check failed: TotalSupply by key {instrumentKey} returned " +
                $"{AmountFormat.Display(totalSupply)}; expected {AmountFormat.Display(ExpectedTotalSupply)}.");
    }

    internal static async Task RunPqsLaneAsync(
        int section,
        IPqsClient? pqsClient,
        string connectionString,
        IReadOnlyList<WrittenAsset> writtenAssets,
        Party owner,
        string ownerLabel,
        TextWriter output,
        bool requirePqs,
        CancellationToken ct,
        PqsLane.HoldingsFilterQuery? holdingsFilterQuery = null)
    {
        if (pqsClient is null)
            return;

        await GuardPqsAsync("PQS lane", PqsRequirementStage.ProjectionReport, output, requirePqs, () => PqsLane.RunAsync(
            section,
            pqsClient,
            connectionString,
            writtenAssets,
            owner,
            ownerLabel,
            output,
            PqsLane.DefaultProjectionTimeout,
            PqsLane.DefaultPollInterval,
            PqsLane.DefaultProgressAfter,
            requirePqs,
            ct,
            holdingsFilterQuery));
    }

    private static async Task GuardPqsAsync(string step, PqsRequirementStage stage, TextWriter output, bool requirePqs, Func<Task> read)
    {
        try
        {
            await read();
        }
        catch (PqsRequirementNotMetException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (requirePqs)
                throw new PqsRequirementNotMetException($"{step} failed ({ex.Message}).", ex, stage);

            output.WriteLine($"  {step} failed ({ex.Message}); the ledger sections above already passed.");
        }
    }

    internal static async Task VerifyEveryTransportSeesEveryAssetAsync(
        int section,
        IReadOnlyList<LedgerTransport> transports,
        IReadOnlyList<WrittenAsset> writtenAssets,
        Party owner,
        CancellationToken ct)
    {
        Console.WriteLine($"\n== {section}. Cross-transport verification ==");
        var firstAssetSeenBy = new Dictionary<ContractId<DemoAsset>, (AssetSnapshot Snapshot, LedgerTransport Reader)>();
        var firstHoldingSeenBy = new Dictionary<string, (HoldingSnapshot Snapshot, LedgerTransport Reader)>(StringComparer.Ordinal);

        foreach (var reader in transports)
        {
            var assets = IndexByContractId(
                await AssetAcsQuery.QueryForPartyAsync(reader.Client, owner, ct),
                asset => asset.ContractId, EqualityComparer<ContractId<DemoAsset>>.Default, reader, "ACS snapshot");
            var holdings = IndexByContractId(
                await HoldingQuery.QueryForPartyAsync(reader.Client, owner, ct),
                holding => holding.ContractId, StringComparer.Ordinal, reader, "IHolding view query");

            foreach (var written in writtenAssets)
            {
                var asset = VisibleOrThrow(assets, written.ContractId, written, reader, "active contract set");
                if (asset.Key != written.Key)
                    throw new DemoVerificationException(
                        $"Cross-transport check failed: the Asset {written.ContractId} carries key {asset.Key} " +
                        $"over {reader.Name} but was written over {written.Transport.Name} under key {written.Key}.");
                AgreeOrThrow(firstAssetSeenBy, written.ContractId, asset, reader, "Asset", snapshot => snapshot.Describe());

                var holding = VisibleOrThrow(holdings, written.ContractId.Value, written, reader, "IHolding views");
                AgreeOrThrow(firstHoldingSeenBy, written.ContractId.Value, holding, reader, "IHolding view", snapshot => snapshot.Describe());

                Console.WriteLine(
                    $"  {reader.Name,-4} reads back the Asset written over {written.Transport.Name,-4} " +
                    $"({written.ContractId}): same key {asset.Key}, same IHolding view " +
                    $"{AmountFormat.Display(holding.Amount)} {holding.InstrumentId}");
            }
        }
    }

    private static string Utc(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("u", System.Globalization.CultureInfo.InvariantCulture);

    private static Dictionary<TId, T> IndexByContractId<T, TId>(
        IReadOnlyList<T> snapshots,
        Func<T, TId> contractId,
        IEqualityComparer<TId> comparer,
        LedgerTransport reader,
        string source)
        where TId : notnull
    {
        var index = new Dictionary<TId, T>(comparer);
        foreach (var snapshot in snapshots)
            if (!index.TryAdd(contractId(snapshot), snapshot))
                throw new DemoVerificationException(
                    $"Cross-transport check failed: {reader.Name} returned the Asset {contractId(snapshot)} " +
                    $"twice in one {source}.");
        return index;
    }

    private static T VisibleOrThrow<T, TId>(
        Dictionary<TId, T> visible, TId contractId, WrittenAsset written, LedgerTransport reader, string source)
        where TId : notnull
    {
        if (visible.TryGetValue(contractId, out var snapshot))
            return snapshot;
        throw new DemoVerificationException(
            $"Cross-transport check failed: the Asset {written.ContractId} written over " +
            $"{written.Transport.Name} is not visible over {reader.Name}; these transports " +
            $"disagree about the {source} ({visible.Count} contract(s) visible).");
    }

    private static void AgreeOrThrow<T, TId>(
        Dictionary<TId, (T Snapshot, LedgerTransport Reader)> firstSeenBy,
        TId contractId,
        T snapshot,
        LedgerTransport reader,
        string what,
        Func<T, string> describe)
        where T : IEquatable<T>
        where TId : notnull
    {
        if (!firstSeenBy.TryGetValue(contractId, out var seen))
        {
            firstSeenBy[contractId] = (snapshot, reader);
            return;
        }

        if (!snapshot.Equals(seen.Snapshot))
            throw new DemoVerificationException(
                $"Cross-transport check failed: the {what} {contractId} reads ({describe(snapshot)}) over " +
                $"{reader.Name} but ({describe(seen.Snapshot)}) over {seen.Reader.Name}; these transports " +
                $"disagree about the {what}.");
    }

    private sealed record Issuance(ContractId<ITransferFactory> FactoryCid, ContractId<DemoAsset> AliceHoldingCid);
}

internal sealed record DemoParties(Party Issuer, Party Alice, Party Bob);

internal sealed record TransferProposal(DemoParties Parties, Transfer Transfer);
