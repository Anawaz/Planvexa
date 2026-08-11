[CmdletBinding()]
param(
    [string]$OutputPath,
    [string]$ConnectionString = $(if ($env:PLANVEXA_CONNECTION_STRING) { $env:PLANVEXA_CONNECTION_STRING } else { 'Host=localhost;Port=5432;Database=planvexa;Username=planvexa;Password=planvexa' })
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$BackupRoot = Join-Path $RepoRoot '.data\backups'

function Parse-ConnectionValue([string]$Cs, [string]$Key, [string]$Default) {
    foreach ($part in $Cs.Split(';')) {
        if ($part -match '^\s*([^=]+)=(.*)$') {
            $name = $matches[1].Trim()
            if ($name.Equals($Key, [StringComparison]::OrdinalIgnoreCase)) { return $matches[2].Trim() }
            if ($Key -eq 'Host' -and $name.Equals('Server', [StringComparison]::OrdinalIgnoreCase)) { return $matches[2].Trim() }
            if ($Key -eq 'Username' -and $name.Equals('User ID', [StringComparison]::OrdinalIgnoreCase)) { return $matches[2].Trim() }
        }
    }
    return $Default
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputPath = Join-Path $BackupRoot "planvexa-$timestamp.dump"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $RepoRoot $OutputPath
}

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutputPath) | Out-Null

$hostName = Parse-ConnectionValue $ConnectionString 'Host' 'localhost'
$port = Parse-ConnectionValue $ConnectionString 'Port' '5432'
$db = Parse-ConnectionValue $ConnectionString 'Database' 'planvexa'
$user = Parse-ConnectionValue $ConnectionString 'Username' 'planvexa'
$password = Parse-ConnectionValue $ConnectionString 'Password' ''

Write-Host "Creating Planvexa PostgreSQL backup at $OutputPath ..."
if (Get-Command pg_dump -ErrorAction SilentlyContinue) {
    $env:PGPASSWORD = $password
    try { pg_dump -h $hostName -p $port -U $user -d $db -Fc -f $OutputPath } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
} else {
    $dockerHost = if ($hostName -in @('localhost','127.0.0.1','::1')) { 'host.docker.internal' } else { $hostName }
    # Docker's bind-mount creates the host side as a DIRECTORY when the source path doesn't already
    # exist, which then makes pg_dump fail inside the container with "Is a directory" trying to write
    # to it as a file. Pre-creating an empty file here is what makes the bind mount a file mount.
    New-Item -ItemType File -Force -Path $OutputPath | Out-Null
    docker run --rm --add-host=host.docker.internal:host-gateway -e PGPASSWORD=$password -v "${OutputPath}:/backup.dump" postgres:18-alpine pg_dump -h $dockerHost -p $port -U $user -d $db -Fc -f /backup.dump
}

if ($LASTEXITCODE -ne 0) { throw "pg_dump failed with exit code $LASTEXITCODE." }
Write-Host "Backup complete: $OutputPath"
