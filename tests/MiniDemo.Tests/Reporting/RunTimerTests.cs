// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace MiniDemo.Tests;

public class RunTimerTests
{
    private sealed class SteppedTimeProvider : TimeProvider
    {
        private long ticks;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => ticks;

        public void Advance(TimeSpan elapsed) => ticks += (long)(elapsed.TotalSeconds * TimestampFrequency);
    }

    [Fact]
    public async Task MeasureAsync_returns_the_exit_code_and_prints_the_elapsed_whole_seconds()
    {
        var clock = new SteppedTimeProvider();
        var output = new StringWriter();

        var exitCode = await RunTimer.MeasureAsync(
            () =>
            {
                clock.Advance(TimeSpan.FromSeconds(10));
                return Task.FromResult(DemoExitCode.Ok);
            },
            output,
            clock);

        exitCode.Should().Be(0);
        output.ToString().ReplaceLineEndings("\n").Should().Be("\nDone in 10s\n");
    }

    [Fact]
    public async Task MeasureAsync_keeps_one_decimal_for_a_fractional_run_time()
    {
        var clock = new SteppedTimeProvider();
        var output = new StringWriter();

        await RunTimer.MeasureAsync(
            () =>
            {
                clock.Advance(TimeSpan.FromSeconds(2.46));
                return Task.FromResult(DemoExitCode.Ok);
            },
            output,
            clock);

        output.ToString().Should().Contain("Done in 2.5s");
    }

    [Fact]
    public async Task MeasureAsync_prints_the_run_time_of_a_failed_run_and_keeps_its_exit_code()
    {
        var clock = new SteppedTimeProvider();
        var output = new StringWriter();

        var exitCode = await RunTimer.MeasureAsync(
            () =>
            {
                clock.Advance(TimeSpan.FromSeconds(3));
                return Task.FromResult(DemoExitCode.VerificationFailed);
            },
            output,
            clock);

        exitCode.Should().Be(65);
        output.ToString().Should().Contain("Done in 3s");
    }

    [Fact]
    public async Task MeasureAsync_prints_the_run_time_and_rethrows_when_the_run_throws()
    {
        var clock = new SteppedTimeProvider();
        var output = new StringWriter();

        var act = () => RunTimer.MeasureAsync(
            () =>
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                return Task.FromException<int>(new InvalidOperationException("unclassified"));
            },
            output,
            clock);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("unclassified");
        output.ToString().Should().Contain("Done in 1s");
    }
}
