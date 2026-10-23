# Agilico MSP Toolkit - DMG Packaging PowerShell Script
param(
    [string]$UvPath = "C:\Users\chris.kendall\.local\bin\uv.exe"
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$DmgScript = Join-Path $ProjectDir "build_dmg.py"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Packaging Agilico MSP Toolkit into macOS DMG Disk Images" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (Test-Path $UvPath) {
    & $UvPath run --with pycdlib python $DmgScript
} else {
    python -m pip install pycdlib --quiet
    python $DmgScript
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " DMG Packaging Complete!" -ForegroundColor Green
Write-Host " Disk Images available in: $(Join-Path $ProjectDir 'publish_output')" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
