#!/usr/bin/env pwsh
# Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
#
# Builds the Daml package and regenerates the committed C# bindings under
# src/MiniDemo.Contracts/Generated via dpm build + dpm codegen-cs.
# Windows-friendly twin of scripts/codegen.sh (Windows PowerShell 5.1 and PowerShell 7+).
# Requires:
#   - dpm  >= 1.0.20  (oci:// component URIs; see https://docs.digitalasset.com, then `dpm install 3.4.11`)
#   - java (JDK 17+, the codegen component's bundled JVM helper decodes the DAR)

#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$DamlDir    = Join-Path $RepoRoot 'daml'
$CodegenDir = Join-Path $RepoRoot 'codegen'
$OutDir     = Join-Path $RepoRoot 'src/MiniDemo.Contracts/Generated'
$DpmFloor   = [version]'1.0.20'
$DpmHint    = "install dpm (need >= $DpmFloor) then 'dpm install 3.4.11'"

function Assert-OnPath([string] $Command, [string] $Hint) {
    if (-not (Get-Command $Command -ErrorAction SilentlyContinue)) {
        throw "'$Command' not found on PATH — $Hint"
    }
}

Assert-OnPath 'dpm'  $DpmHint
Assert-OnPath 'java' 'JDK 17+ required'

$dpmVersionOutput = (& dpm --version 2>$null) -join "`n"
$versionMatch = [regex]::Match($dpmVersionOutput, '(?m)^version:\s*(\S+)')
if (-not $versionMatch.Success) {
    Write-Warning "could not parse 'dpm --version' output — skipping the >= $DpmFloor floor check"
}
else {
    $reported = $versionMatch.Groups[1].Value
    $core = [regex]::Match($reported, '^\d+(\.\d+)+')
    if ($core.Success -and [version] $core.Value -lt $DpmFloor) {
        throw "'dpm' $reported is too old — $DpmHint"
    }
}

Write-Host "[codegen] dpm build $DamlDir"
Push-Location $DamlDir
try {
    & dpm build
    if ($LASTEXITCODE -ne 0) { throw "dpm build failed (exit $LASTEXITCODE)" }
}
finally {
    Pop-Location
}

$dar = Get-ChildItem -Path (Join-Path $DamlDir '.daml/dist') -Filter '*.dar' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $dar) { throw "no .dar produced under $DamlDir/.daml/dist" }
Write-Host "[codegen] built $($dar.FullName)"

Write-Host "[codegen] dpm codegen-cs -> $OutDir"
$tmpOut = "$OutDir.tmp.$([System.IO.Path]::GetRandomFileName())"
New-Item -ItemType Directory -Path $tmpOut -Force | Out-Null
try {
    Push-Location $CodegenDir
    try {
        $env:DPM_AUTO_INSTALL = 'true'
        & dpm codegen-cs --dar $dar.FullName --out $tmpOut --contract-identifiers
        if ($LASTEXITCODE -ne 0) { throw "dpm codegen-cs failed (exit $LASTEXITCODE)" }
    }
    finally {
        Remove-Item Env:\DPM_AUTO_INSTALL -ErrorAction SilentlyContinue
        Pop-Location
    }

    if (-not (Get-ChildItem -Path $tmpOut -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue)) {
        throw "dpm codegen-cs exited 0 but produced no .cs under $tmpOut — refusing to replace $OutDir"
    }

    if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
    New-Item -ItemType Directory -Path (Split-Path -Parent $OutDir) -Force | Out-Null
    Move-Item -Path $tmpOut -Destination $OutDir
    $tmpOut = $null
}
finally {
    if ($tmpOut -and (Test-Path $tmpOut)) { Remove-Item -Recurse -Force $tmpOut }
}

Write-Host "[codegen] done. Generated:"
Get-ChildItem -Path $OutDir -Recurse -Filter '*.cs' | ForEach-Object { $_.FullName }
