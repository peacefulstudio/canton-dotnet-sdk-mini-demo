// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace MiniDemo;

internal static class AmountFormat
{
    public static string Display(decimal amount) => amount.ToString("0.##########", CultureInfo.InvariantCulture);
}
