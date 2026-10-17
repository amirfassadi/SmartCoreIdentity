# Private Debian 12 / Ubuntu 22.04 x64 bundle

This delivery targets the owner's Debian 12 x64 VPS with Python 3.11,
PostgreSQL 18 on loopback 5432, approximately 4 GiB RAM and 21 GiB free disk.
The same archive is also tested on Ubuntu 22.04 x64 with Python 3.10 and PostgreSQL
18. The runner is a foreground private test workspace, not public release.
Existing web, DNS, FTP and other services must be preserved.
Kavenegar remains postponed; no SMS/email/voice provider is connected.

## Package contents

CI builds on Ubuntu 22.04 and publishes the API/BFF assemblies with a shared
Linux x64 .NET/ASP.NET 10 runtime, the production Next standalone app and its static assets, a separate
Node 24 executable, the HTTPS ingress and Python 3.10-compatible control scripts.
The archive requires no build, npm install, .NET SDK/runtime installation, Docker,
Compose, or replacement of system Node/Python. OpenSSL and the existing `psql`
client are required. Native OS libraries required by the runtimes must be present;
CI runs the same bundle both on Ubuntu 22.04 and in a Debian 12 container.
Debian runtime dependencies follow the [official .NET 10 list](https://github.com/dotnet/core/blob/main/release-notes/10.0/dotnet-dependencies.md):
`libc6 libgcc-s1 libgssapi-krb5-2 libicu72 libssl3 libstdc++6 ca-certificates tzdata`
(plus `zlib1g`, Python, OpenSSL and psql for this launcher). Check installed packages
before installing missing libraries; do not replace the system Node or Python.

There is one API, one BFF, one Next server and one loopback TLS ingress. To retain
the existing proxy's two-target contract, both targets point to that single BFF.
This is explicitly single-host private operation, not additional multi-host or
failover evidence. Existing multi-host source-workspace tests remain separate.
The smaller process count helps a limited VPS, but resource fitness is not proven
until this host is measured while its other services are running. Do not load-test
or admit real users on it based only on CI acceptance.

## Download and transfer

Download the `smartcore-identity-private-linux-x64` artifact from the successful
**Debian 12 and Ubuntu private VPS bundle** Actions run. It contains exactly:

- `smartcore-identity-private.tar.gz`
- `smartcore-identity-private.tar.gz.sha256`

The tar archive is created **before** any test initialization. CI uploads only these
two files, never the initialized directory, generated keys or database credentials.
The manifest records source SHA, bundled Node/.NET versions, target and file hashes.
Check the archive SHA against the trusted Actions run's package output, not only an
untrusted checksum delivered alongside a possibly modified archive.

From the client machine, in the folder containing those two files:

```sh
scp -P SSH_PORT smartcore-identity-private.tar.gz smartcore-identity-private.tar.gz.sha256 USER@VPS_HOST:
```

## Install as the normal application user

Replace `SSH_PORT`, `USER` and `VPS_HOST` above with the selected server connection.
Use the normal application user shell (currently `amir` on Debian). The destination must not already exist; do not unpack
over an earlier installation or delete its keys to retry. Compare the expected
SHA from CI, then:

```sh
cd $HOME
sha256sum -c smartcore-identity-private.tar.gz.sha256
test ! -e $HOME/smartcore-identity-private
umask 077
tar -xzf smartcore-identity-private.tar.gz --no-same-owner
cd $HOME/smartcore-identity-private
python3 run.py verify
python3 run.py init
sudo -v
python3 run.py provision-db
python3 run.py run
```

Run each line only after the previous line succeeds. If the destination-exists check
fails, stop; do not continue extracting. Running `run` again resumes the same private
workspace and preserves keys and sessions. `init` preserves a complete existing
configuration and refuses a partial one. There is no automatic destructive repair.

`provision-db` invokes a fixed local administrative psql command via cached sudo.
It creates only role/database `smartcore_identity_private` on PostgreSQL 18 port
5432. A random password remains in the 0600 ignored configuration and is passed to
psql over a private pipe/environment, never printed or put in command arguments.
Database and role comments bind them to this initialization's random ownership
marker. Existing objects with an unrelated marker are rejected before changes.
The application role cannot create databases/roles, replicate or act as superuser.
Repeating provisioning preserves its password and data. A session-level advisory
lock serializes this operation. A crash leaving a newly created unmarked object
requires local inspection; it is never adopted or deleted automatically.

No cluster restart, HBA/configuration edit, package upgrade, Docker action, VPN
change, systemd install, public HTTP/TLS route or firewall rule is performed.
The private role owns its own schema for this developer migration/runtime path;
production migration/runtime privilege separation is still a release requirement.

## Access and test

The services bind only `127.0.0.1`: API 5157, BFF 5171, Next 3181 and HTTPS 7443.
Existing listeners are rejected before migrations. Ports 80/443/5433 are unused by
this bundle; PostgreSQL 18 on 5432 is accessed through the dedicated private database.
The runner does not start or stop that PostgreSQL cluster. Ctrl+C or an SSH hangup
stops only owned app process groups; the restart acceptance sends an actual SIGHUP.
The encrypted persistent key ring and keys remain under
`dev-secrets/private-stack`; preserve them with the database for any later recovery.

From the client machine:

```sh
ssh -p SSH_PORT -N -L 7443:127.0.0.1:7443 USER@VPS_HOST
```

Open `https://localhost:7443/fa` or `/en`. Transfer only the generated public
`dev-secrets/private-stack/tls.crt` to the client and trust it for the local test
browser. No private key/configuration needs to be sent through chat. The server's
own readiness check verifies the certificate; no insecure TLS bypass is used.

Use synthetic test accounts. Registration still verifies contact via the protected
development inbox; normal login accepts the account email/mobile and its personal
password (minimum 15 characters). A second server terminal can read the code:

```sh
cd $HOME/smartcore-identity-private
python3 run.py inbox
# For unfinished setup or password reset:
python3 run.py inbox --kind setup
python3 run.py inbox --kind reset
```

The explicit operator inbox command prints a code only in that terminal. Reset
also requires the previously enrolled one-time recovery code. The HTTPS proxy
continues to block raw Identity, diagnostic and operator routes.

## Verification scope

The bundle workflow uses actual PostgreSQL 18 and Python 3.10 on Ubuntu 22.04,
then runs the same initialized bundle against the same disposable database in a
Debian 12 container with Python 3.11 and native runtime libraries, without an SDK
or system Node. No initialized directory is uploaded. It
checks unowned-role collision refusal, limited role flags, ownership markers,
idempotent provisioning, real bundled-runtime registration/login/read/logout and
session continuity after immediate app restart. The artifact is uploaded only
after success. Unit checks cover manifest tampering/traversal, inherited PG
configuration isolation, generated provisioning values and existing private-runner
key/permission/lock/TIME_WAIT controls. The existing source-workspace and full
backend/browser workflows continue to run separately. The backend matrix also
retains PostgreSQL 17 coverage; new private deployments require 18. An existing
17 deployment needs a backed-up, tested database upgrade before using this bundle;
changing the image tag is not a data migration.

CI evidence is tied to the tested source/run, not proof of deployment on the VPS.
Delivered alerts, accepted load/failover/coordinated-backup evidence, canonical
provenance, retention/contact-release policy, production configuration and legal
notices still gate real users. See [release gates in the source repository](https://github.com/amirfassadi/SmartCoreIdentity/blob/review/identity-bff/docs/implementation/RELEASE_GATES.md).
