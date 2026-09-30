// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo;

internal static class AssetAcsQuery
{
    public static async Task<IReadOnlyList<AssetSnapshot>> QueryForPartyAsync(
        ILedgerClient ledgerClient, Party party, CancellationToken ct)
    {
        var assets = new List<AssetSnapshot>();
        await foreach (var evt in ledgerClient.SubscribeActiveAsync<DemoAsset>(party, cancellationToken: ct))
            switch (evt)
            {
                case AcsSnapshotEntry<DemoAsset>.Created created:
                    var asset = created.Payload;
                    assets.Add(new AssetSnapshot(
                        created.ContractId, asset.Owner.Value, asset.Name, asset.Amount, DecodeKey(created)));
                    break;
                case AcsSnapshotEntry<DemoAsset>.Checkpoint:
                    return assets;
                case AcsSnapshotEntry<DemoAsset>.StreamError streamError:
                    throw new LedgerOperationException(
                        $"ACS snapshot stream failed (status {streamError.Status}" +
                        $"{(streamError.Category is { } category ? $", category {category}" : string.Empty)}): " +
                        $"{streamError.Message}",
                        streamError.Status,
                        streamError.Category,
                        streamError.SourceException,
                        streamError.ErrorId);
                case AcsSnapshotEntry<DemoAsset>.Unclassified unclassified:
                    throw new DemoVerificationException(
                        $"ACS snapshot returned an unclassified event at offset " +
                        $"{unclassified.Offset?.Value.ToString() ?? "unknown"} (kind: {unclassified.Kind}" +
                        $"{(unclassified.RawKind is { } rawKind ? $", wire kind: {rawKind}" : string.Empty)}); " +
                        "a kind of DecodeFailure means an Asset contract did not map to the generated " +
                        "DemoAsset type; the other kinds name a row this query does not expect.");
                default:
                    var unrecognisedType = evt?.GetType().FullName ?? "null";
                    throw new DemoVerificationException(
                        $"ACS snapshot returned an unrecognised entry type '{unrecognisedType}'; " +
                        "the SDK ships a new AcsSnapshotEntry variant that this query does not handle.");
            }
        throw new DemoVerificationException(
            "ACS snapshot stream ended without a terminal checkpoint or stream error, so the " +
            "snapshot is truncated; the contract set it returned would be indistinguishable from a complete one.");
    }

    private static AssetKey DecodeKey(AcsSnapshotEntry<DemoAsset>.Created created)
    {
        if (created.Key is not { } key)
            throw new DemoVerificationException(
                $"ACS snapshot returned the Asset {created.ContractId.Value} without its contract key; " +
                "Asset is keyed by (issuer, name), so every active Asset must carry one.");

        try
        {
            return AssetKey.From(DemoAsset.Key.KeyDecoder(key.Value));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DemoVerificationException(
                $"ACS snapshot returned the Asset {created.ContractId.Value} with a contract key that does not " +
                $"decode as (issuer, name): {ex.Message}");
        }
    }
}

internal sealed record AssetSnapshot(ContractId<DemoAsset> ContractId, string Owner, string Name, decimal Amount, AssetKey Key)
{
    public string Describe() => $"owner={Owner}, name={Name}, amount={Amount}, key={Key}";
}

