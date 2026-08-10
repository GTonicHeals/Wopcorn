<#
.SYNOPSIS
    Builds, configures and runs Wopcorn on this machine, published to the tailnet
    over HTTPS by `tailscale serve`.

.DESCRIPTION
    Kestrel is bound to 127.0.0.1 only. Nothing reaches it except tailscaled,
    which terminates TLS with a real Let's Encrypt certificate for this node's
    MagicDNS name. That is what makes passkeys and Secure cookies work: both need
    a genuine HTTPS origin, and neither works over plain http:// or a self-signed
    certificate.

    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true is load-bearing, not decoration.
    Without it the app sees the proxied request as http:// and builds the wrong
    origin — password-reset links come out as http://, and WebAuthn origin
    validation fails on every passkey.

.PARAMETER Command
    deploy            (default) stop, publish, migrate, start, serve
    start             start the already-published app
    stop              stop the app (leaves the serve mapping in place)
    restart           stop then start
    status            what is running, where, and on which URL
    logs              show the newest log file
    serve             (re)apply the tailscale serve mapping
    unserve           remove the tailscale serve mapping
    install-startup   register a scheduled task that keeps the app running
    uninstall-startup remove that task

.EXAMPLE
    .\Host-Wopcorn.ps1
    Full deploy: publish, migrate, start, and publish to the tailnet.

.EXAMPLE
    .\Host-Wopcorn.ps1 deploy -SkipBuild
    Redeploy without rebuilding — useful when only configuration changed.

.EXAMPLE
    .\Host-Wopcorn.ps1 logs -Follow
#>

#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('deploy', 'start', 'stop', 'restart', 'status', 'logs',
                 'serve', 'unserve', 'install-startup', 'uninstall-startup')]
    [string]$Command = 'deploy',

    [int]$Port,
    [int]$ServePort,
    [string]$DataDir,
    [string]$PublishDir,

    [switch]$SkipBuild,
    [switch]$NoServe,
    [switch]$Force,
    [switch]$Follow,
    [int]$Tail = 60
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- paths -----

$RepoRoot      = Split-Path -Parent $PSScriptRoot
$ServerProject = Join-Path $RepoRoot 'Wopcorn.Server\Wopcorn.Server.csproj'
$SettingsPath  = Join-Path $PSScriptRoot 'wopcorn.host.json'

if (-not (Test-Path -LiteralPath $ServerProject)) {
    throw "Wopcorn.Server.csproj not found at $ServerProject. Run this script from the deploy/ folder of a Wopcorn clone."
}

# ------------------------------------------------------------- output -------

