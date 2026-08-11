$ErrorActionPreference = "Stop"
$srcDir = $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "publish_output"

if (!(Test-Path "$outDir")) {
    New-Item -ItemType Directory -Path "$outDir" | Out-Null
}

Write-Host "Publishing Standalone (Self-Contained) version..."
Set-Location "$srcDir"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -o "bin\Publish\Standalone"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish standalone failed with code $LASTEXITCODE" }

Write-Host "Publishing Lite (Framework-Dependent) version..."
Remove-Item "obj\Release" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=false -o "bin\Publish\Lite"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish lite failed with code $LASTEXITCODE" }

Write-Host "Copying to release directory..."
Copy-Item "bin\Publish\Standalone\Agilico MSP Toolkit.exe" -Destination "$outDir\Agilico MSP Toolkit.exe" -Force
Copy-Item "bin\Publish\Lite\Agilico MSP Toolkit.exe" -Destination "$outDir\Agilico MSP Toolkit Lite.exe" -Force

Write-Host "Done! Outputs copied to: $outDir"
