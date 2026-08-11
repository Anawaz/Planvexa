# SPDX-FileCopyrightText: 2026 Planvexa contributors
# SPDX-License-Identifier: AGPL-3.0-only

param(
    [string]$OutputRoot = 'artifacts/release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$outputPath = Join-Path $repoRoot $OutputRoot
$legalPath = Join-Path $outputPath 'legal'
$apiPath = Join-Path $outputPath 'api'
$webPath = Join-Path $outputPath 'web'

Remove-Item $outputPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $legalPath | Out-Null
New-Item -ItemType Directory -Path $apiPath | Out-Null
New-Item -ItemType Directory -Path $webPath | Out-Null

$legalFiles = @('LICENSE', 'NOTICE', 'ADDITIONAL_TERMS.md', 'TRADEMARKS.md', 'THIRD-PARTY-NOTICES.md')
foreach ($file in $legalFiles) {
    Copy-Item $file (Join-Path $legalPath $file)
}

$gitSha = (git rev-parse HEAD).Trim()
$buildDateUtc = (Get-Date).ToUniversalTime().ToString('o')

$metadata = [ordered]@{
    product = 'Planvexa'
    license = 'AGPL-3.0-only'
    source = 'https://github.com/Anawaz/Planvexa'
    commitSha = $gitSha
    buildDateUtc = $buildDateUtc
}

$metadata | ConvertTo-Json | Set-Content (Join-Path $outputPath 'distribution-metadata.json')

if (Test-Path 'apps/api/Planvexa.Api/bin/Release/net10.0/publish') {
    Copy-Item 'apps/api/Planvexa.Api/bin/Release/net10.0/publish/*' $apiPath -Recurse -Force
}

if (Test-Path 'apps/web/.next/standalone') {
    Copy-Item 'apps/web/.next/standalone/*' $webPath -Recurse -Force
}

foreach ($destination in @($apiPath, $webPath)) {
    $destinationLegal = Join-Path $destination 'legal'
    New-Item -ItemType Directory -Path $destinationLegal -Force | Out-Null
    foreach ($file in $legalFiles) {
        Copy-Item $file (Join-Path $destinationLegal $file) -Force
    }
}

Write-Host "Release artifacts prepared in $outputPath"