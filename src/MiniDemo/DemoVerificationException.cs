// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace MiniDemo;

internal sealed class DemoVerificationException : InvalidOperationException
{
    public DemoVerificationException(string message) : base(message)
    {
    }
}
