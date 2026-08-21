$xamlFiles = Get-ChildItem -Path . -Filter *.xaml
$appXaml = Get-Content -Path 'App.xaml' -Raw
$appKeys = [regex]::Matches($appXaml, 'x:Key="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }

Write-Host "Total keys in App.xaml: $($appKeys.Count)"

foreach ($file in $xamlFiles) {
    $content = Get-Content -Path $file.FullName -Raw
    $localKeys = [regex]::Matches($content, 'x:Key="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    
    $matches = [regex]::Matches($content, 'StaticResource\s+([a-zA-Z0-9_]+)')
    foreach ($m in $matches) {
        $resName = $m.Groups[1].Value
        if ($resName -notin $appKeys -and $resName -notin $localKeys) {
            Write-Host "MISSING RESOURCE in $($file.Name): '$resName'" -ForegroundColor Red
        }
    }
}
