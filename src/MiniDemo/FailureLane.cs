// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Outcomes;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo;

internal static class FailureLane
{
    public const string DuplicateCommandErrorId = "DUPLICATE_COMMAND";
    public const decimal DemoAmount = 1m;
    public static readonly TimeSpan DeduplicationWindow = TimeSpan.FromMinutes(5);

    internal static async Task RunAsync(
        int section,
        IReadOnlyList<LedgerTransport> transports,
        DemoParties parties,
        TextWriter output,
        CancellationToken ct)
    {
        output.WriteLine($"\n== {section}. Failure lane — expected rejections as typed values ==");
        foreach (var transport in transports)
            await RejectDuplicateCommandAsync(transport.Client, transport.Name, transport.ResultStyle, parties, output, ct);
    }

    internal static async Task RejectDuplicateCommandAsync(
        ILedgerWriter writer,
        string transportName,
        ResultStyle style,
        DemoParties parties,
        TextWriter output,
        CancellationToken ct)
    {
        var runToken = Guid.NewGuid().ToString("N");
        var assetName = $"DEDUP-{transportName}-{runToken[..8]}";
        var commandId = new CommandId($"mini-demo-dedup-{transportName}-{runToken}");
        var submission = CommandsSubmission
            .Single(CreateCommand.For(new DemoAsset(parties.Issuer, parties.Issuer, assetName, DemoAmount, Lock: null)))
            .WithCommandId(commandId)
            .WithDeduplicationPeriod(new DeduplicationPeriod.Duration(DeduplicationWindow));
        var submitter = new SubmitterInfo(parties.Issuer);

        output.WriteLine(
            $"  {transportName,-4} submit create Asset(issuer, issuer, {assetName}) as command {commandId.Value} " +
            $"(deduplicated for {DeduplicationWindow.TotalMinutes:0} min)");
        await SubmitFirstAsync(writer, style, submission, submitter, ct);
        output.WriteLine($"  {transportName,-4} committed");

        output.WriteLine($"  {transportName,-4} submit the same command id again ({style.DescribeRejection()})");
        var rejection = await SubmitSecondAsync(writer, style, submission, submitter, ct);
        output.WriteLine(
            $"  {transportName,-4} rejected as expected: {rejection.ErrorId} (category {rejection.Category})");
    }

    private static async Task SubmitFirstAsync(
        ILedgerWriter writer, ResultStyle style, CommandsSubmission submission, SubmitterInfo submitter, CancellationToken ct)
    {
        var outcome = writer.TrySubmitAndWaitForTransactionAsync(submission, submitter, cancellationToken: ct);
        if (style == ResultStyle.OrThrow)
            await outcome.OneOrThrowAsync(nameof(SubmitFirstAsync));
        else
            (await outcome).Unwrap(nameof(SubmitFirstAsync));
    }

    private static async Task<Rejection> SubmitSecondAsync(
        ILedgerWriter writer, ResultStyle style, CommandsSubmission submission, SubmitterInfo submitter, CancellationToken ct)
    {
        var outcome = writer.TrySubmitAndWaitForTransactionAsync(submission, submitter, cancellationToken: ct);
        return style == ResultStyle.OrThrow
            ? await RejectionFromExceptionAsync(outcome)
            : RejectionFromOutcome(await outcome);
    }

    private static async Task<Rejection> RejectionFromExceptionAsync(Task<ExerciseOutcome<TransactionResult>> outcome)
    {
        try
        {
            await outcome.OneOrThrowAsync(nameof(SubmitSecondAsync));
        }
        catch (LedgerOperationException ex) when (ex.ErrorId is { } errorId && ex.Category is { } category)
        {
            if (errorId == DuplicateCommandErrorId)
                return new Rejection(errorId, category);
            throw UnexpectedRejection(errorId, category);
        }

        throw AcceptedDuplicate();
    }

    private static Rejection RejectionFromOutcome(ExerciseOutcome<TransactionResult> outcome)
    {
        switch (outcome)
        {
            case ExerciseOutcome<TransactionResult>.DamlError { ErrorId: DuplicateCommandErrorId } duplicate:
                return new Rejection(duplicate.ErrorId, duplicate.Category);
            case ExerciseOutcome<TransactionResult>.DamlError other:
                throw UnexpectedRejection(other.ErrorId, other.Category);
            default:
                outcome.Unwrap(nameof(SubmitSecondAsync));
                throw AcceptedDuplicate();
        }
    }

    private static DemoVerificationException UnexpectedRejection(string errorId, DamlErrorCategory category) =>
        new($"Failure lane check failed: resubmitting a command id inside its deduplication window was " +
            $"rejected with {errorId} (category {category}); expected {DuplicateCommandErrorId}.");

    private static DemoVerificationException AcceptedDuplicate() =>
        new($"Failure lane check failed: the ledger accepted a second submission of a command id inside its " +
            $"deduplication window; expected a {DuplicateCommandErrorId} rejection.");

    private readonly record struct Rejection(string ErrorId, DamlErrorCategory Category);
}
