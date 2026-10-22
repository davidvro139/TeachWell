[CmdletBinding()]
param([switch]$NoBuild, [switch]$NoQuickLogin, [string]$TimeZone, [ValidateRange(1, 2048)][int]$MaxUploadMegabytes = 2048, [string]$PostgresBin, [int]$DatabasePort = 55432, [string[]]$CanvasHost = @())
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
& (Join-Path $PSScriptRoot 'Initialize-LocalDatabase.ps1') -PostgresBin $PostgresBin -Port $DatabasePort
$localDir = Join-Path $repo '.local'
$dbPassword = (New-Object System.Management.Automation.PSCredential('local', (Import-Clixml (Join-Path $localDir 'database-secret.xml')))).GetNetworkCredential().Password
$demoFile = Join-Path $localDir 'demo-secret.xml'
if (-not (Test-Path $demoFile)) {
    Write-Host 'Choose a demo password (12+ characters, uppercase, lowercase, number, symbol). Stored encrypted for this Windows user.'
    Read-Host 'Demo password' -AsSecureString | Export-Clixml -LiteralPath $demoFile
}
$demoPassword = (New-Object System.Management.Automation.PSCredential('local', (Import-Clixml $demoFile))).GetNetworkCredential().Password
if ($demoPassword.Length -lt 12 -or $demoPassword -cnotmatch '[A-Z]' -or $demoPassword -cnotmatch '[a-z]' -or $demoPassword -notmatch '[0-9]' -or $demoPassword -notmatch '[^a-zA-Z0-9]') {
    Remove-Item -LiteralPath $demoFile
    throw 'Password does not meet Identity policy. Run the script again to choose a stronger password.'
}
$names = @('ASPNETCORE_ENVIRONMENT', 'ConnectionStrings__Database', 'Features__DemoAuth', 'Features__DemoQuickLogin', 'Institution__TimeZone', 'CourseExports__MaxUploadMegabytes', 'Demo__Password', 'PATH', 'Features__CanvasPAT', 'Canvas__EncryptionKey')
$names += @(Get-ChildItem Env: | Where-Object { $_.Name -like 'Canvas__AllowedHosts__*' } | ForEach-Object { $_.Name })
for ($n = 0; $n -lt $CanvasHost.Count; $n++) { $names += "Canvas__AllowedHosts__$n" }
$names = @($names | Select-Object -Unique)
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__Database = "Host=127.0.0.1;Port=$DatabasePort;Database=teachwell;Username=teachwell_local;Password=$dbPassword"
    $env:Features__DemoAuth = 'true'
    # One-click sign-in buttons for the demo accounts (ADR 012); loopback-only. -NoQuickLogin turns them off.
    $env:Features__DemoQuickLogin = if ($NoQuickLogin) { 'false' } else { 'true' }
    # Deadlines are grouped by day in this zone; defaults to this computer's zone. Pass e.g. -TimeZone America/Denver.
    $env:Institution__TimeZone = if ($TimeZone) { $TimeZone } else { [TimeZoneInfo]::Local.Id }
    # Course export upload limit. Locally it defaults to the 2,048 MB maximum (media inside exports is skipped, not unpacked);
    # the API's own default for other deployments is 500 MB. Pass e.g. -MaxUploadMegabytes 500 to run with a lower limit.
    $env:CourseExports__MaxUploadMegabytes = "$MaxUploadMegabytes"
    $env:Demo__Password = $demoPassword
    # Canvas is opt-in. The key is never committed, printed or sent to the browser.
    $env:Features__CanvasPAT = 'false'
    if ($CanvasHost.Count -gt 0) {
        foreach ($name in $names) { if ($name -like 'Canvas__AllowedHosts__*') { [Environment]::SetEnvironmentVariable($name, $null, 'Process') } }
        $keyFile = Join-Path $localDir 'canvas-key.xml'
        if (-not (Test-Path $keyFile)) {
            $keyBytes = New-Object byte[] 32
            $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
            try { $rng.GetBytes($keyBytes) } finally { $rng.Dispose() }
            ConvertTo-SecureString ([Convert]::ToBase64String($keyBytes)) -AsPlainText -Force | Export-Clixml -LiteralPath $keyFile
        }
        $env:Canvas__EncryptionKey = (New-Object System.Management.Automation.PSCredential('local', (Import-Clixml $keyFile))).GetNetworkCredential().Password
        for ($n = 0; $n -lt $CanvasHost.Count; $n++) { [Environment]::SetEnvironmentVariable("Canvas__AllowedHosts__$n", $CanvasHost[$n], 'Process') }
        $env:Features__CanvasPAT = 'true'
    }
    if (-not $NoBuild) {
        $node = Get-Command node -ErrorAction SilentlyContinue
        if (-not $node -and (Test-Path (Join-Path $localDir 'node.exe'))) { $env:PATH = $localDir + ';' + $env:PATH }
        Push-Location src/TeachWell.Web
        try {
            if (Get-Command npm.cmd -ErrorAction SilentlyContinue) {
                & npm.cmd ci --no-audit --no-fund
                if ($LASTEXITCODE -eq 0) { & npm.cmd run build }
            } elseif (Test-Path (Join-Path $localDir 'package/bin/npm-cli.js')) {
                & node (Join-Path $localDir 'package/bin/npm-cli.js') ci --no-audit --no-fund
                if ($LASTEXITCODE -eq 0) { & node (Join-Path $localDir 'package/bin/npm-cli.js') run build }
            } else { throw 'Install Node.js 22 (including npm), then retry.' }
            if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
        } finally { Pop-Location }
        New-Item -ItemType Directory -Force src/TeachWell.Api/wwwroot | Out-Null
        Copy-Item src/TeachWell.Web/dist/* src/TeachWell.Api/wwwroot -Recurse -Force
        & dotnet build TeachWell.sln
        if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    }
    & dotnet dev-certs https --check
    if ($LASTEXITCODE -ne 0) {
        & dotnet dev-certs https
        if ($LASTEXITCODE -ne 0) { throw 'Unable to create development HTTPS certificate.' }
    }
    & dotnet run --project src/TeachWell.Api --no-build -- --seed
    if ($LASTEXITCODE -ne 0) { throw 'Migration/seed failed.' }
    Write-Host 'TeachWell: https://localhost:7143. Press Ctrl+C to stop the API. PostgreSQL remains running.'
    Write-Host 'For browser trust, run dotnet dev-certs https --trust once in a separate terminal.'
    & dotnet run --project src/TeachWell.Api --no-build -- --urls https://localhost:7143
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
