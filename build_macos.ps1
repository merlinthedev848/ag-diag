# Agilico MSP Toolkit - macOS Multi-Architecture Build Script
param(
    [string]$Configuration = "Release",
    [ValidateSet("All", "Full", "Lite")]
    [string]$Edition = "All"
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$OutputDir = Join-Path $ProjectDir "publish_output"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Building Agilico MSP Toolkit for macOS (Edition: $Edition)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Ensure output directory
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

$buildFull = ($Edition -eq "All" -or $Edition -eq "Full")
$buildLite = ($Edition -eq "All" -or $Edition -eq "Lite")

# -------------------------------------------------------------
# FULL STANDALONE EDITIONS (Self-Contained)
# -------------------------------------------------------------
if ($buildFull) {
    Write-Host ""
    Write-Host ">>> [1/2] Building Full Standalone Edition (Self-Contained)..." -ForegroundColor Yellow

    # Apple Silicon (arm64)
    Write-Host " -> Publishing osx-arm64 (Full)..." -ForegroundColor Gray
    $armFullOut = Join-Path $OutputDir "osx-arm64-full"
    dotnet publish -c $Configuration -r osx-arm64 --self-contained true -p:IsFullVersion=true -o $armFullOut $ProjectDir

    $armApp = Join-Path $OutputDir "Agilico_MSP_Toolkit_arm64.app"
    New-Item -ItemType Directory -Force -Path (Join-Path $armApp "Contents\MacOS") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $armApp "Contents\Resources") | Out-Null
    Copy-Item (Join-Path $armFullOut "*") (Join-Path $armApp "Contents\MacOS") -Recurse -Force
    Copy-Item (Join-Path $ProjectDir "Assets\Info.plist") (Join-Path $armApp "Contents\Info.plist") -Force
    Copy-Item (Join-Path $ProjectDir "Assets\logo.png") (Join-Path $armApp "Contents\Resources\logo.png") -Force
    if (Test-Path (Join-Path $ProjectDir "Assets\logo.icns")) {
        Copy-Item (Join-Path $ProjectDir "Assets\logo.icns") (Join-Path $armApp "Contents\Resources\logo.icns") -Force
    }

    # Intel Mac (x64)
    Write-Host " -> Publishing osx-x64 (Full)..." -ForegroundColor Gray
    $x64FullOut = Join-Path $OutputDir "osx-x64-full"
    dotnet publish -c $Configuration -r osx-x64 --self-contained true -p:IsFullVersion=true -o $x64FullOut $ProjectDir

    $x64App = Join-Path $OutputDir "Agilico_MSP_Toolkit_x64.app"
    New-Item -ItemType Directory -Force -Path (Join-Path $x64App "Contents\MacOS") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $x64App "Contents\Resources") | Out-Null
    Copy-Item (Join-Path $x64FullOut "*") (Join-Path $x64App "Contents\MacOS") -Recurse -Force
    Copy-Item (Join-Path $ProjectDir "Assets\Info.plist") (Join-Path $x64App "Contents\Info.plist") -Force
    Copy-Item (Join-Path $ProjectDir "Assets\logo.png") (Join-Path $x64App "Contents\Resources\logo.png") -Force
    if (Test-Path (Join-Path $ProjectDir "Assets\logo.icns")) {
        Copy-Item (Join-Path $ProjectDir "Assets\logo.icns") (Join-Path $x64App "Contents\Resources\logo.icns") -Force
    }
}

