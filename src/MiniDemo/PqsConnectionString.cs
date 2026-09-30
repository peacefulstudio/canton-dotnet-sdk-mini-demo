// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Npgsql;

namespace MiniDemo;

internal static class PqsConnectionString
{
    public const string ConnectionStringEnv = "CANTON_LOCALNET_A_VALIDATOR_1_PQS_CONNECTION_STRING";

    public const string DatabaseName = "pqs-a-validator-1";

    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=" + DatabaseName + ";Username=cnadmin;Password=supersafe";

    public static string Resolve(IReadOnlyDictionary<string, string?> env) =>
        env.TryGetValue(ConnectionStringEnv, out var configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : DefaultConnectionString;

    public static string ToPsqlConnInfo(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"host={builder.Host} port={builder.Port} dbname={builder.Database} user={builder.Username} password={builder.Password}";
    }
}
