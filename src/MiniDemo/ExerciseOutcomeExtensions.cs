// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;

namespace MiniDemo;

internal static class ExerciseOutcomeExtensions
{
    public static T Unwrap<T>(this ExerciseOutcome<T> outcome, string operation) => outcome switch
    {
        ExerciseOutcome<T>.One one => one.Result,
        ExerciseOutcome<T>.DamlError e => throw new InvalidOperationException(
            $"{operation} failed (Daml error {e.ErrorId}, category {e.Category}): {e.Message}"),
        ExerciseOutcome<T>.InfraError i => throw new LedgerOperationException(
            $"{operation} failed (infra, status {i.Status}" +
            $"{(i.Category is { } category ? $", category {category}" : string.Empty)}): {i.Message}",
            i.Status, i.Category, i.SourceException),
        ExerciseOutcome<T>.CommittedUndecodable c => throw new InvalidOperationException(
            $"{operation} committed (update {c.UpdateId ?? "unknown"}) but its response could not be decoded; " +
            $"do not resubmit: {c.Message}",
            c.SourceException),
        ExerciseOutcome<T>.None => throw new InvalidOperationException($"{operation} produced no expected contract."),
        ExerciseOutcome<T>.Many m => throw new InvalidOperationException($"{operation} produced {m.Count} contracts; expected one."),
        _ => throw new InvalidOperationException($"{operation} produced an unexpected outcome: {outcome.GetType().Name}"),
    };
}
