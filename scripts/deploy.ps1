[CmdletBinding()]
param(
    [ValidateSet('dev','prod')]
    [string]$Environment = 'dev',

    [string]$Location = 'eastus'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$paramFile = Join-Path $root "infra\parameters\$Environment.bicepparam"
$mainFile  = Join-Path $root 'infra\main.bicep'

Write-Host "Deploying $Environment from $mainFile" -ForegroundColor Cyan
az deployment sub create `
    --name "ghcp-logicapp-$Environment-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))" `
    --location $Location `
    --template-file $mainFile `
    --parameters $paramFile
