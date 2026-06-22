$dir = "C:\Program Files\Agilico\Agilico Connect for Windows V5"
$dlls = @(
    "Microsoft.Extensions.Primitives.dll",
    "Microsoft.Extensions.DependencyInjection.Abstractions.dll",
    "Microsoft.Extensions.DependencyInjection.dll",
    "Microsoft.Extensions.Logging.Abstractions.dll",
    "Microsoft.Extensions.Logging.dll",
    "Microsoft.Extensions.Options.dll",
    "System.IO.Pipelines.dll",
    "Microsoft.AspNetCore.Connections.Abstractions.dll",
    "Microsoft.AspNetCore.Http.Connections.Common.dll",
    "Microsoft.AspNetCore.Http.Connections.Client.dll",
    "Microsoft.AspNetCore.SignalR.Common.dll",
    "Microsoft.AspNetCore.SignalR.Protocols.Json.dll",
    "Microsoft.AspNetCore.SignalR.Client.Core.dll",
    "Microsoft.AspNetCore.SignalR.Client.dll"
)

foreach ($dll in $dlls) {
    [System.Reflection.Assembly]::LoadFrom((Join-Path $dir $dll)) | Out-Null
}

$urls = @(
    "http://v3.presence.eu-beta.hp2k.co.uk/Presence",
    "https://v3.presence.eu-beta.hp2k.co.uk/Presence",
    "http://v1.rooms.eu-beta.hp2k.co.uk/Rooms",
    "https://v1.rooms.eu-beta.hp2k.co.uk/Rooms",
    "http://v1.softsignalling.eu-beta.hp2k.co.uk/Signals"
)

foreach ($url in $urls) {
    Write-Host "`nTesting: $url"
    try {
        $builder = New-Object Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilder
        $builder = [Microsoft.AspNetCore.SignalR.Client.HubConnectionBuilderExtensions]::WithUrl($builder, $url)
        $connection = $builder.Build()
        
        $task = $connection.StartAsync()
        $task.Wait(4000)
        
        if ($connection.State -eq [Microsoft.AspNetCore.SignalR.Client.HubConnectionState]::Connected) {
            Write-Host "SUCCESS"
            $connection.StopAsync().Wait()
        } else {
            Write-Host "FAILED (State: $($connection.State))"
        }
    } catch {
        Write-Host "ERROR: $($_.Exception.Message)"
        if ($_.Exception.InnerException) {
            Write-Host "  Inner: $($_.Exception.InnerException.Message)"
        }
    }
}
