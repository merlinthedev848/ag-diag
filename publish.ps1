$ErrorActionPreference = "Stop"
$srcDir = $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "publish_output"

# Always wipe intermediate and output folders for a clean build
Write-Host "Cleaning previous build artefacts..."
Remove-Item (Join-Path $srcDir "bin\Publish") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $outDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $outDir | Out-Null

Set-Location $srcDir

# -----------------------------------------------------------------------
# RELEASE — single-file self-extracting exes
# AssemblyName MUST match the final exe filename exactly for WPF BAML
# -----------------------------------------------------------------------

Write-Host ""
Write-Host "Publishing Standalone Release (self-contained, single-file)..."
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=false `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IsFullVersion=true `
    -p:AssemblyName="Agilico MSP Toolkit" `
    -o "bin\Publish\Standalone"
if ($LASTEXITCODE -ne 0) { throw "Standalone Release failed (exit $LASTEXITCODE)" }

Write-Host ""
Write-Host "Publishing Lite Release (framework-dependent, single-file)..."
dotnet publish -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true `
    -p:PublishReadyToRun=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IsFullVersion=false `
    -p:AssemblyName="Agilico MSP Toolkit Lite" `
    -o "bin\Publish\Lite"
if ($LASTEXITCODE -ne 0) { throw "Lite Release failed (exit $LASTEXITCODE)" }

# -----------------------------------------------------------------------
# DEBUG — folder-based (NOT single-file).
# Single-file renames break WPF BAML resolution. A folder publish keeps
# the .dll alongside the .exe so the runtime can always find it by name.
# -----------------------------------------------------------------------

Write-Host ""
Write-Host "Publishing Standalone Debug (self-contained, folder)..."
dotnet publish -c Debug -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:IsFullVersion=true `
    -p:AssemblyName="Agilico MSP Toolkit" `
    -o "bin\Publish\StandaloneDebug"
if ($LASTEXITCODE -ne 0) { throw "Standalone Debug failed (exit $LASTEXITCODE)" }

Write-Host ""
Write-Host "Publishing Lite Debug (framework-dependent, folder)..."
dotnet publish -c Debug -r win-x64 --self-contained false `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=false `
    -p:IsFullVersion=false `
    -p:AssemblyName="Agilico MSP Toolkit Lite" `
    -o "bin\Publish\LiteDebug"
if ($LASTEXITCODE -ne 0) { throw "Lite Debug failed (exit $LASTEXITCODE)" }

# -----------------------------------------------------------------------
# Copy outputs — Release: single exes; Debug: whole folders
# -----------------------------------------------------------------------

Write-Host ""
Write-Host "Copying outputs to publish_output..."

# Release single-file exes (copy just the exe, not the whole folder)
Copy-Item "bin\Publish\Standalone\Agilico MSP Toolkit.exe" `
          -Destination "$outDir\Agilico MSP Toolkit.exe" -Force

Copy-Item "bin\Publish\Lite\Agilico MSP Toolkit Lite.exe" `
          -Destination "$outDir\Agilico MSP Toolkit Lite.exe" -Force

# Debug folders — copy entire published output into named subfolders
$dbgStandalone = "$outDir\Debug - Standalone"
$dbgLite       = "$outDir\Debug - Lite"

Copy-Item "bin\Publish\StandaloneDebug" -Destination $dbgStandalone -Recurse -Force
Copy-Item "bin\Publish\LiteDebug"       -Destination $dbgLite       -Recurse -Force

Write-Host ""
Write-Host "=========================================="
Write-Host " Build complete."
Write-Host "=========================================="
Write-Host ""
Write-Host " RELEASE (single-file, run directly):"
Write-Host "   $outDir\Agilico MSP Toolkit.exe"
Write-Host "   $outDir\Agilico MSP Toolkit Lite.exe"
Write-Host ""
Write-Host " DEBUG (folder, run the .exe INSIDE the folder):"
Write-Host "   $dbgStandalone\Agilico MSP Toolkit.exe"
Write-Host "   $dbgLite\Agilico MSP Toolkit Lite.exe"
Write-Host ""
