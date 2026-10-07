// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Splice.Api.Token.HoldingV2;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo;

internal static class PqsLane
{
    public static readonly TimeSpan DefaultProjectionTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DefaultProgressAfter = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Looks up the IHolding views of exactly <paramref name="contractIds"/>. The production
    /// implementation (<see cref="NpgsqlHoldingsQuery.RunAsync"/>) pushes that filter into the SQL
    /// query itself, so the cost never grows with unrelated holdings left behind by other runs on
    /// a shared, resident PQS.
    /// </summary>
    internal delegate Task<IReadOnlyList<InterfaceContract<IHolding, HoldingView>>> HoldingsFilterQuery(
        string connectionString, IReadOnlyCollection<string> contractIds, CancellationToken cancellationToken);

    private const string DatabasePrefix = "pqs-";

    internal static async Task RunAsync(
        int section,
        IPqsClient pqsClient,
        string connectionString,
        IReadOnlyList<WrittenAsset> writtenAssets,
        Party owner,
        string ownerLabel,
        TextWriter output,
        TimeSpan projectionTimeout,
        TimeSpan pollInterval,
        TimeSpan progressAfter,
        bool requirePqs,
        CancellationToken ct,
        HoldingsFilterQuery? holdingsFilterQuery = null)
    {
        var queryHoldings = holdingsFilterQuery ?? NpgsqlHoldingsQuery.RunAsync;
        output.WriteLine($"\n== {section}. PQS read model ==");

        if (writtenAssets.Count == 0)
        {
            output.WriteLine("  No Asset contracts were written, so there is nothing for PQS to project.");
            return;
        }

        var budget = Stopwatch.StartNew();
        var probeId = writtenAssets[0].ContractId;
        var wait = await WaitForFirstProjectionAsync(
            pqsClient, probeId, output, budget, projectionTimeout, pollInterval, progressAfter, ct);

        if (wait is PqsWaitResult.Unavailable unavailable)
        {
            WriteUnavailableHint(unavailable.Exception, requirePqs, connectionString, output);
            if (requirePqs)
                throw PqsRequirementNotMetException.Unavailable(unavailable.Exception);
            return;
        }

        if (wait is PqsWaitResult.TimedOut)
        {
            WriteTimeoutHint(probeId, projectionTimeout, connectionString, $"Sections 1-{section - 1} already passed", output);
            if (requirePqs)
                throw new PqsRequirementNotMetException(
                    $"PQS did not project any Asset contract within {projectionTimeout.TotalSeconds:0}s.");
            return;
        }

        var expected = writtenAssets.Select(written => written.ContractId.Value).ToHashSet(StringComparer.Ordinal);

        if (!requirePqs)
        {
            await ProjectAndReportAsync(pqsClient, queryHoldings, connectionString, expected, owner, ownerLabel, output, ct);
            return;
        }

        var snapshot = await PollUntilFullyProjectedAsync(
            pqsClient, queryHoldings, connectionString, expected, owner, budget, projectionTimeout, pollInterval, ct);
        WriteAssetProjectionReport(snapshot.Owned, snapshot.MatchedAssetCount, expected.Count, ownerLabel, output);
        WriteHoldingProjectionReport(snapshot.ViewedHoldings, expected.Count, output);

        if (!snapshot.IsComplete(expected.Count))
            throw new PqsRequirementNotMetException(
                $"PQS projected {snapshot.MatchedAssetCount} of {expected.Count} Asset contract(s) and " +
                $"{snapshot.ViewedHoldings.Count} of {expected.Count} IHolding view(s) after " +
                $"{budget.Elapsed.TotalSeconds:0}s of a {projectionTimeout.TotalSeconds:0}s budget.");
    }

