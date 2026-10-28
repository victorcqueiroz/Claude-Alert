#requires -Version 7.0
[CmdletBinding()]
param(
    [string]$Dotnet = 'dotnet',
    [switch]$SkipBuild,
    [switch]$Ui
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'artifacts\publish\win-x64\ClaudeAlerta.exe'
$testRoot = Join-Path $projectRoot ('artifacts\tests\' + [guid]::NewGuid().ToString('N'))
$previousData = [Environment]::GetEnvironmentVariable('CLAUDE_ALERT_DATA', 'Process')
$previousSettings = [Environment]::GetEnvironmentVariable('CLAUDE_ALERT_SETTINGS', 'Process')

try {
    if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Dotnet $Dotnet }
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Compile o projeto antes de usar -SkipBuild.' }
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    $testCases = @(@{ Name = 'core'; Argument = '--self-test' })
    if ($Ui) { $testCases += @{ Name = 'ui'; Argument = '--ui-check' } }
    foreach ($testCase in $testCases) {
        $caseRoot = Join-Path $testRoot $testCase.Name
        New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
        $env:CLAUDE_ALERT_DATA = Join-Path $caseRoot 'data'
        $env:CLAUDE_ALERT_SETTINGS = Join-Path $caseRoot 'settings.json'
        $resultFile = Join-Path $caseRoot 'results.json'
        $quotedResult = '"' + $resultFile + '"'
        $testProcess = Start-Process -FilePath $executable -ArgumentList $testCase.Argument, $quotedResult -Wait -PassThru -WindowStyle Hidden
        if ($testProcess.ExitCode -ne 0) { throw "Teste $($testCase.Name) falhou. Consulte $resultFile." }
        $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
        if (-not $result.success) { throw "Teste $($testCase.Name) não confirmou sucesso. Consulte $resultFile." }
        Write-Host "$($testCase.Name): $($result.checks.Count) verificações aprovadas."
    }
    Write-Host "Resultados: $testRoot"
}
finally {
    [Environment]::SetEnvironmentVariable('CLAUDE_ALERT_DATA', $previousData, 'Process')
    [Environment]::SetEnvironmentVariable('CLAUDE_ALERT_SETTINGS', $previousSettings, 'Process')
}
