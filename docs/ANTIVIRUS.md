# Antivirus and SmartScreen

ServiceKiller performs actions that are also common in administrative tuning tools: it stops services, changes startup configuration, edits selected registry values, closes process trees and can modify BCD. Heuristic security engines can therefore inspect it more aggressively than a normal desktop application.

## Project policy

- Do **not** instruct users to disable Defender, SmartScreen or Firewall to run ServiceKiller.
- Do **not** add exclusions as a normal installation step.
- Do **not** use packing, obfuscation or other AV-evasion techniques to hide behavior.
- Publish source code and the SHA-256 of every release binary.
- Build release binaries from the tagged source.
- Submit exact flagged release binaries to the relevant security vendor for review when appropriate.

## User verification

For an official release:

1. Download only from the repository's GitHub Releases page.
2. Compare the SHA-256 with the value published in the release notes.
3. Review the source or build it locally if desired.
4. If Windows or an antivirus blocks the file, do not weaken protection merely to bypass the warning.

Code signing may improve publisher identity and integrity but is not a guarantee that heuristic detections or SmartScreen reputation warnings will never occur.
