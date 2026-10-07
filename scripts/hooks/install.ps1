# Enable the versioned git hooks in scripts/hooks/ for this clone
# (idempotent — safe to re-run after every fresh clone).
Set-Location (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)
git config core.hooksPath scripts/hooks
Write-Host "pre-push gate enabled (core.hooksPath = scripts/hooks)"
