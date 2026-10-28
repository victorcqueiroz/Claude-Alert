#requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testBase = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$fixture = Join-Path $testBase ('cleanup-test-' + [guid]::NewGuid().ToString('N'))
$checks = [Collections.Generic.List[string]]::new()
function Check([bool]$Condition, [string]$Name) {
    if (-not $Condition) { throw $Name }
    $checks.Add($Name)
}
function Write-Fixture([string]$Path, [string]$Text) {
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))
    [IO.File]::WriteAllText($Path, $Text)
}
try {
    $sample = Join-Path $projectRoot 'artifacts\publish\win-x64\ClaudeAlerta.exe'
    $sampleVersion = [version]((Get-Item -LiteralPath $sample).VersionInfo.ProductVersion -split '\+')[0]
    $keep = '{0}.{1}.0' -f $sampleVersion.Major, ($sampleVersion.Minor + 1)
    $newer = '{0}.{1}.0' -f $sampleVersion.Major, ($sampleVersion.Minor + 2)
    $repo = Join-Path $fixture 'repository'
    $cleanup = Join-Path $repo 'scripts\clean-releases.ps1'
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $cleanup))
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'clean-releases.ps1') -Destination $cleanup
    Write-Fixture (Join-Path $repo 'ClaudeAlerta.csproj') "<Project><PropertyGroup><Version>$keep</Version></PropertyGroup></Project>"
    $packages = Join-Path $repo 'artifacts\releases'
    $zipName = "ClaudeAlert-$keep-win-x64.zip"
    $zip = Join-Path $packages "$keep\$zipName"
    $sum = Join-Path $packages "$keep\SHA256SUMS.txt"
    Write-Fixture $zip 'isolated-package-fixture'
    Write-Fixture $sum 'invalid-checksum'
    $oldPackage = Join-Path $packages '1.0.0'
    Write-Fixture (Join-Path $oldPackage 'ClaudeAlert-1.0.0-win-x64.zip') 'old-fixture'
    Write-Fixture (Join-Path $packages "$newer\ClaudeAlert-$newer-win-x64.zip") 'newer-fixture'
    Write-Fixture (Join-Path $packages '0.9.0\notes.txt') 'unrecognized content'
    $oldInstall = Join-Path $fixture ('release\ClaudeAlerta-' + $sampleVersion + '-backup-test')
    [void][IO.Directory]::CreateDirectory($oldInstall)
    Copy-Item -LiteralPath $sample -Destination (Join-Path $oldInstall 'ClaudeAlerta.exe')
    Write-Fixture (Join-Path $fixture 'release\ClaudeAlerta\keep.txt') 'current installation'
    Write-Fixture (Join-Path $fixture 'release\CasaCameras-1.0.0\keep.txt') 'other project'
    Write-Fixture (Join-Path $fixture 'artifacts\ClaudeAlerta-1.0.0-foreign\ClaudeAlerta.exe') 'not a Claude Alert executable'

    $rejected = $false
    try { & $cleanup -LegacyRoot $fixture | Out-Null } catch { $rejected = $true }
    Check $rejected 'Checksum inválido impede a limpeza'
    Check ((Test-Path -LiteralPath $oldPackage) -and (Test-Path -LiteralPath $oldInstall)) 'Falha de integridade preserva todas as versões anteriores'

    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Fixture $sum "$hash  $zipName`n"
    & $cleanup -LegacyRoot $fixture -WhatIf | Out-Null
    Check ((Test-Path -LiteralPath $oldPackage) -and (Test-Path -LiteralPath $oldInstall)) 'WhatIf não remove pastas'

    $removed = @(& $cleanup -LegacyRoot $fixture)
    Check ($removed.Count -eq 2) 'Remove somente o pacote antigo e a instalação antiga identificada'
    Check ((-not (Test-Path -LiteralPath $oldPackage)) -and (-not (Test-Path -LiteralPath $oldInstall))) 'Versões antigas identificadas foram eliminadas'
    Check ((Test-Path -LiteralPath $zip) -and (Test-Path -LiteralPath $sum)) 'Mantém o pacote atual e seu checksum'
    Check (Test-Path -LiteralPath (Join-Path $packages $newer)) 'Preserva versão mais nova que a solicitada'
    Check (Test-Path -LiteralPath (Join-Path $packages '0.9.0\notes.txt')) 'Não remove pasta de versão sem pacote reconhecido'
    Check (Test-Path -LiteralPath (Join-Path $fixture 'release\ClaudeAlerta\keep.txt')) 'Preserva a instalação sem sufixo'
    Check (Test-Path -LiteralPath (Join-Path $fixture 'release\CasaCameras-1.0.0\keep.txt')) 'Preserva outro projeto'
    Check (Test-Path -LiteralPath (Join-Path $fixture 'artifacts\ClaudeAlerta-1.0.0-foreign\ClaudeAlerta.exe')) 'Nome de pasta sozinho não autoriza remover um executável desconhecido'
    Check (@(& $cleanup -LegacyRoot $fixture).Count -eq 0) 'Limpeza repetida é idempotente'

    $packageRepo = Join-Path $fixture 'packaging'
    [void][IO.Directory]::CreateDirectory((Join-Path $packageRepo 'scripts'))
    foreach ($scriptName in @('package.ps1','clean-releases.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $scriptName) -Destination (Join-Path $packageRepo "scripts\$scriptName")
    }
    $actual = $sampleVersion.ToString()
    Write-Fixture (Join-Path $packageRepo 'ClaudeAlerta.csproj') "<Project><PropertyGroup><Version>$actual</Version></PropertyGroup></Project>"
    foreach ($name in @('README.md','VALIDACAO.md','CHANGELOG.md','docs\example.txt','design\example.txt')) { Write-Fixture (Join-Path $packageRepo $name) 'isolated packaging fixture' }
    $publish = Join-Path $packageRepo 'artifacts\publish\win-x64'
    [void][IO.Directory]::CreateDirectory($publish)
    Copy-Item -LiteralPath $sample -Destination (Join-Path $publish 'ClaudeAlerta.exe')
    $old = Join-Path $packageRepo 'artifacts\releases\0.1.0'
    Write-Fixture (Join-Path $old 'ClaudeAlert-0.1.0-win-x64.zip') 'old package'
    & (Join-Path $packageRepo 'scripts\package.ps1') -SkipBuild | Out-Null
    $newZip = Join-Path $packageRepo "artifacts\releases\$actual\ClaudeAlert-$actual-win-x64.zip"
    Check ((Test-Path -LiteralPath $newZip) -and (-not (Test-Path -LiteralPath $old))) 'Empacotamento aplica a retenção automaticamente'
    $newSum = Join-Path (Split-Path -Parent $newZip) 'SHA256SUMS.txt'
    $expected = (Get-FileHash -LiteralPath $newZip).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($newZip)
    Check ([IO.File]::ReadAllText($newSum).Trim() -ceq $expected) 'Pacote gerado mantém SHA-256 válido'
    [IO.File]::WriteAllText((Join-Path $testBase 'cleanup-test-results.json'), (@{success=$true;checks=$checks} | ConvertTo-Json -Depth 5))
    Write-Host "$($checks.Count) verificações de retenção aprovadas."
}
finally {
    $absolute = [IO.Path]::GetFullPath($fixture)
    if (-not $absolute.StartsWith($testBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture fora de artifacts.' }
    if (Test-Path -LiteralPath $absolute) { Remove-Item -LiteralPath $absolute -Recurse -Force }
}
