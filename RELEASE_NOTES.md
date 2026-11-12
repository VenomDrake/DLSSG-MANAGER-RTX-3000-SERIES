DLSSG Mod Manager 0.4.2 — AMD Radeon support for ANR.

- The ANR path now accepts the upstream-listed AMD Radeon RX 5000, 6000, 7000 and 9000 families, including recognized mobile and XT/XTX/GRE variants, alongside NVIDIA GTX 16 and RTX 20–50. Generic Radeon/APU names, older Radeon families and unreviewed families stay unsupported. This manager remains Windows 10/11 x64.
- GPU, driver and hardware messages now follow the selected provider. ANR diagnostics show the AMD driver link on Radeon systems and keep NVIDIA and AMD links separate on mixed systems. SM86 remains limited to its supported NVIDIA RTX 20/30 GPUs.
- ANR retains its own Visual C++ x64 and Vulkan-loader checks. NVIDIA driver R580 and SM86-specific GPU scheduling instructions are not imposed on Radeon/ANR installations.
- The official ANR Full/Addon packages, automatic repository update checks, SHA-256 validation, backups, provider switching, disable and restore are unchanged. Existing OptiScaler upscaling settings are preserved. No upstream DLLs or models are bundled with the manager.
- Updated English and ten translated interfaces/guides, with refreshed screenshots. Regression checks cover supported/rejected GPU names, AMD-only and mixed-adapter systems, missing prerequisites and AMD session logs.

**Validation scope:** AMD eligibility follows the upstream ANR GPU list and automated fixture tests. No real AMD gameplay test was performed for this manager release; successful generation still needs confirmation in the game's ANR log. ANR itself remains upstream beta FG-V.B15. A loaded DLL or supported GPU name does not prove generated frames or a performance gain.

**Updating:** 0.4.1, 0.3.1 and 0.4.0-beta users can choose Update and restart when 0.4.2 is offered. Manager updates do not modify games automatically. On a supported Radeon PC, select Apex Neural Rendering in Game management, review the checks, then install deliberately. MSI installations still on 0.2.0/0.2.1 need one manual installation of the latest MSI because their old updater may fail with error 1639; 0.1.0 also needs one manual update.

**Online games: risk of a ban.** Use injected mods only in offline single-player games. No anti-cheat bypass or protection against bans is provided.

Credits: sdli1995 and contributors (SM86), Coldwood1026 (SM75), Apex Compute (ANR), and OptiScaler contributors. Upstream materials retain their authors' terms. The manager EXE and MSI remain unsigned.
