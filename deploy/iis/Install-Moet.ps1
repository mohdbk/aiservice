<#
.SYNOPSIS
    Installs or updates the MoET recruitment system on IIS (Ministry of Economy & Tourism).

.DESCRIPTION
    Two IIS sites, both plain ASP.NET Core apps (IIS needs only the .NET 10 Hosting Bundle):

      Backoffice  the staff app, its API and the recruitment assistant, in one app
      Portal      the candidate site and its small server, which calls the backoffice's API with a service key

    The script copies each app from the package, writes its web.config from the settings file, creates or updates the
    app pool and site, sets folder permissions, and checks that each site answers. Run it again with the same settings
    file to update to a new package; each site shows a short "Updating" page while its files are replaced.

    -Roles installs one site on each server, for example the portal alone on a DMZ server.

.PARAMETER SettingsFile
    The settings (see moet.settings.example.psd1). Default: moet.settings.psd1 next to this script.

.PARAMETER PackageRoot
    The unzipped package (folders backoffice, portal, ms-playwright). Default: the folder above this script.

.PARAMETER Roles
    Which sites to install on this server: Backoffice, Portal. Default: both.

.EXAMPLE
    .\Install-Moet.ps1
.EXAMPLE
    .\Install-Moet.ps1 -Roles Portal -SettingsFile D:\secure\portal.settings.psd1
#>
[CmdletBinding()]
param(
    [string]$SettingsFile = (Join-Path $PSScriptRoot 'moet.settings.psd1'),
    [string]$PackageRoot = (Split-Path $PSScriptRoot -Parent),
    [ValidateSet('Backoffice', 'Portal')]
    [string[]]$Roles = @('Backoffice', 'Portal'),
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
if (-not (Test-Path $SettingsFile)) { Fail "settings file not found: $SettingsFile (copy moet.settings.example.psd1 to moet.settings.psd1 and fill it in)." }
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
if (-not (Get-WebGlobalModule -Name AspNetCoreModuleV2 -ErrorAction SilentlyContinue)) {
    Fail 'install the .NET 10 Hosting Bundle first (it adds the ASP.NET Core Module to IIS). See docs/ON_PREM.md, "Windows Server and IIS".'
}
if (-not $dotnet) { Fail 'dotnet.exe not found: install the .NET 10 Hosting Bundle.' }
$runtimes = (& $dotnet --list-runtimes 2>$null) -join "`n"
if ($runtimes -notmatch 'Microsoft\.AspNetCore\.App 10\.') { Fail 'the ASP.NET Core 10 runtime is missing: install the .NET 10 Hosting Bundle.' }

foreach ($r in $Roles) {
    $folder = $r.ToLower()
    if (-not (Test-Path (Join-Path $PackageRoot "$folder\web.config"))) { Fail "the package has no '$folder' app under $PackageRoot (download the built package, not the source code)." }
    if (-not $S.ContainsKey($r)) { Fail "the settings file has no $r section." }
}

function Require($settings, [string]$section, [string[]]$keys) {
    foreach ($k in $keys) { if (-not (Value $settings $k)) { Fail "$section setting $k is empty." } }
}
if ($Roles -contains 'Backoffice') {
    Require $S.Backoffice.Settings 'Backoffice' @('DB_CONNECTION', 'PORTAL_API_KEY', 'PORTAL_API_SECRET', 'AGENT_KEY')
    if ($S.Backoffice.Settings.AGENT_KEY.Length -lt 16) { Fail 'Backoffice AGENT_KEY must be at least 16 characters.' }
}
if ($Roles -contains 'Portal') {
    Require $S.Portal.Settings 'Portal' @('RECRUITMENT_API_URL', 'CAREERS_PORTAL_API_KEY', 'CAREERS_PORTAL_API_SECRET', 'SESSION_SECRET')
    if ($S.Portal.Settings.SESSION_SECRET.Length -lt 32) { Fail 'Portal SESSION_SECRET must be at least 32 characters.' }
}
if ($Roles -contains 'Backoffice' -and $Roles -contains 'Portal') {
    if ($S.Backoffice.Settings.PORTAL_API_KEY -ne $S.Portal.Settings.CAREERS_PORTAL_API_KEY -or $S.Backoffice.Settings.PORTAL_API_SECRET -ne $S.Portal.Settings.CAREERS_PORTAL_API_SECRET) {
        Fail 'the portal''s CAREERS_PORTAL_API_KEY and CAREERS_PORTAL_API_SECRET differ from the backoffice''s PORTAL_API_KEY and PORTAL_API_SECRET.'
    }
}

# ---------------------------------------------------------------- helpers

function Write-WebConfig([string]$target, [string]$dll, [hashtable]$env) {
    # The package's own web.config, with the settings added as environment variables and dotnet's full path.
    [xml]$doc = Get-Content -Raw -Path $target
    $module = $doc.SelectSingleNode('//aspNetCore')
    if (-not $module) { Fail "$target has no aspNetCore section." }
    if ($dotnet) { $module.SetAttribute('processPath', $dotnet) }
    $module.SetAttribute('arguments', ".\$dll")
    $vars = $module.SelectSingleNode('environmentVariables')
    if ($vars) { [void]$module.RemoveChild($vars) }
    $vars = $doc.CreateElement('environmentVariables')
    foreach ($k in ($env.Keys | Sort-Object)) {
        $v = [string]$env[$k]
        if ($v -eq '') { continue }
        $e = $doc.CreateElement('environmentVariable')
        $e.SetAttribute('name', $k)
        $e.SetAttribute('value', $v)
        [void]$vars.AppendChild($e)
    }
    [void]$module.AppendChild($vars)
    $settings = New-Object Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.Encoding = New-Object Text.UTF8Encoding($false)
    $w = [Xml.XmlWriter]::Create($target, $settings)
    try { $doc.Save($w) } finally { $w.Close() }
}

function Copy-App([string]$from, [string]$to) {
    New-Item -ItemType Directory -Force -Path $to | Out-Null
    if (Test-Path (Join-Path $to 'web.config')) {
        # The ASP.NET Core Module stops the app and answers 503 while this file exists, so its files can be replaced.
        Set-Content -Path (Join-Path $to 'app_offline.htm') -Value '<!doctype html><meta charset="utf-8"><title>Updating</title><p>Updating, back in a minute.</p>' -Encoding UTF8
        Start-Sleep -Seconds 3
    }
    & $robocopy $from $to /MIR /NFL /NDL /NJH /NJS /NP /R:5 /W:2 /XF app_offline.htm /XD logs | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "copying $from to $to failed (robocopy exit code $LASTEXITCODE)." }
    $global:LASTEXITCODE = 0
    New-Item -ItemType Directory -Force -Path (Join-Path $to 'logs') | Out-Null
}

