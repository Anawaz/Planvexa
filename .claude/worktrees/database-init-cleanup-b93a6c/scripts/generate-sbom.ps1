# SPDX-FileCopyrightText: 2026 Planvexa contributors
# SPDX-License-Identifier: AGPL-3.0-only

param(
    [string]$OutputRoot = 'artifacts/sbom'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$outputPath = Join-Path $repoRoot $OutputRoot
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null

Push-Location apps/web
try {
    npm sbom --sbom-format cyclonedx | Set-Content (Join-Path $outputPath 'web.cyclonedx.json')
}
finally {
    Pop-Location
}

dotnet list Planvexa.slnx package --include-transitive --format json | Set-Content (Join-Path $outputPath 'dotnet-packages.json')

Write-Host "SBOM files generated in $outputPath"