param([switch]$Live)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
foreach ($project in @('tests/ReleaseTests/ReleaseTests.csproj', 'tests/DetectionTests/DetectionTests.csproj', 'tests/DlssgManager.Tests/DlssgManager.Tests.csproj')) {
    & dotnet run --project (Join-Path $repoRoot $project) --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Test project failed: $project" }
}
$localeRoot = Join-Path $repoRoot 'src/DlssgManager.Desktop/Locales'
$canonical = Get-Content -LiteralPath (Join-Path $localeRoot 'en.json') -Raw | ConvertFrom-Json -AsHashtable
$localeFiles = @(Get-ChildItem -LiteralPath $localeRoot -Filter '*.json' -File)
if ($localeFiles.Count -ne 11) { throw 'Expected exactly 11 languages.' }
foreach ($file in $localeFiles) {
    $locale = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -AsHashtable
    if (Compare-Object @($canonical.Keys | Sort-Object) @($locale.Keys | Sort-Object)) { throw "Language keys differ: $($file.Name)" }
    if (@($locale.Values | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count) { throw "Empty translations: $($file.Name)" }
    Write-Output "PASS language $($file.BaseName): $($locale.Count) keys"
}
& dotnet build (Join-Path $repoRoot 'src/DlssgManager.Desktop/DlssgManager.Desktop.csproj') --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
if ($Live) {
    & dotnet run --project (Join-Path $repoRoot 'tests/ReleaseTests/ReleaseTests.csproj') --configuration Release -- --live
    if ($LASTEXITCODE -ne 0) { throw 'Live upstream check failed.' }
}
