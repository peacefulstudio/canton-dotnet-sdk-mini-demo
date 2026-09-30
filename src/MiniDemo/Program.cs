// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Kernel.Authentication;
using Canton.Ledger.Pqs.Client;
using Canton.Ledger.Rest.Client;
using Daml.Ledger.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peaceful.Canton.Localnet.Testing;
using MiniDemo;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

var requirePqs = args.Contains("--require-pqs", StringComparer.Ordinal);

var env = LocalnetPreflight.ReadEnvironment();
var grpcAddress = LedgerEndpoint.Resolve(env);
var jsonApiAddress = LedgerEndpoint.ResolveJsonApi(env);
var pqsConnectionString = PqsConnectionString.Resolve(env);
LocalnetPreflight.WriteTargetSummary(env, Console.Out);

using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddSimpleConsole(options => options.SingleLine = true).SetMinimumLevel(LogLevel.Warning));

await using var fixture = LocalnetFixture.FromEnvironment(loggerFactory);

return await DemoExitCode.ForRunAsync(RunDemoAsync, Console.Error, ct);

async Task RunDemoAsync(CancellationToken cancellationToken)
{
    var accessToken = await fixture.TokenProvider.GetAccessTokenAsync(cancellationToken);

    await using var grpcServices = new ServiceCollection()
        .AddCantonStaticAuth(accessToken)
        .AddLedgerClient(options => options.GrpcAddress = grpcAddress)
        .BuildServiceProvider();

    await using var restServices = new ServiceCollection()
        .AddCantonStaticAuth(accessToken)
        .AddRestLedgerClient(options =>
        {
            options.HttpAddress = jsonApiAddress;
            options.UserId = fixture.ValidatorUserId;
        })
        .BuildServiceProvider();

    await using var pqsServices = new ServiceCollection()
        .AddPqsClient(options => options.ConnectionString = pqsConnectionString)
        .BuildServiceProvider();

    LedgerTransport[] transports =
    [
        new("gRPC", grpcAddress, grpcServices.GetRequiredService<ICantonLedgerClient>()),
        new("REST", jsonApiAddress, restServices.GetRequiredService<ICantonLedgerClient>(), ResultStyle.OrThrow),
    ];

    var pqsClient = pqsServices.GetRequiredService<IPqsClient>();

    var demo = new MiniDemoRunner(fixture, transports, pqsClient, pqsConnectionString, requirePqs);
    await demo.RunAsync(cancellationToken);
}
