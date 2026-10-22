[CmdletBinding()]
param([switch]$NoBuild, [string]$PostgresBin, [int]$DatabasePort = 55432)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
& (Join-Path $PSScriptRoot 'Initialize-LocalDatabase.ps1') -PostgresBin $PostgresBin -Port $DatabasePort
if (-not $PostgresBin) { $PostgresBin = Join-Path $repo '.local/pgsql/bin' }
$localDir = Join-Path $repo '.local'
$database = 'teachwell_browser_' + [Guid]::NewGuid().ToString('N')
$names = @('ASPNETCORE_ENVIRONMENT', 'ConnectionStrings__Database', 'Features__DemoAuth', 'Features__DemoQuickLogin', 'Features__CanvasPAT', 'Demo__Password', 'TEACHWELL_E2E_PASSWORD', 'PGPASSWORD', 'PATH')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$apiProcess = $null
$dbPassword = (New-Object System.Management.Automation.PSCredential('local', (Import-Clixml (Join-Path $localDir 'database-secret.xml')))).GetNetworkCredential().Password
function Invoke-Frontend([string[]]$Arguments) {
    if (Get-Command npm.cmd -ErrorAction SilentlyContinue) { & npm.cmd @Arguments }
    else { & node (Join-Path $localDir 'package/bin/npm-cli.js') @Arguments }
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build/browser test failed. See output above.' }
}
try {
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) { $env:PATH = $localDir + ';' + $env:PATH }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Features__DemoAuth = 'true'
    $env:Features__DemoQuickLogin = 'true'
    $env:Features__CanvasPAT = 'false'
    $env:ConnectionStrings__Database = "Host=127.0.0.1;Port=$DatabasePort;Database=$database;Username=teachwell_local;Password=$dbPassword"
    $env:Demo__Password = [Guid]::NewGuid().ToString('N') + 'Aa1!'
    $env:TEACHWELL_E2E_PASSWORD = $env:Demo__Password
    $env:PGPASSWORD = $dbPassword
    & (Join-Path $PostgresBin 'createdb.exe') -h 127.0.0.1 -p $DatabasePort -U teachwell_local $database
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the isolated browser database.' }
    if (-not $NoBuild) {
        & dotnet build TeachWell.sln
        if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
        Push-Location src/TeachWell.Web
        try { Invoke-Frontend @('run', 'build') } finally { Pop-Location }
    }
    New-Item -ItemType Directory -Force src/TeachWell.Api/wwwroot | Out-Null
    Copy-Item src/TeachWell.Web/dist/* src/TeachWell.Api/wwwroot -Recurse -Force
    $dll = Join-Path $repo 'src/TeachWell.Api/bin/Debug/net9.0/TeachWell.Api.dll'
    Push-Location src/TeachWell.Api
    try {
        # The journeys assume the minimal baseline (e.g. an unshared DES 120, an unreviewed WEB 101 assignment), not the extended demo data.
        & dotnet $dll --seed --baseline-only
        if ($LASTEXITCODE -ne 0) { throw 'Isolated browser database migration/seed failed.' }
    } finally { Pop-Location }
    $apiProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @(('"' + $dll + '"'), '--urls', 'https://localhost:7443') -WorkingDirectory (Join-Path $repo 'src/TeachWell.Api') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $localDir 'browser-api.stdout.log') -RedirectStandardError (Join-Path $localDir 'browser-api.stderr.log')
    $ready = $false
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        if ($apiProcess.HasExited) { throw 'Test API exited. Inspect .local/browser-api.stderr.log.' }
        $client = New-Object Net.Sockets.TcpClient
        try { $client.Connect('127.0.0.1', 7443); $ready = $true; break } catch { Start-Sleep -Milliseconds 250 } finally { $client.Dispose() }
    }
    if (-not $ready) { throw 'Test API did not become ready on port 7443.' }
    Push-Location src/TeachWell.Web
    try { Invoke-Frontend @('exec', '--', 'playwright', 'test') } finally { Pop-Location }
} finally {
    if ($apiProcess -and -not $apiProcess.HasExited) { Stop-Process -Id $apiProcess.Id; $null = $apiProcess.WaitForExit(10000) }
    if ($database -notmatch '^teachwell_browser_[a-f0-9]{32}$') { throw 'Refusing cleanup outside the isolated browser test database namespace.' }
    & (Join-Path $PostgresBin 'dropdb.exe') -h 127.0.0.1 -p $DatabasePort -U teachwell_local --if-exists --force $database
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
