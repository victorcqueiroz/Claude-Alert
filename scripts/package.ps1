#requires -Version 7.0
[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'ClaudeAlerta.csproj') -Raw
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'A versão deve seguir MAJOR.MINOR.PATCH.' }
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Dotnet $Dotnet }
$publishFolder = Join-Path $projectRoot 'artifacts\publish\win-x64'
$exe = Join-Path $publishFolder 'ClaudeAlerta.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Compile a publicação antes de empacotar.' }
$binaryVersion = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion.Split('+')[0]
if ($binaryVersion -ne $version) { throw "A publicação $binaryVersion não corresponde ao projeto $version." }
foreach ($file in @('README.md', 'VALIDACAO.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination (Join-Path $publishFolder $file) -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $publishFolder 'LEIA-ME.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $publishFolder -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'design') -Destination $publishFolder -Recurse -Force
$outputFolder = Join-Path $projectRoot "artifacts\releases\$version"
New-Item -ItemType Directory -Path $outputFolder -Force | Out-Null
$zip = Join-Path $outputFolder "ClaudeAlert-$version-win-x64.zip"
Compress-Archive -Path (Join-Path $publishFolder '*') -DestinationPath $zip -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText((Join-Path $outputFolder 'SHA256SUMS.txt'), "$hash  $([System.IO.Path]::GetFileName($zip))`n", [System.Text.Encoding]::ASCII)
& (Join-Path $PSScriptRoot 'clean-releases.ps1') -Version $version
Write-Host "Pacote: $zip"
Write-Host "SHA-256: $hash"
