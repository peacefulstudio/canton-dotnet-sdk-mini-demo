// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Streams;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo;

internal static class UpdateStreamObserver
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    internal static Task ObserveCreatedAsync(
        ICantonLedgerClient ledgerClient,
        string transportName,
        Party observer,
        ContractId<DemoAsset> expected,
        LedgerOffset after,
        LedgerOffset through,
        TimeSpan timeout,
        TextWriter output,
        CancellationToken ct) =>
        ObserveCreatedAsync(
            streamCt => ledgerClient.SubscribeAsync<DemoAsset>(new SubmitterInfo(observer), after, through, streamCt),
            transportName, expected, after, through, timeout, output, ct);

    internal static async Task ObserveCreatedAsync(
        Func<CancellationToken, IAsyncEnumerable<ContractStreamEvent<DemoAsset>>> openStream,
        string transportName,
        ContractId<DemoAsset> expected,
        LedgerOffset after,
        LedgerOffset through,
        TimeSpan timeout,
        TextWriter output,
        CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);

        var eventsRead = 0;
        try
        {
            await foreach (var streamEvent in openStream(deadline.Token).WithCancellation(deadline.Token))
            {
                eventsRead++;
                switch (streamEvent)
                {
                    case ContractStreamEvent<DemoAsset>.StreamError error:
                        throw new DemoVerificationException(
                            $"Update-stream check failed: the {transportName} stream ended with a fault ({error.Message}).");
                    case ContractStreamEvent<DemoAsset>.Created created when created.ContractId == expected:
                        EnsureInsideWindow(created.Offset, after, through, transportName);
                        output.WriteLine(
                            $"  observe  {transportName} update stream ({after.Value}, {through.Value}] delivered " +
                            $"Created {expected.Value} to bob at offset {created.Offset.Value} ({eventsRead} event(s) read)");
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new DemoVerificationException(
                $"Update-stream check failed: the {transportName} stream delivered no Created {expected.Value} " +
                $"within {timeout.TotalSeconds:0} s over ({after.Value}, {through.Value}].");
        }

        throw new DemoVerificationException(
            $"Update-stream check failed: the {transportName} stream completed over ({after.Value}, {through.Value}] " +
            $"after {eventsRead} event(s) without a Created {expected.Value}.");
    }

    private static void EnsureInsideWindow(LedgerOffset offset, LedgerOffset after, LedgerOffset through, string transportName)
    {
        if (offset.Value <= after.Value || offset.Value > through.Value)
            throw new DemoVerificationException(
                $"Update-stream check failed: the {transportName} stream delivered an event at offset {offset.Value}, " +
                $"outside the requested window ({after.Value}, {through.Value}].");
    }
}
