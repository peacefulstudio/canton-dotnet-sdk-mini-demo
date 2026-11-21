// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

namespace MiniDemo;

internal sealed class DemoProgress
{
    public DemoStep? Current { get; private set; }

    public void Enter(string name, string? transport = null) => Current = new DemoStep(name, transport);
}
