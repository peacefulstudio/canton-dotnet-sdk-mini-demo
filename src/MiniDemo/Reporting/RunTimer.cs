// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace MiniDemo;

internal static class RunTimer
{
    public static async Task<int> MeasureAsync(Func<Task<int>> run, TextWriter output, TimeProvider timeProvider)
    {
        var started = timeProvider.GetTimestamp();
        try
        {
            return await run();
        }
        finally
        {
            var elapsed = timeProvider.GetElapsedTime(started);
            output.WriteLine($"\nDone in {elapsed.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s");
        }
    }
}
