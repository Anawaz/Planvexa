<#
.SYNOPSIS
    Ensures the PostgreSQL databases and the maintenance role that Planvexa needs exist.

.DESCRIPTION
    Creates ONLY databases and roles. It never creates, starts, stops or otherwise manages the
    PostgreSQL server itself -- that is host-provided.

    Two callers share this one implementation:
      * the Aspire AppHost, as the "db-bootstrap" resource (pwsh -File scripts/ensure-databases.ps1),
        so pressing F5 in Visual Studio takes the same path as the CLI;
      * scripts/dev-up.ps1, which DOT-SOURCES it (". ./ensure-databases.ps1") so the connection-string
        helpers below stay available to it after the ensure step has run.

    Idempotent: existing databases and roles are left alone (the role password/grants are re-applied).

.PARAMETER ConnectionString
    Application connection string. Defaults to ConnectionStrings__Planvexa, then
    PLANVEXA_CONNECTION_STRING, then the documented local development default.

.PARAMETER AdminConnectionString
    Connection used to CREATE DATABASE. Defaults to PLANVEXA_ADMIN_CONNECTION_STRING, then
    -ConnectionString (the dev 'planvexa' login usually has CREATEDB).

.PARAMETER Databases
    Databases to ensure. Defaults to the database from -ConnectionString plus the Keycloak database.
#>
[CmdletBinding()]
param(
    [string]$ConnectionString,
    [string]$AdminConnectionString,
    [string[]]$Databases
)

$ErrorActionPreference = 'Stop'

function Mask-ConnectionString([string]$Value) {
    return ($Value -replace '(?i)(Password|Pwd)=[^;]*', '$1=***')
}

function Parse-ConnectionValue([string]$ConnectionString, [string]$Key, [string]$Default) {
    foreach ($part in $ConnectionString.Split(';')) {
        if ($part -match '^\s*([^=]+)=(.*)$') {
            $name = $matches[1].Trim()
            if ($name.Equals($Key, [StringComparison]::OrdinalIgnoreCase)) { return $matches[2].Trim() }
            if ($Key -eq 'Host' -and $name.Equals('Server', [StringComparison]::OrdinalIgnoreCase)) { return $matches[2].Trim() }
            if ($Key -eq 'Port' -and $name.Equals('Port', [StringComparison]::OrdinalIgnoreCase)) { return $matches[2].Trim() }
        }
    }
    return $Default
}

# Uses a local libpq client when one is on PATH (fast, and works without Docker); otherwise falls back
# to a throwaway postgres:18-alpine container, which has to reach the host as host.docker.internal.
function Invoke-PostgresClient([string]$AdminConnection, [string]$Tool, [string[]]$ToolArgs) {
    $hostName = Parse-ConnectionValue $AdminConnection 'Host' 'localhost'
    $port = Parse-ConnectionValue $AdminConnection 'Port' '5432'
    $user = Parse-ConnectionValue $AdminConnection 'Username' 'postgres'
    $password = Parse-ConnectionValue $AdminConnection 'Password' ''

    $local = Get-Command $Tool -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($local) {
        $previous = $env:PGPASSWORD
        $env:PGPASSWORD = $password
        try {
            return & $local.Path -h $hostName -p $port -U $user @ToolArgs
        } finally {
            $env:PGPASSWORD = $previous
        }
    }

    $dockerHost = if ($hostName -in @('localhost', '127.0.0.1', '::1')) { 'host.docker.internal' } else { $hostName }
    return docker run --rm -e PGPASSWORD=$password postgres:18-alpine $Tool -h $dockerHost -p $port -U $user @ToolArgs
}

function Invoke-PostgresScalar([string]$AdminConnection, [string]$Database, [string]$Sql) {
    $result = Invoke-PostgresClient $AdminConnection 'psql' @('-d', $Database, '-tAc', $Sql)
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL client command failed for database '$Database'." }
    return ($result | Out-String).Trim()
}

function Ensure-PostgresDatabase([string]$AdminConnection, [string]$DatabaseName) {
    $escaped = $DatabaseName.Replace("'", "''")
    $exists = Invoke-PostgresScalar $AdminConnection 'postgres' "SELECT 1 FROM pg_database WHERE datname = '$escaped';"
    if ($exists -eq '1') {
        Write-Host "Database '$DatabaseName' exists."
        return
    }

    Write-Host "Creating PostgreSQL database '$DatabaseName' using configured admin connection..."
    Invoke-PostgresClient $AdminConnection 'createdb' @($DatabaseName) | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not create PostgreSQL database '$DatabaseName'. Create it manually or provide PLANVEXA_ADMIN_CONNECTION_STRING with CREATEDB permission." }
}

