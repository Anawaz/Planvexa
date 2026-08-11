[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$RunRoot = Join-Path $RepoRoot '.run'
$PidPath = Join-Path $RunRoot 'apphost.json'

# Aspire's orchestrator (DCP) labels every container it creates. Containers are only removed once
# their creating DCP process is gone, so an AppHost running in parallel keeps its own and nothing
# outside Aspire is ever touched. PostgreSQL is host-provided and never in scope.
$DcpNameLabel = 'com.microsoft.developer.usvc-dev.name'
$DcpCreatorLabel = 'com.microsoft.developer.usvc-dev.creatorProcessId'
$DcpPersistentLabel = 'com.microsoft.developer.usvc-dev.persistent'

function Test-DockerAvailable {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { return $false }
    docker info *> $null
    return $LASTEXITCODE -eq 0
}

function Get-ContainerLabel([string]$ContainerId, [string]$Label) {
    $value = docker inspect $ContainerId --format "{{index .Config.Labels `"$Label`"}}" 2>$null
    return ($value | Out-String).Trim()
}

function Remove-OrphanedAspireContainers {
    if (-not (Test-DockerAvailable)) {
        Write-Host 'Docker is not available; skipping Aspire container cleanup.'
        return
    }

    $ids = @(docker ps -aq --filter "label=$DcpNameLabel" 2>$null | Where-Object { $_ })
    if ($ids.Count -eq 0) {
        Write-Host 'No Aspire-managed containers found.'
        return
    }

    $removed = @()
    foreach ($id in $ids) {
        # DCP keeps persistent resources alive between runs on purpose.
        if ((Get-ContainerLabel $id $DcpPersistentLabel) -eq 'true') { continue }

        # A live creator process means another AppHost still owns this container.
        $creator = Get-ContainerLabel $id $DcpCreatorLabel
        if ($creator -match '^\d+$' -and (Get-Process -Id ([int]$creator) -ErrorAction SilentlyContinue)) { continue }

        $name = Get-ContainerLabel $id $DcpNameLabel
        docker rm -f $id *> $null
        if ($LASTEXITCODE -eq 0) {
            $removed += $(if ($name) { $name } else { $id })
        }
    }

    if ($removed.Count -gt 0) {
        Write-Host "Removed orphaned Aspire containers: $($removed -join ', ')"
    } else {
        Write-Host 'No orphaned Aspire containers to remove.'
    }
}

function Stop-AppHost([int]$AppHostPid) {
    # Ask the process tree to exit first so DCP can tear its own containers down. Console hosts
    # usually refuse a polite kill, so fall back to a forced stop and sweep afterwards.
    taskkill /PID $AppHostPid /T *> $null

    $deadline = (Get-Date).AddSeconds(8)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Process -Id $AppHostPid -ErrorAction SilentlyContinue)) { return }
        Start-Sleep -Milliseconds 250
    }

    $process = Get-Process -Id $AppHostPid -ErrorAction SilentlyContinue
    if ($process) {
        Stop-Process -Id $AppHostPid -Force -ErrorAction SilentlyContinue
        $process.WaitForExit(15000) | Out-Null
    }
}

if (Test-Path $PidPath) {
    $state = Get-Content $PidPath -Raw | ConvertFrom-Json
    $appHostPid = [int]$state.pid
    if (Get-Process -Id $appHostPid -ErrorAction SilentlyContinue) {
        Write-Host "Stopping Planvexa Aspire AppHost (PID $appHostPid)..."
        Stop-AppHost $appHostPid
    } else {
        Write-Host "Removing stale Planvexa AppHost PID file for PID $appHostPid."
    }

    Remove-Item $PidPath -Force -ErrorAction SilentlyContinue
} else {
    Write-Host 'No Planvexa AppHost PID file found; sweeping for orphaned containers anyway.'
}

# DCP exits with its parent, but not instantly; give it a moment before deciding what is orphaned.
Start-Sleep -Seconds 2
Remove-OrphanedAspireContainers

Write-Host 'Planvexa AppHost stopped. PostgreSQL was not stopped or modified.'
