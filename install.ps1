# Builds Gwendoline from source, installs it for this user and starts it at every login.
# No administrator rights: Windows lets any user change what closing the lid does.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Support\common.ps1"

Assert-DotNet
& (Join-Path $Root 'build.ps1')

Stop-Gwendoline
New-Item -ItemType Directory -Force $InstallDir | Out-Null
Copy-Item (Join-Path $BuildDir '*') $InstallDir -Recurse -Force

# Starts at login. The supervisor in Gwendoline.exe starts the tray app again after a crash.
Set-ItemProperty -Path $RunKey -Name 'Gwendoline' -Value "`"$Exe`""

# In the Start menu, to start it again after Quit.
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($Shortcut)
$link.TargetPath = $Exe
$link.WorkingDirectory = $InstallDir
$link.Description = 'Keeps the PC awake with the lid closed'
$link.Save()

Start-Process $Exe

# Windows 11 hides new tray icons behind the arrow. Show this one on the taskbar.
$deadline = (Get-Date).AddSeconds(10)
$promoted = $false
while (-not $promoted -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    Get-ChildItem 'HKCU:\Control Panel\NotifyIconSettings' -ErrorAction SilentlyContinue | ForEach-Object {
        if ((Get-ItemProperty $_.PSPath).ExecutablePath -like '*\Programs\Gwendoline\Gwendoline.exe') {
            Set-ItemProperty $_.PSPath -Name 'IsPromoted' -Value 1 -Type DWord
            $promoted = $true
        }
    }
}

Write-Host 'Gwendoline is installed and running in the taskbar.'
if (-not $promoted) {
    Write-Host 'If its icon is hidden behind the arrow: Settings > Personalization > Taskbar > Other system tray icons > Gwendoline.'
}
