# Validation

This document records the public functional validation performed before the first open-source release.

## Test platform

- Windows 11 Pro 25H2
- Build 26200
- x64
- ServiceKiller V1.1.3.01 Candidate/RC lineage corresponding to the public 1.1.3.01 engine

## Persistent-mode cycle

Sequence:

1. Start from a clean/restored ServiceKiller state.
2. Apply **Aggressive / Persistent**.
3. Generate diagnostic before reboot.
4. Reboot.
5. Confirm journal and changes remain.
6. Restore all pending changes.
7. Generate diagnostic before reboot.
8. Reboot and confirm restored state remains clean.

Observed result:

- 35 actions selected;
- 22 changes applied;
- 13 required no change;
- 0 errors;
- 18 persistent journal entries;
- after reboot: 18 persistent entries still present;
- global restore: **18 requested / 18 restored / 0 pending / 0 errors**;
- after final reboot: 0 pending journals.

## Temporary-mode cycle

Sequence:

1. Start from a clean/restored state.
2. Apply **Aggressive / Temporary until reboot**.
3. Confirm the session journal, scheduled restore task and protected restore worker exist.
4. Reboot/log on.
5. Confirm automatic restoration and cleanup.

Observed result:

- 30 actions selected;
- 17 changes applied;
- 13 required no change;
- 0 errors;
- 13 session journal entries;
- protected restore worker present and SHA-256 verified before reboot;
- automatic restore report: **13 journal tweaks / 18 components verified / 18 correct / 0 failures**;
- `session-state.json` absent after restoration;
- restore task absent after restoration;
- protected restore worker removed after deferred cleanup;
- final pending count: 0.

## What this does not prove

This validation demonstrates expected application/restoration behavior on the stated Windows build. It does not establish universal performance gains, compatibility with every Windows edition/build, or immunity from future Windows changes.
