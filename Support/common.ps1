# Paths and helpers shared by the install scripts.

$Root = Split-Path $PSScriptRoot -Parent
$BuildDir = Join-Path $Root 'build'
$InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\Gwendoline'
$Exe = Join-Path $InstallDir 'Gwendoline.exe'
$DataDir = Join-Path $env:LOCALAPPDATA 'Gwendoline'
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$SettingsKey = 'HKCU:\Software\Gwendoline'
$Shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Gwendoline.lnk'

function Assert-DotNet {
    $sdks = & dotnet --list-sdks 2>$null
    if (-not ($sdks | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 9 })) {
        throw 'Gwendoline needs the .NET 9 SDK or later: https://dotnet.microsoft.com/download'
    }
}

# Asks a running Gwendoline to quit: it gives sleep back and sends its closing message first.
# Whatever is still running after 15 seconds is stopped; the next launch gives sleep back then.
function Stop-Gwendoline {
    $quit = $null
    if ([System.Threading.EventWaitHandle]::TryOpenExisting('Local\Gwendoline.Quit', [ref]$quit)) {
        [void]$quit.Set()
        $quit.Dispose()
    }
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Process Gwendoline -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    Get-Process Gwendoline -ErrorAction SilentlyContinue | Stop-Process -Force
}
