# Builds everything the way CI does — native C kernels with MSBuild, then the
# .NET projects — running the full test gate first.
# From an elevated "Developer PowerShell for VS": .\scripts\build.ps1
# Or plain PowerShell after `dotnet` and VS Build Tools 2022 (C++ workload) install.
# -SkipTests publishes only (CI is the authoritative gate; use sparingly).
param(
    [string]$Configuration = "Release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent

if (-not $SkipTests) {
    & "$PSScriptRoot\test.ps1" -Configuration $Configuration
}

dotnet publish "$repo\src\Compositor\Compositor.csproj" -c $Configuration -r win-x64 --self-contained false -o "$repo\publish"
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Copy-Item "$repo\src\Compositor.Native\out\Compositor.Native.dll" "$repo\publish\"
Write-Host "Built $repo\publish\Compositor.exe"
