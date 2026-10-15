# AlterDPI security boundaries

Owner: Furkan Okan. Target: Windows 10/11 x64; local traffic adaptation, DNS protection, browser bridge, installer and signed updates.

## Required properties

- Preserve IPv4 hop limit (65 on the verified host), addresses, MTU, IPv6, foreign DNS policy and shared drivers.
- Ordinary-user UI; privileged service accepts only its installed UI/native-host images and the installation owner SID. Reject remote pipe clients and arbitrary commands, paths, profile arguments and download URLs.
- Program Files payload owned by SYSTEM/Administrators, ordinary users read/execute only. State and undo journals protected; no reparse-point traversal or unowned recursive deletion.
- Restore only owned DNS rules on stop, startup recovery, rollback and uninstall. Failed trials cannot destroy the last good profile or leave DNS pointing at a dead listener.
- TLS certificate checks remain enabled. Data and executable updates use separate pinned signing keys, bounded responses, hashes and replay controls. Invalid updates retain the prior verified state.
- Block pages contain no remote content, injected HTML or automatic access exceptions. Exactly two explicit confirmations precede an exact-host, 10-minute exception.
- No telemetry or raw browsing URLs/SSID names in user logs. Neutral test IDs; known failures must not be described as a verified attack or universal connectivity success.

## In scope

Privilege escalation, pipe impersonation, service/file ACLs, installer rollback/deletion, DNS parsing/resource limits, policy ownership, update trust and replay, browser permissions/rules, exception scope, cancellation races and secrets in release output.

## Limits

No VPN/tunnel, HTTPS interception, antivirus replacement or guaranteed IP-block bypass. Compromise of an administrator, upstream signing private key or the OS is outside the application's prevention guarantee; document relevant exposure honestly.

Review source offline. Do not contact suspicious domains or mutate the host network as part of the static security scan. Report concrete reachable evidence and unresolved coverage separately.