function Write-Step { param([string]$Text) Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Ok   { param([string]$Text) Write-Host "    $Text" -ForegroundColor Green }
function Write-Info { param([string]$Text) Write-Host "    $Text" -ForegroundColor Gray }
function Write-Note { param([string]$Text) Write-Host "  ! $Text" -ForegroundColor Yellow }
function Stop-WithError { param([string]$Text) Write-Host "  x $Text" -ForegroundColor Red; exit 1 }

# ------------------------------------------------------------ settings ------

function New-SettingsFile {
    $tmdbToken = ''
    $tmdbKey   = ''

    # On the machine the app was developed on the credentials are already in
    # user secrets. Copy them across so the first run has a working catalog.
    # User secrets only load in Development, so a Production host needs its own.
    try {
        $secrets = & dotnet user-secrets list --project $ServerProject 2>$null
        if ($LASTEXITCODE -eq 0) {
            foreach ($line in $secrets) {
                if ($line -match '^\s*Tmdb:ReadAccessToken\s*=\s*(.+?)\s*$') { $tmdbToken = $Matches[1] }
                if ($line -match '^\s*Tmdb:ApiKey\s*=\s*(.+?)\s*$')          { $tmdbKey   = $Matches[1] }
            }
        }
    } catch {
        # No secrets on this machine. Expected on a dedicated host.
    }

    $template = [ordered]@{
        Port       = 5080
        ServePort  = 443
        DataDir    = (Join-Path $env:ProgramData 'Wopcorn')
        PublishDir = (Join-Path $env:ProgramData 'Wopcorn\app')
        Tmdb       = [ordered]@{
            ReadAccessToken = $tmdbToken
            ApiKey          = $tmdbKey
        }
        # Leave Host empty to run without mail: the app logs the password-reset
        # link at Information instead of sending it, and the flow still works.
        Smtp       = [ordered]@{
            Host        = ''
            Port        = 587
            UseStartTls = $true
            UserName    = ''
            Password    = ''
            FromAddress = 'no-reply@wopcorn.local'
            FromName    = 'Wopcorn'
            # Empty means "use the origin the request arrived on", which is
            # correct here — forwarded headers make that the ts.net URL.
            AppBaseUrl  = ''
        }
    }

    $template | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $SettingsPath -Encoding UTF8

    Write-Step "Created $SettingsPath"
    if ($tmdbToken -or $tmdbKey) {
        Write-Ok 'TMDB credentials imported from this machine''s user secrets.'
    } else {
        Write-Note 'No TMDB credentials found. Put your v4 read access token in Tmdb.ReadAccessToken,'
        Write-Note 'or every catalog request will answer 503 and the app will have nothing to show.'
    }
    Write-Info 'Review the file, then run this script again.'
}

if (-not (Test-Path -LiteralPath $SettingsPath)) {
    New-SettingsFile
    exit 0
}

$Settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json

# Command line wins over the settings file, which wins over the defaults.
function Resolve-Setting {
    param($Override, $FromFile, $Default)
    if ($null -ne $Override -and $Override -ne '' -and $Override -ne 0) { return $Override }
    if ($null -ne $FromFile -and $FromFile -ne '' -and $FromFile -ne 0) { return $FromFile }
    return $Default
}

$Port       = [int](Resolve-Setting $Port       $Settings.Port       5080)
$ServePort  = [int](Resolve-Setting $ServePort  $Settings.ServePort  443)
$DataDir    =      Resolve-Setting $DataDir    $Settings.DataDir    (Join-Path $env:ProgramData 'Wopcorn')
$PublishDir =      Resolve-Setting $PublishDir $Settings.PublishDir (Join-Path $DataDir 'app')

$ExePath  = Join-Path $PublishDir 'Wopcorn.Server.exe'
$DbPath   = Join-Path $DataDir 'wopcorn.db'
$LogDir   = Join-Path $DataDir 'logs'
$PidPath  = Join-Path $DataDir 'wopcorn.pid'
$TaskName = 'Wopcorn'

# ---------------------------------------------------------- environment -----

# Every value the published app needs. User secrets do not load outside
# Development, so anything the app reads from configuration has to arrive here.
function Get-AppEnvironment {
    $map = [ordered]@{
        ASPNETCORE_ENVIRONMENT              = 'Production'
        ASPNETCORE_URLS                     = "http://127.0.0.1:$Port"
        ASPNETCORE_FORWARDEDHEADERS_ENABLED = 'true'
        ConnectionStrings__Wopcorn          = "Data Source=$DbPath"
    }

    if ($Settings.Tmdb) {
        if ($Settings.Tmdb.ReadAccessToken) { $map['Tmdb__ReadAccessToken'] = $Settings.Tmdb.ReadAccessToken }
        if ($Settings.Tmdb.ApiKey)          { $map['Tmdb__ApiKey']          = $Settings.Tmdb.ApiKey }
    }

    if ($Settings.Smtp -and $Settings.Smtp.Host) {
        $map['Smtp__Host']        = $Settings.Smtp.Host
        $map['Smtp__Port']        = [string]$Settings.Smtp.Port
        $map['Smtp__UseStartTls'] = ([string]$Settings.Smtp.UseStartTls).ToLowerInvariant()
        if ($Settings.Smtp.UserName)    { $map['Smtp__UserName']    = $Settings.Smtp.UserName }
        if ($Settings.Smtp.Password)    { $map['Smtp__Password']    = $Settings.Smtp.Password }
        if ($Settings.Smtp.FromAddress) { $map['Smtp__FromAddress'] = $Settings.Smtp.FromAddress }
        if ($Settings.Smtp.FromName)    { $map['Smtp__FromName']    = $Settings.Smtp.FromName }
    }
    if ($Settings.Smtp -and $Settings.Smtp.AppBaseUrl) {
        $map['Smtp__AppBaseUrl'] = $Settings.Smtp.AppBaseUrl
    }

    return $map
}

function Invoke-WithAppEnvironment {
    param([scriptblock]$Body)

    $map   = Get-AppEnvironment
    $saved = @{}
    foreach ($key in $map.Keys) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $map[$key], 'Process')
    }
    try {
        & $Body
    } finally {
        foreach ($key in $saved.Keys) {
            [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process')
        }
    }
}

