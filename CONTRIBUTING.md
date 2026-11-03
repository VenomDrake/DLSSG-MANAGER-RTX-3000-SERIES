# Contributing

Use the .NET 10 SDK on Windows x64. Run `scripts/test.ps1` and build the desktop application before opening a pull request.

Keep game writes in `InstallService`. Preserve its staging, backup, inventory and journal guarantees. Add a synthetic regression test for changes to update identity, archive validation, conflict detection or restoration. Never add proprietary upstream DLLs or real user/game data as fixtures.

`src/DlssgManager.Desktop/Locales/en.json` is the source dictionary. Add the same keys to all other dictionaries. Translated text should explain the result to a player; keep configuration values such as `version.dll` and `MaxGeneratedFrames` unchanged. Test Arabic/Urdu layout when changing controls.

When upstream changes, review its complete package, manifest, required files, CUDA/driver floor, INI semantics and log schema. Update the explicit installation recipe only after verification. Do not mark an unknown future release compatible just because its filenames or minor version match.

Reports should identify the manager/mod versions, check codes and reproduction steps. Use the manager's redacted report export instead of posting entire private paths or unreviewed logs.