# -------------------------------------------------------------
# LITE EDITIONS (Framework-Dependent)
# -------------------------------------------------------------
if ($buildLite) {
    Write-Host ""
    Write-Host ">>> [2/2] Building Lite Edition (Framework-Dependent)..." -ForegroundColor Yellow

    # Apple Silicon Lite (arm64)
    Write-Host " -> Publishing osx-arm64 (Lite)..." -ForegroundColor Gray
    $armLiteOut = Join-Path $OutputDir "osx-arm64-lite"
    dotnet publish -c $Configuration -r osx-arm64 --self-contained false -p:IsFullVersion=false -o $armLiteOut $ProjectDir

    $armLiteApp = Join-Path $OutputDir "Agilico_MSP_Toolkit_Lite_arm64.app"
    New-Item -ItemType Directory -Force -Path (Join-Path $armLiteApp "Contents\MacOS") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $armLiteApp "Contents\Resources") | Out-Null
    Copy-Item (Join-Path $armLiteOut "*") (Join-Path $armLiteApp "Contents\MacOS") -Recurse -Force
    Copy-Item (Join-Path $ProjectDir "Assets\Info.Lite.plist") (Join-Path $armLiteApp "Contents\Info.plist") -Force
    Copy-Item (Join-Path $ProjectDir "Assets\logo.png") (Join-Path $armLiteApp "Contents\Resources\logo.png") -Force
    if (Test-Path (Join-Path $ProjectDir "Assets\logo.icns")) {
        Copy-Item (Join-Path $ProjectDir "Assets\logo.icns") (Join-Path $armLiteApp "Contents\Resources\logo.icns") -Force
    }

    # Intel Mac Lite (x64)
    Write-Host " -> Publishing osx-x64 (Lite)..." -ForegroundColor Gray
    $x64LiteOut = Join-Path $OutputDir "osx-x64-lite"
    dotnet publish -c $Configuration -r osx-x64 --self-contained false -p:IsFullVersion=false -o $x64LiteOut $ProjectDir

    $x64LiteApp = Join-Path $OutputDir "Agilico_MSP_Toolkit_Lite_x64.app"
    New-Item -ItemType Directory -Force -Path (Join-Path $x64LiteApp "Contents\MacOS") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $x64LiteApp "Contents\Resources") | Out-Null
    Copy-Item (Join-Path $x64LiteOut "*") (Join-Path $x64LiteApp "Contents\MacOS") -Recurse -Force
    Copy-Item (Join-Path $ProjectDir "Assets\Info.Lite.plist") (Join-Path $x64LiteApp "Contents\Info.plist") -Force
    Copy-Item (Join-Path $ProjectDir "Assets\logo.png") (Join-Path $x64LiteApp "Contents\Resources\logo.png") -Force
    if (Test-Path (Join-Path $ProjectDir "Assets\logo.icns")) {
        Copy-Item (Join-Path $ProjectDir "Assets\logo.icns") (Join-Path $x64LiteApp "Contents\Resources\logo.icns") -Force
    }
}

# -------------------------------------------------------------
# Generate macOS .dmg disk images
# -------------------------------------------------------------
Write-Host ""
Write-Host ">>> Creating macOS .dmg Disk Images..." -ForegroundColor Yellow
$dmgScript = Join-Path $ProjectDir "build_dmg.ps1"
if (Test-Path $dmgScript) {
    & $dmgScript
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " macOS Build & DMG Creation Complete!" -ForegroundColor Green
Write-Host " Output Directory: $OutputDir" -ForegroundColor Green
if ($buildFull) {
    Write-Host " - Full ARM64 DMG: $(Join-Path $OutputDir 'Agilico_MSP_Toolkit_arm64.dmg')" -ForegroundColor Green
    Write-Host " - Full x64 DMG:   $(Join-Path $OutputDir 'Agilico_MSP_Toolkit_x64.dmg')" -ForegroundColor Green
}
if ($buildLite) {
    Write-Host " - Lite ARM64 DMG: $(Join-Path $OutputDir 'Agilico_MSP_Toolkit_Lite_arm64.dmg')" -ForegroundColor Green
    Write-Host " - Lite x64 DMG:   $(Join-Path $OutputDir 'Agilico_MSP_Toolkit_Lite_x64.dmg')" -ForegroundColor Green
}
Write-Host "==========================================================" -ForegroundColor Green
