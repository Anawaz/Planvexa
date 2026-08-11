[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$RunRoot = Join-Path $RepoRoot '.run'
$LogRoot = Join-Path $RunRoot 'logs'
$PidPath = Join-Path $RunRoot 'apphost.json'
$EnvPath = Join-Path $RepoRoot '.env'

function Import-DotEnv {
    if (-not (Test-Path $EnvPath)) { return }
    Get-Content $EnvPath | ForEach-Object {
        $line = $_.Trim()
        if ($line.Length -eq 0 -or $line.StartsWith('#') -or -not $line.Contains('=')) { return }
        $name, $value = $line.Split('=', 2)
        if (-not [string]::IsNullOrWhiteSpace($name) -and -not [Environment]::GetEnvironmentVariable($name)) {
            [Environment]::SetEnvironmentVariable($name.Trim(), $value.Trim().Trim('"'), 'Process')
        }
    }
}

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Wait-Http([string]$Url, [int]$Seconds = 90) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) { return }
        } catch {
            Start-Sleep -Seconds 2
        }
    } while ((Get-Date) -lt $deadline)
    throw "Timed out waiting for $Url. Check .run/logs/apphost.out.log and the Aspire dashboard."
}

function Stop-StartedProcess {
    param([int]$ProcessId)
    try {
        $proc = Get-Process -Id $ProcessId -ErrorAction Stop
        Stop-Process -Id $proc.Id -ErrorAction Stop
        $proc.WaitForExit(15000)
    } catch {
        Write-Warning "Could not stop process ${ProcessId}: $($_.Exception.Message)"
    }
}

Import-DotEnv
Require-Command dotnet
Require-Command node
Require-Command npm
Require-Command docker

$dotnetVersion = (& dotnet --version).Trim()
if (-not ($dotnetVersion -match '^10\.')) { throw ".NET 10 SDK is required. Found $dotnetVersion." }
$nodeVersion = (& node --version).TrimStart('v')
if ([int]($nodeVersion.Split('.')[0]) -lt 24) { throw "Node 24+ is required. Found $nodeVersion." }

# Dot-sourced, not invoked: the ensure step runs exactly as the AppHost's "db-bootstrap" resource runs
# it, and its connection-string helpers stay in scope for the Keycloak defaults below.
. (Join-Path $PSScriptRoot 'ensure-databases.ps1')

# $AdminConnectionString / $keycloakDatabase come from the dot-sourced ensure-databases.ps1.
if (-not $env:KEYCLOAK_DB_USERNAME) { $env:KEYCLOAK_DB_USERNAME = Parse-ConnectionValue $AdminConnectionString 'Username' 'postgres' }
if (-not $env:KEYCLOAK_DB_PASSWORD) { $env:KEYCLOAK_DB_PASSWORD = Parse-ConnectionValue $AdminConnectionString 'Password' '' }
if (-not $env:KEYCLOAK_DB_NAME) { $env:KEYCLOAK_DB_NAME = $keycloakDatabase }
New-Item -ItemType Directory -Force $RunRoot, $LogRoot | Out-Null

if (Test-Path $PidPath) {
    $state = Get-Content $PidPath -Raw | ConvertFrom-Json
    $existing = Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Planvexa Aspire AppHost is already running (PID $($state.pid))."
        Write-Host "Dashboard: $($state.dashboardUrl)"
        return
    }
    Remove-Item $PidPath -Force
}

Write-Host 'Restoring and building solution...'
Push-Location $RepoRoot
try {
    dotnet restore Planvexa.slnx --nologo
    dotnet build Planvexa.slnx -c Release --nologo
    if (-not (Test-Path (Join-Path $RepoRoot 'apps\web\node_modules'))) {
        Push-Location (Join-Path $RepoRoot 'apps\web')
        try { npm ci } finally { Pop-Location }
    }

    $outLog = Join-Path $LogRoot 'apphost.out.log'
    $errLog = Join-Path $LogRoot 'apphost.err.log'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:PLANVEXA_WEB_URL = if ($env:PLANVEXA_WEB_URL) { $env:PLANVEXA_WEB_URL } else { 'http://localhost:3000' }

    Write-Host 'Starting Aspire AppHost...'
    $process = Start-Process -FilePath 'dotnet' -ArgumentList @('run','--project','apps\apphost\Planvexa.AppHost.csproj','--configuration','Release','--no-build') -WorkingDirectory $RepoRoot -RedirectStandardOutput $outLog -RedirectStandardError $errLog -PassThru
    $dashboardUrl = 'http://localhost:15096'
    [pscustomobject]@{
        pid = $process.Id
        startedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
        dashboardUrl = $dashboardUrl
        webUrl = 'http://localhost:3000'
        apiUrl = 'http://localhost:8080'
        logs = @{ stdout = $outLog; stderr = $errLog }
    } | ConvertTo-Json -Depth 5 | Set-Content $PidPath -Encoding UTF8

    Write-Host ''
    Write-Host 'Planvexa startup initiated via Aspire AppHost.'
    Write-Host "  Aspire dashboard: $dashboardUrl"
    Write-Host '  Web:             http://localhost:3000'
    Write-Host '  API:             http://localhost:8080'
    Write-Host '  API docs:        http://localhost:8080/scalar/v1'
    Write-Host '  Keycloak:        http://localhost:8081'
    Write-Host '  Mailpit:         http://localhost:8025'
    Write-Host '  Jaeger:          http://localhost:16686'
    Write-Host "  Logs:            $LogRoot"
    Write-Host '  Development users: owner@planvexa.local, admin@planvexa.local, member@planvexa.local, guest@planvexa.local'
    Write-Host '  Default dev password source: PLANVEXA_DEV_PASSWORD (fallback is documented development-only default).'
    Write-Host 'Waiting for API and web readiness...'
    Wait-Http 'http://localhost:8080/health/live' 120
    Wait-Http 'http://localhost:8080/health/ready' 120
    Wait-Http 'http://localhost:3000/login' 120
    Write-Host 'Planvexa API and web are responding.'
} catch {
    if ($process -and -not $process.HasExited) { Stop-StartedProcess -ProcessId $process.Id }
    if (Test-Path $PidPath) { Remove-Item $PidPath -Force }
    if (Test-Path $errLog) { Get-Content $errLog -Tail 80 | Write-Error }
    throw
} finally {
    Pop-Location
}






