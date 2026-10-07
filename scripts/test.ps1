# Full local test gate — the same steps CI runs, in the same order:
#   1. build the native C kernels (MSBuild + C++ workload)
#   2. build the xUnit test project
#   3. run the suite (TRX report; add -Coverage for coverlet)
# The pre-push hook calls this on Windows machines. Exit code is non-zero on
# any failure, so it can be used as a gate directly.
# Usage: .\scripts\test.ps1 [-Configuration Release] [-Coverage]
param(
    [string]$Configuration = "Release",
    [switch]$Coverage
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent

msbuild "$repo\src\Compositor.Native\Compositor.Native.vcxproj" "/p:Configuration=$Configuration" "/p:Platform=x64" /m /v:minimal
if ($LASTEXITCODE -ne 0) { throw "native build failed" }

dotnet build "$repo\src\Compositor.Tests\Compositor.Tests.csproj" -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "tests build failed" }

$testArgs = @(
    "test", "$repo\src\Compositor.Tests\Compositor.Tests.csproj",
    "-c", $Configuration, "--no-build",
    "--logger", "trx"
)
if ($Coverage) { $testArgs += "--collect:`"XPlat Code Coverage`"" }

& dotnet @testArgs
if ($LASTEXITCODE -ne 0) { throw "tests failed" }

Write-Host "All tests passed ($Configuration)."
