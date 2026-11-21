// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Daml.Ledger.Abstractions;
using Daml.Runtime.Outcomes;

namespace MiniDemo;

internal static partial class LedgerFailureReport
{
    private const string UnstructuredFailureHint =
        "The ledger answered but did not complete the operation. Check the LocalNet logs for the underlying cause.";

    [GeneratedRegex(@"^(?<label>.*?): Daml error \[[^\]]*\]: (?<detail>.*)$", RegexOptions.Singleline)]
    private static partial Regex DamlErrorMessage();

    public static void Write(LedgerOperationException exception, DemoStep? step, TextWriter writer)
    {
        var parsed = DamlErrorMessage().Match(exception.Message);
        var label = parsed.Success ? parsed.Groups["label"].Value : null;
        var detail = parsed.Success ? parsed.Groups["detail"].Value : exception.Message;

        var lines = new List<string> { Header(step, label) };
        AddField(lines, "code", CodeOf(exception));
        AddField(lines, "status", StatusOf(exception));
        AddField(lines, "message", DamlFailureMessage.Summarize(detail));
        AddField(lines, "command id", exception.CommandId?.Value);
        AddField(lines, "hint", HintOf(exception));
        writer.WriteLine(string.Join('\n', lines));
    }

    private static string Header(DemoStep? step, string? label)
    {
        if (step is null)
            return $"\n\u2717 {label ?? "Ledger operation"} failed";
        var transport = step.Transport is null ? "" : $" over {step.Transport}";
        var failedStep = $"\n\u2717 {step.Name} failed{transport}";
        return label is null ? failedStep : $"{failedStep}: {label}";
    }

    private static void AddField(List<string> lines, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            lines.Add($"  {name,-10} {value}");
    }

    private static string? CodeOf(LedgerOperationException exception) =>
        exception.ErrorId is { } errorId
            ? exception.Category is { } category ? $"{errorId} ({category})" : errorId
            : null;

    private static string? StatusOf(LedgerOperationException exception) =>
        exception.ErrorId is null && exception.Status is TransportStatus.Grpc grpc
            ? grpc.StatusCode.ToString()
            : null;

    private static string? HintOf(LedgerOperationException exception) =>
        exception.ErrorId is null ? UnstructuredFailureHint : ErrorHint.For(exception.ErrorId);
}
