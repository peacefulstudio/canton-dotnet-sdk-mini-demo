// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Outcomes;

namespace MiniDemo.Tests;

internal static class GrpcStatus
{
    public static TransportStatus Unavailable { get; } = new TransportStatus.Grpc(GrpcStatusCode.Unavailable);
    public static TransportStatus Internal { get; } = new TransportStatus.Grpc(GrpcStatusCode.Internal);
    public static TransportStatus Unauthenticated { get; } = new TransportStatus.Grpc(GrpcStatusCode.Unauthenticated);
}
