// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Xunit;

namespace MiniDemo.Tests;

public class DamlFailureMessageTests
{
    [Fact]
    public void Summarize_keeps_only_the_text_after_the_error_category_of_an_interpretation_error()
    {
        const string raw =
            "DAML_FAILURE(9,21d28f9a): Interpretation error: Error: User failure: " +
            "UNHANDLED_EXCEPTION/DA.Exception.AssertionFailed:AssertionFailed (error category 9): " +
            "the input holdings cover the amount";

        DamlFailureMessage.Summarize(raw).Should().Be("the input holdings cover the amount");
    }

    [Fact]
    public void Summarize_returns_the_full_message_when_there_is_no_error_category_marker()
    {
        const string raw = "CONTRACT_NOT_FOUND(11,ab12cd34): Contract could not be found with id 00abc";

        DamlFailureMessage.Summarize(raw).Should().Be(raw);
    }

    [Fact]
    public void Summarize_returns_the_full_message_when_nothing_follows_the_error_category_marker()
    {
        const string raw = "Interpretation error: User failure: Foo (error category 9): ";

        DamlFailureMessage.Summarize(raw).Should().Be(raw);
    }

    [Fact]
    public void Summarize_keeps_a_message_that_itself_mentions_an_error_category()
    {
        const string raw = "x (error category 9): outer (error category 7): inner detail";

        DamlFailureMessage.Summarize(raw).Should().Be("outer (error category 7): inner detail");
    }
}
