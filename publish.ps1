$ErrorActionPreference = "Stop"
$srcDir = $PSScriptRoot
$outDir = Join-Path $PSScriptRoot "publish_output"

if (!(Test-Path "$outDir")) {
    New-Item -ItemType Directory -Path "$outDir" | Out-Null
}

$architectures = @("win-x64", "win-x86")

foreach ($arch in $architectures) {
    Write-Host "Publishing Standalone (Self-Contained) version for $arch..."
    Set-Location "$srcDir"
    dotnet publish -c Release -r $arch --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=false -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IsFullVersion=true -o "bin\Publish\Standalone\$arch"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish standalone failed for $arch with code $LASTEXITCODE" }

    Write-Host "Publishing Lite (Framework-Dependent) version for $arch..."
    Remove-Item "obj\Release" -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish -c Release -r $arch --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=false -p:IsFullVersion=false -o "bin\Publish\Lite\$arch"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish lite failed for $arch with code $LASTEXITCODE" }

    Write-Host "Zipping outputs for $arch..."
    # DO NOT rename the executable. WPF Single-File apps crash if the .exe is renamed!
    Compress-Archive -Path "bin\Publish\Standalone\$arch\Agilico MSP Toolkit.exe" -DestinationPath "$outDir\AgilicoNetworkDiagnosticTool-Standalone-$arch.zip" -Force
    Compress-Archive -Path "bin\Publish\Lite\$arch\Agilico MSP Toolkit.exe" -DestinationPath "$outDir\AgilicoNetworkDiagnosticTool-Lite-$arch.zip" -Force
}

Write-Host "Done! Outputs copied to: $outDir"
