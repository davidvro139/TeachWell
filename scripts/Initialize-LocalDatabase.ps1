[CmdletBinding()]
param(
    [string]$PostgresBin,
    [int]$Port = 55432
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$localDir = Join-Path $repo '.local'
New-Item -ItemType Directory -Force -Path $localDir | Out-Null
if (-not $PostgresBin) {
    $PostgresBin = Join-Path $localDir 'pgsql/bin'
    if (-not (Test-Path (Join-Path $PostgresBin 'initdb.exe'))) {
        $installed = Get-ChildItem 'C:/Program Files/PostgreSQL' -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
        if ($installed) { $PostgresBin = Join-Path $installed.FullName 'bin' }
    }
}
if (-not (Test-Path (Join-Path $PostgresBin 'initdb.exe'))) {
    throw 'PostgreSQL binaries are missing. Install PostgreSQL 17 or extract its Windows binary archive into .local (creating .local/pgsql/bin), then retry. See docs/LOCAL_DEVELOPMENT.md.'
}
$PostgresBin = (Resolve-Path $PostgresBin).Path
$dataDir = Join-Path $localDir 'postgres-data'
$secretFile = Join-Path $localDir 'database-secret.xml'
if (-not (Test-Path $secretFile)) {
    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $secure = ConvertTo-SecureString ([Convert]::ToBase64String($bytes)) -AsPlainText -Force
    $secure | Export-Clixml -LiteralPath $secretFile
}
$secret = Import-Clixml -LiteralPath $secretFile
$credential = New-Object System.Management.Automation.PSCredential('teachwell_local', $secret)
$password = $credential.GetNetworkCredential().Password
if (-not (Test-Path (Join-Path $dataDir 'PG_VERSION'))) {
    $passwordFile = Join-Path $localDir 'initdb-password.tmp'
    try {
        [IO.File]::WriteAllText($passwordFile, $password)
        & (Join-Path $PostgresBin 'initdb.exe') -D $dataDir -U teachwell_local --encoding=UTF8 --locale=C --auth=scram-sha-256 "--pwfile=$passwordFile"
        if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL initialization failed.' }
        Add-Content -LiteralPath (Join-Path $dataDir 'postgresql.conf') -Value "`nlisten_addresses = '127.0.0.1'`nport = $Port"
    } finally {
        if (Test-Path $passwordFile) { Remove-Item -LiteralPath $passwordFile }
    }
}
& (Join-Path $PostgresBin 'pg_ctl.exe') -D $dataDir status *> $null
if ($LASTEXITCODE -ne 0) {
    $log = Join-Path $localDir 'postgres.log'
    $pg = Start-Process -FilePath (Join-Path $PostgresBin 'pg_ctl.exe') -ArgumentList @('-D', ('"' + $dataDir + '"'), '-l', ('"' + $log + '"'), '-w', 'start') -WindowStyle Hidden -PassThru
    # Start-Process -Wait waits for the entire process tree, including PostgreSQL.
    # Wait only for pg_ctl to finish its readiness check.
    if (-not $pg.WaitForExit(30000)) { throw "PostgreSQL startup timed out. Inspect $log." }
    if ($pg.ExitCode -ne 0) { throw "PostgreSQL could not start. Inspect $log. It may indicate a port conflict." }
}
$previousPassword = $env:PGPASSWORD
try {
    $env:PGPASSWORD = $password
    $exists = & (Join-Path $PostgresBin 'psql.exe') -h 127.0.0.1 -p $Port -U teachwell_local -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'teachwell'"
    if ($LASTEXITCODE -ne 0) { throw 'Could not connect to the local PostgreSQL cluster. Use the same port as initialization.' }
    if ($exists -ne '1') {
        & (Join-Path $PostgresBin 'createdb.exe') -h 127.0.0.1 -p $Port -U teachwell_local teachwell
        if ($LASTEXITCODE -ne 0) { throw 'Could not create the development database.' }
    }
} finally { $env:PGPASSWORD = $previousPassword }
Write-Host "Local PostgreSQL ready on 127.0.0.1:$Port. Data and encrypted local credentials are in .local."