# ------------------------------------------------------------ process -------

function Get-AppProcess {
    $candidates = @(Get-Process -Name 'Wopcorn.Server' -ErrorAction SilentlyContinue)
    foreach ($proc in $candidates) {
        try {
            if ($proc.Path -and ($proc.Path -eq $ExePath)) { return $proc }
        } catch {
            # Access denied reading Path (started by another user, e.g. SYSTEM).
            # Fall back to the recorded pid below.
        }
    }
    if (Test-Path -LiteralPath $PidPath) {
        $recorded = (Get-Content -LiteralPath $PidPath -Raw).Trim()
        if ($recorded -match '^\d+$') {
            $proc = Get-Process -Id ([int]$recorded) -ErrorAction SilentlyContinue
            if ($proc -and $proc.ProcessName -eq 'Wopcorn.Server') { return $proc }
        }
    }
    return $null
}

function Test-AppHealthy {
    param([int]$TimeoutSec = 3)
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/api/config" `
            -UseBasicParsing -TimeoutSec $TimeoutSec
        return ($response.StatusCode -eq 200)
    } catch {
        return $false
    }
}

function Wait-AppHealthy {
    param([int]$TimeoutSec = 60)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Test-AppHealthy) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

function Stop-App {
    $proc = Get-AppProcess
    if (-not $proc) {
        Write-Info 'Not running.'
        if (Test-Path -LiteralPath $PidPath) { Remove-Item -LiteralPath $PidPath -Force }
        return
    }

    Write-Step "Stopping Wopcorn (pid $($proc.Id))"
    Stop-Process -Id $proc.Id -Force
    # A running instance holds Wopcorn.Server.exe open; msbuild fails with MSB3027
    # until the handle is gone, so wait for the process to actually exit.
    try { $proc.WaitForExit(15000) | Out-Null } catch { }
    Start-Sleep -Milliseconds 500
    if (Test-Path -LiteralPath $PidPath) { Remove-Item -LiteralPath $PidPath -Force }
    Write-Ok 'Stopped.'
}

function Start-App {
    if (-not (Test-Path -LiteralPath $ExePath)) {
        Stop-WithError "Nothing published at $ExePath. Run: .\Host-Wopcorn.ps1 deploy"
    }

    $existing = Get-AppProcess
    if ($existing) {
        Write-Info "Already running (pid $($existing.Id))."
        return
    }

    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    $stamp     = Get-Date -Format 'yyyyMMdd-HHmmss'
    $outLog    = Join-Path $LogDir "wopcorn-$stamp.log"
    $errLog    = Join-Path $LogDir "wopcorn-$stamp.err.log"

    Write-Step "Starting Wopcorn on http://127.0.0.1:$Port"
    $proc = Invoke-WithAppEnvironment {
        Start-Process -FilePath $ExePath -WorkingDirectory $PublishDir `
            -RedirectStandardOutput $outLog -RedirectStandardError $errLog `
            -WindowStyle Hidden -PassThru
    }
    Set-Content -LiteralPath $PidPath -Value $proc.Id -Encoding ASCII

    if (Wait-AppHealthy -TimeoutSec 60) {
        Write-Ok "Healthy (pid $($proc.Id))."
        Write-Info "Log: $outLog"
    } else {
        Write-Note 'The app did not answer /api/config within 60s. Last log lines:'
        foreach ($file in @($outLog, $errLog)) {
            if ((Test-Path -LiteralPath $file) -and (Get-Item -LiteralPath $file).Length -gt 0) {
                Write-Host "--- $file" -ForegroundColor DarkGray
                Get-Content -LiteralPath $file -Tail 25 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
            }
        }
        exit 1
    }

    # Keep the last ten runs and no more.
    Get-ChildItem -LiteralPath $LogDir -Filter 'wopcorn-*.log' |
        Sort-Object LastWriteTime -Descending | Select-Object -Skip 20 |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

# -------------------------------------------------------------- build -------

function Invoke-Publish {
    Write-Step 'Publishing (this builds the Vue client too, so it takes a minute)'
    New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

    & dotnet publish $ServerProject -c Release -o $PublishDir --nologo
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError 'dotnet publish failed. If the error is MSB3027, the app is still running — stop it and retry.'
    }
    Write-Ok "Published to $PublishDir"
}

