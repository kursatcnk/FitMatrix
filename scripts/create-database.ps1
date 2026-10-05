$ErrorActionPreference = 'Stop'

$dataDirectory = 'D:\FitMatrixData'
$sqlCmd = 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE'

if (-not (Test-Path -LiteralPath $sqlCmd)) {
    throw "sqlcmd bulunamadı: $sqlCmd"
}

New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null

$query = @"
IF DB_ID(N'FitMatrix') IS NULL
BEGIN
    CREATE DATABASE [FitMatrix]
    ON PRIMARY (NAME=N'FitMatrix', FILENAME=N'D:\FitMatrixData\FitMatrix.mdf')
    LOG ON (NAME=N'FitMatrix_log', FILENAME=N'D:\FitMatrixData\FitMatrix_log.ldf');
END;
SELECT DB_NAME(database_id) AS DatabaseName, physical_name
FROM sys.master_files
WHERE database_id = DB_ID(N'FitMatrix');
"@

& $sqlCmd -S '(localdb)\MSSQLLocalDB' -E -Q $query
