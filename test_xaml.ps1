Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$app = New-Object System.Windows.Application
$appXamlPath = (Resolve-Path 'App.xaml').Path
$appDict = [System.Windows.Markup.XamlReader]::Load([System.Xml.XmlReader]::Create($appXamlPath))
Write-Host "App.xaml parsed successfully with 0 resources!" -ForegroundColor Green

$mainXamlPath = (Resolve-Path 'MainWindow.xaml').Path
# Merge app resources into test scope
$app.Resources.MergedDictionaries.Add($appDict)

try {
    $stream = [System.IO.File]::OpenRead($mainXamlPath)
    $parserCtx = New-Object System.Windows.Markup.ParserContext
    $parserCtx.BaseUri = [System.Uri]"pack://application:,,,/"
    Write-Host "Testing MainWindow.xaml XAML loading..."
    # Load MainWindow XAML
    $mainObj = [System.Windows.Markup.XamlReader]::Load($stream, $parserCtx)
    Write-Host "MainWindow.xaml loaded successfully without StaticResource errors!" -ForegroundColor Green
} catch {
    Write-Host "XAML PARSE ERROR in MainWindow.xaml: " -ForegroundColor Red
    if ($_.Exception.InnerException) {
        Write-Host "INNER EXCEPTION: " -ForegroundColor Red
    }
}
