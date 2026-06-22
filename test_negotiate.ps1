$urls = @(
    'http://v3.presence.eu-beta.hp2k.co.uk/Presence/negotiate',
    'http://v1.rooms.eu-beta.hp2k.co.uk/Rooms/negotiate',
    'http://v1.softsignalling.eu-beta.hp2k.co.uk/Signals/negotiate'
)

foreach ($u in $urls) {
    try {
        $r = Invoke-WebRequest -Uri $u -Method Post -ErrorAction Stop
        Write-Output "$u -> $($r.StatusCode) $($r.Content)"
    } catch {
        Write-Output "$u -> Error: $_"
    }
}
