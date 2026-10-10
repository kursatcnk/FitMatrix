$ErrorActionPreference = 'Stop'
$statePath = 'D:\FitMatrixData\runtime\processes.json'

if (-not (Test-Path -LiteralPath $statePath)) {
    Write-Host 'FitMatrix süreç kaydı bulunamadı; durdurulacak kayıtlı süreç yok.'
    exit 0
}

$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$processIds = @($state.BackendPid, $state.FrontendPid) | Where-Object { $_ -is [int] -or $_ -is [long] }

foreach ($processId in $processIds) {
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        Stop-Process -Id $processId -Force
        Write-Host "Durduruldu: $($process.ProcessName) ($processId)"
    }
}

Remove-Item -LiteralPath $statePath -Force
