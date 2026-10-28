# Stops Gwendoline, gives sleep back, and removes the app, the login entry, the settings and the Telegram file.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\Support\common.ps1"

Stop-Gwendoline
# Gwendoline stopped by force leaves the lid on Do nothing: put every power plan back as it was.
if (Test-Path $Exe) {
    # A windowed app: without -Wait, PowerShell would not wait for it.
    Start-Process $Exe -ArgumentList '--restore' -Wait
}

Remove-ItemProperty -Path $RunKey -Name 'Gwendoline' -ErrorAction SilentlyContinue
Remove-Item $Shortcut -Force -ErrorAction SilentlyContinue
Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $DataDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $SettingsKey -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'Gwendoline is uninstalled.'
