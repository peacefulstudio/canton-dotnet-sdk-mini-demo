// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Canton.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Data;
using Splice.Api.Token.HoldingV2;

namespace MiniDemo;

internal static class HoldingQuery
{
    public static async Task<IReadOnlyList<HoldingSnapshot>> QueryForPartyAsync(
        ICantonLedgerClient ledgerClient, Party party, CancellationToken ct)
    {
        var holdings = await ledgerClient.QueryActiveAsync<IHolding, HoldingView>(party, cancellationToken: ct);
        return holdings.Select(active => HoldingSnapshot.From(active.Contract)).ToList();
    }

    public static async Task<DisclosedContract> ReadDisclosureAsync(
        ICantonLedgerClient ledgerClient, Party stakeholder, string contractId, CancellationToken ct)
    {
        var holdings = await ledgerClient.QueryActiveAsync<IHolding, HoldingView>(
            stakeholder, includeDisclosure: true, cancellationToken: ct);
        var holding = holdings.SingleOrDefault(active => active.Contract.Id.Value == contractId)
            ?? throw new DemoVerificationException(
                $"The ACS returned no active holding {contractId} for {stakeholder.Value}, so there is nothing to disclose.");
        return holding.Disclosure
            ?? throw new DemoVerificationException(
                $"The ACS returned {contractId} without its disclosure, so it cannot be disclosed.");
    }
}

internal sealed record HoldingSnapshot(
    string ContractId,
    string? Owner,
    string? Provider,
    string AccountId,
    string Admin,
    string InstrumentId,
    decimal Amount,
    LockSnapshot? Lock,
    string Meta)
{
    public static HoldingSnapshot From(InterfaceContract<IHolding, HoldingView> holding)
    {
        var view = holding.View;
        return new HoldingSnapshot(
            holding.Id.Value,
            view.Account.Owner?.Value,
            view.Account.Provider?.Value,
            view.Account.Id,
            view.InstrumentId.Admin.Value,
            view.InstrumentId.Id,
            view.Amount,
            view.Lock is { } holdingLock ? LockSnapshot.From(holdingLock) : null,
            string.Join(", ", view.Meta.Values.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}={entry.Value}")));
    }

    public bool Locked => Lock is not null;

    public string Headline(string ownerLabel) =>
        $"{ownerLabel}: {AmountFormat.Display(Amount)} {InstrumentId}{(Locked ? " 🔒" : string.Empty)}";

    public string Describe() =>
        $"owner={Owner ?? "none"}, admin={Admin}, instrument={InstrumentId}, amount={Amount}" +
        $"{(Provider is null ? string.Empty : $", provider={Provider}")}" +
        $"{(AccountId.Length == 0 ? string.Empty : $", account={AccountId}")}" +
        $"{(Lock is null ? string.Empty : $", {Lock.Describe()}")}" +
        $"{(Meta.Length == 0 ? string.Empty : $", meta={{{Meta}}}")}";
}

internal sealed record LockSnapshot(string Holders, DateTimeOffset? ExpiresAt, string? Context)
{
    public static LockSnapshot From(Splice.Api.Token.HoldingV2.Lock holdingLock) =>
        new(
            string.Join(", ", holdingLock.Holders.Select(holder => holder.Value)),
            holdingLock.ExpiresAt,
            holdingLock.Context);

    public string Describe() =>
        $"locked by {Holders}" +
        $"{(ExpiresAt is { } expiresAt ? $" until {expiresAt.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)}" : string.Empty)}" +
        $"{(Context is null ? string.Empty : $" ({Context})")}";
}