# Avatars are written into wwwroot at runtime, which would put user uploads
# inside the folder every redeploy overwrites. A junction keeps the real files
# in the data directory, next to the database that references them.
function Set-AvatarJunction {
    $target = Join-Path $DataDir 'avatars'
    New-Item -ItemType Directory -Force -Path $target | Out-Null

    $webRoot = Join-Path $PublishDir 'wwwroot'
    if (-not (Test-Path -LiteralPath $webRoot)) {
        New-Item -ItemType Directory -Force -Path $webRoot | Out-Null
    }

    $link = Join-Path $webRoot 'avatars'
    $item = Get-Item -LiteralPath $link -Force -ErrorAction SilentlyContinue
    if ($item) {
        if ($item.LinkType -in @('Junction', 'SymbolicLink')) { return }
        Get-ChildItem -LiteralPath $link -Force | ForEach-Object {
            Move-Item -LiteralPath $_.FullName -Destination $target -Force
        }
        Remove-Item -LiteralPath $link -Recurse -Force
    }
    New-Item -ItemType Junction -Path $link -Target $target | Out-Null
    Write-Ok "Avatars stored in $target"
}

function Invoke-Migrate {
    Write-Step 'Applying database migrations'
    New-Item -ItemType Directory -Force -Path $DataDir | Out-Null

    $null = & dotnet ef --version
    if ($LASTEXITCODE -ne 0) {
        Write-Info 'Installing the dotnet-ef tool...'
        & dotnet tool install --global dotnet-ef --version "10.*"
        if ($LASTEXITCODE -ne 0) {
            Stop-WithError 'Could not install dotnet-ef. Install it manually: dotnet tool install --global dotnet-ef'
        }
    }

    # --no-build reuses what publish already built. Without it the design-time
    # build pulls in the client esproj and runs npm all over again, so if the
    # output is missing (a -SkipBuild run on a clean machine) build it once with
    # the client reference switched off instead.
    $assembly = Join-Path $RepoRoot 'Wopcorn.Server\bin\Release\net10.0\Wopcorn.Server.dll'
    if (-not (Test-Path -LiteralPath $assembly)) {
        & dotnet build $ServerProject -c Release -p:BuildClient=false --nologo
        if ($LASTEXITCODE -ne 0) { Stop-WithError 'Build failed; cannot run migrations.' }
    }

    # Nothing migrates at startup, by design — this is the only thing that
    # creates the database or brings it up to date. Skip it and every query
    # against a new table fails at runtime with "no such table".
    Invoke-WithAppEnvironment {
        & dotnet ef database update --project $ServerProject --context WopcornDbContext `
            --no-build --configuration Release
    }
    if ($LASTEXITCODE -ne 0) {
        Stop-WithError 'Migrations failed. Every query against a new table would fail at runtime, so this is fatal.'
    }
    Write-Ok "Database at $DbPath"
}

# ---------------------------------------------------------- tailscale -------

function Get-TailscaleExe {
    $cmd = Get-Command tailscale -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $fallback = Join-Path $env:ProgramFiles 'Tailscale\tailscale.exe'
    if (Test-Path -LiteralPath $fallback) { return $fallback }
    return $null
}

function Get-TailscaleNode {
    $exe = Get-TailscaleExe
    if (-not $exe) { return $null }
    $raw = & $exe status --json
    if ($LASTEXITCODE -ne 0) { return $null }
    $status = ($raw -join "`n") | ConvertFrom-Json
    if ($status.BackendState -ne 'Running') {
        return [pscustomobject]@{ State = $status.BackendState; DnsName = $null }
    }
    return [pscustomobject]@{
        State   = $status.BackendState
        DnsName = ($status.Self.DNSName).TrimEnd('.')
    }
}

function Get-ServeHandler {
    param([string]$HostPort)
    $exe = Get-TailscaleExe
    $raw = & $exe serve status --json
    if ($LASTEXITCODE -ne 0) { return $null }
    $text = ($raw -join "`n").Trim()
    if (-not $text -or $text -eq 'null') { return $null }
    $config = $text | ConvertFrom-Json
    if (-not $config.Web) { return $null }
    $entry = $config.Web.PSObject.Properties | Where-Object { $_.Name -eq $HostPort }
    if (-not $entry) { return $null }
    $root = $entry.Value.Handlers.PSObject.Properties | Where-Object { $_.Name -eq '/' }
    if (-not $root) { return $null }
    return $root.Value.Proxy
}

function Set-TailscaleServe {
    $exe = Get-TailscaleExe
    if (-not $exe) {
        Write-Note 'tailscale.exe not found. Install Tailscale, then run: .\Host-Wopcorn.ps1 serve'
        return
    }

    $node = Get-TailscaleNode
    if (-not $node -or -not $node.DnsName) {
        $state = 'unknown'
        if ($node) { $state = $node.State }
        Write-Note "Tailscale is not connected (state: $state). Run 'tailscale up', then: .\Host-Wopcorn.ps1 serve"
        return
    }

    $hostPort = "$($node.DnsName):$ServePort"
    $wanted   = "http://127.0.0.1:$Port"
    $existing = Get-ServeHandler -HostPort $hostPort

    if ($existing -and ($existing -ne $wanted) -and -not $Force) {
        Write-Note "$hostPort is already served to $existing."
        Write-Note "Another app owns that port. Either free it, or pick a different one:"
        Write-Note "  .\Host-Wopcorn.ps1 serve -ServePort 8443"
        Write-Note "Re-run with -Force to take it over."
        return
    }
    $suffix = ''
    if ($ServePort -ne 443) { $suffix = ":$ServePort" }

    if ($existing -eq $wanted) {
        Write-Ok "Already served at https://$($node.DnsName)$suffix"
        return
    }

    Write-Step "Publishing to the tailnet on port $ServePort"
    & $exe serve --bg "--https=$ServePort" $wanted
    if ($LASTEXITCODE -ne 0) {
        Write-Note 'tailscale serve failed. The usual causes, in order of likelihood:'
        Write-Note '  1. HTTPS certificates are not enabled for the tailnet (admin console -> DNS -> HTTPS Certificates).'
        Write-Note '  2. The command needs an elevated PowerShell on Windows.'
        Write-Note '  3. MagicDNS is off.'
        return
    }

    Write-Ok "Live at https://$($node.DnsName)$suffix"
}

function Remove-TailscaleServe {
    $exe = Get-TailscaleExe
    if (-not $exe) { Stop-WithError 'tailscale.exe not found.' }
    Write-Step "Removing the serve mapping on port $ServePort"
    & $exe serve "--https=$ServePort" off
    if ($LASTEXITCODE -eq 0) { Write-Ok 'Removed.' } else { Write-Note 'tailscale serve --off failed.' }
}

# ------------------------------------------------------- startup task -------

function Test-Administrator {
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Install-StartupTask {
    if (-not (Test-Administrator)) {
        Stop-WithError 'Registering a scheduled task needs an elevated PowerShell. Right-click -> Run as administrator.'
    }

    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" start"
    $action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                    -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)

    # Two triggers: one at boot, and one that re-runs every ten minutes. `start`
    # is a no-op when the app is already up, so the repeat is a cheap supervisor
    # that brings it back after a crash.
    $triggers = @(New-ScheduledTaskTrigger -AtStartup)
    try {
        $repeat = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(2) `
            -RepetitionInterval (New-TimeSpan -Minutes 10) -RepetitionDuration ([TimeSpan]::MaxValue)
        $triggers += $repeat
    } catch {
        Write-Note 'Could not add the ten-minute health trigger; the app will still start at boot.'
    }

    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers `
        -Principal $principal -Settings $settings -Force | Out-Null

    Write-Ok "Scheduled task '$TaskName' registered: starts at boot, checks every 10 minutes."
}

function Uninstall-StartupTask {
    if (-not (Test-Administrator)) {
        Stop-WithError 'Removing a scheduled task needs an elevated PowerShell.'
    }
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Ok "Scheduled task '$TaskName' removed."
}

# ------------------------------------------------------------ status --------

function Show-Status {
    Write-Step 'Wopcorn'

    $proc = Get-AppProcess
    if ($proc) {
        $healthy = 'not answering'
        if (Test-AppHealthy) { $healthy = 'healthy' }
        Write-Ok "Running: pid $($proc.Id), http://127.0.0.1:$Port ($healthy)"
    } else {
        Write-Info 'Not running.'
    }

    Write-Info "Published: $PublishDir"
    if (Test-Path -LiteralPath $DbPath) {
        $size = [math]::Round((Get-Item -LiteralPath $DbPath).Length / 1MB, 1)
        Write-Info "Database:  $DbPath ($size MB)"
    } else {
        Write-Note "Database:  $DbPath (does not exist yet)"
    }

    $node = Get-TailscaleNode
    if ($node -and $node.DnsName) {
        $hostPort = "$($node.DnsName):$ServePort"
        $handler  = Get-ServeHandler -HostPort $hostPort
        $suffix   = ''
        if ($ServePort -ne 443) { $suffix = ":$ServePort" }
        if ($handler -eq "http://127.0.0.1:$Port") {
            Write-Ok "URL:       https://$($node.DnsName)$suffix"
        } elseif ($handler) {
            Write-Note "URL:       $hostPort is served to $handler, not to Wopcorn."
        } else {
            Write-Note "URL:       nothing served on $hostPort. Run: .\Host-Wopcorn.ps1 serve"
        }
    } else {
        Write-Note 'Tailscale: not connected.'
    }

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($task) {
        Write-Info "Autostart: scheduled task '$TaskName' ($($task.State))"
    } else {
        Write-Info "Autostart: off. Enable with: .\Host-Wopcorn.ps1 install-startup (as administrator)"
    }

    if (-not $Settings.Tmdb -or (-not $Settings.Tmdb.ReadAccessToken -and -not $Settings.Tmdb.ApiKey)) {
        Write-Note 'No TMDB credentials in wopcorn.host.json — every catalog request will answer 503.'
    }
}

function Show-Logs {
    if (-not (Test-Path -LiteralPath $LogDir)) { Stop-WithError "No logs in $LogDir yet." }
    $latest = Get-ChildItem -LiteralPath $LogDir -Filter 'wopcorn-*.log' |
        Where-Object { $_.Name -notlike '*.err.log' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { Stop-WithError "No logs in $LogDir yet." }

    Write-Step $latest.FullName
    if ($Follow) {
        Get-Content -LiteralPath $latest.FullName -Tail $Tail -Wait
    } else {
        Get-Content -LiteralPath $latest.FullName -Tail $Tail
    }
}

# ----------------------------------------------------------- dispatch -------

switch ($Command) {
    'deploy' {
        if (-not $Settings.Tmdb -or (-not $Settings.Tmdb.ReadAccessToken -and -not $Settings.Tmdb.ApiKey)) {
            Write-Note 'No TMDB credentials configured. Deploying anyway — search and every title screen'
            Write-Note 'will answer 503 until you add one to wopcorn.host.json and run: .\Host-Wopcorn.ps1 restart'
        }
        Stop-App
        if (-not $SkipBuild) { Invoke-Publish }
        Set-AvatarJunction
        Invoke-Migrate
        Start-App
        if (-not $NoServe) { Set-TailscaleServe }
        Write-Host ''
        Show-Status
    }
    'start'   { Start-App }
    'stop'    { Stop-App }
    'restart' { Stop-App; Start-App }
    'status'  { Show-Status }
    'logs'    { Show-Logs }
    'serve'   { Set-TailscaleServe }
    'unserve' { Remove-TailscaleServe }
    'install-startup'   { Install-StartupTask }
    'uninstall-startup' { Uninstall-StartupTask }
}
