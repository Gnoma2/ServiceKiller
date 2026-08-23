# Changelog

All notable public changes to ServiceKiller are documented here.

## V1.1.3.01 — 2026-08-23

Initial public open-source release.

### Added / included

- Three built-in profiles: Conservative, Balanced and Aggressive.
- Persistent and temporary-until-reboot modes.
- Reversible journals for backed-up system changes.
- Automatic temporary-session restoration at next logon.
- Protected restore worker in `C:\ProgramData\ServiceKiller\SessionRestore`.
- SHA-256 verification of the protected worker.
- Task Scheduler 2.0 COM integration using the originating user and highest available privileges.
- Journal validation and protected machine-data directory permissions.
- Restoration verification report.
- Best-effort anonymized diagnostic report.
- Startup-management support for Epic Games Launcher, PowerToys, Microsoft Teams and reWASD.
- Custom resident-application support.

### Validation status

Validated on Windows 11 Pro 25H2 x64, build 26200. Windows 10 is not currently validated and is not advertised as supported.
