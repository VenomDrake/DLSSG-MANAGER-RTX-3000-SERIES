# Validation of 0.1.0

Validated on Windows x64 on 3 November 2026.

- **25 deterministic release tests:** initial download, exact identity comparison, same-version asset replacement, offline distinction, modified cache recovery, cancellation, archive traversal/case collisions, future recipe and runtime mismatch refusal.
- **30 detection/log tests:** synthetic x64 runtime with version resources, native runtime generation checks, executable selection, unknown proxy and anti-cheat blocking, corrupted inventory, cancellation, latest process/session isolation, malformed log fields and actual FG-vs-SR discrimination.
- **Installation transaction suite:** first installation, unchanged no-op, option changes, disable, update/restore chains, adoption of a recognized manual installation, original/extra files, backup integrity, cancellation, real commit failure, recovery journal, active-process blocking, malformed/null records and path traversal.
- **11 localization dictionaries:** identical 161 keys and non-empty values. English and Italian screens inspected. Arabic and Urdu use right-to-left layout; broad native-speaker review is still needed.
- **Live upstream check:** official 0.4.1 downloaded, archive and 28 manifest files verified; a second check returned “up to date.”
- **Live read-only machine/game check:** RTX 3090 SM 8.6, driver 621.01, integrated Intel adapter and 54 Steam games detected. The existing working manual 0.4.1 installation in Funnel Runners was recognized; its current recorded FG creation/evaluation was read successfully. No game files were changed by this validation.
- **Desktop build:** no compiler warnings or errors. Self-contained portable EXE opened successfully without a separate runtime install step.
- **MSI:** built as a per-user package. Administrative extraction completed successfully and the embedded EXE matched the portable EXE's SHA-256. A complete per-user install/uninstall on a clean Windows machine has not yet been exercised.

Synthetic tests contain no NVIDIA binaries and do not depend on a particular GPU or installed game. The `--live` release test is optional and requires network access. The screenshot is an actual read-only preview, not a claim of runtime compatibility for the displayed game.

This is an unsigned first public preview. Testing a broader range of games, clean Windows installations, GPUs and translations remains community validation work. Future upstream releases are not marked installable until the manager has an explicit compatible recipe.
