// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Net.Sockets;
using AwesomeAssertions;
using Npgsql;
using Xunit;

namespace MiniDemo.Tests;

public class PqsAvailabilityTests
{
    [Fact]
    public void IsPqsUnavailable_is_true_for_a_missing_database_error()
    {
        var missingDatabase = new PostgresException("Identifier not found", "FATAL", "FATAL", "3D000");

        PqsAvailability.IsPqsUnavailable(missingDatabase).Should().BeTrue();
    }

    [Fact]
    public void IsPqsUnavailable_is_true_for_a_connection_refused_socket_error()
    {
        PqsAvailability.IsPqsUnavailable(new SocketException((int)SocketError.ConnectionRefused))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("42883")]
    [InlineData("42P01")]
    public void IsPqsUnavailable_is_true_for_a_database_scribe_never_initialised(string sqlState)
    {
        var uninitialised = new PostgresException("relation or function missing", "ERROR", "ERROR", sqlState);

        PqsAvailability.IsPqsUnavailable(uninitialised).Should().BeTrue();
    }

    [Fact]
    public void IsPqsUnavailable_is_true_for_a_wrapped_undefined_function_error()
    {
        var undefined = new PostgresException("function active(text) does not exist", "ERROR", "ERROR", "42883");

        PqsAvailability.IsPqsUnavailable(new InvalidOperationException("outer", undefined)).Should().BeTrue();
    }

    [Fact]
    public void IsPqsUnavailable_is_false_for_an_unrelated_postgres_error()
    {
        var wrongPassword = new PostgresException("password authentication failed", "FATAL", "FATAL", "28P01");

        PqsAvailability.IsPqsUnavailable(wrongPassword).Should().BeFalse();
    }

    [Fact]
    public void IsPqsUnavailable_is_false_for_an_unrelated_socket_error()
    {
        PqsAvailability.IsPqsUnavailable(new SocketException((int)SocketError.TimedOut)).Should().BeFalse();
    }

    [Fact]
    public void IsPqsUnavailable_is_true_for_a_wrapped_missing_database_error()
    {
        var missingDatabase = new PostgresException("Identifier not found", "FATAL", "FATAL", "3D000");
        var wrapped = new InvalidOperationException("outer", missingDatabase);

        PqsAvailability.IsPqsUnavailable(wrapped).Should().BeTrue();
    }

    [Fact]
    public void IsPqsUnavailable_is_true_for_a_wrapped_connection_refused_error()
    {
        var wrapped = new InvalidOperationException(
            "outer", new SocketException((int)SocketError.ConnectionRefused));

        PqsAvailability.IsPqsUnavailable(wrapped).Should().BeTrue();
    }

    [Fact]
    public void IsPqsUnavailable_is_false_for_an_unrelated_exception()
    {
        PqsAvailability.IsPqsUnavailable(new InvalidOperationException("boom")).Should().BeFalse();
    }

    [Fact]
    public void Unavailable_requirement_failure_message_stays_on_one_line()
    {
        var cause = new InvalidOperationException("function active(text) does not exist\n\nPOSITION: 34");

        PqsRequirementNotMetException.Unavailable(cause).Message
            .Should().Be("PQS was never available (function active(text) does not exist POSITION: 34).");
    }
}
