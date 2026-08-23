# Security Policy

## Supported public version

| Version | Security support |
| --- | --- |
| 1.1.3.01 | Yes |
| Older / development builds | No public security support commitment |

Platform validation currently covers Windows 11 Pro 25H2 x64, build 26200.

## Reporting a vulnerability

Please **do not open a public GitHub issue for a security vulnerability**.

For this repository, enable GitHub **Private vulnerability reporting** and use the repository's **Report a vulnerability** function. This provides a private channel between the reporter and maintainer.

Useful reports should include:

- affected version and commit/tag;
- Windows version/build;
- exact reproduction steps;
- expected vs. observed behavior;
- whether administrator privileges are involved;
- relevant logs/diagnostics after reviewing them for private data;
- proof-of-concept code only when needed to demonstrate the issue.

## Security-sensitive areas

Extra care is required around:

- elevated worker execution;
- restoration journals;
- ACLs under `C:\ProgramData\ServiceKiller`;
- Task Scheduler task creation/removal;
- restore-worker hashing and cleanup;
- registry and service restoration;
- BCD changes;
- startup-entry restoration.

Changes to these areas should fail closed where possible and should never silently treat an inaccessible or corrupt restoration journal as a clean state.

## Antivirus detections

Do not work around Defender/SmartScreen or other security products by weakening user protection. If a legitimate release is detected, record the exact SHA-256 and submit the file to the vendor through its normal false-positive/software-developer review process.

See [docs/ANTIVIRUS.md](docs/ANTIVIRUS.md).
