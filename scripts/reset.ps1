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

$rgName = "rg-ghcp-logicapp-$Environment"
Write-Host "Deleting resource group $rgName..." -ForegroundColor Cyan
az group delete --name $rgName --yes --no-wait
Write-Host "Resource group $rgName deletion initiated." -ForegroundColor Green
