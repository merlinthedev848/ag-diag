# Agilico MSP Toolkit - macOS Multi-Architecture Build Script
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectDir = $PSScriptRoot
$OutputDir = Join-Path $ProjectDir "publish_output"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Building Agilico MSP Toolkit for macOS" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Clean output directory
if (Test-Path $OutputDir) {
    Remove-Item -Recurse -Force $OutputDir
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# 2. Publish Apple Silicon (osx-arm64)
Write-Host "[1/2] Publishing Apple Silicon (osx-arm64)..." -ForegroundColor Yellow
$armOut = Join-Path $OutputDir "osx-arm64"
dotnet publish -c $Configuration -r osx-arm64 --self-contained -o $armOut $ProjectDir

# Create .app bundle structure for osx-arm64
$armApp = Join-Path $OutputDir "Agilico_MSP_Toolkit_arm64.app"
$armContents = Join-Path $armApp "Contents"
$armMacOS = Join-Path $armContents "MacOS"
$armResources = Join-Path $armContents "Resources"

New-Item -ItemType Directory -Force -Path $armMacOS | Out-Null
New-Item -ItemType Directory -Force -Path $armResources | Out-Null
Copy-Item (Join-Path $armOut "*") $armMacOS -Recurse -Force
Copy-Item (Join-Path $ProjectDir "Assets\Info.plist") $armContents -Force
Copy-Item (Join-Path $ProjectDir "Assets\logo.png") (Join-Path $armResources "logo.png") -Force
if (Test-Path (Join-Path $ProjectDir "Assets\logo.icns")) {
    Copy-Item (Join-Path $ProjectDir "Assets\logo.icns") (Join-Path $armResources "logo.icns") -Force
}

# 3. Publish Intel Mac (osx-x64)
Write-Host "[2/2] Publishing Intel Mac (osx-x64)..." -ForegroundColor Yellow
$x64Out = Join-Path $OutputDir "osx-x64"
dotnet publish -c $Configuration -r osx-x64 --self-contained -o $x64Out $ProjectDir

# Create .app bundle structure for osx-x64
$x64App = Join-Path $OutputDir "Agilico_MSP_Toolkit_x64.app"
$x64Contents = Join-Path $x64App "Contents"
$x64MacOS = Join-Path $x64Contents "MacOS"
$x64Resources = Join-Path $x64Contents "Resources"

New-Item -ItemType Directory -Force -Path $x64MacOS | Out-Null
New-Item -ItemType Directory -Force -Path $x64Resources | Out-Null
Copy-Item (Join-Path $x64Out "*") $x64MacOS -Recurse -Force
Copy-Item (Join-Path $ProjectDir "Assets\Info.plist") $x64Contents -Force
Copy-Item (Join-Path $ProjectDir "Assets\logo.png") (Join-Path $x64Resources "logo.png") -Force
if (Test-Path (Join-Path $ProjectDir "Assets\logo.icns")) {
    Copy-Item (Join-Path $ProjectDir "Assets\logo.icns") (Join-Path $x64Resources "logo.icns") -Force
}

# 4. Generate macOS .dmg disk images
Write-Host "[3/3] Creating macOS .dmg disk images..." -ForegroundColor Yellow
$dmgScript = Join-Path $ProjectDir "build_dmg.ps1"
if (Test-Path $dmgScript) {
    & $dmgScript
}

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " macOS Build & DMG Creation Complete!" -ForegroundColor Green
Write-Host " Output Directory: $OutputDir" -ForegroundColor Green
Write-Host " - Apple Silicon App: $armApp" -ForegroundColor Green
Write-Host " - Intel Mac App:     $x64App" -ForegroundColor Green
Write-Host " - Apple Silicon DMG: $(Join-Path $OutputDir 'Agilico_MSP_Toolkit_arm64.dmg')" -ForegroundColor Green
Write-Host " - Intel Mac DMG:     $(Join-Path $OutputDir 'Agilico_MSP_Toolkit_x64.dmg')" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
