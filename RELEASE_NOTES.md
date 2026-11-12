DLSSG Mod Manager 0.4.1 — stable manager release with SM86 and ANR support.

Choose **DLSSG for SM86 (NVIDIA DLSS-G / XeSS-FG)** or **Apex Neural Rendering FG** in one manager. This is a normal stable manager release; the upstream ANR FG package itself remains a beta, with the reviewed installation recipe pinned to **FG-V.B15**.

- Separate GPU and prerequisite checks: SM86 targets RTX 20/30; ANR in this manager supports NVIDIA GTX 16 and RTX 20/30/40/50. GTX 10 / Pascal is unsupported.
- Official Full/Addon downloads with SHA-256 verification, independent caches, inspected layouts and no execution of upstream installers. Unknown future ANR recipes remain blocked from installation until reviewed.
- Verified backups, a recovery journal, safe provider switching, disable and restore. Existing compatible OptiScaler configurations, loaders and unrelated plugins are preserved.
- Clearer ANR setup guidance: enable NVIDIA DLSS Frame Generation in the game and start at 2×. A grey multiplier selector can simply mean that only 2× is offered; it is not proof of generation.
- Diagnostics distinguish a loaded provider from a session with confirmed generated frames. ANR session counters, errors, multiplier and recording time are displayed when available. Truncated or incomplete logs cannot falsely certify a session.
- HDR reference luminance accepts valid whole-number settings, including values outside the previous preset list.
- Updated interface and documentation in all 11 languages. Manual smoke tests included Death Stranding 2 and Far Far West on the tested setup; these do not certify every configuration or establish a performance advantage over SM86.

**Updating:** users on manager 0.3.1 or 0.4.0-beta can choose **Update and restart** when 0.4.1 is offered. Installing a manager update does not automatically replace the mod in any game. Select a game, review its checks and apply a provider deliberately. MSI installations still on 0.2.0/0.2.1 need one manual installation of the latest MSI because their old updater may fail with Windows Installer error 1639; 0.1.0 also needs one manual update.

**Online games: risk of a ban.** Use injected mods only in offline single-player games. No anti-cheat bypass or protection against bans is provided.

Original SM86 mod: **sdli1995** and contributors; RTX 20 / SM75 adaptation: **Coldwood1026**. ANR: **Apex Compute**, with **OptiScaler** and its contributors. Upstream DLLs/models are downloaded from their authors at runtime and are not bundled with this manager. Consult upstream notices and terms; the manager does not relicense that material. The manager EXE and MSI remain unsigned.
