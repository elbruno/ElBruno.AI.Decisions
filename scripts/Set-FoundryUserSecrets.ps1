<#
.SYNOPSIS
Sets the Microsoft-Decision-1 Foundry endpoint and key for the samples and live tests.
.DESCRIPTION
Prompts for the scoring URL and a masked key and passes JSON through standard input to Secret Manager.
Uses the shared ElBruno.AI.Decisions.Jev.Development store; other secrets are preserved.
User-secrets are stored outside the repository but are not encrypted.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$storeId = 'ElBruno.AI.Decisions.Jev.Development'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Install the .NET 10 SDK before configuring user-secrets.'
}

if (-not $PSCmdlet.ShouldProcess($storeId, 'Set Decisions:Foundry:* for samples and live tests')) {
    return
}

$endpoint = Read-Host 'Enter the full HTTPS scoring URL of the Foundry deployment'
if ($endpoint -notmatch '^https://\S+$') {
    throw 'The endpoint must be an HTTPS URL without whitespace. No secret was changed.'
}

$secureKey = Read-Host 'Enter the Foundry API key (input is hidden)' -AsSecureString
$buffer = [IntPtr]::Zero
$plainKey = $null
$json = $null

try {
    $buffer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureKey)
    $plainKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($buffer)
    if ([string]::IsNullOrWhiteSpace($plainKey) -or $plainKey -match '\s') {
        throw 'The API key must be nonempty and contain no whitespace. No secret was changed.'
    }

    $json = @{ 'Decisions:Foundry:Endpoint' = $endpoint; 'Decisions:Foundry:ApiKey' = $plainKey } | ConvertTo-Json -Compress
    $json | dotnet user-secrets set --id $storeId
    if ($LASTEXITCODE -ne 0) {
        throw "Secret Manager failed with exit code $LASTEXITCODE."
    }

    Write-Host 'Configured Decisions:Foundry:Endpoint and Decisions:Foundry:ApiKey. No request was made.'
}
finally {
    if ($buffer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($buffer)
    }
    $plainKey = $null
    $json = $null
}
