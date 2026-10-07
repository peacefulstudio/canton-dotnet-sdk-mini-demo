// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;

namespace MiniDemo;

internal static class DemoExitCode
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int VerificationFailed = 65;
    public const int PqsRequirementNotMet = 69;
    public const int Unresponsive = 75;
    public const int ConfigurationInvalid = 78;
    public const int Interrupted = 130;

    public static async Task<int> ForRunAsync(
        Func<CancellationToken, Task> run, TextWriter error, CancellationToken ct)
    {
        try
        {
            await run(ct);
            return Ok;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LocalnetPreflight.WriteCancelledNotice(error);
            return Interrupted;
        }
        catch (PqsRequirementNotMetException ex)
        {
            LocalnetPreflight.WritePqsRequirementFailure(ex, error);
            return PqsRequirementNotMet;
        }
        catch (Exception ex) when (LocalnetPreflight.IsLocalnetUnreachable(ex))
        {
            LocalnetPreflight.WriteUnreachableHelp(ex, error);
            return Failed;
        }
        catch (Exception ex) when (LocalnetPreflight.IsLocalnetUnresponsive(ex))
        {
            LocalnetPreflight.WriteUnresponsiveHelp(ex, error);
            return Unresponsive;
        }
        catch (LedgerOperationException ex)
        {
            LocalnetPreflight.WriteLedgerOperationFailure(ex, error);
            return Failed;
        }
        catch (DemoVerificationException ex)
        {
            LocalnetPreflight.WriteVerificationReport(ex, error);
            return VerificationFailed;
        }
    }
}
