// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Sockets;
using Npgsql;

namespace MiniDemo;

internal static class PqsAvailability
{
    private const string DatabaseNotFoundSqlState = "3D000";

    public static bool IsPqsUnavailable(Exception exception) =>
        ExceptionChain.Flatten(exception).Any(current =>
            IsMissingDatabase(current) || IsConnectionRefused(current));

    private static bool IsMissingDatabase(Exception exception) =>
        exception is PostgresException postgres && postgres.SqlState == DatabaseNotFoundSqlState;

    private static bool IsConnectionRefused(Exception exception) =>
        exception is SocketException socket && socket.SocketErrorCode == SocketError.ConnectionRefused;
}
