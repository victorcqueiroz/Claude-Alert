#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishFolder = Join-Path $projectRoot 'artifacts\publish\win-x64'

Push-Location -LiteralPath $projectRoot
try {
    & $Dotnet publish .\ClaudeAlerta.csproj -c $Configuration -r win-x64 --self-contained true -o $publishFolder
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish terminou com código $LASTEXITCODE." }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $publishFolder 'LEIA-ME.md')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'VALIDACAO.md') -Destination $publishFolder
    Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $publishFolder
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs') -Destination $publishFolder -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'design') -Destination $publishFolder -Recurse -Force
    Write-Host "Publicação concluída: $publishFolder"
}
finally {
    Pop-Location
}
