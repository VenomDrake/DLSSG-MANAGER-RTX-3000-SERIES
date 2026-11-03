DLSSG Mod Manager 0.2.3 clarifies game management and manager-update notices.

- Renames the main workspace to **Game management**, translated into all 11 manager languages. This tab opens game controls; it does not indicate an available software update.
- Shows **Update and restart** only when a newer stable manager release is actually available. The simulated update notice used for previews is removed.
- Retains the game-support banner and the MSI updater correction introduced in 0.2.2.

**Updating:** users on 0.2.2 can choose **Update and restart** when 0.2.3 is offered. MSI installations still on 0.2.0 or 0.2.1 need one manual installation of the latest MSI (0.2.2 or later): close the manager and run this release's 0.2.3 MSI in the same installation folder. Their older updater may fail with error 1639 and cannot acquire this correction automatically. Installing 0.2.1 does not fix that defect. Portable installations are unaffected by this MSI issue. Users on 0.1.0 must update manually once because that version has no updater.

Manager updates are separate from game mod updates; the reviewed upstream recipe remains **0.4.1**. The online-game ban warning remains: use the mod only in offline single-player games; no ban protection or bypass is provided.

The manager and MSI remain unsigned. Original mod: **sdli1995** and contributors; RTX 20 / SM75 adaptation: **Coldwood1026**. See the README and third-party notices.
