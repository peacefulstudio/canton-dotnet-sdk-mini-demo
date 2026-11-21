// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;

namespace MiniDemo.Tests;

internal static class StagedLedgerFailure
{
    // Workaround: Daml.Ledger.Abstractions 0.8.1 exposes LedgerOperationException.CommandId with an
    // internal setter and no public constructor taking one, so a test staging a failure the ledger
    // clients would stamp with a command id has to write the backing property by reflection.
    public static LedgerOperationException WithCommandId(this LedgerOperationException exception, string commandId)
    {
        typeof(LedgerOperationException)
            .GetProperty(nameof(LedgerOperationException.CommandId), BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(exception, [new CommandId(commandId)]);
        return exception;
    }
}
