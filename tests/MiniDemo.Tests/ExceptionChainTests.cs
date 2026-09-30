// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace MiniDemo.Tests;

public class ExceptionChainTests
{
    [Fact]
    public void Flatten_yields_only_the_exception_itself_when_there_is_no_inner_exception()
    {
        var bare = new InvalidOperationException("boom");

        ExceptionChain.Flatten(bare).Should().ContainSingle().Which.Should().BeSameAs(bare);
    }

    [Fact]
    public void Flatten_walks_a_linear_inner_exception_chain_outer_to_inner()
    {
        var innermost = new TimeoutException("innermost");
        var middle = new IOException("middle", innermost);
        var outer = new InvalidOperationException("outer", middle);

        ExceptionChain.Flatten(outer).Should().Equal(outer, middle, innermost);
    }

    [Fact]
    public void Flatten_walks_every_branch_of_an_AggregateException()
    {
        var first = new InvalidOperationException("first");
        var second = new TimeoutException("second");
        var aggregate = new AggregateException(first, second);

        ExceptionChain.Flatten(aggregate).Should().BeEquivalentTo(new Exception[] { aggregate, first, second });
    }

    [Fact]
    public void Flatten_walks_inner_exceptions_of_branches_inside_an_AggregateException()
    {
        var innermost = new TimeoutException("innermost");
        var branch = new IOException("branch", innermost);
        var aggregate = new AggregateException(branch);

        ExceptionChain.Flatten(aggregate).Should().Equal(aggregate, branch, innermost);
    }

    [Fact]
    public void Flatten_does_not_revisit_an_exception_referenced_from_two_places()
    {
        var shared = new TimeoutException("shared");
        var aggregate = new AggregateException(shared, shared);

        ExceptionChain.Flatten(aggregate).Should().Equal(aggregate, shared);
    }
}
