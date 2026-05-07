# Chạy PowerShell "Run as Administrator" để mở TCP 5096 (API) và 5280 (AdminWeb) cho mạng LAN.
# Điện thoại dùng: http://<IP-máy-PC>:5096 và :5280 — không bind được nếu firewall chặn.

$rules = @(
    @{ Name = "TravelGuide API (5096)"; Port = 5096 },
    @{ Name = "TravelGuide AdminWeb (5280)"; Port = 5280 }
)

foreach ($r in $rules) {
    $existing = Get-NetFirewallRule -DisplayName $r.Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Rule exists: $($r.Name)"
        continue
    }
    New-NetFirewallRule -DisplayName $r.Name -Direction Inbound -Action Allow -Protocol TCP -LocalPort $r.Port | Out-Null
    Write-Host "Created: $($r.Name)"
}

Write-Host "Done. Bind servers with launchSettings (0.0.0.0) or dotnet run --urls http://0.0.0.0:5096"
