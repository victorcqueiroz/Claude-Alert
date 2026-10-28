#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$Version,
    [string]$LegacyRoot
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'ClaudeAlerta.csproj') -Raw
    $Version = [string]$project.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Informe a versão MAJOR.MINOR.PATCH a preservar.' }
$keepVersion = [version]$Version
$packagesRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts\releases'))
$keepFolder = Join-Path $packagesRoot $Version
$keepZip = Join-Path $keepFolder "ClaudeAlert-$Version-win-x64.zip"
$keepSum = Join-Path $keepFolder 'SHA256SUMS.txt'

function Assert-ContainedDirectory([string]$Path, [string]$Root) {
    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd([char[]]'\/')
    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path.TrimEnd([char[]]'\/')
    if (-not $resolvedPath.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Pasta fora do limite de limpeza: $resolvedPath"
    }
    $current = Get-Item -LiteralPath $resolvedPath -Force
    if (-not $current.PSIsContainer) { throw "O alvo não é uma pasta: $resolvedPath" }
    while ($current -and $current.FullName -ne $resolvedRoot) {
        if ($current.LinkType) { throw "Link ou junção não permitido na limpeza: $($current.FullName)" }
        $current = $current.Parent
    }
    $ancestor = Get-Item -LiteralPath $resolvedRoot -Force
    while ($ancestor) {
        if ($ancestor.LinkType) { throw "Raiz ou ancestral é link/junção: $($ancestor.FullName)" }
        $ancestor = $ancestor.Parent
    }
    foreach ($entry in Get-ChildItem -LiteralPath $resolvedPath -Recurse -Force) {
        if ($entry.LinkType) { throw "Link ou junção encontrado no alvo: $($entry.FullName)" }
    }
    return $resolvedPath
}

# Never remove an older package until the replacement and its checksum are available.
if (-not (Test-Path -LiteralPath $keepZip -PathType Leaf) -or -not (Test-Path -LiteralPath $keepSum -PathType Leaf)) {
    throw "O pacote $Version e seu SHA-256 precisam existir antes da limpeza."
}
[void](Assert-ContainedDirectory $keepFolder $packagesRoot)
$expected = (Get-FileHash -LiteralPath $keepZip -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($keepZip)
if ([IO.File]::ReadAllText($keepSum).Trim() -cne $expected) { throw 'O SHA-256 da release preservada não confere. Nenhuma pasta foi removida.' }

$targets = [Collections.Generic.List[object]]::new()
foreach ($folder in Get-ChildItem -LiteralPath $packagesRoot -Directory -Force) {
    if ($folder.Name -notmatch '^\d+\.\d+\.\d+$' -or [version]$folder.Name -ge $keepVersion) { continue }
    if (-not (Test-Path -LiteralPath (Join-Path $folder.FullName "ClaudeAlert-$($folder.Name)-win-x64.zip") -PathType Leaf)) { continue }
    $targets.Add([pscustomobject]@{Path=$folder.FullName;Root=$packagesRoot})
}

# Legacy locations are opt-in; only named Claude Alert distributions with older executables qualify.
if ($LegacyRoot) {
    $legacyBase = (Resolve-Path -LiteralPath $LegacyRoot).Path
    foreach ($leaf in @('release', 'artifacts')) {
        $root = Join-Path $legacyBase $leaf
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        foreach ($folder in Get-ChildItem -LiteralPath $root -Directory -Force) {
            if ($folder.Name -notmatch '^ClaudeAlerta-(?:backup-)?(?<release>\d+\.\d+\.\d+)(?:[-.].*)?$') { continue }
            if ([version]$Matches.release -ge $keepVersion) { continue }
            $candidate = Assert-ContainedDirectory $folder.FullName $root
            $exe = Join-Path $candidate 'ClaudeAlerta.exe'
            if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { continue }
            $info = (Get-Item -LiteralPath $exe).VersionInfo
            $binaryVersion = $null
            if ($info.ProductName -ne 'ClaudeAlerta' -or -not [version]::TryParse(($info.ProductVersion -split '\+')[0], [ref]$binaryVersion) -or $binaryVersion -ge $keepVersion) { continue }
            $targets.Add([pscustomobject]@{Path=$candidate;Root=$root})
        }
    }
}

# Validate every absolute target before the first recursive removal.
$validated = foreach ($target in $targets) {
    $path = Assert-ContainedDirectory $target.Path $target.Root
    $bytes = (Get-ChildItem -LiteralPath $path -File -Recurse -Force | Measure-Object Length -Sum).Sum
    [pscustomobject]@{Path=$path;Root=$target.Root;Bytes=[long]$bytes}
}
foreach ($target in $validated) {
    [void](Assert-ContainedDirectory $target.Path $target.Root)
    if ($PSCmdlet.ShouldProcess($target.Path, "Remover release local antiga; preservar $Version")) {
        Remove-Item -LiteralPath $target.Path -Recurse -Force
        [pscustomobject]@{RemovedPath=$target.Path;Bytes=$target.Bytes}
    }
}
