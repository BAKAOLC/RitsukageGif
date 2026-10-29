param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$Commit
)
$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^v\d+\.\d+\.\d+\.\d+$' -or $Commit -notmatch '^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$') {
    throw 'Invalid release tag or commit.'
}
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$prompt = $env:GIT_TERMINAL_PROMPT
$previousExitCode = $global:LASTEXITCODE
try {
    $env:GIT_TERMINAL_PROMPT = '0'
    $refs = @(& git -C $root -c http.lowSpeedLimit=1 -c http.lowSpeedTime=10 ls-remote --tags origin "refs/tags/$Tag" "refs/tags/$Tag^{}" 2>$null)
    if ($LASTEXITCODE -ne 0) {
        Write-Warning 'Release tag lookup failed; the development build will continue.'
        return $false
    }
    $direct = @($refs | Where-Object { $_ -match "^([0-9a-fA-F]+)\s+refs/tags/$([regex]::Escape($Tag))$" } | ForEach-Object { ($_ -split '\s+')[0] })
    $peeled = @($refs | Where-Object { $_ -match "^([0-9a-fA-F]+)\s+refs/tags/$([regex]::Escape($Tag))\^\{\}$" } | ForEach-Object { ($_ -split '\s+')[0] })
    if ($direct.Count -ne 1 -or $peeled.Count -gt 1) { return $false }
    $target = if ($peeled.Count -eq 1) { $peeled[0] } else { $direct[0] }
    return $target -ieq $Commit
}
catch {
    Write-Warning 'Release tag lookup failed; the development build will continue.'
    return $false
}
finally {
    $env:GIT_TERMINAL_PROMPT = $prompt
    # GitHub's PowerShell runner propagates LASTEXITCODE after the script; a failed optional lookup must not fail CI.
    $global:LASTEXITCODE = $previousExitCode
}
