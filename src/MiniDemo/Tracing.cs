// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MiniDemo;

internal static class Tracing
{
    public const string OtlpEndpointEnv = "OTEL_EXPORTER_OTLP_ENDPOINT";
    private const string ServiceName = "mini-demo";
    private const string SourceName = "MiniDemo";
    private const string RunActivityName = "mini-demo run";

    private static readonly ActivitySource Source = new(SourceName);

    public static TracerProvider? Start(IReadOnlyDictionary<string, string?> env) =>
        env.TryGetValue(OtlpEndpointEnv, out var endpoint) && !string.IsNullOrWhiteSpace(endpoint)
            ? Sdk.CreateTracerProviderBuilder()
                .ConfigureResource(resource => resource.AddService(ServiceName))
                .AddSource(SourceName)
                .AddCantonLedgerInstrumentation()
                .AddOtlpExporter()
                .Build()
            : null;

    public static Activity? StartRun() => Source.StartActivity(RunActivityName);
}
