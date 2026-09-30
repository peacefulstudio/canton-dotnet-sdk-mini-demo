#!/usr/bin/env pwsh
# Copyright (c) 2026 Peaceful Studio OÜ. All rights reserved.
# SPDX-License-Identifier: Apache-2.0
#
# Builds the Daml package and regenerates the committed C# bindings under
# src/MiniDemo.Contracts/Generated via dpm build + dpm codegen-cs.
# Windows-friendly twin of scripts/codegen.sh (Windows PowerShell 5.1 and PowerShell 7+).
# Requires:
#   - dpm  >= 1.0.20  (oci:// component URIs; see https://docs.digitalasset.com, then `dpm install 3.5.2`)
#   - java (JDK 17+, the codegen component's bundled JVM helper decodes the DAR)

#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$DamlDir    = Join-Path $RepoRoot 'daml'
$CodegenDir = Join-Path $RepoRoot 'codegen'
$OutDir     = Join-Path $RepoRoot 'src/MiniDemo.Contracts/Generated'
$DpmFloor   = [version]'1.0.20'
$DpmHint    = "install dpm (need >= $DpmFloor) then 'dpm install 3.5.2'"

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

$componentMatch = Select-String -Path (Join-Path $CodegenDir 'daml.yaml') -Pattern '^\s*-\s*(oci://\S*dpm-codegen-cs\S*)' | Select-Object -First 1
if (-not $componentMatch) { throw "no dpm-codegen-cs component pinned in $CodegenDir/daml.yaml" }
$componentUri = $componentMatch.Matches[0].Groups[1].Value
$componentName = $componentUri -replace '^oci://', '' -replace '[:@].*$', ''
$dpmHome = if ($env:DPM_HOME) { $env:DPM_HOME } else { Join-Path $HOME '.dpm' }
$componentCache = Join-Path $dpmHome "cache/components/$componentName"
$componentMarker = Join-Path $componentCache '.pinned-uri'
$cachedUri = if (Test-Path $componentMarker) { (Get-Content -Raw $componentMarker).Trim() } else { $null }
if ((Test-Path $componentCache) -and $cachedUri -ne $componentUri) {
    Write-Host "[codegen] cached $componentName does not match the pinned $(($componentUri -split '/')[-1]); refreshing it"
    # Workaround: dpm keys its component cache by component name, not tag or digest, so a bumped pin keeps running the old emitter.
    Remove-Item -Recurse -Force $componentCache
}

function Get-OrdinalRelativeFiles {
    param(
        [string] $BaseDir,
        [string] $RootDir,
        [string] $Filter = '*'
    )
    $byRel = @{}
    Get-ChildItem -Path $BaseDir -Recurse -File -Filter $Filter | ForEach-Object {
        $rel = $_.FullName.Substring($RootDir.Length + 1).Replace('\', '/')
        $byRel[$rel] = $_.FullName
    }
    $rels = [string[]] $byRel.Keys
    [Array]::Sort($rels, [StringComparer]::Ordinal)
    $rels | ForEach-Object { [pscustomobject]@{ Rel = $_; FullName = $byRel[$_] } }
}

function Get-ContentAddressedPackageName {
    $baseName = 'canton-mini-demo'
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $ms = New-Object System.IO.MemoryStream
    try {
        $damlSourceDir = Join-Path $DamlDir 'daml'
        Get-OrdinalRelativeFiles -BaseDir $damlSourceDir -RootDir $DamlDir | ForEach-Object {
            $header = $utf8NoBom.GetBytes("FILE:$($_.Rel)`n")
            $ms.Write($header, 0, $header.Length)
            $content = [System.IO.File]::ReadAllText($_.FullName) -replace "`r`n", "`n" -replace "`r", "`n"
            $bytes = $utf8NoBom.GetBytes($content)
            $ms.Write($bytes, 0, $bytes.Length)
        }

        Get-Content (Join-Path $DamlDir 'daml.yaml') |
            Where-Object { $_ -notmatch '^name:' } |
            ForEach-Object {
                $line = $utf8NoBom.GetBytes("$_`n")
                $ms.Write($line, 0, $line.Length)
            }

        $darsDir = Join-Path $DamlDir 'dars'
        if (Test-Path $darsDir) {
            Get-OrdinalRelativeFiles -BaseDir $darsDir -RootDir $DamlDir -Filter '*.dar' | ForEach-Object {
                $header = $utf8NoBom.GetBytes("DAR:$($_.Rel)`n")
                $ms.Write($header, 0, $header.Length)
                $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
                $ms.Write($bytes, 0, $bytes.Length)
            }
        }

        $ms.Position = 0
        $hashHex = -join ($sha256.ComputeHash($ms) | ForEach-Object { $_.ToString('x2') })
        return "$baseName-h$($hashHex.Substring(0, 12))"
    }
    finally {
        $ms.Dispose()
        $sha256.Dispose()
    }
}

$packageName = Get-ContentAddressedPackageName
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$damlYamlPath = Join-Path $DamlDir 'daml.yaml'
$updatedLines = (Get-Content $damlYamlPath) | ForEach-Object {
    if ($_ -match '^name:') { "name: $packageName" } else { $_ }
}
[System.IO.File]::WriteAllText($damlYamlPath, (($updatedLines -join "`n") + "`n"), $utf8NoBom)
Write-Host "[codegen] daml.yaml name -> $packageName"

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
        & dpm codegen-cs --dar $dar.FullName --out $tmpOut --namespace MiniDemo.Asset
        if ($LASTEXITCODE -ne 0) { throw "dpm codegen-cs failed (exit $LASTEXITCODE)" }
    }
    finally {
        Remove-Item Env:\DPM_AUTO_INSTALL -ErrorAction SilentlyContinue
        Pop-Location
    }

    if (-not (Get-ChildItem -Path $tmpOut -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue)) {
        throw "dpm codegen-cs exited 0 but produced no .cs under $tmpOut — refusing to replace $OutDir"
    }

    Set-Content -Path $componentMarker -Value $componentUri
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
