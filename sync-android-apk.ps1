param(
    [string]$SourceApk = "",
    [string]$SearchRoot = "TravelGuide",
    [string]$DestinationApk = "TravelGuide.AdminWeb/WEB/apk/travelguide-latest.apk"
)

$ErrorActionPreference = "Stop"

function Resolve-RepoPath([string]$relativePath) {
    if ([System.IO.Path]::IsPathRooted($relativePath)) {
        return $relativePath
    }

    return Join-Path -Path $PSScriptRoot -ChildPath $relativePath
}

function Is-ValidApkPath([string]$path) {
    return $path -and (Test-Path -LiteralPath $path -PathType Leaf) -and $path.ToLowerInvariant().EndsWith(".apk")
}

$destinationPath = Resolve-RepoPath $DestinationApk
$destinationDir = Split-Path -Parent $destinationPath
if (-not (Test-Path -LiteralPath $destinationDir -PathType Container)) {
    New-Item -ItemType Directory -Path $destinationDir -Force | Out-Null
}

$selectedApk = ""

if (-not [string]::IsNullOrWhiteSpace($SourceApk)) {
    $explicit = Resolve-RepoPath $SourceApk
    if (-not (Is-ValidApkPath $explicit)) {
        throw "SourceApk is invalid or does not exist: $SourceApk"
    }

    $selectedApk = $explicit
}
else {
    $searchPath = Resolve-RepoPath $SearchRoot
    if (-not (Test-Path -LiteralPath $searchPath -PathType Container)) {
        throw "SearchRoot does not exist: $SearchRoot"
    }

    $candidates = Get-ChildItem -Path $searchPath -Filter *.apk -File -Recurse |
        Where-Object {
            $_.FullName -ne $destinationPath -and
            $_.FullName -notmatch "\\bin\\Debug\\" -and
            $_.FullName -notmatch "\\obj\\"
        } |
        Sort-Object LastWriteTimeUtc -Descending

    if (-not $candidates -or $candidates.Count -eq 0) {
        throw "No APK file found under '$SearchRoot'. Build/publish Android first, then run this script again."
    }

    $selectedApk = $candidates[0].FullName
}

Copy-Item -LiteralPath $selectedApk -Destination $destinationPath -Force

$fileInfo = Get-Item -LiteralPath $destinationPath
Write-Host "Android APK synced successfully." -ForegroundColor Green
Write-Host "Source      : $selectedApk"
Write-Host "Destination : $destinationPath"
Write-Host ("Size        : {0:N2} MB" -f ($fileInfo.Length / 1MB))
Write-Host "Updated UTC : $($fileInfo.LastWriteTimeUtc.ToString("u"))"
