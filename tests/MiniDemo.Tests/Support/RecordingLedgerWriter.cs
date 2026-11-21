// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;

namespace MiniDemo.Tests;

internal sealed record RecordedCreate(object Payload, SubmitterInfo Submitter);

internal sealed record RecordedSubmission(CommandsSubmission Submission, SubmitterInfo Submitter);

internal sealed class RecordingLedgerWriter(ILedgerWriter inner) : ILedgerWriter
{
    public List<RecordedCreate> Creates { get; } = [];

    public List<RecordedSubmission> Submissions { get; } = [];

    public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
        TTemplate payload,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        where TTemplate : ITemplate
    {
        Creates.Add(new RecordedCreate(payload!, submitter));
        return inner.TryCreateAsync(payload, submitter, workflowId, commandId, timeout, cancellationToken);
    }

    public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
        CommandsSubmission submission,
        SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        Submissions.Add(new RecordedSubmission(submission, submitter));
        return inner.TrySubmitAndWaitForTransactionAsync(submission, submitter, timeout, cancellationToken);
    }

    public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
        ExerciseCommand command,
        SubmitterInfo submitter,
        string? workflowId = null,
        CommandId? commandId = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        inner.TryExerciseAsync<TResult>(command, submitter, workflowId, commandId, timeout, cancellationToken);

    public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
        CommandsSubmission submission,
        SubmitterInfo submitter,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        inner.SubmitAndWaitAsync(submission, submitter, timeout, cancellationToken);
}
