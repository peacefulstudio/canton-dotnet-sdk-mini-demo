// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace MiniDemo;

internal enum PqsRequirementStage
{
    ProjectionReport,
    PendingHoldingRead,
}

internal sealed class PqsRequirementNotMetException : InvalidOperationException
{
    public PqsRequirementNotMetException(
        string message, PqsRequirementStage stage = PqsRequirementStage.ProjectionReport) : base(message)
    {
        Stage = stage;
    }

    public PqsRequirementNotMetException(
        string message, Exception innerException, PqsRequirementStage stage = PqsRequirementStage.ProjectionReport)
        : base(message, innerException)
    {
        Stage = stage;
    }

    private PqsRequirementNotMetException(
        string message, Exception innerException, PqsRequirementStage stage, bool pqsWasUnavailable)
        : base(message, innerException)
    {
        Stage = stage;
        PqsWasUnavailable = pqsWasUnavailable;
    }

    public bool PqsWasUnavailable { get; }

    public PqsRequirementStage Stage { get; }

    public static PqsRequirementNotMetException Unavailable(
        Exception cause, PqsRequirementStage stage = PqsRequirementStage.ProjectionReport) =>
        new($"PQS was never available ({PqsAvailability.DescribeCause(cause)}).", cause, stage, pqsWasUnavailable: true);
}
