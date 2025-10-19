$base = 'http://localhost:5000'
$targets = @(
  @{name='weatherforecast'; method='GET'; url="$base/weatherforecast"},
  @{name='weather/latest'; method='GET'; url="$base/api/weather/latest"},
  @{name='recent events'; method='GET'; url="$base/api/events/recent/5"},
  @{name='npc_reply events'; method='GET'; url="$base/api/events?type=npc_reply&count=5"},
  @{name='characters'; method='GET'; url="$base/api/characters"},
  @{name='reset'; method='POST'; url="$base/api/dev/reset-characters"},
  @{name='seed'; method='POST'; url="$base/api/dev/seed-characters"},
  @{name='tick'; method='POST'; url="$base/api/dev/tick-now"}
)

function Check($t){
    $name = $t.name
    $method = $t.method
    $url = $t.url
    try{
        $r = Invoke-RestMethod -Method $method -Uri $url -TimeoutSec 20
        if ($null -eq $r) { Write-Output "OK|$name|empty"; return }
        if ($r -is [System.Array]) { Write-Output "OK|$name|array(count=$($r.Count))"; return }
        if ($r -is [System.Object]) { Write-Output "OK|$name|object"; return }
        Write-Output "OK|$name|$($r.ToString())"
    } catch {
        Write-Output "ERR|$name|$($_.Exception.Message)"
    }
}

foreach ($t in $targets) { Check $t }

# If characters exist, fetch first character detail and events
try{
    $chars = Invoke-RestMethod -Method GET -Uri "$base/api/characters" -TimeoutSec 10
    if ($chars -and $chars.Count -gt 0) {
        $id = $chars[0].id
        try{ Invoke-RestMethod -Method GET -Uri "$base/api/characters/$id" -TimeoutSec 10 | Out-Null; Write-Output "OK|character detail|id=$id" } catch { Write-Output "ERR|character detail|$($_.Exception.Message)" }
        try{ $evs = Invoke-RestMethod -Method GET -Uri "$base/api/characters/$id/events?count=10" -TimeoutSec 10; Write-Output "OK|character events|count=$($evs.Count)" } catch { Write-Output "ERR|character events|$($_.Exception.Message)" }
    } else { Write-Output "WARN|characters|no characters" }
} catch { Write-Output "ERR|fetch characters|$($_.Exception.Message)" }
