// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime.Contracts;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo;

internal sealed record LedgerTransport
{
    public LedgerTransport(
        string name,
        string endpoint,
        ICantonLedgerClient client,
        ResultStyle resultStyle = ResultStyle.OutcomePatternMatch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentNullException.ThrowIfNull(client);
        Name = name;
        Endpoint = endpoint;
        Client = client;
        ResultStyle = resultStyle;
    }

    public string Name { get; }
    public string Endpoint { get; }
    public ICantonLedgerClient Client { get; }
    public ResultStyle ResultStyle { get; }
}

internal sealed record WrittenAsset(LedgerTransport Transport, ContractId<DemoAsset> ContractId, AssetKey Key);