function Ensure-MaintenanceRole([string]$AdminConnection, [string]$DatabaseName, [string]$RoleName, [string]$Password) {
    # Cross-tenant background work (outbox, notifications, recurring, export/retention) cannot run
    # under the RLS-bound application role: see MaintenanceConnection. Provision the privileged role
    # here. Throws when the connection lacks CREATEROLE; the caller decides how to handle that.
    $escapedRole = $RoleName.Replace("'", "''")
    $escapedPassword = $Password.Replace("'", "''")
    $sql = @"
DO `$`$ BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '$escapedRole') THEN
        CREATE ROLE $RoleName LOGIN BYPASSRLS PASSWORD '$escapedPassword';
    ELSE
        ALTER ROLE $RoleName LOGIN BYPASSRLS PASSWORD '$escapedPassword';
    END IF;
END `$`$;
DO `$`$
DECLARE s text;
BEGIN
    FOR s IN SELECT nspname FROM pg_namespace WHERE nspname NOT LIKE 'pg\_%' AND nspname <> 'information_schema' LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO $RoleName;', s);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO $RoleName;', s);
        EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO $RoleName;', s);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO $RoleName;', s);
        EXECUTE format('ALTER DEFAULT PRIVILEGES IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO $RoleName;', s);
    END LOOP;
END `$`$;
"@
    Invoke-PostgresScalar $AdminConnection $DatabaseName $sql | Out-Null
    Write-Host "Maintenance role '$RoleName' ensured on database '$DatabaseName'."
}

function Test-PostgresTcp([string]$Connection) {
    $hostName = Parse-ConnectionValue $Connection 'Host' 'localhost'
    $port = [int](Parse-ConnectionValue $Connection 'Port' '5432')
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $task = $client.ConnectAsync($hostName, $port)
        if (-not $task.Wait([TimeSpan]::FromSeconds(5))) { throw "Timed out connecting to ${hostName}:$port" }
    } finally {
        $client.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) { $ConnectionString = $env:ConnectionStrings__Planvexa }
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { $ConnectionString = $env:PLANVEXA_CONNECTION_STRING }
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { $ConnectionString = 'Host=localhost;Port=5432;Database=planvexa;Username=planvexa;Password=planvexa' }
$env:PLANVEXA_CONNECTION_STRING = $ConnectionString
$env:ConnectionStrings__Planvexa = $ConnectionString

if ([string]::IsNullOrWhiteSpace($AdminConnectionString)) {
    $AdminConnectionString = if ($env:PLANVEXA_ADMIN_CONNECTION_STRING) { $env:PLANVEXA_ADMIN_CONNECTION_STRING } else { $ConnectionString }
}

$planvexaDatabase = Parse-ConnectionValue $ConnectionString 'Database' 'planvexa'
$keycloakDatabase = if ($env:KEYCLOAK_DB_NAME) { $env:KEYCLOAK_DB_NAME } else { 'keycloak' }
if (-not $Databases -or $Databases.Count -eq 0) { $Databases = @($planvexaDatabase, $keycloakDatabase) }

Write-Host 'Checking existing PostgreSQL connectivity (TCP only; Planvexa never starts/stops PostgreSQL)...'
try {
    Test-PostgresTcp $ConnectionString
} catch {
    throw "PostgreSQL is not reachable for configured Planvexa connection string ($(Mask-ConnectionString $ConnectionString)). Start your local PostgreSQL server and create the configured login role, then retry. Details: $($_.Exception.Message)"
}

foreach ($database in $Databases) {
    Ensure-PostgresDatabase $AdminConnectionString $database
}

# Always attempted, not gated on PLANVEXA_ADMIN_CONNECTION_STRING: both appsettings.Development.json
# and the AppHost point ConnectionStrings:PlanvexaMaintenance at planvexa_maint, so a fresh machine
# without that env var used to boot against a role that did not exist. A connection without CREATEROLE
# is a supported single-role posture (MaintenanceConnection no-ops on a blank connection string), so a
# failure here warns rather than blocking startup.
$maintenancePassword = if ($env:PLANVEXA_MAINTENANCE_PASSWORD) { $env:PLANVEXA_MAINTENANCE_PASSWORD } else { 'planvexa_maint' }
try {
    Ensure-MaintenanceRole $AdminConnectionString $planvexaDatabase 'planvexa_maint' $maintenancePassword
} catch {
    Write-Warning "Could not provision the 'planvexa_maint' role: $($_.Exception.Message)"
    Write-Warning "Background sweeps will fall back to the application connection. Provide PLANVEXA_ADMIN_CONNECTION_STRING with CREATEROLE, or clear ConnectionStrings__PlanvexaMaintenance."
}

Write-Host 'PostgreSQL databases ensured.'