    internal static async Task<HoldingSnapshot?> ReadLockedHoldingAsync(
        IPqsClient pqsClient,
        string connectionString,
        ContractId<DemoAsset> lockedHoldingCid,
        TextWriter output,
        TimeSpan projectionTimeout,
        TimeSpan pollInterval,
        TimeSpan progressAfter,
        bool requirePqs,
        CancellationToken ct,
        HoldingsFilterQuery? holdingsFilterQuery = null)
    {
        var queryHoldings = holdingsFilterQuery ?? NpgsqlHoldingsQuery.RunAsync;
        var budget = Stopwatch.StartNew();
        var wait = await WaitForFirstProjectionAsync(
            pqsClient, lockedHoldingCid, output, budget, projectionTimeout, pollInterval, progressAfter, ct);

        if (wait is PqsWaitResult.Unavailable unavailable)
        {
            WriteUnavailableHint(unavailable.Exception, requirePqs, connectionString, output);
            if (requirePqs)
                throw PqsRequirementNotMetException.Unavailable(unavailable.Exception, PqsRequirementStage.PendingHoldingRead);
            return null;
        }

        if (wait is PqsWaitResult.TimedOut)
        {
            WriteTimeoutHint(
                lockedHoldingCid, projectionTimeout, connectionString, "The transfer proposal already committed", output);
            if (requirePqs)
                throw new PqsRequirementNotMetException(
                    $"PQS did not project the locked holding {lockedHoldingCid.Value} within {projectionTimeout.TotalSeconds:0}s.",
                    PqsRequirementStage.PendingHoldingRead);
            return null;
        }

        var lockedHoldingId = new[] { lockedHoldingCid.Value };
        while (true)
        {
            var viewed = await queryHoldings(connectionString, lockedHoldingId, ct);
            if (viewed.Count == 1)
                return HoldingSnapshot.From(viewed[0]);

            if (budget.Elapsed >= projectionTimeout)
            {
                output.WriteLine(
                    $"  PQS projected the locked holding {lockedHoldingCid.Value} but not yet its IHolding view.");
                if (requirePqs)
                    throw new PqsRequirementNotMetException(
                        $"PQS returned {viewed.Count} IHolding view(s) of the locked holding {lockedHoldingCid.Value} " +
                        $"after {budget.Elapsed.TotalSeconds:0}s of a {projectionTimeout.TotalSeconds:0}s budget.",
                        PqsRequirementStage.PendingHoldingRead);
                return null;
            }

            await Task.Delay(pollInterval, ct);
        }
    }

    private static async Task<PqsProjectionSnapshot> ProjectAsync(
        IPqsClient pqsClient,
        HoldingsFilterQuery queryHoldings,
        string connectionString,
        HashSet<string> expected,
        Party owner,
        CancellationToken ct)
    {
        var owned = await QueryOwnedAssetsAsync(pqsClient, expected, owner, ct);
        var matched = owned.Count(contract => expected.Contains(contract.Id.Value));
        var viewed = await queryHoldings(connectionString, expected, ct);

        return new PqsProjectionSnapshot(owned, matched, viewed);
    }

    private static async Task ProjectAndReportAsync(
        IPqsClient pqsClient,
        HoldingsFilterQuery queryHoldings,
        string connectionString,
        HashSet<string> expected,
        Party owner,
        string ownerLabel,
        TextWriter output,
        CancellationToken ct)
    {
        var owned = await QueryOwnedAssetsAsync(pqsClient, expected, owner, ct);
        var matched = owned.Count(contract => expected.Contains(contract.Id.Value));
        WriteAssetProjectionReport(owned, matched, expected.Count, ownerLabel, output);

        var viewed = await queryHoldings(connectionString, expected, ct);
        WriteHoldingProjectionReport(viewed, expected.Count, output);
    }

    private static async Task<IReadOnlyList<Contract<DemoAsset>>> QueryOwnedAssetsAsync(
        IPqsClient pqsClient, HashSet<string> expected, Party owner, CancellationToken ct)
    {
        var limitRevealingOneContractBeyondExpected = expected.Count + 1;
        return await pqsClient.QueryAsync<DemoAsset>(
            Filter.Field<DemoAsset>(asset => asset.Owner, owner.Value),
            new PqsPage(limit: limitRevealingOneContractBeyondExpected),
            ct);
    }

    private static async Task<PqsProjectionSnapshot> PollUntilFullyProjectedAsync(
        IPqsClient pqsClient,
        HoldingsFilterQuery queryHoldings,
        string connectionString,
        HashSet<string> expected,
        Party owner,
        Stopwatch budget,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken ct)
    {
        while (true)
        {
            var snapshot = await ProjectAsync(pqsClient, queryHoldings, connectionString, expected, owner, ct);
            if (snapshot.IsComplete(expected.Count) || budget.Elapsed >= timeout)
                return snapshot;

            await Task.Delay(pollInterval, ct);
        }
    }

    private static void WriteAssetProjectionReport(
        IReadOnlyList<Contract<DemoAsset>> owned, int matchedCount, int expectedCount, string ownerLabel, TextWriter output)
    {
        output.WriteLine(
            $"  PQS projected {matchedCount} of {expectedCount} Asset contract(s) owned by {ownerLabel} " +
            "(WHERE owner = ? and LIMIT pushed into Postgres — the participant is never queried):");
        foreach (var contract in owned.OrderByDescending(contract => contract.Data.Amount).ThenBy(contract => contract.Id.Value, StringComparer.Ordinal))
            output.WriteLine($"             {contract.Id.Value} name={contract.Data.Name} amount={contract.Data.Amount}");

        if (matchedCount < expectedCount)
            output.WriteLine("  PQS is still catching up on the rest of the run; scribe projects asynchronously.");
    }

