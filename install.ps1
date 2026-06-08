# install.ps1 — wire up cs on Windows (e.g. the ufo box).
# Requires: python3, git, fzf on PATH (bat optional for previews).
#   * put a cs / cs-sync shim on PATH
#   * configure git union-merge for the store
#   * register a daily sync Scheduled Task
# Idempotent. Run:  powershell -ExecutionPolicy Bypass -File install.ps1
$ErrorActionPreference = "Stop"
$Repo = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "cs install (Windows) — repo: $Repo"

# ── 1. shim on PATH ──────────────────────────────────────────────────────
$Bin = Join-Path $env:USERPROFILE "bin"
New-Item -ItemType Directory -Force -Path $Bin | Out-Null
Copy-Item -Force (Join-Path $Repo "cs.cmd") (Join-Path $Bin "cs.cmd")
# cs-sync shim
@"
@echo off
setlocal
set "CS_HOME=$Repo"
bash "$Repo/cs-sync"
"@ | Set-Content -Encoding ASCII (Join-Path $Bin "cs-sync.cmd")

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$Bin*") {
  [Environment]::SetEnvironmentVariable("Path", "$userPath;$Bin", "User")
  Write-Host "  + added $Bin to user PATH (restart shells to pick up)"
}
Write-Host "  OK shim: $Bin\cs.cmd"

# ── 2. git config ────────────────────────────────────────────────────────
if (Test-Path (Join-Path $Repo ".git")) {
  git -C $Repo config merge.union.driver "true"   | Out-Null
  git -C $Repo config rerere.enabled true          | Out-Null
  Write-Host "  OK git configured (union merge)"
}

# ── 3. daily sync Scheduled Task ─────────────────────────────────────────
$taskName = "cs-sync"
$action = New-ScheduledTaskAction -Execute (Join-Path $Bin "cs-sync.cmd")
$trigger = New-ScheduledTaskTrigger -Daily -At 9am
try {
  Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
    -Force -Description "Sync cs command cheatsheet" | Out-Null
  Write-Host "  OK scheduled task '$taskName' (daily 9am)"
} catch {
  Write-Host "  ! could not register scheduled task: $_"
}

Write-Host ""
Write-Host "cs installed. Run 'cs' to search, 'cs check' to review seeds."
Write-Host "Clipboard uses clip.exe automatically. For the Alt-/ zellij pane,"
Write-Host "the loki-term zellij config already binds it."
