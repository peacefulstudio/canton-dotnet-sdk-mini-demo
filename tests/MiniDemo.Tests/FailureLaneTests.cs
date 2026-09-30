// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Testing;
using Daml.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Commands;
using Daml.Runtime.Contracts;
using Daml.Runtime.Data;
using Daml.Runtime.Outcomes;
using Xunit;
using DemoAsset = MiniDemo.Asset.Asset;

namespace MiniDemo.Tests;

public class FailureLaneTests
{
    private static readonly DemoParties Parties =
        new(new Party("issuer"), new Party("alice"), new Party("bob"));

    public static TheoryData<ResultStyle> BothStyles => new() { ResultStyle.OutcomePatternMatch, ResultStyle.OrThrow };

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task RejectDuplicateCommandAsync_reports_the_duplicate_rejection_as_expected(ResultStyle style)
    {
        var writer = new SequencedSubmissionWriter(Committed(), Duplicate());
        var output = new StringWriter();

        var act = () => FailureLane.RejectDuplicateCommandAsync(writer, "gRPC", style, Parties, output, CancellationToken.None);

        await act.Should().NotThrowAsync();
        output.ToString().Should().Contain(
            "gRPC rejected as expected: DUPLICATE_COMMAND (category InvalidGivenCurrentSystemStateResourceExists)");
        writer.Submissions.Should().HaveCount(2);
    }

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task RejectDuplicateCommandAsync_submits_one_create_twice_under_one_command_id_with_a_five_minute_window(
        ResultStyle style)
    {
        var writer = new SequencedSubmissionWriter(Committed(), Duplicate());

        await FailureLane.RejectDuplicateCommandAsync(
            writer, "REST", style, Parties, new StringWriter(), CancellationToken.None);

        var (first, second) = (writer.Submissions[0], writer.Submissions[1]);
        second.Should().Be(first);
        first.CommandId!.Value.Value.Should().StartWith("mini-demo-dedup-REST-");
        first.DeduplicationPeriod.Should().Be(new DeduplicationPeriod.Duration(TimeSpan.FromMinutes(5)));
        var create = first.Commands.Should().ContainSingle().Subject.Should().BeOfType<CreateCommand>().Subject;
        create.TemplateId.Should().Be(DemoAsset.TemplateId);
        DemoAsset.FromRecord(create.CreateArguments).Name.Should().StartWith("DEDUP-REST-");
    }

    [Fact]
    public async Task RejectDuplicateCommandAsync_creates_an_asset_that_alice_does_not_own_and_the_issuer_signs()
    {
        var writer = new SequencedSubmissionWriter(Committed(), Duplicate());

        await FailureLane.RejectDuplicateCommandAsync(
            writer, "gRPC", ResultStyle.OutcomePatternMatch, Parties, new StringWriter(), CancellationToken.None);

        var create = (CreateCommand)writer.Submissions[0].Commands[0];
        var payload = DemoAsset.FromRecord(create.CreateArguments);
        payload.Issuer.Value.Should().Be("issuer");
        payload.Owner.Value.Should().Be("issuer");
        writer.Submitters[0].ActAs.Should().Equal(Parties.Issuer);
    }

