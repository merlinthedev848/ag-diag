$ErrorActionPreference = "Stop"
$srcDir = $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "publish_output"

if (!(Test-Path "$outDir")) {
    New-Item -ItemType Directory -Path "$outDir" | Out-Null
}

# Building as win-x86 (32-bit) ensures the executable will run on both 32-bit and 64-bit Windows systems natively (via WOW64 on 64-bit machines).
$arch = "win-x86"

Write-Host "Publishing Standalone (Self-Contained) Universal (32/64-bit) version..."
Set-Location "$srcDir"
dotnet publish -c Release -r $arch --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IsFullVersion=true -o "bin\Publish\Standalone\Universal"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish standalone failed for $arch with code $LASTEXITCODE" }

Write-Host "Publishing Lite (Framework-Dependent) Universal (32/64-bit) version..."
Remove-Item "obj\Release" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish -c Release -r $arch --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=false -p:IsFullVersion=false -o "bin\Publish\Lite\Universal"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish lite failed for $arch with code $LASTEXITCODE" }

Write-Host "Copying to release directory..."
Copy-Item "bin\Publish\Standalone\Universal\Agilico MSP Toolkit.exe" -Destination "$outDir\Agilico MSP Toolkit-Universal.exe" -Force
Copy-Item "bin\Publish\Lite\Universal\Agilico MSP Toolkit.exe" -Destination "$outDir\Agilico MSP Toolkit Lite-Universal.exe" -Force

Write-Host "Done! Outputs copied to: $outDir"
