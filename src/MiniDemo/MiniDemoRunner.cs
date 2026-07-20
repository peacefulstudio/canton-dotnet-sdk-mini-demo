// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Kernel.Authentication;
using Canton.Mini.Demo;
using Daml.Ledger.Abstractions.Extensions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Peaceful.Canton.Localnet.Testing;
using DemoAsset = Canton.Mini.Demo.Asset;

namespace MiniDemo;

internal sealed class MiniDemoRunner
{
    private readonly LocalnetFixture _fixture;
    private readonly string _grpcAddress;

    public MiniDemoRunner(LocalnetFixture fixture, string grpcAddress)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentException.ThrowIfNullOrWhiteSpace(grpcAddress);
        _fixture = fixture;
        _grpcAddress = grpcAddress;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var parties = await BootstrapAsync(ct);

        Console.WriteLine("\n== 2. Connect gRPC SDK ==");
        Console.WriteLine($"Ledger API gRPC endpoint: {_grpcAddress}");
        var token = await _fixture.TokenProvider.GetAccessTokenAsync(ct);
        ITokenProvider tokenProvider = new StaticTokenProvider(token);
        var options = new LedgerClientOptions { GrpcAddress = _grpcAddress };
        using var ledgerClient = new LedgerClient(options, tokenProvider);

        var createdCid = await CreateAssetAsync(ledgerClient, parties, ct);
        var transferredCid = await TransferAsync(ledgerClient, createdCid, parties, ct);
        await ReportActiveAssetsAsync(ledgerClient, transferredCid, parties, ct);

        Console.WriteLine("\nDone — create -> transfer -> ACS query round-trip complete.");
    }

    private async Task<DemoParties> BootstrapAsync(CancellationToken ct)
    {
        Console.WriteLine("== 1. Bootstrap ==");
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

        await _fixture.GrantUserRightsAsync(
            _fixture.ValidatorUserId,
            actAs: new[] { issuer.PartyId, alice.PartyId, bob.PartyId },
            cancellationToken: ct);
        Console.WriteLine($"Granted act-as (issuer/alice/bob) to ledger user {_fixture.ValidatorUserId}");

        return new DemoParties(
            new Party(issuer.PartyId),
            new Party(alice.PartyId),
            new Party(bob.PartyId));
    }

    private static async Task<ContractId<DemoAsset>> CreateAssetAsync(
        LedgerClient ledgerClient, DemoParties parties, CancellationToken ct)
    {
        Console.WriteLine("\n== 3. Create Asset (issuer signs, owner = alice) ==");
        var asset = new DemoAsset(Issuer: parties.Issuer, Owner: parties.Alice, Name: "GOLD", Amount: 42m);
        var outcome = await ledgerClient.CreateAsync(
            asset,
            submitter: new SubmitterInfo(parties.Issuer, new HashSet<Party>()),
            cancellationToken: ct);
        var createdCid = outcome.Unwrap(nameof(CreateAssetAsync));
        Console.WriteLine($"Created Asset contract id: {createdCid.Value}");
        return createdCid;
    }

    private static async Task<string> TransferAsync(
        LedgerClient ledgerClient, ContractId<DemoAsset> createdCid, DemoParties parties, CancellationToken ct)
    {
        Console.WriteLine("\n== 4. Exercise Transfer (alice -> bob) ==");
        var command = new ExerciseCommand(
            DemoAsset.TemplateId,
            createdCid,
            DemoAsset.ChoiceTransfer.Name,
            new DemoAsset.Transfer(NewOwner: parties.Bob).ToRecord());
        var outcome = await ledgerClient.TryCreateOneByExerciseAsync<DemoAsset>(
            command,
            submitter: new SubmitterInfo(parties.Alice, new HashSet<Party> { parties.Bob }),
            workflowId: "mini-demo",
            cancellationToken: ct);
        var transferredCid = outcome.Unwrap(nameof(TransferAsync));
        Console.WriteLine($"Transferred. New Asset contract id: {transferredCid.Value}");
        return transferredCid.Value;
    }

    private static async Task ReportActiveAssetsAsync(
        LedgerClient ledgerClient,
        string transferredCid,
        DemoParties parties,
        CancellationToken ct)
    {
        Console.WriteLine("\n== 5. Query ACS via LedgerClient ==");
        var assets = await AssetAcsQuery.QueryForPartyAsync(ledgerClient, parties.Bob, ct);

        Console.WriteLine($"Active Asset contracts visible to bob ({assets.Count}):");
        foreach (var asset in assets)
        {
            var marker = asset.Owner == parties.Bob.Id ? "  <- owner is now bob" : string.Empty;
            Console.WriteLine($"  {asset.ContractId}: name={asset.Name} amount={asset.Amount} owner={asset.Owner}{marker}");
        }

        var landed = assets.Any(asset => asset.ContractId == transferredCid && asset.Owner == parties.Bob.Id);
        if (!landed)
            throw new InvalidOperationException(
                $"ACS round-trip check failed: transferred Asset {transferredCid} owned by bob ({parties.Bob.Id}) " +
                $"was not found in the active contract set ({assets.Count} contract(s) returned).");
    }
}

internal sealed record DemoParties(Party Issuer, Party Alice, Party Bob);
