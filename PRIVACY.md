# Privacy

ServiceKiller is designed as a local Windows utility.

## Network activity

The current public source tree does not implement telemetry, analytics, update checks or network transmission. A static source audit found no use of `System.Net`, `HttpClient`, `WebRequest`, sockets or HTTP/HTTPS endpoints.

The Windows **WebClient** name present in the tweak catalog refers to the operating-system WebClient/WebDAV service, not to the .NET networking class.

## Local data

Machine-wide state is stored under:

```text
C:\ProgramData\ServiceKiller\
```

This includes restoration journals, protected restore-worker files and machine logs.

Per-user UI/session data is stored under:

```text
%LOCALAPPDATA%\ServiceKiller\
```

This can include UI state, profiles, custom applications, last-boost summary and per-user logs.

## Diagnostic reports

The built-in diagnostic report attempts to anonymize identifying values such as machine name, user name, Windows account, SID and common user-profile paths.

This anonymization is **best effort**, not a mathematical guarantee. Third-party application names, unusual paths, command-line data or other unexpected values can still contain identifying information.

**Always review a diagnostic report before posting it publicly.**

## Data sharing

ServiceKiller itself does not upload these files. If a user attaches a diagnostic or log to a GitHub issue, the user is choosing to publish that content to GitHub and should review it first.
