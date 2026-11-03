DLSSG Mod Manager 0.2.2 fixes the MSI updater used by installed copies of the manager.

- Corrects Windows Installer arguments for installation folders containing spaces, preventing error 1639 and the unexpected installer help window.
- Improves installer diagnostics and failure recovery while preserving games, manager settings and backups.
- Retains the translated game-support banner introduced in 0.2.1: unsupported, needs review, required files detected, or installation blocked. Preliminary file checks still require confirmation by running the game and reading its log.

**Installed with MSI, currently on 0.2.0 or 0.2.1:** close the manager, download this release's **0.2.2 MSI** and run it once, keeping the same installation folder. Those older versions contain the defective MSI updater, so publishing this release cannot repair their update command automatically. **Installing the 0.2.1 MSI is not a lasting fix.** Once 0.2.2 is installed, subsequent manager updates can use the corrected in-app updater.

**Portable installations on 0.2.0 or 0.2.1:** this MSI issue does not affect the portable updater. Choose **Update and restart** when stable 0.2.2 is offered, or extract the complete new portable package manually.

**Users on 0.1.0:** install 0.2.2 manually once, using the same package type as before; 0.1.0 does not contain an updater.

Manager updates are separate from game mod updates. The reviewed DLSSG for SM86 installation recipe remains **0.4.1**. Using the mod changes game files and can lead to bans in online games. Use it only in offline single-player games; no ban protection or bypass is provided.

The manager and MSI remain unsigned. Original mod credit belongs to **sdli1995** and the upstream contributors; credit for the RTX 20 / SM75 adaptation belongs to **Coldwood1026**. See the README and third-party notices.
