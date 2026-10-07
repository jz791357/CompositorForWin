# Builds everything the way CI does: the native C kernels with MSBuild, then the
# .NET projects. From an elevated "Developer PowerShell for VS": .\scripts\build.ps1
# Or plain PowerShell after `dotnet` and VS Build Tools 2022 (C++ workload) install.
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent

msbuild "$repo\src\Compositor.Native\Compositor.Native.vcxproj" "/p:Configuration=$Configuration" "/p:Platform=x64" /m /v:minimal
if ($LASTEXITCODE -ne 0) { throw "native build failed" }

dotnet build "$repo\src\Compositor.Tests\Compositor.Tests.csproj" -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "tests build failed" }

dotnet test "$repo\src\Compositor.Tests\Compositor.Tests.csproj" -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw "tests failed" }

dotnet publish "$repo\src\Compositor\Compositor.csproj" -c $Configuration -r win-x64 --self-contained false -o "$repo\publish"
Copy-Item "$repo\src\Compositor.Native\out\Compositor.Native.dll" "$repo\publish\"
Write-Host "Built $repo\publish\Compositor.exe"
