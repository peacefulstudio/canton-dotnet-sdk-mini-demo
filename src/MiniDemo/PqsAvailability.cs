// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Sockets;
using Npgsql;

namespace MiniDemo;

internal static class PqsAvailability
{
    private static readonly string[] UnavailableSqlStates = ["3D000", "42883", "42P01"];

    public static bool IsPqsUnavailable(Exception exception) =>
        ExceptionChain.Flatten(exception).Any(current =>
            IsUnavailableDatabase(current) || IsConnectionRefused(current));

    public static string DescribeCause(Exception cause) =>
        string.Join(' ', cause.Message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsUnavailableDatabase(Exception exception) =>
        exception is PostgresException postgres && UnavailableSqlStates.Contains(postgres.SqlState);

    private static bool IsConnectionRefused(Exception exception) =>
        exception is SocketException socket && socket.SocketErrorCode == SocketError.ConnectionRefused;
}
