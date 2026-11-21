// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Canton.Ledger.Abstractions;
using Canton.Ledger.Grpc.Client;
using Canton.Ledger.Rest.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MiniDemo.Tests;

public class LedgerTokenProviderTests
{
    [Fact]
    public async Task GetTokenAsync_asks_the_source_on_every_call_so_a_refreshed_token_reaches_the_clients()
    {
        var issued = 0;
        var provider = new LedgerTokenProvider(_ => Task.FromResult($"token-{++issued}"));

        var first = await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        var second = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        first.Should().Be("token-1");
        second.Should().Be("token-2");
    }

    [Fact]
    public async Task GetTokenAsync_hands_the_caller_cancellation_token_to_the_source()
    {
        using var cancellation = new CancellationTokenSource();
        CancellationToken seen = default;
        var provider = new LedgerTokenProvider(token =>
        {
            seen = token;
            return Task.FromResult("token");
        });

        await provider.GetTokenAsync(cancellation.Token);

        seen.Should().Be(cancellation.Token);
    }

    [Fact]
    public void Registered_before_the_ledger_clients_it_stays_the_token_provider_they_resolve()
    {
        var provider = new LedgerTokenProvider(_ => Task.FromResult("token"));
        using var grpc = new ServiceCollection()
            .AddSingleton<ITokenProvider>(provider)
            .AddLedgerClient(options => options.GrpcAddress = "http://localhost:11901")
            .BuildServiceProvider();
        using var rest = new ServiceCollection()
            .AddSingleton<ITokenProvider>(provider)
            .AddRestLedgerClient(options =>
            {
                options.HttpAddress = "http://localhost:11975";
                options.UserId = "user";
            })
            .BuildServiceProvider();

        grpc.GetRequiredService<ITokenProvider>().Should().BeSameAs(provider);
        rest.GetRequiredService<ITokenProvider>().Should().BeSameAs(provider);
    }
}
