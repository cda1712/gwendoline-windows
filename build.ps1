# Builds build\Gwendoline.exe, a single file that runs on the .NET 9 Desktop Runtime.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Support\common.ps1"

Assert-DotNet
$runtime = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
& dotnet publish (Join-Path $Root 'src\Gwendoline\Gwendoline.csproj') -c Release -r $runtime --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -o $BuildDir --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
Write-Host "Built $BuildDir\Gwendoline.exe"
