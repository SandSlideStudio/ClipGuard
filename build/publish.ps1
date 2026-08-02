<#
.SYNOPSIS
    Builds a distributable ClipGuard.

.DESCRIPTION
    Produces a self-contained win-x64 build so the machine needs no .NET
    install - for a tray app people are meant to install once and forget,
    a runtime prerequisite is a support burden.

    Signing is a separate step and needs a certificate you supply. See the
    "Packaging and signing" section of README.md.

.EXAMPLE
    ./build/publish.ps1
    ./build/publish.ps1 -Configuration Debug -OutDir ./out
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutDir = "$PSScriptRoot/../dist",
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '../src/ClipGuard/ClipGuard.csproj' | Resolve-Path
$output = Join-Path $OutDir $Runtime

Write-Host "Publishing ClipGuard ($Configuration, $Runtime)" -ForegroundColor Cyan

# Tests gate the build: the detection logic is the product, and shipping it
# unverified is how the shell-quoting bug reached a user last time.
$tests = Join-Path $PSScriptRoot '../tests/ClipGuard.Tests/ClipGuard.Tests.csproj' | Resolve-Path
Write-Host 'Running tests...' -ForegroundColor Cyan
dotnet test $tests --configuration $Configuration --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Tests failed - not publishing.' }

if (Test-Path $output) { Remove-Item $output -Recurse -Force }

dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    --nologo

if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $output 'ClipGuard.exe'
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)

# The licence for the embedded Archivo font has to travel with the binary, so
# the distributable is a zip rather than a bare exe. Shipping the exe on its own
# would leave THIRD-PARTY-LICENSES behind and breach the OFL.
Copy-Item (Join-Path $PSScriptRoot '../LICENSE.md') $output -Force
Copy-Item (Join-Path $PSScriptRoot '../THIRD-PARTY-NOTICES.md') $output -Force

$csproj = Get-Content (Join-Path $PSScriptRoot '../src/ClipGuard/ClipGuard.csproj') -Raw
$version = [regex]::Match($csproj, '<Version>([^<]+)</Version>').Groups[1].Value

$zip = Join-Path $OutDir "ClipGuard-$version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$hash  $(Split-Path $zip -Leaf)" | Out-File (Join-Path $OutDir 'SHA256SUMS.txt') -Encoding ascii

Write-Host ''
Write-Host "Published: $exe ($size MB)" -ForegroundColor Green
Write-Host "Package  : $zip" -ForegroundColor Green
Write-Host "SHA-256  : $hash"
Write-Host 'Unsigned - see README.md before distributing.' -ForegroundColor Yellow
