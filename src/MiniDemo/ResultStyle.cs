// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace MiniDemo;

/// <summary>How a transport's lane reads the typed outcome of a ledger call.</summary>
public enum ResultStyle
{
    /// <summary>The <c>Try...Async</c> outcome is pattern-matched into a value or a failure.</summary>
    OutcomePatternMatch,

    /// <summary>The <c>Try...Async</c> outcome is unwrapped with <c>OneOrThrowAsync</c>, which throws on failure.</summary>
    OrThrow,
}

internal static class ResultStyleExtensions
{
    public static string Describe(this ResultStyle style) => style switch
    {
        ResultStyle.OutcomePatternMatch => "TryCreateAsync, outcome pattern-matched",
        ResultStyle.OrThrow => "TryCreateAsync(...).OneOrThrowAsync",
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown result style."),
    };

    public static string DescribeRejection(this ResultStyle style) => style switch
    {
        ResultStyle.OutcomePatternMatch => "rejection read from the DamlError outcome",
        ResultStyle.OrThrow => "rejection read from the LedgerOperationException",
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown result style."),
    };
}
