# Security and data handling

This application changes files in a user-selected game folder. It does not change drivers, disable security software, import certificates, collect telemetry or upload reports. Administrative privileges are not requested by default; an unwritable game directory is reported as an error.

Update downloads are restricted to the official upstream GitHub release and approved GitHub asset hosts. SHA-256 and the complete file manifest are checked before extraction/install; archive traversal, alternate data streams, duplicate names and reparse points are refused. Hash agreement proves that the bytes match the upstream release, not that upstream code is harmless.

The manager checks its own public GitHub releases separately. Only newer stable releases with both official assets and GitHub SHA-256 digests are offered. Nothing is installed until the user chooses **Update and restart**. The verified worker waits for the original process to exit, checks the staged payload again, and updates only the manager's owned files or runs its validated per-user MSI. Portable replacements retain a recovery backup and roll back on failure. Game folders are outside this operation.

Use the mod only in offline single-player games. Modified game files may violate game rules or trigger anti-cheat enforcement, including account bans. The manager cannot certify online safety or provide protection against bans.

Keep backup folders until you no longer need restoration. Do not edit transaction journals, installation inventories or snapshots manually. Unknown conflicting loaders and modified managed files are blocked for review.

Do not post access tokens or private logs in issues. For a suspected vulnerability, use GitHub's private vulnerability reporting when enabled by the repository owner. Otherwise contact the repository owner privately before publishing reproduction details.
