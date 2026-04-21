<#
.SYNOPSIS
    POSTs a sample approval request to the Logic App's HTTP trigger.

.DESCRIPTION
    If -TriggerUrl is not provided, the script fetches it from Azure using
    the az CLI for the deployed workflow in the given environment. This
    avoids the easy-to-hit pitfall of pasting an unquoted URL on the
    PowerShell command line (where '&' is a command separator).

.EXAMPLE
    # Easiest — no copy/paste:
    ./scripts/invoke.ps1 -Environment dev -Amount 2500

.EXAMPLE
    # Explicit URL (must be wrapped in SINGLE QUOTES):
    ./scripts/invoke.ps1 -TriggerUrl 'https://prod-73.eastus.logic.azure.com/...&sig=...'
#>
[CmdletBinding()]
param(
    [string]$TriggerUrl,

    [ValidateSet('dev','prod')]
    [string]$Environment = 'dev',

    [int]$Amount = 2500,
    [string]$RequestId = "REQ-$((Get-Random -Maximum 9999))",
    [string]$Requester = 'alice@contoso.com',
    [string]$Description = 'Demo approval request',

    [string]$TriggerName = 'When_an_approval_request_is_received'
)

$ErrorActionPreference = 'Stop'

function Get-TriggerUrl {
    param([string]$Env, [string]$Trigger)

    $rg = "rg-ghcp-logicapp-$Env"
    $workflow = "la-approval-$Env"

    Write-Host "Fetching trigger URL for $workflow in $rg..." -ForegroundColor DarkGray
    $json = az rest --method post `
        --uri "https://management.azure.com/subscriptions/$(az account show --query id -o tsv)/resourceGroups/$rg/providers/Microsoft.Logic/workflows/$workflow/triggers/$Trigger/listCallbackUrl?api-version=2016-06-01" 2>$null
    if (-not $json) {
        throw "Could not retrieve trigger URL. Is the workflow deployed? (rg=$rg, workflow=$workflow)"
    }
    return ($json | ConvertFrom-Json).value
}

if (-not $TriggerUrl) {
    $TriggerUrl = Get-TriggerUrl -Env $Environment -Trigger $TriggerName
}

if ($TriggerUrl -notmatch '(?i)[?&]sig=') {
    Write-Warning "TriggerUrl is missing the 'sig=' query parameter."
    Write-Warning "If you pasted it in, wrap it in SINGLE QUOTES — PowerShell treats '&' as a command separator and silently truncates the URL."
    Write-Warning "Received: $TriggerUrl"
}

$body = @{
    requestId   = $RequestId
    requester   = $Requester
    amount      = $Amount
    description = $Description
} | ConvertTo-Json

Write-Host "POST $TriggerUrl" -ForegroundColor Cyan
Write-Host $body

try {
    $response = Invoke-WebRequest `
        -Method Post `
        -Uri $TriggerUrl `
        -ContentType 'application/json' `
        -Body $body `
        -UseBasicParsing
    Write-Host ""
    Write-Host "HTTP $([int]$response.StatusCode) $($response.StatusDescription)" -ForegroundColor Green
    if ($response.Content) { Write-Host $response.Content }
}
catch {
    $resp = $_.Exception.Response
    Write-Host ""
    if ($resp) {
        $status = [int]$resp.StatusCode
        Write-Host "HTTP $status $($resp.StatusCode)" -ForegroundColor Red
        try {
            $stream = $resp.GetResponseStream()
            $reader = [System.IO.StreamReader]::new($stream)
            $errBody = $reader.ReadToEnd()
            if ($errBody) { Write-Host $errBody -ForegroundColor Red }
        } catch { }
        switch ($status) {
            401 { Write-Host "Hint: SAS signature mismatch. If you passed -TriggerUrl, wrap it in single quotes; or omit it to let the script fetch it." -ForegroundColor Yellow }
            403 { Write-Host "Hint: Access denied. Check IP restrictions on the workflow." -ForegroundColor Yellow }
            404 { Write-Host "Hint: Workflow or trigger not found. Confirm the workflow is deployed and enabled." -ForegroundColor Yellow }
            500 { Write-Host "Hint: The run started but an action failed (often: Office 365 connection not authorized in the portal)." -ForegroundColor Yellow }
            502 { Write-Host "Hint: 502 = a downstream connector returned an error. Almost always: the Office 365 connection isn't authorized. Open the Logic App in the portal -> API connections -> office365 -> Edit API connection -> Authorize. Or run: ./scripts/invoke.ps1 -Amount 100  (below threshold, skips the connector entirely)." -ForegroundColor Yellow }
        }
    } else {
        Write-Host $_.Exception.Message -ForegroundColor Red
    }
    exit 1
}

