[CmdletBinding()]
param(
    [ValidateSet('dev','prod')]
    [string]$Environment = 'dev'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Write-Host "Resetting working tree to baseline..." -ForegroundColor Yellow
Push-Location $root
try {
    git restore .
    git clean -fd scenarios docs 2>$null | Out-Null
}
finally {
    Pop-Location
}

Write-Host "Re-deploying baseline ($Environment)..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'deploy.ps1') -Environment $Environment
