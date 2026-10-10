param(
    [switch]$OpenBrowser
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'use-dev-drives.ps1')

$runtimeDirectory = 'D:\FitMatrixData\runtime'
$logDirectory = Join-Path $runtimeDirectory 'logs'
$statePath = Join-Path $runtimeDirectory 'processes.json'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

function Test-LocalEndpoint([string]$Uri) {
    try {
        $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    }
    catch {
        return $false
    }
}

$backendProcess = $null
$frontendProcess = $null
if (-not (Test-LocalEndpoint 'http://127.0.0.1:5080/api/system/health')) {
    $backendProcess = Start-Process -FilePath (Join-Path $env:DOTNET_ROOT 'dotnet.exe') `
        -ArgumentList @('run', '--project', (Join-Path $repositoryRoot 'backend\FitMatrix.Api.csproj'), '--urls', 'http://127.0.0.1:5080') `
        -WorkingDirectory $repositoryRoot -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $logDirectory 'backend.out.log') `
        -RedirectStandardError (Join-Path $logDirectory 'backend.err.log')
}

if (-not (Test-LocalEndpoint 'http://127.0.0.1:5173/')) {
    $npmCommand = 'D:\Tools\node\npm.cmd'
    if (-not (Test-Path -LiteralPath $npmCommand)) { $npmCommand = (Get-Command npm.cmd -ErrorAction Stop).Source }
    $frontendProcess = Start-Process -FilePath $npmCommand `
        -ArgumentList @('run', 'dev', '--', '--host', '127.0.0.1') `
        -WorkingDirectory (Join-Path $repositoryRoot 'frontend') -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $logDirectory 'frontend.out.log') `
        -RedirectStandardError (Join-Path $logDirectory 'frontend.err.log')
}

[pscustomobject]@{
    BackendPid = $backendProcess.Id
    FrontendPid = $frontendProcess.Id
    StartedAt = [DateTimeOffset]::Now
} | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding utf8

$deadline = [DateTimeOffset]::Now.AddSeconds(30)
do {
    $backendReady = Test-LocalEndpoint 'http://127.0.0.1:5080/api/system/health'
    $frontendReady = Test-LocalEndpoint 'http://127.0.0.1:5173/'
    if ($backendReady -and $frontendReady) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTimeOffset]::Now -lt $deadline)

if (-not $backendReady -or -not $frontendReady) {
    throw "FitMatrix 30 saniyede hazır olmadı. Günlükler: $logDirectory"
}

Write-Host 'FitMatrix hazır: http://127.0.0.1:5173/' -ForegroundColor Green
Write-Host "Çalışma günlükleri: $logDirectory"
if ($OpenBrowser) {
    Start-Process 'http://127.0.0.1:5173/'
}
