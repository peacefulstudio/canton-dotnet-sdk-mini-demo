// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace MiniDemo;

internal static class LedgerEndpoint
{
    public const string GrpcAddressEnv = "CANTON_LOCALNET_LEDGER_GRPC";
    public const string DefaultGrpcAddress = "http://localhost:11901";

    public static string Resolve(IReadOnlyDictionary<string, string?> env) =>
        env.TryGetValue(GrpcAddressEnv, out var configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : DefaultGrpcAddress;
}
