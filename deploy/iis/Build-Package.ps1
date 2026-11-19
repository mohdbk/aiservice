<#
.SYNOPSIS
    Builds the IIS package of the MoET recruitment system (Ministry of Economy & Tourism).

.DESCRIPTION
    Run on a Windows build machine with the .NET 10 SDK, Node.js 22 and internet access (packages and Chromium are
    downloaded here, so the servers need none). The IIS Package workflow in GitHub Actions runs this script and
    attaches the zip to the run. The result:

        moet-recruitment-iis\
          api\            the API (framework-dependent, win-x64)
          agent\          the assistant
          backoffice\     the built staff app
          portal\         the portal's Node server and built candidate app
          ms-playwright\  Chromium (headless shell) for PDF letters
          iis\            Install-Moet.ps1, templates, settings example
          VERSION.txt

.PARAMETER Out
    Output folder. Default: out\moet-recruitment-iis under the repository root, and a zip next to it.
#>
[CmdletBinding()]
param(
    [string]$Out = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'out\moet-recruitment-iis'),
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

Say 'API'
Run 'dotnet' @('publish', 'src\Moet.Recruitment.Api\Moet.Recruitment.Api.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-o', (Join-Path $Out 'api')) (Join-Path $repo 'backend')
Say 'Agent'
Run 'dotnet' @('publish', 'src\Moet.Recruitment.Agent\Moet.Recruitment.Agent.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'false', '-o', (Join-Path $Out 'agent')) (Join-Path $repo 'agent')

Say 'Backoffice'
$bo = Join-Path $repo 'backoffice\web'
Run 'npm' @('install', '--no-audit', '--no-fund') $bo
Run 'npm' @('run', 'build') $bo
Copy-Item -Recurse (Join-Path $bo 'dist') (Join-Path $Out 'backoffice')

Say 'Careers portal'
$pw = Join-Path $repo 'portal\web'
Run 'npm' @('install', '--no-audit', '--no-fund') $pw
Run 'npm' @('run', 'build') $pw
New-Item -ItemType Directory -Force -Path (Join-Path $Out 'portal\server'), (Join-Path $Out 'portal\web') | Out-Null
Copy-Item -Recurse (Join-Path $repo 'portal\server\src') (Join-Path $Out 'portal\server\src')
Copy-Item (Join-Path $repo 'portal\server\package.json') (Join-Path $Out 'portal\server\package.json')
Copy-Item -Recurse (Join-Path $pw 'dist') (Join-Path $Out 'portal\web\dist')

Say 'Chromium for PDF letters'
# Playwright's own driver, from the published API, so the browser matches the library version.
$env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $Out 'ms-playwright'
$driver = Join-Path $Out 'api\.playwright'
Run (Join-Path $driver 'node\win32_x64\node.exe') @((Join-Path $driver 'package\cli.js'), 'install', '--only-shell', 'chromium') $Out
Remove-Item Env:\PLAYWRIGHT_BROWSERS_PATH

Say 'Install scripts'
New-Item -ItemType Directory -Force -Path (Join-Path $Out 'iis') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'Install-Moet.ps1'), (Join-Path $PSScriptRoot 'moet.settings.example.psd1'), (Join-Path $PSScriptRoot 'README.md') (Join-Path $Out 'iis')
Copy-Item -Recurse (Join-Path $PSScriptRoot 'templates') (Join-Path $Out 'iis\templates')

$commit = ''
try { $commit = (& git -C $repo rev-parse --short HEAD).Trim() } catch { }
if ($env:GITHUB_SHA) { $commit = $env:GITHUB_SHA.Substring(0, 7) }
Set-Content -Path (Join-Path $Out 'VERSION.txt') -Value "MoET recruitment system`r`nCommit: $commit`r`nBuilt: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm')) UTC"

foreach ($f in 'api\Moet.Recruitment.Api.dll', 'agent\Moet.Recruitment.Agent.dll', 'backoffice\index.html', 'portal\web\dist\index.html', 'portal\server\src\server.js') {
    if (-not (Test-Path (Join-Path $Out $f))) { throw "package is missing $f" }
}
if (-not $NoZip) {
    $zip = "$Out.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Say "Zip -> $zip"
    Compress-Archive -Path (Join-Path $Out '*') -DestinationPath $zip
}
Say 'Done.'
