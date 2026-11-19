<#
.SYNOPSIS
    Builds the IIS package of the MoET HRMS (Ministry of Economy & Tourism).

.DESCRIPTION
    Run on a Windows build machine with the .NET 10 SDK, Node.js 22 and internet access (packages and Chromium are
    downloaded here, so the servers need none). The IIS Package workflow in GitHub Actions runs this script and
    attaches the zip to the run. The result:

        moet-hrms-iis\
          hrms\     site 1: the API with the assistant, and the staff app in wwwroot (win-x64, framework-dependent)
          portal\         site 2: the portal server, and the candidate app in wwwroot
          ms-playwright\  Chromium (headless shell) for PDF letters
          iis\            Install-Moet.ps1, settings example
          VERSION.txt

.PARAMETER Out
    Output folder. Default: out\moet-hrms-iis under the repository root, and a zip next to it.
#>
[CmdletBinding()]
param(
    [string]$Out = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'out\moet-hrms-iis'),
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
function Say([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Run([string]$exe, [string[]]$arguments, [string]$where) {
    Push-Location $where
    try {
        & $exe @arguments
        if ($LASTEXITCODE -ne 0) { throw "$exe $($arguments -join ' ') failed with exit code $LASTEXITCODE" }
    } finally { Pop-Location }
}

if (Test-Path $Out) { Remove-Item -Recurse -Force $Out }
New-Item -ItemType Directory -Force -Path $Out | Out-Null

Say 'HRMS site: the API (with the assistant) and the staff app'
$bo = Join-Path $Out 'hrms'
Run 'dotnet' @('publish', 'src\Moet.Hrms.Api\Moet.Hrms.Api.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-o', $bo) (Join-Path $repo 'backend')
$web = Join-Path $repo 'hrms\web'
Run 'npm' @('install', '--no-audit', '--no-fund') $web
Run 'npm' @('run', 'build') $web
Copy-Item -Recurse (Join-Path $web 'dist') (Join-Path $bo 'wwwroot')

Say 'Careers portal site: the portal server and the candidate app'
$po = Join-Path $Out 'portal'
Run 'dotnet' @('publish', 'Moet.Careers.Portal\Moet.Careers.Portal.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-o', $po) (Join-Path $repo 'portal\host')
$web = Join-Path $repo 'portal\web'
Run 'npm' @('install', '--no-audit', '--no-fund') $web
Run 'npm' @('run', 'build') $web
Copy-Item -Recurse (Join-Path $web 'dist') (Join-Path $po 'wwwroot')

Say 'Chromium for PDF letters'
# Playwright's own driver, from the published API, so the browser matches the library version.
$env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $Out 'ms-playwright'
$driver = Join-Path $Out 'hrms\.playwright'
Run (Join-Path $driver 'node\win32_x64\node.exe') @((Join-Path $driver 'package\cli.js'), 'install', '--only-shell', 'chromium') $Out
Remove-Item Env:\PLAYWRIGHT_BROWSERS_PATH

Say 'Install scripts'
New-Item -ItemType Directory -Force -Path (Join-Path $Out 'iis') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Install-Moet.ps1'), (Join-Path $PSScriptRoot 'moet.settings.example.psd1'), (Join-Path $PSScriptRoot 'README.md') (Join-Path $Out 'iis')

$commit = ''
try { $commit = (& git -C $repo rev-parse --short HEAD).Trim() } catch { }
if ($env:GITHUB_SHA) { $commit = $env:GITHUB_SHA.Substring(0, 7) }
Set-Content -Path (Join-Path $Out 'VERSION.txt') -Value "MoET HRMS`r`nCommit: $commit`r`nBuilt: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm')) UTC"

foreach ($f in 'hrms\Moet.Hrms.Api.dll', 'hrms\Moet.Hrms.Agent.dll', 'hrms\web.config', 'hrms\wwwroot\index.html',
    'portal\Moet.Careers.Portal.dll', 'portal\web.config', 'portal\wwwroot\index.html') {
    if (-not (Test-Path (Join-Path $Out $f))) { throw "package is missing $f" }
}
if (-not $NoZip) {
    $zip = "$Out.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Say "Zip -> $zip"
    Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $zip
}
Say 'Done.'
