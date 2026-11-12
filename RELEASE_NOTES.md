DLSSG Mod Manager 0.4.0-beta.2 — experimental ANR integration.

**This is a pre-release.** Stable 0.3.1 remains the latest stable version. Existing stable managers do not offer this beta automatically. Download the beta portable ZIP to test it separately, or use the beta MSI deliberately. Windows Installer uses numeric version 0.4.0; the application identifies itself as 0.4.0-beta.2. Returning from the beta MSI to an older stable MSI requires uninstalling the manager first; game mods and backup history are retained.

- Separates NVIDIA GPU eligibility by technology: GTX 16 is available only for ANR; the reviewed SM86 path remains RTX 20/30. GTX 10 / Pascal is not offered as supported.
- Choose **DLSSG for SM86 (NVIDIA DLSS-G / XeSS)** or **Apex Neural Rendering FG**. ANR supports NVIDIA GTX 16 and RTX 20/30/40/50 in this Windows manager. GTX 10 / Pascal is unsupported.
- Downloads official ANR Full or Addon packages with GitHub SHA-256 checks, separate verified caches and explicit layout checks. The reviewed recipe is **FG-V.B15**. Unknown future layouts are downloaded but blocked from installation until reviewed.
- Detects an existing compatible OptiScaler installation and uses addon mode; checks game files, graphics API evidence, GPU, Visual C++ runtime, conflicts and running processes before changing files.
- Stages changes, verifies backups and inventories, deactivates the previous provider, commits the new provider last, and journals interrupted changes for recovery. Switching providers and restoration retain previous states. External OptiScaler settings and unrelated plugins are preserved.
- ANR controls include compute path, scene-cut protection, HDR reference luminance and a multiplier cap. Actual multipliers depend on the game. Start with **2×**. ANR uses its own log reader and does not claim runtime verification from package presence alone.
- The interface and documentation include all 11 manager languages. Credits remain with sdli1995, Coldwood1026, Apex Compute and the OptiScaler authors.

This beta has automated fixture and package-validation coverage. Game performance, image quality and stability on each user's machine require in-game testing. Keep the existing working configuration available through restore.

**Online games: risk of a ban.** Use injected mods only in offline single-player games. No anti-cheat bypass or ban protection is provided.

ANR and OptiScaler binaries are downloaded from the original project at runtime and are not redistributed in these manager packages. ANR source/model licensing is currently unspecified upstream; consult the project and its notices. Manager binaries and MSI are unsigned.
