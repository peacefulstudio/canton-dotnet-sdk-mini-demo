// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Npgsql;
using Peaceful.Canton.Localnet.Testing;

namespace MiniDemo;

internal static class PqsConnectionString
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=pqs-a-validator-1;Username=cnadmin;Password=supersafe";

    public static string EnvFor(LocalnetProfile profile) =>
        $"CANTON_LOCALNET_{LocalnetSlot.EnvKey(profile)}_PQS_CONNECTION_STRING";

    public static string DefaultFor(LocalnetProfile profile) =>
        $"Host=localhost;Port=5432;Database=pqs-{LocalnetSlot.Name(profile)};Username=cnadmin;Password=supersafe";

    public static string Resolve(LocalnetProfile profile, IReadOnlyDictionary<string, string?> env) =>
        env.TryGetValue(EnvFor(profile), out var configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : DefaultFor(profile);

    public static string ContainerName(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;

    public static string ToPsqlConnInfo(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"host={builder.Host} port={builder.Port} dbname={builder.Database} user={builder.Username} password={builder.Password}";
    }
}
