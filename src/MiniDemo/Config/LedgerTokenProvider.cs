// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;

namespace MiniDemo;

internal sealed class LedgerTokenProvider(Func<CancellationToken, Task<string>> fetchAccessToken) : ITokenProvider
{
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default) =>
        await fetchAccessToken(cancellationToken);
}
