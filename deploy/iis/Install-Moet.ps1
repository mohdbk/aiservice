<#
.SYNOPSIS
    Installs or updates the MoET recruitment system on IIS (Ministry of Economy & Tourism).

.DESCRIPTION
    Copies the apps from the package, writes each site's web.config from the settings file, creates or updates the app
    pools and sites, sets IIS up for them (ARR proxy for the backoffice's /api, folder permissions), and checks that
    each app answers. Run it again with the same settings file to update to a new package; the apps are taken offline
    for the few seconds the copy takes.

    Two public sites: the backoffice (staff) and the careers portal (candidates). The API and the agent are IIS sites
    too, but bound to this machine or the internal network only. -Roles installs a part of the system on each server,
    for example the portal alone on a DMZ server.

.PARAMETER SettingsFile
    The settings (see moet.settings.example.psd1). Default: moet.settings.psd1 next to this script.

.PARAMETER PackageRoot
    The unzipped package (folders api, agent, backoffice, portal, ms-playwright). Default: the folder above this script.

.PARAMETER Roles
    Which apps to install on this server: Api, Agent, Backoffice, Portal. Default: all four.

.EXAMPLE
    .\Install-Moet.ps1
.EXAMPLE
    .\Install-Moet.ps1 -Roles Portal -SettingsFile D:\secure\portal.settings.psd1
#>
[CmdletBinding()]
param(
    [string]$SettingsFile = (Join-Path $PSScriptRoot 'moet.settings.psd1'),
    [string]$PackageRoot = (Split-Path $PSScriptRoot -Parent),
    [ValidateSet('Api', 'Agent', 'Backoffice', 'Portal')]
    [string[]]$Roles = @('Agent', 'Api', 'Backoffice', 'Portal'),
    [switch]$SkipHealthCheck
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

function Say([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Fail([string]$text) { throw "MoET install: $text" }
function Value($table, [string]$key, $default = '') {
    if ($null -ne $table -and $table.ContainsKey($key) -and $null -ne $table[$key]) { return $table[$key] }
    return $default
}

# ---------------------------------------------------------------- checks

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Fail 'run this script as an administrator.' }
if (-not (Test-Path $SettingsFile)) { Fail "settings file not found: $SettingsFile (copy moet.settings.example.psd1 and fill it in)." }
$S = Import-PowerShellDataFile -Path $SettingsFile
$root = Value $S 'InstallRoot'
if (-not $root) { Fail 'InstallRoot is empty in the settings file.' }
$fullRoot = [IO.Path]::GetFullPath($root).TrimEnd('\')
$fullPackage = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\')
if ($fullRoot -eq $fullPackage -or $fullRoot.StartsWith("$fullPackage\", 'OrdinalIgnoreCase') -or $fullPackage.StartsWith("$fullRoot\", 'OrdinalIgnoreCase')) {
    Fail "InstallRoot ($fullRoot) must be a separate folder from the package ($fullPackage), for example D:\MoET\Recruitment."
}

# Windows' own tools by full path: some servers have a PATH without System32.
$sys32 = Join-Path $env:SystemRoot 'System32'
$robocopy = Join-Path $sys32 'robocopy.exe'
$icacls = Join-Path $sys32 'icacls.exe'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $found = Get-Command dotnet -ErrorAction SilentlyContinue; $dotnet = $(if ($found) { $found.Source } else { '' }) }

Import-Module WebAdministration
$modules = @(Get-WebGlobalModule | ForEach-Object { $_.Name })
$need = @{ 'AspNetCoreModuleV2' = '.NET 10 Hosting Bundle' }
if ($Roles -contains 'Backoffice') {
    $need['RewriteModule'] = 'IIS URL Rewrite 2.1'
    $need['ApplicationRequestRouting'] = 'IIS Application Request Routing 3.0'
}
$missing = @($need.Keys | Where-Object { $modules -notcontains $_ } | ForEach-Object { "$($need[$_]) ($_)" })
if ($missing.Count -gt 0) { Fail ("install first: " + ($missing -join ', ') + '. See docs/ON_PREM.md, "Windows Server and IIS".') }

if ($Roles -contains 'Api' -or $Roles -contains 'Agent') {
    if (-not $dotnet) { Fail 'dotnet.exe not found: install the .NET 10 Hosting Bundle.' }
    $runtimes = (& $dotnet --list-runtimes 2>$null) -join "`n"
    if ($runtimes -notmatch 'Microsoft\.AspNetCore\.App 10\.') { Fail 'the ASP.NET Core 10 runtime is missing: install the .NET 10 Hosting Bundle.' }
}
$node = Value $S 'NodePath' 'C:\Program Files\nodejs\node.exe'
if ($Roles -contains 'Portal') {
    if (-not (Test-Path $node)) { Fail "node.exe not found at $node (install Node.js 22 LTS or set NodePath)." }
    $nodeVersion = (& $node --version).Trim()
    if ([int]($nodeVersion.TrimStart('v').Split('.')[0]) -lt 22) { Fail "Node.js 22 or later is needed; $node is $nodeVersion." }
}
foreach ($r in $Roles) {
    $folder = $r.ToLower()
    if (-not (Test-Path (Join-Path $PackageRoot $folder))) { Fail "the package has no '$folder' folder under $PackageRoot." }
    if (-not $S.ContainsKey($r)) { Fail "the settings file has no $r section." }
}

function Require($settings, [string]$section, [string[]]$keys) {
    foreach ($k in $keys) { if (-not (Value $settings $k)) { Fail "$section setting $k is empty." } }
}
if ($Roles -contains 'Api') { Require $S.Api.Settings 'Api' @('DB_CONNECTION', 'PORTAL_API_KEY', 'PORTAL_API_SECRET', 'AGENT_KEY') }
if ($Roles -contains 'Agent') { Require $S.Agent.Settings 'Agent' @('BACKEND_URL', 'AGENT_KEY') }
if ($Roles -contains 'Portal') {
    Require $S.Portal.Settings 'Portal' @('RECRUITMENT_API_URL', 'CAREERS_PORTAL_API_KEY', 'CAREERS_PORTAL_API_SECRET', 'SESSION_SECRET')
    if ($S.Portal.Settings.SESSION_SECRET.Length -lt 32) { Fail 'Portal SESSION_SECRET must be at least 32 characters.' }
}
if ($Roles -contains 'Api' -and $Roles -contains 'Portal') {
    if ($S.Api.Settings.PORTAL_API_KEY -ne $S.Portal.Settings.CAREERS_PORTAL_API_KEY -or $S.Api.Settings.PORTAL_API_SECRET -ne $S.Portal.Settings.CAREERS_PORTAL_API_SECRET) {
        Fail 'the portal key and secret differ from the API''s PORTAL_API_KEY and PORTAL_API_SECRET.'
    }
}
if ($Roles -contains 'Api' -and $Roles -contains 'Agent' -and $S.Api.Settings.AGENT_KEY -ne $S.Agent.Settings.AGENT_KEY) {
    Fail 'AGENT_KEY differs between the API and the agent.'
}

# ---------------------------------------------------------------- helpers

function Write-WebConfig([string]$template, [string]$target, [hashtable]$env, [hashtable]$tokens) {
    $text = Get-Content -Raw -Path (Join-Path $PSScriptRoot "templates\$template")
    $lines = foreach ($k in ($env.Keys | Sort-Object)) {
        $v = [string]$env[$k]
        if ($v -ne '') { '          <environmentVariable name="{0}" value="{1}" />' -f $k, [Security.SecurityElement]::Escape($v) }
    }
    $text = $text.Replace('{{ENV}}', ($lines -join "`r`n"))
    if ($dotnet) { $text = $text.Replace('processPath="dotnet"', 'processPath="' + [Security.SecurityElement]::Escape($dotnet) + '"') }
    foreach ($t in $tokens.Keys) { $text = $text.Replace("{{$t}}", [Security.SecurityElement]::Escape([string]$tokens[$t])) }
    [xml]$text | Out-Null   # a malformed file would take the site down: check before writing
    [IO.File]::WriteAllText($target, $text, (New-Object Text.UTF8Encoding($false)))
}

function Copy-App([string]$from, [string]$to, [bool]$offline) {
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    $marker = Join-Path $to 'app_offline.htm'
    if ($offline -and (Test-Path (Join-Path $to 'web.config'))) {
        # The ASP.NET Core Module stops the app and answers 503 while this file exists, so its files can be replaced.
        Set-Content -Path $marker -Value '<!doctype html><title>Updating</title><p>Updating, back in a minute.</p>' -Encoding UTF8
        Start-Sleep -Seconds 3
    }
    & $robocopy $from $to /MIR /NFL /NDL /NJH /NJS /NP /R:5 /W:2 /XF web.config app_offline.htm /XD logs | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "copying $from to $to failed (robocopy exit code $LASTEXITCODE)." }
    $global:LASTEXITCODE = 0
    New-Item -ItemType Directory -Force -Path (Join-Path $to 'logs') | Out-Null
}

function Grant([string]$path, [string]$who, [string]$rights) {
    & $icacls $path /grant:r "${who}:(OI)(CI)$rights" /T /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "could not grant $who on $path." }
}

function Protect-Folder([string]$path, [string]$pool) {
    # Only administrators, SYSTEM and the app's own pool: web.config holds the app's passwords and keys.
    & $icacls $path /inheritance:r /grant:r 'BUILTIN\Administrators:(OI)(CI)F' 'NT AUTHORITY\SYSTEM:(OI)(CI)F' /C /Q | Out-Null
    Grant $path "IIS AppPool\$pool" 'RX'
    Grant (Join-Path $path 'logs') "IIS AppPool\$pool" 'M'
}

function Set-Pool([string]$name, [hashtable]$identity, [bool]$single) {
    $p = "IIS:\AppPools\$name"
    if (-not (Test-Path $p)) { New-WebAppPool -Name $name | Out-Null }
    Set-ItemProperty $p -Name managedRuntimeVersion -Value ''
    Set-ItemProperty $p -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty $p -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty $p -Name processModel.loadUserProfile -Value $true
    Set-ItemProperty $p -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
    # One worker at a time: the API's background jobs and database updates never run twice during a recycle.
    if ($single) { Set-ItemProperty $p -Name recycling.disallowOverlappingRotation -Value $true }
    $user = Value $identity 'User'
    if ($user) {
        Set-ItemProperty $p -Name processModel -Value @{ identityType = 'SpecificUser'; userName = $user; password = (Value $identity 'Password') }
    } else {
        Set-ItemProperty $p -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    }
}

function Set-Site([string]$name, [string]$path, [string]$pool, [hashtable]$cfg) {
    $ip = Value $cfg 'IpAddress' '*'
    $hostName = Value $cfg 'HostName'
    $https = [int](Value $cfg 'HttpsPort' 0)
    $http = [int](Value $cfg 'HttpPort' 0)
    $thumb = (Value $cfg 'CertificateThumbprint') -replace '\s', ''
    if ($https -gt 0 -and -not $thumb) { Fail "$name has an HTTPS port but no CertificateThumbprint." }
    if ($https -le 0 -and $http -le 0) { Fail "$name has neither an HTTPS nor an HTTP port." }
    $bindings = @()
    if ($https -gt 0) { $bindings += @{ protocol = 'https'; bindingInformation = "${ip}:${https}:$hostName"; sslFlags = $(if ($hostName) { 1 } else { 0 }) } }
    if ($http -gt 0) { $bindings += @{ protocol = 'http'; bindingInformation = "${ip}:${http}:$hostName" } }
    $site = "IIS:\Sites\$name"
    if (-not (Test-Path $site)) { New-Item $site -bindings $bindings[0] -physicalPath $path | Out-Null }
    Set-ItemProperty $site -Name bindings -Value $bindings
    Set-ItemProperty $site -Name physicalPath -Value $path
    Set-ItemProperty $site -Name applicationPool -Value $pool
    Set-ItemProperty $site -Name serverAutoStart -Value $true
    if ($https -gt 0) {
        if (-not (Test-Path "Cert:\LocalMachine\My\$thumb")) { Fail "certificate $thumb is not in Cert:\LocalMachine\My." }
        foreach ($b in @(Get-WebBinding -Name $name -Protocol https)) {
            try { $b.RemoveSslCertificate() } catch { }   # none yet, or bound to an older certificate
            $b.AddSslCertificate($thumb, 'My')
        }
    }
    $apphost = 'MACHINE/WEBROOT/APPHOST'
    # Anonymous requests run as the app pool, so the pool's folder permissions are the only ones needed.
    Set-WebConfigurationProperty -PSPath $apphost -Location $name -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $true
    Set-WebConfigurationProperty -PSPath $apphost -Location $name -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name userName -Value ''
    foreach ($auth in 'windowsAuthentication', 'basicAuthentication') {
        try { Set-WebConfigurationProperty -PSPath $apphost -Location $name -Filter "system.webServer/security/authentication/$auth" -Name enabled -Value $false } catch { }
    }
    return $(if ($http -gt 0) { "http://$(if ($ip -eq '*') { '127.0.0.1' } else { $ip }):$http" } else { '' })
}

function Set-Preload([string]$name) {
    $f = $null
    try { $f = Get-WindowsFeature -Name Web-AppInit -ErrorAction Stop } catch { }
    $module = Get-WebGlobalModule -Name ApplicationInitializationModule -ErrorAction SilentlyContinue
    if (($f -and $f.Installed) -or $module) {
        Set-ItemProperty "IIS:\Sites\$name" -Name applicationDefaults.preloadEnabled -Value $true
    } else {
        Write-Warning "IIS Application Initialization (Web-AppInit) is not installed: $name starts on its first request instead of with IIS."
    }
}

$probes = @()
function Restart-App([string]$site, [string]$pool, [string]$dir, [string]$url, [string]$path, [int]$wait) {
    Remove-Item -Path (Join-Path $dir 'app_offline.htm') -ErrorAction SilentlyContinue
    if ((Get-WebAppPoolState -Name $pool).Value -ne 'Started') { Start-WebAppPool -Name $pool } else { Restart-WebAppPool -Name $pool }
    if ((Get-WebsiteState -Name $site).Value -ne 'Started') { Start-Website -Name $site }
    if ($url) { $script:probes += [pscustomobject]@{ Name = $site; Url = "$url$path"; Wait = $wait } }
}

# ---------------------------------------------------------------- server-wide IIS settings

if ($Roles -contains 'Backoffice') {
    Say 'IIS: Application Request Routing proxy for the backoffice /api'
    $apphost = 'MACHINE/WEBROOT/APPHOST'
    Set-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/proxy' -Name enabled -Value $true
    # The API checks that a staff request comes from its own address (Origin against Host): keep the original Host.
    Set-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/proxy' -Name preserveHostHeader -Value $true
    Set-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/proxy' -Name reverseRewriteHostInResponseHeaders -Value $false
    # The assistant streams its answers and can take minutes: no response buffering, 10 minute timeout.
    Set-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/proxy' -Name responseBufferLimit -Value 0
    Set-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/proxy' -Name timeout -Value ([TimeSpan]::FromMinutes(10))
    foreach ($opt in @(@('includePortInXForwardedFor', $false), @('arrResponseHeader', $false))) {
        try { Set-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/proxy' -Name $opt[0] -Value $opt[1] } catch { Write-Warning "ARR setting $($opt[0]) not available on this version." }
    }
    $allowed = @(Get-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/rewrite/allowedServerVariables' -Name collection | ForEach-Object { $_.name })
    if ($allowed -notcontains 'HTTP_X_FORWARDED_PROTO') {
        Add-WebConfigurationProperty -PSPath $apphost -Filter 'system.webServer/rewrite/allowedServerVariables' -Name '.' -Value @{ name = 'HTTP_X_FORWARDED_PROTO' }
    }
}

New-Item -ItemType Directory -Force -Path $root | Out-Null

# ---------------------------------------------------------------- agent

if ($Roles -contains 'Agent') {
    $c = $S.Agent; $dir = Join-Path $root 'agent'; $pool = Value $c 'AppPool' 'MoetRecruitmentAgent'
    Say "Agent -> $dir"
    Copy-App (Join-Path $PackageRoot 'agent') $dir $true
    $env = @{ ASPNETCORE_ENVIRONMENT = 'Production' }
    foreach ($k in $c.Settings.Keys) { $env[$k] = $c.Settings[$k] }
    Write-WebConfig 'agent.web.config' (Join-Path $dir 'web.config') $env @{ DLL = 'Moet.Recruitment.Agent.dll' }
    Set-Pool $pool (Value $c 'Identity' @{}) $false
    Protect-Folder $dir $pool
    $url = Set-Site (Value $c 'SiteName' 'MoET Recruitment Agent') $dir $pool $c
    Set-Preload (Value $c 'SiteName' 'MoET Recruitment Agent')
    Restart-App (Value $c 'SiteName' 'MoET Recruitment Agent') $pool $dir $url '/healthz' 60
}

# ---------------------------------------------------------------- API

if ($Roles -contains 'Api') {
    $c = $S.Api; $dir = Join-Path $root 'api'; $pool = Value $c 'AppPool' 'MoetRecruitmentApi'
    $browsers = Join-Path $root 'ms-playwright'
    Say "API -> $dir"
    Copy-App (Join-Path $PackageRoot 'api') $dir $true
    if (Test-Path (Join-Path $PackageRoot 'ms-playwright')) {
        Say "Chromium for PDF letters -> $browsers"
        & $robocopy (Join-Path $PackageRoot 'ms-playwright') $browsers /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { Fail "copying Chromium failed (robocopy exit code $LASTEXITCODE)." }
        $global:LASTEXITCODE = 0
    } else {
        Write-Warning 'The package has no ms-playwright folder: PDF letters need Chromium (see docs/ON_PREM.md, "PDF letters").'
    }
    # Behind the backoffice's proxy, the client address and scheme come from X-Forwarded-For and X-Forwarded-Proto.
    $env = @{ ASPNETCORE_ENVIRONMENT = 'Production'; TRUST_PROXY = '1'; PLAYWRIGHT_BROWSERS_PATH = $browsers }
    foreach ($k in $c.Settings.Keys) { $env[$k] = $c.Settings[$k] }
    Write-WebConfig 'api.web.config' (Join-Path $dir 'web.config') $env @{ DLL = 'Moet.Recruitment.Api.dll' }
    Set-Pool $pool (Value $c 'Identity' @{}) $true
    Protect-Folder $dir $pool
    if (Test-Path $browsers) { Grant $browsers "IIS AppPool\$pool" 'RX' }
    $site = Value $c 'SiteName' 'MoET Recruitment API'
    $url = Set-Site $site $dir $pool $c
    Set-Preload $site
    # The first start creates or updates the database, which can take a few minutes.
    Restart-App $site $pool $dir $url '/healthz' 300
}

# ---------------------------------------------------------------- backoffice

if ($Roles -contains 'Backoffice') {
    $c = $S.Backoffice; $dir = Join-Path $root 'backoffice'; $pool = Value $c 'AppPool' 'MoetRecruitmentBackoffice'
    $api = ([string](Value $c 'ApiUrl' 'http://127.0.0.1:5080')).TrimEnd('/')
    Say "Backoffice -> $dir (API at $api)"
    Copy-App (Join-Path $PackageRoot 'backoffice') $dir $false
    Write-WebConfig 'backoffice.web.config' (Join-Path $dir 'web.config') @{} @{ API_URL = $api }
    Set-Pool $pool (Value $c 'Identity' @{}) $false
    Protect-Folder $dir $pool
    $site = Value $c 'SiteName' 'MoET Recruitment Backoffice'
    $url = Set-Site $site $dir $pool $c
    Restart-App $site $pool $dir $url '/healthz' 30
    if ($url) { $script:probes += [pscustomobject]@{ Name = "$site (API through the proxy)"; Url = "$url/api/backoffice/me"; Wait = 120 } }
}

# ---------------------------------------------------------------- portal

if ($Roles -contains 'Portal') {
    $c = $S.Portal; $dir = Join-Path $root 'portal'; $pool = Value $c 'AppPool' 'MoetCareersPortal'
    Say "Careers portal -> $dir"
    Copy-App (Join-Path $PackageRoot 'portal') $dir $true
    # The Node server listens on this machine only, behind IIS, and takes the client address from the last
    # X-Forwarded-For entry (the one IIS adds), so a browser cannot choose its own address for the rate limits.
    $env = @{ NODE_ENV = 'production'; HOST = '127.0.0.1'; TRUST_PROXY = '1'; CLIENT_IP_FROM = 'last'; COOKIE_SECURE = $(if ([int](Value $c 'HttpsPort' 0) -gt 0) { '1' } else { '0' }) }
    foreach ($k in $c.Settings.Keys) { $env[$k] = $c.Settings[$k] }
    Write-WebConfig 'portal.web.config' (Join-Path $dir 'web.config') $env @{ NODE = $node; DIR = $dir }
    Set-Pool $pool (Value $c 'Identity' @{}) $false
    Protect-Folder $dir $pool
    $site = Value $c 'SiteName' 'MoET Careers Portal'
    $url = Set-Site $site $dir $pool $c
    Set-Preload $site
    Restart-App $site $pool $dir $url '/healthz' 60
}

# ---------------------------------------------------------------- health

if (-not $SkipHealthCheck -and $probes.Count -gt 0) {
    Say 'Checking that each app answers'
    $bad = 0
    foreach ($p in $probes) {
        $deadline = (Get-Date).AddSeconds($p.Wait)
        $status = 0
        do {
            try {
                $status = [int](Invoke-WebRequest -Uri $p.Url -UseBasicParsing -TimeoutSec 15).StatusCode
            } catch {
                $status = 0
                if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
            }
            # 401 from /api/backoffice/me means the request reached the API through the proxy (nobody is signed in).
            $ok = ($status -eq 200) -or ($p.Url -like '*/api/backoffice/me' -and $status -eq 401)
            if (-not $ok) { Start-Sleep -Seconds 3 }
        } while (-not $ok -and (Get-Date) -lt $deadline)
        if ($ok) { Write-Host "  ok   $($p.Name)  $($p.Url)" -ForegroundColor Green }
        else { $bad++; Write-Host "  FAIL $($p.Name)  $($p.Url)  (status $status)" -ForegroundColor Red }
    }
    if ($bad -gt 0) { Fail "$bad check(s) failed. Look in the Windows Event Log (Application) and in each app's logs folder." }
}

$https = @($Roles | Where-Object { $_ -in 'Backoffice', 'Portal' } | Where-Object { [int](Value $S[$_] 'HttpPort' 0) -le 0 })
foreach ($r in $https) { Write-Host "  $r answers on HTTPS only: open https://$(Value $S[$r] 'HostName' 'this-server')/ to check it." }
Say 'Done.'
