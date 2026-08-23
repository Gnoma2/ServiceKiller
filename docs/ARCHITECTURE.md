# Architecture and restoration model

## Execution model

The WinForms GUI can start without elevation. Operations that require system changes use UAC/elevated worker execution.

A global machine-operation lock prevents overlapping privileged apply/restore operations.

## Persistent mode

Backed-up changes are recorded in:

```text
C:\ProgramData\ServiceKiller\active-state.json
```

The original state is retained until the user explicitly restores the pending tweak(s).

## Temporary-until-reboot mode

Session-backed changes are recorded separately in:

```text
C:\ProgramData\ServiceKiller\session-state.json
```

The persistent and temporary journals are intentionally separate so a temporary restore cannot accidentally restore unrelated persistent changes.

Before temporary changes are applied, ServiceKiller prepares a protected restore worker at:

```text
C:\ProgramData\ServiceKiller\SessionRestore\ServiceKiller.SessionRestore.exe
```

The worker is copied from the currently running build, protected under the machine data ACL model and accompanied by a SHA-256 record.

A Task Scheduler 2.0 COM task is created for the originating user and configured for the next logon with highest available privileges. The task points to the protected worker, not to an executable in Downloads/Desktop or another user-writable path.

ServiceKiller reads back and verifies the task definition after registration.

## Protected machine data

`C:\ProgramData\ServiceKiller` contains privileged restoration inputs. The application explicitly protects the machine-data tree and individual sensitive files so non-elevated processes cannot freely replace privileged restore inputs.

## Journal validation

Restoration journals are treated as untrusted privileged inputs until validated. The validator constrains the accepted structure and known target types before restoration.

An inaccessible/corrupt journal is not silently treated as a clean state.

## Restore verification

After restoration, ServiceKiller verifies the restored components against the saved baseline. Temporary restoration writes a verification report to:

```text
C:\ProgramData\ServiceKiller\Logs\last-session-restore-verification.txt
```

## Deferred worker cleanup

A restore worker cannot safely delete its own executable while it is still running. After successful automatic restoration it creates a protected cleanup marker. The next normal ServiceKiller launch verifies that no temporary session/task remains and removes the residual protected worker files.

## BCD

The current catalog's hypervisor tweak changes `hypervisorlaunchtype` using the Windows system `bcdedit.exe` path and requires restart. It is included only in Persistent mode because it changes next-boot behavior.
