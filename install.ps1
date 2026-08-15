param(
    [string]$InstallPath = "$env:LOCALAPPDATA\PerformanceCheckerForWin"
)

$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null

Get-ChildItem -Path $source -File | Where-Object {
    $_.Name -notin @('install.ps1', 'uninstall.ps1') -and $_.Extension -notin @('.zip')
} | Copy-Item -Destination $InstallPath -Force

Write-Host "Installed to: $InstallPath"
Write-Host "No registry entries are created by this script."
Write-Host "Start: $InstallPath\PerformanceChecker.exe"
