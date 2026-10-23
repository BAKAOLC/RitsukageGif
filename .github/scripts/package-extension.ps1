param(
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-arm64')]
    [string]$RuntimeIdentifier = '',
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$versions = & (Join-Path $PSScriptRoot 'get-package-version.ps1')
$architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
$hostPlatform = if ($IsWindows) { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
if (!$RuntimeIdentifier) { $RuntimeIdentifier = "$hostPlatform-$architecture" }
if ($RuntimeIdentifier -ne "$hostPlatform-$architecture") { throw 'Native AOT packaging requires a matching host OS and architecture.' }
$target = $RuntimeIdentifier.Replace('win-', 'win32-').Replace('osx-', 'darwin-')
if (!$Output) { $Output = "artifacts/packages/rgscript-$($versions.Extension)-$target.vsix" }
$outputPath = [IO.Path]::GetFullPath($Output, $root)
$artifacts = Join-Path $root 'artifacts'
$staging = [IO.Path]::GetFullPath((Join-Path $artifacts 'vscode-package'))
if (!$staging.StartsWith([IO.Path]::GetFullPath($artifacts) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging directory.' }
if ($outputPath.StartsWith($staging + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'The output must be outside the staging directory.' }
if (Test-Path -LiteralPath $staging) { [IO.Directory]::Delete($staging, $true) }
New-Item -ItemType Directory -Force -Path $staging, (Split-Path -Parent $outputPath) | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'extensions/vscode/extension.js'), (Join-Path $root 'extensions/vscode/package.json'), (Join-Path $root 'extensions/vscode/package.nls.json'), (Join-Path $root 'extensions/vscode/package.nls.zh-cn.json'), (Join-Path $root 'extensions/vscode/language-configuration.json'), (Join-Path $root 'extensions/vscode/syntax.json'), (Join-Path $root 'LICENSE') -Destination $staging -Force
& dotnet publish (Join-Path $root 'src/RitsukageGif.LanguageServer') -c Release -r $RuntimeIdentifier -o (Join-Path $staging 'server')
if ($LASTEXITCODE -ne 0) { throw 'Language server publish failed.' }
@('server/**/*.pdb', 'server/**/*.xml', 'server/**/*.dbg', 'server/**/*.dSYM/**') | Set-Content -LiteralPath (Join-Path $staging '.vscodeignore')
Push-Location $staging
try {
    & npx --yes '@vscode/vsce@4.0.0' package --no-dependencies --allow-missing-repository --target $target --out $outputPath
    if ($LASTEXITCODE -ne 0) { throw 'Extension packaging failed.' }
}
finally { Pop-Location }
