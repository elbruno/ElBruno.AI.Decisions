[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('release', 'workflow_dispatch')]
    [string]$EventName,
    [string]$ReleaseTag,
    [string]$Version,
    [string]$Ref,
    [string]$SourceVersion,
    [string]$SourceCommit,
    [Parameter(Mandatory)]
    [string]$Repository,
    [switch]$ApproveUnverifiedPublication,
    [string]$VerifyPublishedRun = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($EventName -ceq 'release') { $Version = $ReleaseTag -creplace '^v', '' }
if ($Version -ceq 'latest') {
    if ($EventName -cne 'workflow_dispatch' -or $Ref -or
        $SourceCommit -cnotmatch '^[0-9a-fA-F]{40}\z' -or [string]::IsNullOrWhiteSpace($SourceVersion)) {
        throw 'Latest requires a manual dispatch, an empty ref, and a source version and immutable source commit.'
    }
    $Version = $SourceVersion
    $Ref = $SourceCommit
}
if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z') {
    throw 'Expected SemVer without build metadata; published tags may have one lowercase v prefix.'
}
$parts = $Version.Split('-', 2)
if ($parts.Count -eq 2) {
    foreach ($identifier in $parts[1].Split('.')) {
        if ($identifier -match '^0[0-9]+$') { throw 'Numeric prerelease identifiers cannot start with zero.' }
    }
}
if ($EventName -ceq 'workflow_dispatch' -and $Ref -cne "v$Version" -and $Ref -cne $Version -and $Ref -notmatch '^[0-9a-fA-F]{40}\z') {
    throw 'Manual ref must be the matching version tag (optionally prefixed v) or a full commit SHA.'
}
if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw 'Invalid repository identity.' }
$publishAllowed = $false
if ($VerifyPublishedRun) {
    if ($EventName -cne 'workflow_dispatch' -or $VerifyPublishedRun -notmatch '^[1-9][0-9]{0,19}\z' -or $ApproveUnverifiedPublication) {
        throw 'Public verification requires a numeric original publication run ID, manual dispatch, and publication approval disabled.'
    }
}
if ($ApproveUnverifiedPublication) {
    if ($EventName -cne 'workflow_dispatch') {
        throw 'Publication requires an explicitly approved manual dispatch.'
    }
    $publishAllowed = $true
}
$checkoutRef = if ($EventName -ceq 'release') {
    "refs/tags/$ReleaseTag"
} elseif ($Ref -match '^[0-9a-fA-F]{40}\z') {
    $Ref
} else {
    "refs/tags/$Ref"
}
[pscustomobject]@{
    Version = $Version
    CheckoutRef = $checkoutRef
    RepositoryUrl = "https://github.com/$Repository"
    PublishAllowed = $publishAllowed
    VerificationRun = $VerifyPublishedRun
}
