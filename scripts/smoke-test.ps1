# Smoke test for Imperium backend
# Usage: powershell -ExecutionPolicy Bypass -File .\scripts\smoke-test.ps1
param(
    [string]$BaseUrl = 'http://localhost:5000'
)

Write-Host "Resetting dev data..."
Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/dev/reset-characters"

Write-Host "Triggering one tick..."
Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/dev/tick-now"

Write-Host "Waiting 2s for events to flush..."
Start-Sleep -Seconds 2

Write-Host "Fetching recent npc_reply events..."
$events = Invoke-RestMethod -Method Get -Uri "$BaseUrl/api/events?type=npc_reply&count=20"

if ($events.Count -gt 0) {
    Write-Host "Found $($events.Count) npc_reply events"
    $events | ForEach-Object {
        $payload = $_.payloadJson
        if (-not $payload) { $payload = $_.PayloadJson }
        Write-Host "- Event: $($_.id) at $($_.timestamp) ->" $payload
    }
    exit 0
} else {
    Write-Host "No npc_reply events found"
    exit 2
}