    [Fact]
    public async Task RejectDuplicateCommandAsync_mints_a_fresh_command_id_and_asset_name_on_every_run()
    {
        var first = new SequencedSubmissionWriter(Committed(), Duplicate());
        var second = new SequencedSubmissionWriter(Committed(), Duplicate());

        await FailureLane.RejectDuplicateCommandAsync(
            first, "gRPC", ResultStyle.OutcomePatternMatch, Parties, new StringWriter(), CancellationToken.None);
        await FailureLane.RejectDuplicateCommandAsync(
            second, "gRPC", ResultStyle.OutcomePatternMatch, Parties, new StringWriter(), CancellationToken.None);

        first.Submissions[0].CommandId.Should().NotBe(second.Submissions[0].CommandId);
        NameOf(first).Should().NotBe(NameOf(second));
    }

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task RejectDuplicateCommandAsync_fails_verification_when_the_ledger_accepts_the_duplicate(ResultStyle style)
    {
        var writer = new SequencedSubmissionWriter(Committed(), Committed());

        var act = () => FailureLane.RejectDuplicateCommandAsync(
            writer, "gRPC", style, Parties, new StringWriter(), CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("accepted a second submission").And.Contain("DUPLICATE_COMMAND");
    }

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task RejectDuplicateCommandAsync_fails_verification_when_the_rejection_is_not_a_duplicate(ResultStyle style)
    {
        var otherRejection = new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidIndependentOfSystemState,
            "INVALID_ARGUMENT",
            "bad argument",
            new Dictionary<string, string>());
        var writer = new SequencedSubmissionWriter(Committed(), otherRejection);

        var act = () => FailureLane.RejectDuplicateCommandAsync(
            writer, "gRPC", style, Parties, new StringWriter(), CancellationToken.None);

        (await act.Should().ThrowAsync<DemoVerificationException>())
            .Which.Message.Should().Contain("INVALID_ARGUMENT").And.Contain("expected DUPLICATE_COMMAND");
    }

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task RejectDuplicateCommandAsync_lets_an_infra_fault_on_the_first_submission_escape_unclassified(
        ResultStyle style)
    {
        var writer = new SequencedSubmissionWriter(LedgerOutcomes.InfraError<TransactionResult>(
            GrpcStatus.Unavailable, "connection refused"));

        var act = () => FailureLane.RejectDuplicateCommandAsync(
            writer, "gRPC", style, Parties, new StringWriter(), CancellationToken.None);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Status.Should().Be(GrpcStatus.Unavailable);
        writer.Submissions.Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task RejectDuplicateCommandAsync_lets_an_infra_fault_on_the_second_submission_escape_unclassified(
        ResultStyle style)
    {
        var writer = new SequencedSubmissionWriter(
            Committed(),
            LedgerOutcomes.InfraError<TransactionResult>(GrpcStatus.Unavailable, "connection reset"));

        var act = () => FailureLane.RejectDuplicateCommandAsync(
            writer, "gRPC", style, Parties, new StringWriter(), CancellationToken.None);

        (await act.Should().ThrowAsync<LedgerOperationException>())
            .Which.Status.Should().Be(GrpcStatus.Unavailable);
    }

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task RejectDuplicateCommandAsync_does_not_resubmit_when_the_first_submission_is_rejected(ResultStyle style)
    {
        var writer = new SequencedSubmissionWriter(Duplicate());

        var act = () => FailureLane.RejectDuplicateCommandAsync(
            writer, "gRPC", style, Parties, new StringWriter(), CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        writer.Submissions.Should().ContainSingle();
    }

    [Fact]
    public async Task RunAsync_writes_the_section_header_and_visits_every_transport_in_order()
    {
        var output = new StringWriter { NewLine = "\r\n" };
        var transports = new[]
        {
            new LedgerTransport("gRPC", "http://localhost:11901", AcceptingClient()),
            new LedgerTransport("REST", "http://localhost:11975", AcceptingClient(), ResultStyle.OrThrow),
        };

        var act = () => FailureLane.RunAsync(5, transports, Parties, output, CancellationToken.None);

        await act.Should().ThrowAsync<DemoVerificationException>();
        output.ToString().ReplaceLineEndings("\n").Should().StartWith(
            "\n== 5. Failure lane — expected rejections as typed values ==\n  gRPC ");
    }

    [Fact]
    public async Task RunAsync_exits_with_the_verification_code_when_a_transport_accepts_the_duplicate()
    {
        var transports = new[] { new LedgerTransport("gRPC", "http://localhost:11901", AcceptingClient()) };

        var exitCode = await DemoExitCode.ForRunAsync(
            ct => FailureLane.RunAsync(5, transports, Parties, new StringWriter(), ct),
            new StringWriter(),
            CancellationToken.None);

        exitCode.Should().Be(65);
    }

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task RunAsync_classifies_an_unreachable_ledger_exactly_as_the_other_sections_do(ResultStyle style)
    {
        var client = FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.InfraError<TransactionResult>(GrpcStatus.Unavailable, "connection refused"))
            .Build();
        var transports = new[] { new LedgerTransport("gRPC", "http://localhost:11901", client, style) };
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            ct => FailureLane.RunAsync(5, transports, Parties, new StringWriter(), ct), error, CancellationToken.None);

        exitCode.Should().Be(1);
        error.ToString().Should().Contain("Canton LocalNet is not reachable");
    }

