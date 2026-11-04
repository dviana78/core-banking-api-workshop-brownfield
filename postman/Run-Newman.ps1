[CmdletBinding()]
param(
    [switch]$OpenReport,
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$databaseName = 'CoreBankingNewman_' + $runId.Replace('-', '_')
$containerName = 'corebanking-newman-' + $runId.ToLowerInvariant()
$reportDirectory = Join-Path $PSScriptRoot "reports\$runId"
$htmlReport = Join-Path $reportDirectory 'newman-responses.html'
$mutex = [System.Threading.Mutex]::new($false, 'Local\CoreBankingNewmanRunner')
$locked = $false
$restoreApi = $false
$temporaryApi = $false
$exitCode = 1
$environmentNames = @('NEWMAN_ORIGIN', 'NEWMAN_REPORT_DIR', 'NEWMAN_CLIENT_SECRET', 'NEWMAN_QUIET')
$previousEnvironment = @{}

function Invoke-Docker {
    param([string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker failed (exit $LASTEXITCODE)."
    }
}

function Wait-Api {
    param([string]$Origin)
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    do {
        try {
            $response = Invoke-WebRequest -Uri "$Origin/health" -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -eq 200) { return }
        }
        catch {
            if ($timer.Elapsed.TotalSeconds -ge 45) { throw "API did not become healthy at $Origin within 45 seconds." }
        }
    } while ($timer.Elapsed.TotalSeconds -lt 45)
    throw "API did not become healthy at $Origin within 45 seconds."
}

try {
    try { $locked = $mutex.WaitOne(0) }
    catch [System.Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'Another Newman execution is running. Wait until it finishes.' }
    Get-Command node, npm.cmd, docker -ErrorAction Stop | Out-Null
    if (-not (Test-Path (Join-Path $root '.env'))) {
        throw 'Missing .env. Configure the workshop values using .env.example before running.'
    }
    foreach ($name in $environmentNames) {
        $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    Push-Location $root
    try {
        Invoke-Docker -Arguments @('info', '--format', '{{.ServerVersion}}')
        & node (Join-Path $PSScriptRoot 'generate.cjs')
        if ($LASTEXITCODE -ne 0) { throw 'Postman collection generation failed.' }
        if (-not (Test-Path (Join-Path $PSScriptRoot 'node_modules\newman\package.json'))) {
            & npm.cmd ci --prefix $PSScriptRoot --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw 'Newman dependency installation failed.' }
        }

        $configText = Invoke-Docker -Arguments @('compose', 'config', '--format', 'json')
        $config = ($configText -join "`n") | ConvertFrom-Json
        $apiPort = [int]$config.services.api.ports[0].published
        if ($apiPort -le 0) { throw 'Cannot determine API_PORT from Compose configuration.' }
        $origin = "http://localhost:$apiPort"
        $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
        $connection.set_ConnectionString($config.services.api.environment.ConnectionStrings__Banking)
        $connection.set_Item('Database', $databaseName)
        $isolatedConnection = 'ConnectionStrings__Banking=' + $connection.get_ConnectionString()

        $upArguments = @('compose', 'up', '-d', '--wait')
        if ($Rebuild) { $upArguments += '--build' }
        Invoke-Docker -Arguments $upArguments
        Wait-Api -Origin $origin
        Write-Host "Run: $runId"
        Write-Host "Isolated database: $databaseName"
        Write-Host "API port: $apiPort (only one API at a time)"
        Invoke-Docker -Arguments @('compose', 'run', '--rm', '--no-deps', '-e', $isolatedConnection, 'migrate')

        $restoreApi = $true
        Invoke-Docker -Arguments @('compose', 'stop', 'api')
        $temporaryApi = $true
        Invoke-Docker -Arguments @('compose', 'run', '-d', '--no-deps', '--service-ports', '--name', $containerName, '-e', $isolatedConnection, 'api')
        Wait-Api -Origin $origin

        $env:NEWMAN_ORIGIN = $origin
        $env:NEWMAN_REPORT_DIR = $reportDirectory
        $env:NEWMAN_CLIENT_SECRET = $config.services.api.environment.Workshop__ClientSecret
        $env:NEWMAN_QUIET = '1'
        & node (Join-Path $PSScriptRoot 'run-newman.cjs')
        $exitCode = $LASTEXITCODE
        if (Test-Path $htmlReport) {
            Write-Host "HTML report: $htmlReport"
            Write-Host "JSON report: $(Join-Path $reportDirectory 'newman-responses.json')"
        }
        if ($exitCode -ne 0) { Write-Warning 'Newman found differences or execution errors. Review the report.' }
    }
    finally {
        if ($temporaryApi) {
            & docker rm -f $containerName
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove temporary container $containerName." }
        }
        if ($restoreApi) {
            try {
                Invoke-Docker -Arguments @('compose', 'start', 'api')
                Wait-Api -Origin $origin
                Write-Host "Main API restored: $origin/swagger/index.html"
            }
            catch {
                $exitCode = 1
                Write-Warning "Main API restoration failed: $($_.Exception.Message)"
            }
        }
        foreach ($name in $previousEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process')
        }
        Pop-Location
    }
}
catch {
    Write-Warning $_.Exception.Message
    $exitCode = 1
}
finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}

if ($OpenReport -and (Test-Path $htmlReport)) {
    Start-Process -FilePath $htmlReport
}
exit $exitCode