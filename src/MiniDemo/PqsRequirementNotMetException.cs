// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace MiniDemo;

internal sealed class PqsRequirementNotMetException : InvalidOperationException
{
    public PqsRequirementNotMetException(string message) : base(message)
    {
    }

    public PqsRequirementNotMetException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