    private static void WriteHoldingProjectionReport(
        IReadOnlyList<InterfaceContract<IHolding, HoldingView>> viewed, int expectedCount, TextWriter output)
    {
        output.WriteLine(
            $"  PQS projected {viewed.Count} of {expectedCount} of them as IHolding views " +
            "(the interface view, decoded without naming the Asset template):");
        foreach (var holding in viewed.OrderByDescending(holding => holding.View.Amount).ThenBy(holding => holding.Id.Value, StringComparer.Ordinal))
            output.WriteLine($"             {holding.Id.Value} {HoldingSnapshot.From(holding).Describe()}");
    }

    internal static async Task<PqsWaitResult> WaitForFirstProjectionAsync(
        IPqsClient pqsClient,
        ContractId<DemoAsset> contractId,
        TextWriter output,
        Stopwatch budget,
        TimeSpan timeout,
        TimeSpan pollInterval,
        TimeSpan progressAfter,
        CancellationToken ct)
    {
        var progressWritten = false;
        while (true)
        {
            Contract<DemoAsset>? found;
            try
            {
                found = await pqsClient.FetchByIdAsync(contractId, ct);
            }
            catch (Exception ex) when (PqsAvailability.IsPqsUnavailable(ex))
            {
                return new PqsWaitResult.Unavailable(ex);
            }

            if (found is not null)
                return new PqsWaitResult.Found();

            if (budget.Elapsed >= timeout)
                return new PqsWaitResult.TimedOut();

            if (!progressWritten && budget.Elapsed >= progressAfter)
            {
                output.WriteLine($"  Waiting for PQS to project Asset {contractId.Value} (scribe indexes asynchronously)...");
                progressWritten = true;
            }

            await Task.Delay(pollInterval, ct);
        }
    }

    private static void WriteUnavailableHint(Exception exception, bool requirePqs, string connectionString, TextWriter output)
    {
        var consequence = requirePqs ? "--require-pqs is set, so the run fails." : "skipping.";
        output.WriteLine($"  PQS is not available ({PqsAvailability.DescribeCause(exception)}); {consequence} {HowToEnable(connectionString)}");
    }

    private static string SlotOf(string database) =>
        database.StartsWith(DatabasePrefix, StringComparison.Ordinal) ? database[DatabasePrefix.Length..] : database;

    private static string HowToEnable(string connectionString)
    {
        var database = PqsConnectionString.ContainerName(connectionString);
        return database == PqsConnectionString.ContainerName(PqsConnectionString.DefaultConnectionString)
            ? "Run `make up PQS=true` to enable it."
            : $"`make up PQS=true` does not start PQS for {SlotOf(database)}; only a-validator-1 gets a read model from it.";
    }

    private static void WriteTimeoutHint(
        ContractId<DemoAsset> contractId, TimeSpan timeout, string connectionString, string ledgerProgress, TextWriter output) =>
        output.WriteLine(
            $"  PQS did not project the Asset contract {contractId.Value} within {timeout.TotalSeconds:0}s.\n" +
            $"  {ledgerProgress}, so the ledger is fine — this is the read model lagging or stopped.\n\n" +
            $"    docker logs --tail 50 {PqsConnectionString.ContainerName(connectionString)} | grep -i \"unknown Daml package\"\n" +
            $"    psql \"{PqsConnectionString.ToPsqlConnInfo(connectionString)}\" -c \"select * from __watermark\"\n\n" +
            "  If you see \"unknown Daml package detected\", scribe is restarting to discover a newly\n" +
            "  uploaded package and will recover on its own — re-run in a minute. Otherwise the scribe\n" +
            "  container is down or still starting.");
}

internal abstract record PqsWaitResult
{
    private PqsWaitResult()
    {
    }

    public sealed record Found : PqsWaitResult;

    public sealed record TimedOut : PqsWaitResult;

    public sealed record Unavailable(Exception Exception) : PqsWaitResult;
}

internal sealed record PqsProjectionSnapshot(
    IReadOnlyList<Contract<DemoAsset>> Owned,
    int MatchedAssetCount,
    IReadOnlyList<InterfaceContract<IHolding, HoldingView>> ViewedHoldings)
{
    public bool IsComplete(int expectedCount) =>
        MatchedAssetCount == expectedCount && ViewedHoldings.Count == expectedCount;
}
