param(
    [string[]]$Lists,
    [string[]]$Queries,
    [switch]$Manufacturers,
    [string[]]$Sources,
    [int]$MaxPagesPerSource = 0,
    [int]$DelaySeconds = 0,
    [int]$MaxQueries = 0,
    [switch]$AllowUnverified,
    [switch]$Status,
    [switch]$Cancel,
    [string]$ApiBase = 'http://127.0.0.1:5080'
)

# Grows the local catalog. Only products with three published body dimensions and a source URL are stored.
#  - Manufacturer mode reads product pages straight from manufacturers' sitemaps (largest yield).
#  - List mode sends curated model names through FitMatrix's product discovery.
# Examples:
#   .\scripts\harvest-catalog.ps1 -Manufacturers                          # every source in manufacturers.json
#   .\scripts\harvest-catalog.ps1 -Manufacturers -Sources razer,asus      # selected manufacturers
#   .\scripts\harvest-catalog.ps1 -Manufacturers -MaxPagesPerSource 20    # quick trial
#   .\scripts\harvest-catalog.ps1                                         # every model list
#   .\scripts\harvest-catalog.ps1 -Lists monitors,mice -MaxQueries 20     # selected lists, quick trial
#   .\scripts\harvest-catalog.ps1 -Status                                 # progress of the running job
#   .\scripts\harvest-catalog.ps1 -Cancel                                 # stop the running job

$ErrorActionPreference = 'Stop'
$endpoint = "$ApiBase/api/admin/catalog/harvest"

function Show-Status($state) {
    $percent = if ($state.total -gt 0) { [math]::Round(100 * $state.processed / $state.total) } else { 0 }
    Write-Host ("{0}/{1} (%{2}) · yeni {3} · güncellenen {4} · zaten var {5} · doğrulanamayan {6} · hata {7}" -f `
        $state.processed, $state.total, $percent, $state.imported, $state.updated, $state.alreadyInCatalog, $state.noVerifiedMatch, $state.failed)
    if ($state.currentQuery) { Write-Host "Şu an: $($state.currentQuery)" -ForegroundColor DarkGray }
    $state.recent | Select-Object -First 8 | ForEach-Object {
        $color = switch ($_.outcome) { 'Imported' { 'Green' } 'Updated' { 'Cyan' } 'Failed' { 'Red' } default { 'DarkGray' } }
        Write-Host ("  {0,-16} {1}  {2}" -f $_.outcome, $_.query, $_.message) -ForegroundColor $color
    }
}

if ($Cancel) {
    Invoke-RestMethod -Method Delete -Uri $endpoint | Out-Null
    Write-Host 'Katalog toplama işi durduruldu.'
    return
}

if ($Status) {
    Show-Status (Invoke-RestMethod -Uri $endpoint)
    return
}

if ($Manufacturers) {
    $body = @{
        sources = $Sources
        delaySeconds = if ($DelaySeconds -gt 0) { $DelaySeconds } else { 3 }
        maxPagesPerSource = if ($MaxPagesPerSource -gt 0) { $MaxPagesPerSource } else { $null }
    } | ConvertTo-Json
    $state = Invoke-RestMethod -Method Post -Uri "$endpoint/manufacturers" -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    Write-Host 'Üretici ürün haritalarından katalog toplama başladı.' -ForegroundColor Green
}
else {
    $body = @{
        lists = $Lists
        queries = $Queries
        requireVerified = -not $AllowUnverified
        delaySeconds = if ($DelaySeconds -gt 0) { $DelaySeconds } else { 6 }
        maxQueries = if ($MaxQueries -gt 0) { $MaxQueries } else { $null }
    } | ConvertTo-Json
    $state = Invoke-RestMethod -Method Post -Uri $endpoint -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
    Write-Host "Katalog toplama başladı: $($state.total) model sorgusu." -ForegroundColor Green
}
Write-Host 'İş arka planda sürer; bu pencereyi kapatmak işi durdurmaz. İlerleme için -Status kullan.'
Show-Status $state
