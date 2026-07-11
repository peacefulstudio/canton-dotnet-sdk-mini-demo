// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using DemoAsset = Canton.Mini.Demo.Asset;

namespace MiniDemo;

internal static class AssetAcsQuery
{
    public static async Task<IReadOnlyList<AssetSnapshot>> QueryForPartyAsync(
        ILedgerClient ledgerClient, Party party, CancellationToken ct)
    {
        var assets = new List<AssetSnapshot>();
        await foreach (var evt in ledgerClient.SubscribeActiveAsync<DemoAsset>(party, ct))
            switch (evt)
            {
                case ContractStreamEvent<DemoAsset>.Created created:
                    var asset = DemoAsset.FromRecord(created.Payload);
                    assets.Add(new AssetSnapshot(created.ContractId.Value, asset.Owner.Id, asset.Name, asset.Amount));
                    break;
                case ContractStreamEvent<DemoAsset>.Unclassified unclassified:
                    throw new InvalidOperationException(
                        $"ACS snapshot returned an unclassified event at offset {unclassified.Offset} " +
                        $"(kind: {unclassified.Kind}); an Asset contract may have failed to map to the generated DemoAsset type.");
            }
        return assets;
    }
}

internal sealed record AssetSnapshot(string ContractId, string Owner, string Name, decimal Amount);
