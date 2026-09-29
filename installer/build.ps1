<#
.SYNOPSIS
    Build the PeakMasterMenu one-click installer.

.DESCRIPTION
    Builds the mod itself (Release) first, then builds the installer, embedding the
    freshly built mod dll into the installer exe. The result is a single exe that
    other people can double-click to install everything.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Debug
    .\build.ps1 -SkipModBuild      # when iterating on the installer only
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $SkipModBuild
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'PeakMasterMenuInstaller\PeakMasterMenuInstaller.csproj'

# 'dotnet' is not always on PATH; fall back to the default install location.
$dotnetPath = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnetPath) {
    $candidate = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path $candidate) {
        $dotnetPath = $candidate
    } else {
        Write-Error 'Cannot find dotnet. Install the .NET SDK first.'
        exit 1
    }
}

$arguments = @('build', $project, '-c', $Configuration, '-v', 'm')
if ($SkipModBuild) {
    $arguments += '-p:SkipModBuild=true'
}

& $dotnetPath @arguments
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

$outputDirectory = Join-Path $PSScriptRoot ('PeakMasterMenuInstaller\bin\' + $Configuration + '\net48')

Write-Host ''
Write-Host 'Installer built:' -ForegroundColor Green
Get-ChildItem -Path $outputDirectory -File |
    Where-Object { $_.Name -like 'PeakMasterMenuInstaller.*' } |
    ForEach-Object {
        Write-Host ('  ' + $_.FullName + '  (' + [Math]::Round($_.Length / 1KB, 1) + ' KB)')
    }

Write-Host ''
Write-Host 'Ship that exe (plus the .config file if one was produced) - users just double-click it.'
