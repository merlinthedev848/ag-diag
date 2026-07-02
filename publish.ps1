$ErrorActionPreference = "Stop"
$srcDir = "C:\Users\chris.kendall\.gemini\antigravity\scratch\dev"
$outDir = "C:\Users\chris.kendall\.gemini\antigravity\scratch\agilico-connect-checker"

if (!(Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir | Out-Null
}

Write-Host "Publishing Standalone (Self-Contained) version..."
Set-Location $srcDir
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -o "bin\Publish\Standalone"

Write-Host "Publishing Lite (Framework-Dependent) version..."
Remove-Item "obj\Release" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=false -o "bin\Publish\Lite"

Write-Host "Copying to release directory..."
Copy-Item "bin\Publish\Standalone\AgilicoNetworkDiagnosticTool.exe" -Destination "$outDir\AgilicoNetworkDiagnosticTool-Standalone.exe" -Force
Copy-Item "bin\Publish\Lite\AgilicoNetworkDiagnosticTool.exe" -Destination "$outDir\AgilicoNetworkDiagnosticTool-Lite.exe" -Force

Write-Host "Done!"
