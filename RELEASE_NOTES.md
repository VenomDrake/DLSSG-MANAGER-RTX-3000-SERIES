DLSSG Mod Manager 0.4.3 — repository name migration.

The GitHub repository is now named **VenomDrake/DLSSG-Mod-Manager**, matching the application name. Source, issues, tags, releases and images remain in the same repository. The manager's AMD/NVIDIA ANR support and SM86 scope are unchanged.

- The manager updater now uses the new repository endpoint and accepts only the exact new and legacy repository paths under VenomDrake. HTTPS, release/version, asset-name, size and SHA-256 checks remain enforced.
- Updated download, documentation and packaged README links in English and ten translated guides. Refreshed screenshots show manager 0.4.3.
- Existing installation paths, Windows Installer UpgradeCode, game inventories, settings and backups are unchanged. The MSI and portable package keep their familiar filenames.

**One-time migration for manager 0.4.2 and earlier:** those binaries validate the old repository path, so GitHub's redirect alone may not let them discover releases after the rename. Close the manager, then install the latest MSI once, or replace the complete portable package using the same installation type. Version 0.4.3 and later check the new repository for subsequent updates. Updating the manager does not change mods in games.

GitHub redirects the former repository URL to the new one. Do not recreate a repository under the old name, because that would disable those redirects.

**Validation scope:** update metadata and download validation are covered with canonical/legacy fixtures and negative URL cases. AMD gameplay still requires verification on real AMD hardware; ANR remains upstream beta FG-V.B15.

**Online games: risk of a ban.** Use injected mods only in offline single-player games. No anti-cheat bypass or protection against bans is provided.

Credits: sdli1995 and contributors (SM86), Coldwood1026 (SM75), Apex Compute (ANR), and OptiScaler contributors. Upstream materials retain their authors' terms and are not bundled with the manager. The manager EXE and MSI remain unsigned.
