[CmdletBinding()]
param(
    [ValidateSet('dev','prod')]
    [string]$Environment = 'dev',

    [string]$Location = 'eastus',

    [switch]$SkipContent
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$paramFile = Join-Path $root "infra-standard\parameters\$Environment.bicepparam"
$mainFile  = Join-Path $root 'infra-standard\main.bicep'
$projectDir = Join-Path $root 'standard'

Write-Host "Deploying Logic Apps Standard infra ($Environment) from $mainFile" -ForegroundColor Cyan
$deployJson = az deployment sub create `
    --name "ghcp-logicapp-std-$Environment-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))" `
    --location $Location `
    --template-file $mainFile `
    --parameters $paramFile `
    --query 'properties.outputs' `
    -o json
if ($LASTEXITCODE -ne 0) { throw "Bicep deployment failed." }

$outputs = $deployJson | ConvertFrom-Json
$rgName   = $outputs.resourceGroupName.value
$siteName = $outputs.siteName.value

if ($SkipContent) {
    Write-Host "Infra deployed. Skipping content publish." -ForegroundColor Yellow
    return
}

if (-not (Get-Command func -ErrorAction SilentlyContinue)) {
    Write-Warning "Azure Functions Core Tools (func) not found on PATH."
    Write-Warning "Install from https://learn.microsoft.com/azure/azure-functions/functions-run-local then run:"
    Write-Warning "    cd $projectDir; func azure functionapp publish $siteName"
    return
}

Push-Location $projectDir
try {
    Write-Host "Publishing $projectDir to $siteName..." -ForegroundColor Cyan
    func azure functionapp publish $siteName
}
finally {
    Pop-Location
}

Write-Host "Done. Site: $siteName in $rgName" -ForegroundColor Green
