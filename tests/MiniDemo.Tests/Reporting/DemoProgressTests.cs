// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace MiniDemo.Tests;

public class DemoProgressTests
{
    [Fact]
    public void Current_is_empty_before_any_step_is_entered()
    {
        new DemoProgress().Current.Should().BeNull();
    }

    [Fact]
    public void Enter_records_the_step_name_and_transport()
    {
        var progress = new DemoProgress();

        progress.Enter("propose", "gRPC");

        progress.Current.Should().Be(new DemoStep("propose", "gRPC"));
    }

    [Fact]
    public void Enter_replaces_the_previous_step_and_allows_a_step_without_a_transport()
    {
        var progress = new DemoProgress();
        progress.Enter("propose", "gRPC");

        progress.Enter("bootstrap");

        progress.Current.Should().Be(new DemoStep("bootstrap", null));
    }
}
