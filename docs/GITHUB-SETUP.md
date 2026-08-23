# Repository setup

Recommended GitHub repository settings.

## Repository

- Name: `ServiceKiller`
- Visibility: **Public**
- Description: `Open-source Windows utility for reversible service, process and startup optimization.`
- Default branch: `main`
- License: GPL-3.0
- Suggested topics: `windows`, `windows-11`, `winforms`, `dotnet-framework`, `system-utilities`, `optimization`, `gaming`

When creating the repository from this prepared tree, do **not** ask GitHub to pre-create another README, `.gitignore` or license file.

## Security settings after publication

Enable:

- Private vulnerability reporting.
- Secret scanning / push protection when available.
- Dependabot alerts if dependencies are added later.

The repository includes `SECURITY.md`, issue templates and a source-build GitHub Actions workflow.

## First release

After the source repository is public and the exact release executable has been built from the tag:

- tag: `v1.1.3.01`
- release title: `ServiceKiller V1.1.3.01`
- attach `ServiceKiller.exe`
- attach `SHA256SUMS.txt`
- state that Windows 11 Pro 25H2 x64 build 26200 is validated;
- state that Windows 10 is not currently validated.
