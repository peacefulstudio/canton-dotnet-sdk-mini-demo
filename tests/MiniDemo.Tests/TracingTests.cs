// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using MiniDemo;
using Xunit;

namespace MiniDemo.Tests;

public class TracingTests
{
    [Fact]
    public void endpoint_variable_is_the_standard_otlp_one()
    {
        Tracing.OtlpEndpointEnv.Should().Be("OTEL_EXPORTER_OTLP_ENDPOINT");
    }

    [Fact]
    public void no_tracer_provider_is_started_when_the_endpoint_is_missing()
    {
        var env = new Dictionary<string, string?>();

        using var provider = Tracing.Start(env);

        provider.Should().BeNull();
    }

    [Fact]
    public void no_tracer_provider_is_started_when_the_endpoint_is_whitespace()
    {
        var env = new Dictionary<string, string?> { [Tracing.OtlpEndpointEnv] = "   " };

        using var provider = Tracing.Start(env);

        provider.Should().BeNull();
    }

    [Fact]
    public void run_activity_is_not_recorded_when_tracing_is_off()
    {
        using var run = Tracing.StartRun();

        run.Should().BeNull();
    }

    [Fact]
    public void run_activity_is_named_mini_demo_run_when_tracing_is_on()
    {
        var env = new Dictionary<string, string?> { [Tracing.OtlpEndpointEnv] = "http://localhost:1" };

        using var provider = Tracing.Start(env);
        using var run = Tracing.StartRun();

        provider.Should().NotBeNull();
        run.Should().NotBeNull();
        run!.OperationName.Should().Be("mini-demo run");
        run.Source.Name.Should().Be("MiniDemo");
    }
}
