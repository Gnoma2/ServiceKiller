# Contributing

Contributions are welcome when they preserve ServiceKiller's core design goals: reversibility, explicit user control, transparent consequences and conservative handling of privileged state.

## Before opening a pull request

- Base changes on the current `main` branch.
- Explain exactly what Windows component is modified.
- Document the original state that is captured for restoration.
- Document failure modes and user-visible consequences.
- Do not add tweaks that disable Defender, SmartScreen or Firewall as a performance optimization.
- Do not add telemetry or silent network communication.
- Avoid universal FPS/input-latency claims without a reproducible benchmark methodology.

## Privileged changes

Any change that touches services, HKLM, BCD, Task Scheduler, machine-wide startup state or `C:\ProgramData\ServiceKiller` must include a restoration path and validation logic.

A journal that is corrupt, inaccessible or untrusted must not be silently treated as absent.

## Licensing

By submitting a contribution, you agree that your contribution may be distributed under the repository license, **GPL-3.0-only**, and that you have the right to submit it.

Do not copy code, assets or text from third-party projects unless its license is compatible and the required attribution/notices are added.
