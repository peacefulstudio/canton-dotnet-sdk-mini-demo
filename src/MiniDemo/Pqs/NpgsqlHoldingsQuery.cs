// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Canton.Ledger.Abstractions;
using Daml.Runtime;
using Daml.Runtime.Contracts;
using Daml.Runtime.Serialization;
using Npgsql;
using Splice.Api.Token.HoldingV2;

namespace MiniDemo;

internal static class NpgsqlHoldingsQuery
{
    public static async Task<IReadOnlyList<InterfaceContract<IHolding, HoldingView>>> RunAsync(
        string connectionString, IReadOnlyCollection<string> contractIds, CancellationToken ct)
    {
        if (contractIds.Count == 0)
            return [];

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(
            "SELECT contract_id, payload FROM active(@typeId) WHERE contract_id = ANY(@contractIds)", connection);
        command.Parameters.AddWithValue("@typeId", GetDamlTypeId<IHolding>());
        command.Parameters.AddWithValue("@contractIds", contractIds.ToArray());

        try
        {
            var results = new List<InterfaceContract<IHolding, HoldingView>>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                results.Add(Deserialize(reader.GetString(0), reader.GetString(1)));

            return results;
        }
        catch (PostgresException ex) when (IsTypeNotFoundError(ex))
        {
            return [];
        }
    }

    private static InterfaceContract<IHolding, HoldingView> Deserialize(string contractId, string payloadJson) =>
        new(new ContractId<IHolding>(contractId), HoldingView.FromRecord(DamlLfJsonReader.ReadRecord<HoldingView>(payloadJson)));

    private static string GetDamlTypeId<T>() where T : IDamlType
    {
        var descriptor = T.DamlTypeId;
        if (string.IsNullOrEmpty(descriptor.PackageName))
            throw new InvalidOperationException(
                $"Daml type '{typeof(T).FullName}' has an empty static PackageName; "
                + "cannot build the package-name identifier required by PQS active().");

        return $"{descriptor.PackageName}:{descriptor.Identifier.ModuleName}:{descriptor.Identifier.EntityName}";
    }

    // Workaround for PQS's active() function: it raises P0001 "Identifier not found" when no
    // contracts of a given type have ever been created, which is semantically "no results"
    // rather than an error (same behaviour Canton.Ledger.Pqs.Client's PqsClient works around).
    private static bool IsTypeNotFoundError(PostgresException ex) =>
        ex.SqlState == "P0001" && ex.MessageText.StartsWith("Identifier not found:", StringComparison.Ordinal);
}
