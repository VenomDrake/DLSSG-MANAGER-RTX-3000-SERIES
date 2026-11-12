param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$versionFile = Join-Path $repoRoot 'Version.props'
$versionDocument = [xml][IO.File]::ReadAllText($versionFile)
$appVersion = [string]$versionDocument.Project.PropertyGroup.Version
if ($appVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Version.props must contain a stable three-part version for Windows Installer.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repoRoot 'artifacts' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$publishDirectory = Join-Path $OutputDirectory 'portable'
& dotnet publish (Join-Path $repoRoot 'src/DlssgManager.Desktop/DlssgManager.Desktop.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -p:DebugSymbols=false -o $publishDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publication failed.' }
foreach ($name in @('LICENSE', 'THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $publishDirectory -Force }
foreach ($name in @('DOTNET-RUNTIME-LICENSE.txt', 'WPF-LICENSE.txt', 'DOTNET-THIRD-PARTY-NOTICES.txt')) { Copy-Item -LiteralPath (Join-Path $repoRoot "docs/$name") -Destination $publishDirectory -Force }
$readme = [IO.File]::ReadAllText((Join-Path $repoRoot 'README.md'))
$readme = [regex]::Replace($readme, '(?m)^!\[[^\]\r\n]*\]\([^\r\n]*\)\r?\n', '')
$prereleaseLabel = [string]$versionDocument.Project.PropertyGroup.PrereleaseLabel
$documentationRef = if ($prereleaseLabel) { "v$appVersion-$prereleaseLabel" } else { 'main' }
$repositoryRoot = "https://github.com/VenomDrake/DLSSG-Mod-Manager/blob/$documentationRef/"
$readme = [regex]::Replace($readme, '(?m)(\[[^\]]*\]\()((?:docs/i18n/README\.[A-Za-z-]+\.md|README\.md|CONTRIBUTING\.md|SECURITY\.md|THIRD_PARTY_NOTICES\.md))((?:#[^)]*)?\))', {
    param($match)
    $match.Groups[1].Value + $repositoryRoot + $match.Groups[2].Value + $match.Groups[3].Value
})
[IO.File]::WriteAllText((Join-Path $publishDirectory 'README.md'), $readme, [Text.UTF8Encoding]::new($false))
$wixDirectory = Join-Path $repoRoot 'tools/wix'
$wix = Join-Path $wixDirectory 'wix.exe'
if (-not (Test-Path -LiteralPath $wix)) {
    & dotnet tool install wix --version 4.0.6 --tool-path $wixDirectory
    if ($LASTEXITCODE -ne 0) { throw 'WiX tool installation failed.' }
}
& $wix extension add -g WixToolset.UI.wixext/4.0.6
if ($LASTEXITCODE -ne 0) { throw 'WiX UI extension setup failed.' }
$msi = Join-Path $OutputDirectory 'DLSSGModManager-Setup.msi'
& $wix build (Join-Path $repoRoot 'installer/Package.wxs') -arch x64 -ext WixToolset.UI.wixext -d "PublishDir=$publishDirectory" -d "RepoDir=$repoRoot" -d "AppVersion=$appVersion" -o $msi
if ($LASTEXITCODE -ne 0) { throw 'MSI build failed.' }
$zip = Join-Path $OutputDirectory 'DLSSGModManager-Portable-win-x64.zip'
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zip -Force
$checksums = foreach ($artifact in @((Join-Path $publishDirectory 'DLSSGModManager.exe'), $zip, $msi)) {
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($artifact))"
}
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
Write-Output "Built portable EXE, ZIP and MSI in $OutputDirectory"
