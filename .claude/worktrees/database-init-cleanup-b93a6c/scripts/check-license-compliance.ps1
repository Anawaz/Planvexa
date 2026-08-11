# SPDX-FileCopyrightText: 2026 Planvexa contributors
# SPDX-License-Identifier: AGPL-3.0-only

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$requiredFiles = @(
    'LICENSE',
    'NOTICE',
    'ADDITIONAL_TERMS.md',
    'TRADEMARKS.md',
    'THIRD-PARTY-NOTICES.md'
)

foreach ($path in $requiredFiles) {
    if (-not (Test-Path $path)) {
        throw "Required legal file missing: $path"
    }
}

$license = Get-Content LICENSE -Raw
if ($license -notmatch 'GNU AFFERO GENERAL PUBLIC LICENSE' -or $license -notmatch 'Version 3, 19 November 2007') {
    throw 'LICENSE does not contain the official AGPLv3 text.'
}

$packageJson = Get-Content apps/web/package.json -Raw | ConvertFrom-Json
if ($packageJson.license -ne 'AGPL-3.0-only') {
    throw 'apps/web/package.json must declare AGPL-3.0-only.'
}

$buildProps = Get-Content Directory.Build.props -Raw
if ($buildProps -notmatch '<PackageLicenseExpression>AGPL-3\.0-only</PackageLicenseExpression>') {
    throw 'Directory.Build.props must declare PackageLicenseExpression AGPL-3.0-only.'
}

$appSettings = Get-Content apps/api/Planvexa.Api/appsettings.json -Raw | ConvertFrom-Json
if ($appSettings.Distribution.LicenseIdentifier -ne 'AGPL-3.0-only') {
    throw 'Distribution.LicenseIdentifier must be AGPL-3.0-only.'
}

if ([string]::IsNullOrWhiteSpace($appSettings.Distribution.SourceCodeUrl)) {
    throw 'Distribution.SourceCodeUrl must be configured.'
}

$dockerFiles = @('infrastructure/docker/api.Dockerfile', 'infrastructure/docker/web.Dockerfile')
foreach ($dockerFile in $dockerFiles) {
    $dockerText = Get-Content $dockerFile -Raw
    if ($dockerText -notmatch 'org\.opencontainers\.image\.licenses="AGPL-3\.0-only"') {
        throw "$dockerFile is missing the AGPL OCI label."
    }
}

$obsoleteMatches = rg -n --glob '!**/package-lock.json' --glob '!**/obj/**' --glob '!**/bin/**' --glob '!apps/web/.next/**' --glob '!THIRD-PARTY-NOTICES.md' --glob '!docs/legal/**' --glob '!scripts/check-license-compliance.ps1' 'GPL-2\.0|GPL v2|GNU General Public License Version 2|MIT License|Apache License|Apache-2\.0|proprietary|paid plan|commercial licence|all rights reserved|no commercial use|no resale|no redistribution|cannot sell|services only'
if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($obsoleteMatches | Out-String))) {
    throw "Obsolete or contradictory license language remains:`n$obsoleteMatches"
}

Write-Host 'License compliance checks passed.'