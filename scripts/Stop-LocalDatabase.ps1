[CmdletBinding()]
param([string]$PostgresBin)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $PostgresBin) { $PostgresBin = Join-Path $repo '.local/pgsql/bin' }
& (Join-Path $PostgresBin 'pg_ctl.exe') -D (Join-Path $repo '.local/postgres-data') -m fast -w stop
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL stop failed. Check the binary path and whether the database is running.' }
