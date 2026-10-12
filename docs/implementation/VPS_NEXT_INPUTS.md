# VPS continuation after private browser acceptance

The code runs privately in Development/Testing only. No script in this slice
installs services, migrates a VPS database, binds a public port or restarts Nginx/VPN.
B2 is privately verified and B3 acceptance is tracked in BFF_BROWSER_RECOVERY.md.
All RELEASE_GATES still precede real-user deployment.

Observed host context from the owner's earlier inventory: Ubuntu, SSH botdev on
port 22110, public address 45.59.70.27, existing Nginx/PostgreSQL 17/Redis/VPN/bot.
An earlier report places ocserv on 443. These are historical observations and must
be checked on the host before assigning ports or proposing an ingress change.
Build artifacts elsewhere to avoid assuming that the small VPS can run Next builds.
The browser pins Node 24; an existing Node 20 installation does not meet that pin.
Do not replace the existing runtime used by other services as an incidental step.

The committed read-only inventory can be inspected and run in an operator session:

```sh
bash deploy/preflight/identity-vps-readonly.sh
```

It prints OS, memory/disk, relevant listener ownership, runtime versions, service
state and PostgreSQL major version through optional local peer authentication.
It reads no .env, service configuration, process arguments, database contents,
passwords or private keys. It performs no privileged write or service action.
Run it only from an explicitly checked-out SmartCoreIdentity working branch.

Inputs needed for the deployment plan:

- Identity domain/DNS ownership and the chosen way to preserve the existing 443 VPN
  listener; no port reassignment is presumed.
- Confirmed isolated Identity/BFF databases and roles; existing databases must stay
  untouched. Database choice is specific to SmartCoreIdentity; another application’s
  database or credentials are not reused.
- Backup location and demonstrated restore for the chosen database/key-ring volumes.
- Iran SMS provider/account and OTP template/delivery capability; secrets stay in an
  operator-managed mount and are never posted in chat or committed.
- Alert destination, finite audit retention and accepted load/failover thresholds.

Before VPS access, [LOCAL_PREFLIGHT.md](LOCAL_PREFLIGHT.md) documents the isolated
local runner and its verified native PostgreSQL/Chromium execution.
The code/test workflow can continue without a purchased domain. These inputs block
real-host rollout/provider activation; they do not certify or replace code acceptance.
