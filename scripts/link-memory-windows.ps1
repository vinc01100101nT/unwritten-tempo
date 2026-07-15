#!/usr/bin/env pwsh
# Link this machine's Claude Code memory path to the repo's committed memory folder.
# Run ONCE per machine after cloning:  pwsh ./scripts/link-memory.ps1
# Idempotent + safe to re-run. Backs up any real folder already at the target.
$ErrorActionPreference = 'Stop'

$repo    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repoMem = Join-Path $repo '.claude\memory'
if (-not (Test-Path $repoMem)) { throw "Repo memory folder not found: $repoMem" }

# Claude Code names its per-project state dir by the repo's absolute path with
# every ':' '\' '/' replaced by '-' (case preserved). Derive it from THIS clone,
# so the link is correct no matter where the repo was cloned.
$encoded       = ($repo -replace '[:\\/]', '-')
$harness       = Join-Path $HOME ".claude\projects\$encoded\memory"
$harnessParent = Split-Path $harness -Parent

New-Item -ItemType Directory -Force -Path $harnessParent | Out-Null

if (Test-Path $harness) {
    $item = Get-Item $harness -Force
    if ($item.LinkType) {
        # Already a link -> delete just the reparse point (never follow into target).
        [System.IO.Directory]::Delete($harness, $false)
    } else {
        $bak = "$harness.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
        Move-Item $harness $bak
        Write-Host "Backed up existing memory folder -> $bak"
    }
}

New-Item -ItemType Junction -Path $harness -Target $repoMem | Out-Null
Write-Host "Linked Claude Code memory:"
Write-Host "  $harness"
Write-Host "    -> $repoMem"