    [Theory]
    [MemberData(nameof(BothStyles))]
    public async Task RunAsync_classifies_a_non_transport_infra_fault_as_a_failed_ledger_operation(ResultStyle style)
    {
        var client = FakeLedgerClient.Create()
            .WithSubmissionOutcome(LedgerOutcomes.InfraError<TransactionResult>(GrpcStatus.Internal, "boom"))
            .Build();
        var transports = new[] { new LedgerTransport("gRPC", "http://localhost:11901", client, style) };

        var exitCode = await DemoExitCode.ForRunAsync(
            ct => FailureLane.RunAsync(5, transports, Parties, new StringWriter(), ct),
            new StringWriter(),
            CancellationToken.None);

        exitCode.Should().Be(1);
    }

    [Fact]
    public void FailureLane_pins_the_duplicate_command_error_id_and_window_by_literal()
    {
        FailureLane.DuplicateCommandErrorId.Should().Be("DUPLICATE_COMMAND");
        FailureLane.DeduplicationWindow.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void ContractId_compares_by_value_across_separately_built_instances()
    {
        var one = new ContractId<DemoAsset>("asset-1");
        var sameValue = new ContractId<DemoAsset>("asset-1");
        var another = new ContractId<DemoAsset>("asset-2");

        (one == sameValue).Should().BeTrue();
        (one == another).Should().BeFalse();
    }

    [Fact]
    public void ResultStyle_labels_name_the_api_each_transport_uses()
    {
        ResultStyle.OutcomePatternMatch.Describe().Should().Be("TryCreateAsync, outcome pattern-matched");
        ResultStyle.OrThrow.Describe().Should().Be("TryCreateAsync(...).OneOrThrowAsync");
    }

    [Fact]
    public void LedgerTransport_defaults_to_the_outcome_pattern_match_style()
    {
        new LedgerTransport("gRPC", "http://localhost:11901", AcceptingClient())
            .ResultStyle.Should().Be(ResultStyle.OutcomePatternMatch);
    }

    private static string NameOf(SequencedSubmissionWriter writer) =>
        DemoAsset.FromRecord(((CreateCommand)writer.Submissions[0].Commands[0]).CreateArguments).Name;

    private static Canton.Ledger.Abstractions.ICantonLedgerClient AcceptingClient() =>
        FakeLedgerClient.Create().WithSubmissionOutcome(Committed()).Build();

    private static ExerciseOutcome<TransactionResult> Committed() =>
        LedgerOutcomes.One(LedgerResults.Transaction("update-1", LedgerOffset.At(2), [], [], new CommandId("command-1")));

    private static ExerciseOutcome<TransactionResult> Duplicate() =>
        new ExerciseOutcome<TransactionResult>.DamlError(
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
            "DUPLICATE_COMMAND",
            "Command submission already exists.",
            new Dictionary<string, string>());

    private sealed class SequencedSubmissionWriter(params ExerciseOutcome<TransactionResult>[] outcomes) : ILedgerWriter
    {
        private readonly Queue<ExerciseOutcome<TransactionResult>> _outcomes = new(outcomes);

        public List<CommandsSubmission> Submissions { get; } = [];

        public List<SubmitterInfo> Submitters { get; } = [];

        public Task<ExerciseOutcome<TransactionResult>> TrySubmitAndWaitForTransactionAsync(
            CommandsSubmission submission,
            SubmitterInfo submitter,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            Submissions.Add(submission);
            Submitters.Add(submitter);
            return Task.FromResult(_outcomes.Dequeue());
        }

        public Task<ExerciseOutcome<ContractId<TTemplate>>> TryCreateAsync<TTemplate>(
            TTemplate payload,
            SubmitterInfo submitter,
            string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
            where TTemplate : ITemplate =>
            throw new NotSupportedException();

        public Task<ExerciseOutcome<TResult>> TryExerciseAsync<TResult>(
            ExerciseCommand command,
            SubmitterInfo submitter,
            string? workflowId = null,
            CommandId? commandId = null,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SubmitAndWaitResult> SubmitAndWaitAsync(
            CommandsSubmission submission,
            SubmitterInfo submitter,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
