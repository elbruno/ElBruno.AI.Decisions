[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$policy = Join-Path $PSScriptRoot 'Get-ReleaseMetadata.ps1'
$passed = 0

function Assert-ReleasePolicy {
    param([hashtable]$Inputs, [string]$ExpectedVersion, [bool]$ExpectedPublish = $false, [string]$ExpectedError, [string]$ExpectedVerificationRun = '', [string]$ExpectedCheckoutRef)
    $failure = $null
    $result = $null
    try {
        $result = & $policy -Repository 'elbruno/ElBruno.AI.Decisions' @Inputs
    }
    catch {
        $failure = $_.Exception.Message
    }
    if ($ExpectedError) {
        if (-not $failure -or $failure -notmatch $ExpectedError) {
            throw "Expected '$ExpectedError', received '$failure'."
        }
    }
    elseif ($failure -or $result.Version -cne $ExpectedVersion -or $result.PublishAllowed -ne $ExpectedPublish -or
        $result.RepositoryUrl -cne 'https://github.com/elbruno/ElBruno.AI.Decisions' -or $result.VerificationRun -cne $ExpectedVerificationRun -or
        ($ExpectedCheckoutRef -and $result.CheckoutRef -cne $ExpectedCheckoutRef)) {
        throw "Unexpected release policy result: $failure"
    }
    $script:passed++
}

Assert-ReleasePolicy @{ EventName = 'release'; ReleaseTag = 'v0.6.0' } -ExpectedVersion '0.6.0' -ExpectedCheckoutRef 'refs/tags/v0.6.0'
Assert-ReleasePolicy @{ EventName = 'release'; ReleaseTag = '1.0.0' } -ExpectedVersion '1.0.0' -ExpectedCheckoutRef 'refs/tags/1.0.0'
Assert-ReleasePolicy @{ EventName = 'release'; ReleaseTag = 'v0.6.0'; ApproveUnverifiedPublication = $true } -ExpectedError 'explicitly approved manual'
foreach ($version in @('0.10.0', '1.0.0', '12.3.4-beta.1')) {
    Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = $version; Ref = "v$version" } -ExpectedVersion $version -ExpectedCheckoutRef "refs/tags/v$version"
    Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = $version; Ref = "v$version"; ApproveUnverifiedPublication = $true } -ExpectedVersion $version -ExpectedPublish $true
    Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = $version; Ref = ('a' * 40); ApproveUnverifiedPublication = $true } -ExpectedVersion $version -ExpectedPublish $true -ExpectedCheckoutRef ('a' * 40)
}
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = 'latest'; SourceVersion = '0.10.0'; SourceCommit = ('b' * 40); ApproveUnverifiedPublication = $true } -ExpectedVersion '0.10.0' -ExpectedPublish $true -ExpectedCheckoutRef ('b' * 40)
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = 'latest'; SourceVersion = '0.10.0'; SourceCommit = ('b' * 40) } -ExpectedVersion '0.10.0'
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = 'latest'; Ref = 'main'; SourceVersion = '0.10.0'; SourceCommit = ('b' * 40) } -ExpectedError 'Latest requires'
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = 'latest'; SourceVersion = '0.10.0'; SourceCommit = 'main' } -ExpectedError 'Latest requires'
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = 'latest'; SourceCommit = ('b' * 40) } -ExpectedError 'Latest requires'
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = 'latest'; SourceVersion = 'latest'; SourceCommit = ('b' * 40) } -ExpectedError 'Expected SemVer'
foreach ($version in @('01.6.0', '0.6.0+build', '0.6.0;echo unsafe')) {
    Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = $version; Ref = ('a' * 40) } -ExpectedError 'Expected SemVer'
}
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = '0.6.0-preview.01'; Ref = ('a' * 40) } -ExpectedError 'cannot start with zero'
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = '0.6.0'; Ref = 'main'; ApproveUnverifiedPublication = $true } -ExpectedError 'Manual ref must'
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = '0.6.0'; Ref = 'v0.6.1'; ApproveUnverifiedPublication = $true } -ExpectedError 'Manual ref must'
Assert-ReleasePolicy @{ EventName = 'release'; ReleaseTag = 'V0.6.0' } -ExpectedError 'Expected SemVer'
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = '0.6.0'; Ref = ('a' * 40); VerifyPublishedRun = '35729776067' } -ExpectedVersion '0.6.0' -ExpectedVerificationRun '35729776067'
Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = '0.6.0'; Ref = ('a' * 40); VerifyPublishedRun = '35729776067'; ApproveUnverifiedPublication = $true } -ExpectedError 'publication approval disabled'
Assert-ReleasePolicy @{ EventName = 'release'; ReleaseTag = 'v0.6.0'; VerifyPublishedRun = '35729776067' } -ExpectedError 'manual dispatch'
foreach ($run in @('0', '001', '-1', 'bad/run')) {
    Assert-ReleasePolicy @{ EventName = 'workflow_dispatch'; Version = '0.6.0'; Ref = ('a' * 40); VerifyPublishedRun = $run } -ExpectedError 'numeric original publication run ID'
}
Write-Host "PASS: $passed release-policy regression checks."