function Grant([string]$path, [string]$who, [string]$rights) {
    & $icacls $path /grant:r "${who}:(OI)(CI)$rights" /T /C /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { Fail "could not grant $who on $path." }
}

function Protect-Folder([string]$path, [string]$pool) {
    # Only administrators, SYSTEM and the site's own pool: web.config holds the app's passwords and keys.
    & $icacls $path /inheritance:r /grant:r 'BUILTIN\Administrators:(OI)(CI)F' 'NT AUTHORITY\SYSTEM:(OI)(CI)F' /C /Q | Out-Null
    Grant $path "IIS AppPool\$pool" 'RX'
    Grant (Join-Path $path 'logs') "IIS AppPool\$pool" 'M'
}

function Set-Pool([string]$name, [hashtable]$identity) {
    $p = "IIS:\AppPools\$name"
    if (-not (Test-Path $p)) { New-WebAppPool -Name $name | Out-Null }
    Set-ItemProperty $p -Name managedRuntimeVersion -Value ''
    Set-ItemProperty $p -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty $p -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty $p -Name processModel.loadUserProfile -Value $true
    Set-ItemProperty $p -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
    # One worker at a time: background jobs and database updates never run twice during a recycle.
    Set-ItemProperty $p -Name recycling.disallowOverlappingRotation -Value $true
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
    $local = [int](Value $cfg 'LocalPort' 0)
    $portalPort = [int](Value $cfg 'PortalPort' 0)
    $portalIp = Value $cfg 'PortalIpAddress' '*'
    $thumb = (Value $cfg 'CertificateThumbprint') -replace '\s', ''
    if ($https -gt 0 -and -not $thumb) { Fail "$name has an HTTPS port but no CertificateThumbprint." }
    if ($https -le 0 -and $http -le 0) { Fail "$name has neither an HTTPS nor an HTTP port." }
    $bindings = @()
    if ($https -gt 0) { $bindings += @{ protocol = 'https'; bindingInformation = "${ip}:${https}:$hostName"; sslFlags = $(if ($hostName) { 1 } else { 0 }) } }
    if ($http -gt 0) { $bindings += @{ protocol = 'http'; bindingInformation = "${ip}:${http}:$hostName" } }
    # A plain HTTP binding on this machine only: the portal on the same server reaches the API there.
    if ($local -gt 0) { $bindings += @{ protocol = 'http'; bindingInformation = "127.0.0.1:${local}:" } }
    # An HTTPS port for the portal on another server (DMZ): only the portal API answers on it, so the firewall rule can
    # name just this port. It uses the site's certificate.
    if ($portalPort -gt 0) {
        if (-not $thumb) { Fail "$name has a PortalPort but no CertificateThumbprint." }
        $bindings += @{ protocol = 'https'; bindingInformation = "${portalIp}:${portalPort}:"; sslFlags = 0 }
    }
    $site = "IIS:\Sites\$name"
    if (-not (Test-Path $site)) { New-Item $site -bindings $bindings[0] -physicalPath $path | Out-Null }
    Set-ItemProperty $site -Name bindings -Value $bindings
    Set-ItemProperty $site -Name physicalPath -Value $path
    Set-ItemProperty $site -Name applicationPool -Value $pool
    Set-ItemProperty $site -Name serverAutoStart -Value $true
    if ($https -gt 0 -or $portalPort -gt 0) {
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
    $f = $null
    try { $f = Get-WindowsFeature -Name Web-AppInit -ErrorAction Stop } catch { }
    if (($f -and $f.Installed) -or (Get-WebGlobalModule -Name ApplicationInitializationModule -ErrorAction SilentlyContinue)) {
        Set-ItemProperty $site -Name applicationDefaults.preloadEnabled -Value $true
    } else {
        Write-Warning "IIS Application Initialization (Web-AppInit) is not installed: $name starts on its first request instead of with IIS."
    }
    $staff = $(if ($http -gt 0) { "http://$(if ($ip -eq '*') { '127.0.0.1' } else { $ip }):$http" } else { '' })
    $localUrl = $(if ($local -gt 0) { "http://127.0.0.1:$local" } else { '' })
    return @{ Staff = $staff; Local = $localUrl }
}

$probes = @()
function Start-App([string]$site, [string]$pool, [string]$dir) {
    Remove-Item -Path (Join-Path $dir 'app_offline.htm') -ErrorAction SilentlyContinue
    if ((Get-WebAppPoolState -Name $pool).Value -ne 'Started') { Start-WebAppPool -Name $pool } else { Restart-WebAppPool -Name $pool }
    if ((Get-WebsiteState -Name $site).Value -ne 'Started') { Start-Website -Name $site }
}
function Probe([string]$name, [string]$url, [int[]]$ok, [int]$wait) {
    if ($url) { $script:probes += [pscustomobject]@{ Name = $name; Url = $url; Ok = $ok; Wait = $wait } }
}

New-Item -ItemType Directory -Force -Path $root | Out-Null

# ---------------------------------------------------------------- backoffice (staff app, API, assistant)

if ($Roles -contains 'Backoffice') {
    $c = $S.Backoffice; $dir = Join-Path $root 'backoffice'; $pool = Value $c 'AppPool' 'MoetRecruitmentBackoffice'
    $site = Value $c 'SiteName' 'MoET Recruitment Backoffice'
    $browsers = Join-Path $root 'ms-playwright'
    Say "Backoffice -> $dir"
    Copy-App (Join-Path $PackageRoot 'backoffice') $dir
    if (Test-Path (Join-Path $PackageRoot 'ms-playwright')) {
        Say "Chromium for PDF letters -> $browsers"
        & $robocopy (Join-Path $PackageRoot 'ms-playwright') $browsers /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { Fail "copying Chromium failed (robocopy exit code $LASTEXITCODE)." }
        $global:LASTEXITCODE = 0
    } else {
        Write-Warning 'The package has no ms-playwright folder: PDF letters need Chromium (see docs/ON_PREM.md, "PDF letters").'
    }
    $env = @{ ASPNETCORE_ENVIRONMENT = 'Production'; PLAYWRIGHT_BROWSERS_PATH = $browsers }
    # The local port and the portal port answer only the portal API.
    $portalOnly = @([int](Value $c 'LocalPort' 0), [int](Value $c 'PortalPort' 0)) | Where-Object { $_ -gt 0 }
    if ($portalOnly) { $env['PORTAL_ONLY_PORTS'] = ($portalOnly -join ',') }
    # Staff sign-in needs HTTPS unless the site has no HTTPS binding (a test without a certificate).
    if ([int](Value $c 'HttpsPort' 0) -le 0) { $env['COOKIE_SECURE'] = '0' }
    foreach ($k in $c.Settings.Keys) { $env[$k] = $c.Settings[$k] }
    Write-WebConfig (Join-Path $dir 'web.config') 'Moet.Recruitment.Api.dll' $env
    Set-Pool $pool (Value $c 'Identity' @{})
    Protect-Folder $dir $pool
    if (Test-Path $browsers) { Grant $browsers "IIS AppPool\$pool" 'RX' }
    $urls = Set-Site $site $dir $pool $c
    Start-App $site $pool $dir
    # The first start creates or updates the database, which can take a few minutes.
    $first = $(if ($urls.Local) { $urls.Local } else { $urls.Staff })
    Probe "$site (API)" $(if ($first) { "$first/healthz" } else { '' }) @(200) 300
    if ($urls.Staff) {
        Probe "$site (staff app)" "$($urls.Staff)/" @(200) 60
        Probe "$site (sign-in check)" "$($urls.Staff)/api/backoffice/me" @(401) 60
    }
    if ($urls.Local) { Probe "$site (local port: portal API only)" "$($urls.Local)/" @(404) 60 }
}

# ---------------------------------------------------------------- portal (candidate site)

if ($Roles -contains 'Portal') {
    $c = $S.Portal; $dir = Join-Path $root 'portal'; $pool = Value $c 'AppPool' 'MoetCareersPortal'
    $site = Value $c 'SiteName' 'MoET Careers Portal'
    Say "Careers portal -> $dir"
    Copy-App (Join-Path $PackageRoot 'portal') $dir
    $env = @{ ASPNETCORE_ENVIRONMENT = 'Production'; COOKIE_SECURE = $(if ([int](Value $c 'HttpsPort' 0) -gt 0) { '1' } else { '0' }) }
    foreach ($k in $c.Settings.Keys) { $env[$k] = $c.Settings[$k] }
    Write-WebConfig (Join-Path $dir 'web.config') 'Moet.Careers.Portal.dll' $env
    Set-Pool $pool (Value $c 'Identity' @{})
    Protect-Folder $dir $pool
    $urls = Set-Site $site $dir $pool $c
    Start-App $site $pool $dir
    $url = $(if ($urls.Staff) { $urls.Staff } else { $urls.Local })
    if ($url) {
        Probe "$site (pages)" "$url/healthz" @(200) 60
        Probe "$site (reaches the API)" "$url/api/config" @(200) 120
    }
}

# ---------------------------------------------------------------- health

if (-not $SkipHealthCheck -and $probes.Count -gt 0) {
    Say 'Checking that each site answers'
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
            $ok = $p.Ok -contains $status
            if (-not $ok) { Start-Sleep -Seconds 3 }
        } while (-not $ok -and (Get-Date) -lt $deadline)
        if ($ok) { Write-Host "  ok   $($p.Name)  $($p.Url)" -ForegroundColor Green }
        else { $bad++; Write-Host "  FAIL $($p.Name)  $($p.Url)  (status $status)" -ForegroundColor Red }
    }
    if ($bad -gt 0) { Fail "$bad check(s) failed. Look in the Windows Event Log (Application) and in each site's logs folder." }
}
foreach ($r in $Roles) {
    $cfg = $S[$r]
    if ([int](Value $cfg 'HttpPort' 0) -le 0 -and [int](Value $cfg 'LocalPort' 0) -le 0) {
        Write-Host "  $r answers on HTTPS only: open https://$(Value $cfg 'HostName' 'this-server')/ to check it."
    }
}
Say 'Done.'
