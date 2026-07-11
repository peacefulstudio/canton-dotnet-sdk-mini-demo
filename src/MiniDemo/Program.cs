// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Peaceful.Canton.Localnet.Testing;
using MiniDemo;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

var env = LocalnetPreflight.ReadEnvironment();
var grpcAddress = LedgerEndpoint.Resolve(env);
LocalnetPreflight.WriteTargetSummary(env, Console.Out);

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddSimpleConsole(options => options.SingleLine = true).SetMinimumLevel(LogLevel.Warning));

await using var fixture = LocalnetFixture.FromEnvironment(loggerFactory);

var demo = new MiniDemoRunner(fixture, grpcAddress);
try
{
    await demo.RunAsync(ct);
}
catch (Exception ex) when (LocalnetPreflight.IsLocalnetUnreachable(ex))
{
    LocalnetPreflight.WriteUnreachableHelp(ex, Console.Error);
    return 1;
}
return 0;
