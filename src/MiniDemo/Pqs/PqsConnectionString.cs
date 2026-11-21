// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Npgsql;
using Peaceful.Canton.Localnet.Testing;

namespace MiniDemo;

internal static class PqsConnectionString
{
    public const int DefaultPort = 5432;

    public const string DatabasePrefix = "pqs-";

    private const string SlotPlaceholder = "<SLOT>";

    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=pqs-a-validator-1;Username=cnadmin;Password=supersafe";

    public static string EnvFor(LocalnetProfile profile) =>
        $"CANTON_LOCALNET_{LocalnetSlot.EnvKey(profile)}_PQS_CONNECTION_STRING";

    public static string PortEnvFor(LocalnetProfile profile) =>
        $"CANTON_LOCALNET_{LocalnetSlot.EnvKey(profile)}_PQS_PORT";

    public static string DefaultFor(LocalnetProfile profile, int port = DefaultPort) =>
        $"Host=localhost;Port={port};Database={DatabasePrefix}{LocalnetSlot.Name(profile)};Username=cnadmin;Password=supersafe";

    public static string Resolve(LocalnetProfile profile, IReadOnlyDictionary<string, string?> env) =>
        ConfiguredConnectionString(profile, env) ?? DefaultFor(profile, ConfiguredPort(profile, env) ?? DefaultPort);

    public static string? FindPortProblem(LocalnetProfile profile, IReadOnlyDictionary<string, string?> env)
    {
        if (ConfiguredConnectionString(profile, env) is not null)
            return null;

        var portEnv = PortEnvFor(profile);
        var configured = env.GetValueOrDefault(portEnv);
        return string.IsNullOrWhiteSpace(configured) || ConfiguredPort(profile, env) is not null
            ? null
            : $"{portEnv} is \"{configured}\", which is not a TCP port.\nSet it to the host port LocalNet publishes Postgres on (1-65535, for example 5433), or unset it to use {DefaultPort}.";
    }

    private static string? ConfiguredConnectionString(LocalnetProfile profile, IReadOnlyDictionary<string, string?> env) =>
        env.TryGetValue(EnvFor(profile), out var configured) && !string.IsNullOrWhiteSpace(configured)
            ? configured
            : null;

    private static int? ConfiguredPort(LocalnetProfile profile, IReadOnlyDictionary<string, string?> env) =>
        env.TryGetValue(PortEnvFor(profile), out var configured)
        && int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
        && port is >= 1 and <= 65535
            ? port
            : null;

    public static string Endpoint(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{builder.Host}:{builder.Port}";
    }

    public static string SlotEnvKeyOf(string connectionString)
    {
        var database = ContainerName(connectionString);
        return database.StartsWith(DatabasePrefix, StringComparison.Ordinal)
            ? database[DatabasePrefix.Length..].Replace('-', '_').ToUpperInvariant()
            : SlotPlaceholder;
    }

    public static string ContainerName(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;

    public static string ToPsqlConnInfo(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"host={builder.Host} port={builder.Port} dbname={builder.Database} user={builder.Username}";
    }
}
