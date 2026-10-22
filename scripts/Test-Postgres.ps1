[CmdletBinding()]
param([switch]$NoBuild, [string]$PostgresBin, [int]$DatabasePort = 55432)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
& (Join-Path $PSScriptRoot 'Initialize-LocalDatabase.ps1') -PostgresBin $PostgresBin -Port $DatabasePort
$secret = Import-Clixml (Join-Path $repo '.local/database-secret.xml')
$password = (New-Object System.Management.Automation.PSCredential('local', $secret)).GetNetworkCredential().Password
$previous = $env:TEACHWELL_TEST_POSTGRES
try {
    $env:TEACHWELL_TEST_POSTGRES = "Host=127.0.0.1;Port=$DatabasePort;Username=teachwell_local;Password=$password"
    if ($NoBuild) { & dotnet test TeachWell.sln --no-build }
    else { & dotnet test TeachWell.sln }
    if ($LASTEXITCODE -ne 0) { throw 'Native PostgreSQL tests failed. See output above.' }
} finally { $env:TEACHWELL_TEST_POSTGRES = $previous }
