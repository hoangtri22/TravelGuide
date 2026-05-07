param(
    [Parameter(Mandatory = $true)]
    [string]$ApiTunnelUrl,
    [string]$AdminTunnelUrl = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Normalize-HttpUrl([string]$Raw) {
    $value = $Raw.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "URL is empty."
    }

    $uri = $null
    if (-not [Uri]::TryCreate($value, [UriKind]::Absolute, [ref]$uri)) {
        throw "Invalid URL: $Raw"
    }
    if ($uri.Scheme -ne [Uri]::UriSchemeHttp -and $uri.Scheme -ne [Uri]::UriSchemeHttps) {
        throw "Only http/https are supported: $Raw"
    }

    return $uri.ToString().TrimEnd("/")
}

$apiBase = Normalize-HttpUrl $ApiTunnelUrl
$adminInput = if ([string]::IsNullOrWhiteSpace($AdminTunnelUrl)) { $ApiTunnelUrl } else { $AdminTunnelUrl }
$adminBase = Normalize-HttpUrl $adminInput

$targetFile = Join-Path $PSScriptRoot "..\TravelGuide\Resources\Raw\device_endpoints.json"
$targetFile = [System.IO.Path]::GetFullPath($targetFile)

$json = @"
{
  "androidPhysicalApiBaseUrl": "$apiBase",
  "androidPhysicalAdminWebBaseUrl": "$adminBase",
  "notes": "Set from Dev Tunnel URL. Re-run this script whenever tunnel URL changes."
}
"@

Set-Content -Path $targetFile -Value $json -Encoding UTF8

Write-Host "[devtunnel] Updated: $targetFile" -ForegroundColor Green
Write-Host "[devtunnel] androidPhysicalApiBaseUrl => $apiBase"
Write-Host "[devtunnel] androidPhysicalAdminWebBaseUrl => $adminBase"
