// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;
using Xunit;

namespace MiniDemo.Tests;

public class LedgerFailureReportTests
{
    private const string AssertionFailedMessage =
        "TransferFactory.TransferFactory_Transfer: Daml error [InvalidGivenCurrentSystemStateOther/DAML_FAILURE]: " +
        "DAML_FAILURE(9,21d28f9a): Interpretation error: Error: User failure: " +
        "UNHANDLED_EXCEPTION/DA.Exception.AssertionFailed:AssertionFailed (error category 9): " +
        "the input holdings cover the amount";

    private static LedgerOperationException DamlRejection(string message, DamlErrorCategory category, string errorId) =>
        new(message, category, errorId, new Dictionary<string, string>());

    private static string Render(LedgerOperationException exception, DemoStep? step = null)
    {
        var writer = new StringWriter();
        LedgerFailureReport.Write(exception, step, writer);
        return writer.ToString().ReplaceLineEndings("\n");
    }

    [Fact]
    public void Write_prints_the_command_code_short_message_and_hint_of_a_daml_rejection()
    {
        var exception = DamlRejection(
            AssertionFailedMessage, DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "DAML_FAILURE");

        Render(exception).Should().Be(
            "\n✗ TransferFactory.TransferFactory_Transfer failed\n" +
            "  code       DAML_FAILURE (InvalidGivenCurrentSystemStateOther)\n" +
            "  message    the input holdings cover the amount\n" +
            "  hint       A Daml `require`, `assert` or `abort` rejected the command. The ledger is reachable.\n");
    }

    [Fact]
    public void Write_names_the_step_and_transport_in_the_header_when_a_step_is_known()
    {
        var exception = DamlRejection(
            AssertionFailedMessage, DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "DAML_FAILURE");

        Render(exception, new DemoStep("propose", "gRPC")).Should().Be(
            "\n\u2717 propose failed over gRPC: TransferFactory.TransferFactory_Transfer\n" +
            "  code       DAML_FAILURE (InvalidGivenCurrentSystemStateOther)\n" +
            "  message    the input holdings cover the amount\n" +
            "  hint       A Daml `require`, `assert` or `abort` rejected the command. The ledger is reachable.\n");
    }

    [Fact]
    public void Write_omits_the_transport_from_the_header_when_the_step_has_none()
    {
        var exception = DamlRejection(
            AssertionFailedMessage, DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "DAML_FAILURE");

        Render(exception, new DemoStep("bootstrap", null))
            .Should().StartWith("\n\u2717 bootstrap failed: TransferFactory.TransferFactory_Transfer\n");
    }

    [Fact]
    public void Write_omits_the_label_from_the_header_when_the_message_carries_none()
    {
        var exception = DamlRejection(
            "something went wrong", DamlErrorCategory.InvalidIndependentOfSystemState, "ODD_ONE");

        Render(exception, new DemoStep("mint", "REST")).Should().StartWith("\n\u2717 mint failed over REST\n");
    }

    [Fact]
    public void Write_never_calls_a_daml_rejection_a_reachability_issue()
    {
        var exception = DamlRejection(
            AssertionFailedMessage, DamlErrorCategory.InvalidGivenCurrentSystemStateOther, "DAML_FAILURE");

        Render(exception).Should().NotContain("reachability").And.NotContain("file an issue");
    }

    [Fact]
    public void Write_omits_the_hint_line_for_an_unknown_error_code()
    {
        var exception = DamlRejection(
            "Asset.Create: Daml error [InvalidIndependentOfSystemState/BRAND_NEW_CODE]: something odd happened",
            DamlErrorCategory.InvalidIndependentOfSystemState,
            "BRAND_NEW_CODE");

        Render(exception).Should().Be(
            "\n✗ Asset.Create failed\n" +
            "  code       BRAND_NEW_CODE (InvalidIndependentOfSystemState)\n" +
            "  message    something odd happened\n");
    }

    [Fact]
    public void Write_prints_the_command_id_when_the_exception_carries_one()
    {
        var exception = DamlRejection(
            "Asset.Create: Daml error [InvalidGivenCurrentSystemStateResourceExists/DUPLICATE_COMMAND]: already submitted",
            DamlErrorCategory.InvalidGivenCurrentSystemStateResourceExists,
            "DUPLICATE_COMMAND").WithCommandId("cmd-7f3a");

        Render(exception).Should().Contain("  command id cmd-7f3a\n");
    }

    [Fact]
    public void Write_falls_back_to_the_full_message_when_the_daml_error_prefix_is_absent()
    {
        var exception = DamlRejection(
            "something went wrong", DamlErrorCategory.InvalidIndependentOfSystemState, "ODD_ONE");

        Render(exception).Should().Be(
            "\n✗ Ledger operation failed\n" +
            "  code       ODD_ONE (InvalidIndependentOfSystemState)\n" +
            "  message    something went wrong\n");
    }

    [Fact]
    public void Write_reports_a_failure_with_no_error_code_by_its_transport_status_and_points_at_the_logs()
    {
        var exception = new LedgerOperationException(
            "ACS snapshot stream failed: internal error", GrpcStatus.Internal, category: null);

        Render(exception).Should().Be(
            "\n✗ Ledger operation failed\n" +
            "  status     Internal\n" +
            "  message    ACS snapshot stream failed: internal error\n" +
            "  hint       The ledger answered but did not complete the operation. Check the LocalNet logs for the underlying cause.\n");
    }
}
