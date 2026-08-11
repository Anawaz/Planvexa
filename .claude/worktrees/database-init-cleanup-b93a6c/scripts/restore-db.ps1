[CmdletBinding()]
param(
    [string]$InputPath,
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

if ([string]::IsNullOrWhiteSpace($InputPath)) {
    $latestBackup = Get-ChildItem -LiteralPath $BackupRoot -Filter '*.dump' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $latestBackup) { throw "No .dump backups found in $BackupRoot. Pass -InputPath to restore a specific file." }
    $InputPath = $latestBackup.FullName
} elseif (-not [System.IO.Path]::IsPathRooted($InputPath)) {
    $InputPath = Join-Path $RepoRoot $InputPath
}
$InputPath = [System.IO.Path]::GetFullPath($InputPath)
if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) { throw "Backup file not found: $InputPath" }

$hostName = Parse-ConnectionValue $ConnectionString 'Host' 'localhost'
$port = Parse-ConnectionValue $ConnectionString 'Port' '5432'
$db = Parse-ConnectionValue $ConnectionString 'Database' 'planvexa'
$user = Parse-ConnectionValue $ConnectionString 'Username' 'planvexa'
$password = Parse-ConnectionValue $ConnectionString 'Password' ''

Write-Host "Restoring Planvexa PostgreSQL backup from $InputPath ..."
if (Get-Command pg_restore -ErrorAction SilentlyContinue) {
    $env:PGPASSWORD = $password
    try { pg_restore -h $hostName -p $port -U $user -d $db --clean --if-exists --no-owner $InputPath } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
} else {
    $dockerHost = if ($hostName -in @('localhost','127.0.0.1','::1')) { 'host.docker.internal' } else { $hostName }
    docker run --rm -i --add-host=host.docker.internal:host-gateway -e PGPASSWORD=$password -v "${InputPath}:/backup.dump:ro" postgres:18-alpine pg_restore -h $dockerHost -p $port -U $user -d $db --clean --if-exists --no-owner /backup.dump
}

if ($LASTEXITCODE -ne 0) { throw "pg_restore failed with exit code $LASTEXITCODE." }
Write-Host 'Restore complete. PostgreSQL server was not started or stopped.'
