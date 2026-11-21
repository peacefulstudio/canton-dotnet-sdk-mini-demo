// Copyright 2026 Peaceful Studio OÜ
// SPDX-License-Identifier: Apache-2.0

using Daml.Runtime.Data;
using Daml.Runtime.Stdlib;

namespace MiniDemo;

internal sealed record AssetKey(string Issuer, string Name)
{
    public static AssetKey From(Tuple2<Party, string> key) => new(key._1.Value, key._2);

    public Tuple2<Party, string> ToDaml() => new(new Party(Issuer), Name);

    public override string ToString() => $"({Issuer}, {Name})";
}
