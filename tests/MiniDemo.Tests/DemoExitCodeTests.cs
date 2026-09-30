// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Sockets;
using AwesomeAssertions;
using Daml.Ledger.Abstractions;
using Xunit;

namespace MiniDemo.Tests;

public class DemoExitCodeTests
{
    [Fact]
    public async Task ForRunAsync_returns_Ok_and_stays_quiet_when_the_run_completes()
    {
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(_ => Task.CompletedTask, error, CancellationToken.None);

        exitCode.Should().Be(DemoExitCode.Ok);
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task ForRunAsync_returns_Interrupted_and_writes_the_notice_when_the_user_cancels()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            ct => Task.FromException(new OperationCanceledException(ct)), error, cts.Token);

        exitCode.Should().Be(DemoExitCode.Interrupted);
        error.ToString().Should().Contain("cancelled");
    }

    [Fact]
    public async Task ForRunAsync_returns_Failed_and_writes_the_help_when_localnet_is_unreachable()
    {
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            _ => Task.FromException(
                new LedgerOperationException("CreateAssetAsync failed", GrpcStatus.Unavailable, category: null)),
            error,
            CancellationToken.None);

        exitCode.Should().Be(DemoExitCode.Failed);
        error.ToString().Should().Contain("Canton LocalNet is not reachable");
    }

    [Fact]
    public async Task ForRunAsync_returns_Unresponsive_and_writes_the_help_when_localnet_hangs()
    {
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            _ => Task.FromException(
                new TaskCanceledException("the request timed out", new TimeoutException("HttpClient timeout"))),
            error,
            CancellationToken.None);

        exitCode.Should().Be(DemoExitCode.Unresponsive);
        error.ToString().Should().Contain("did not respond");
    }

    [Fact]
    public async Task ForRunAsync_returns_Failed_and_writes_the_report_for_an_unclassified_ledger_operation_failure()
    {
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            _ => Task.FromException(
                new LedgerOperationException("ACS snapshot stream failed (status 13): internal error", GrpcStatus.Internal, category: null)),
            error,
            CancellationToken.None);

        exitCode.Should().Be(DemoExitCode.Failed);
        error.ToString().Should().Contain("The ledger reported an operation failure");
        error.ToString().Should().NotContain("Canton LocalNet is not reachable");
        error.ToString().Should().NotContain("did not respond");
    }

    [Fact]
    public async Task ForRunAsync_returns_VerificationFailed_and_writes_the_report_when_the_demo_check_fails()
    {
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            _ => Task.FromException(
                new DemoVerificationException("Cross-transport check failed: the Asset 00ab is not visible over REST.")),
            error,
            CancellationToken.None);

        exitCode.Should().Be(DemoExitCode.VerificationFailed);
        error.ToString().Should().Contain("Demo verification failed");
    }

    [Fact]
    public async Task ForRunAsync_returns_69_and_writes_the_report_when_require_pqs_is_not_met()
    {
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            _ => Task.FromException(
                new PqsRequirementNotMetException(
                    "PQS projected 1 of 2 Asset contract(s) and 0 of 2 IHolding view(s) within 120s.")),
            error,
            CancellationToken.None);

        exitCode.Should().Be(69);
        error.ToString().Should().Contain("--require-pqs was set");
        error.ToString().Should().Contain("PQS projected 1 of 2 Asset contract(s)");
    }

    [Fact]
    public async Task ForRunAsync_returns_69_when_the_pqs_requirement_wraps_a_connection_refused_socket_exception()
    {
        var error = new StringWriter();
        var connectionRefused = new SocketException((int)SocketError.ConnectionRefused);

        var exitCode = await DemoExitCode.ForRunAsync(
            _ => Task.FromException(
                new PqsRequirementNotMetException(
                    $"PQS was never available ({connectionRefused.Message}).", connectionRefused)),
            error,
            CancellationToken.None);

        exitCode.Should().Be(69);
        error.ToString().Should().Contain("--require-pqs was set");
        error.ToString().Should().NotContain("Canton LocalNet is not reachable");
    }

    [Fact]
    public async Task ForRunAsync_returns_69_when_the_pqs_requirement_wraps_an_inner_timeout_exception()
    {
        var error = new StringWriter();
        var timedOut = new TimeoutException("Timeout during reading attempt");
        var connectionFailure = new InvalidOperationException("Exception while reading from stream", timedOut);

        var exitCode = await DemoExitCode.ForRunAsync(
            _ => Task.FromException(
                new PqsRequirementNotMetException(
                    $"PQS was never available ({connectionFailure.Message}).", connectionFailure)),
            error,
            CancellationToken.None);

        exitCode.Should().Be(69);
        error.ToString().Should().Contain("--require-pqs was set");
        error.ToString().Should().NotContain("did not respond");
    }

    [Fact]
    public async Task ForRunAsync_keeps_the_verification_message_in_the_report()
    {
        var error = new StringWriter();

        await DemoExitCode.ForRunAsync(
            _ => Task.FromException(
                new DemoVerificationException("ACS round-trip check failed: transferred Asset 00cd was not found.")),
            error,
            CancellationToken.None);

        error.ToString().Should().Contain("ACS round-trip check failed: transferred Asset 00cd was not found.");
    }

    [Fact]
    public async Task ForRunAsync_reports_a_verification_failure_that_mentions_a_connection_as_VerificationFailed()
    {
        var error = new StringWriter();

        var exitCode = await DemoExitCode.ForRunAsync(
            _ => Task.FromException(
                new DemoVerificationException(
                    "ACS snapshot stream failed (status 14): the connection to localhost:11901 timed out.")),
            error,
            CancellationToken.None);

        exitCode.Should().Be(DemoExitCode.VerificationFailed);
        error.ToString().Should().NotContain("Canton LocalNet is not reachable");
        error.ToString().Should().NotContain("did not respond");
    }

    [Fact]
    public async Task ForRunAsync_propagates_a_cancellation_the_user_did_not_ask_for()
    {
        var error = new StringWriter();

        var act = async () => await DemoExitCode.ForRunAsync(
            _ => Task.FromException(new OperationCanceledException("an internal deadline expired")),
            error,
            CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task ForRunAsync_propagates_an_unrecognised_failure()
    {
        var error = new StringWriter();

        var act = async () => await DemoExitCode.ForRunAsync(
            _ => Task.FromException(new InvalidOperationException("something else went wrong")),
            error,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        error.ToString().Should().BeEmpty();
    }
}
