param(
    [string]$InstallPath = "$env:LOCALAPPDATA\PerformanceCheckerForWin"
)

$ErrorActionPreference = 'Stop'

Get-Process -Name 'PerformanceChecker' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $InstallPath) {
    Remove-Item -Path $InstallPath -Recurse -Force
    Write-Host "Uninstalled: $InstallPath"
} else {
    Write-Host "Install directory not found: $InstallPath"
}

Write-Host "No registry entries are removed because the installer does not create any."
