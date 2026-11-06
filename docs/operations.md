# Operations

## Docker Compose

```bash
cp .env.example .env          # set the required values, then the optional ones you need
docker compose up -d --build
```

**`.env` is the only file you edit.** Every setting of a Compose installation (variant, addresses, sign-in, agents,
notifications, retention, logging) is a variable in `.env`; `.env.example` lists all of them with their defaults and
explanations. The compose files, Caddyfiles and `appsettings.json` stay as delivered, so updates never conflict with your
configuration. Lists (e-mail recipients, group values, events) are comma-separated.

ODM listens on <http://localhost:8080> (`ODM_PORT`). The default variant starts ODM, PostgreSQL and an S3-compatible store
(RustFS). Other variants are chosen with `COMPOSE_FILE` in `.env` (Docker Compose 2.24.4 or newer):

| `COMPOSE_FILE` | Variant |
|---|---|
| (not set) or `docker-compose.yml` | HTTP on port 8080, PostgreSQL, bundled S3 |
| `docker-compose.yml:deploy/docker-compose.https.yml` | plus Caddy with HTTPS on 80/443, see [HTTPS with Docker Compose](#https-with-docker-compose) |
| `docker-compose.yml:deploy/docker-compose.mssql.yml` | SQL Server instead of PostgreSQL (combinable with HTTPS) |

On Windows the separator is `;`. An external database or S3 service is set with `ODM_DB_CONNECTION` or `ODM_S3_*`; the
bundled container then stays unused.

Required values in `.env`:

| Variable | Purpose |
|---|---|
| `ODM_JWT_SECRET` | signs session tokens, at least 32 characters |
| `ODM_ADMIN_USER` / `ODM_ADMIN_PASSWORD` | bootstrap administrator, created on first start when no user exists; keep it as emergency access |
| `ODM_DB_PASSWORD` | database password |
| `ODM_S3_PASSWORD` | package store password (at least 8 characters) |
| `ODM_SECRETS_KEY` | master key for stored secrets, `openssl rand -base64 32`. **Keep it safe: without it, stored secrets are unreadable.** |

## Configuration reference

The server reads `appsettings.json` and environment variables (`Section__Key`). With Docker Compose you set them only
through the `.env` variable in the last column; Kubernetes uses the Helm values (see below).

| Key | Default | Meaning | `.env` |
|---|---|---|---|
| `Database:Provider` | `Postgres` | `Postgres` or `SqlServer` | `COMPOSE_FILE` (SQL Server variant) |
| `Database:ConnectionString` | | connection string | `ODM_DB_PASSWORD`, `ODM_DB_CONNECTION` |
| `Jwt:Secret` / `Jwt:ExpiryMinutes` | / 60 | session token signing key (min. 32 chars) and lifetime | `ODM_JWT_SECRET`, `ODM_SESSION_MINUTES` |
| `Secrets:MasterKey` | | base64 key (32 bytes) for stored secrets | `ODM_SECRETS_KEY` |
| `Storage:S3:*` | bundled | `ServiceUrl`, `Region`, `Bucket`, `AccessKey`, `SecretKey`, `ForcePathStyle` | `ODM_S3_*` |
| `Identity:BootstrapAdmin:*` | | initial administrator | `ODM_ADMIN_USER`, `ODM_ADMIN_PASSWORD` |
| `Identity:Ldap:*`, `Identity:Oidc:*` | off | see [authentication.md](authentication.md) | `ODM_LDAP_*`, `ODM_OIDC_*` |
| `Software:MaxPackageMegabytes` | 4096 | upload limit for PSADT packages (S3 single upload: 5 GB maximum); also the Caddy limit | `ODM_MAX_PACKAGE_MB` |
| `Software:ReconcileSeconds` | 60 | interval of the desired/actual state reconciliation | `ODM_RECONCILE_SECONDS` |
| `Software:RepoRefreshHours` | 24 | how often auto-updating repository packages are refreshed (0 = never) | `ODM_REPO_REFRESH_HOURS` |
| `OsUpdates:CheckSeconds` | 60 | how often maintenance windows are evaluated | `ODM_OS_UPDATE_CHECK_SECONDS` |
| `Agents:HeartbeatSeconds` | 60 | heartbeat interval handed to agents (minimum 10) | `ODM_AGENT_HEARTBEAT_SECONDS` |
| `Agents:PublicBaseUrl` | | public address for install scripts (set it behind a reverse proxy) | `ODM_PUBLIC_URL` |
| `Agents:Update:*` | off | agent self-update policy, see [agent.md](agent.md) | `ODM_AGENT_UPDATE_*` |
| `Agents:Mtls:*` | off | agent client certificates, see [agent.md](agent.md) | `ODM_MTLS_*` |
| `Proxy:ForwardedHeaders` | `false` | take client address and scheme from `X-Forwarded-*` (reverse proxy) | `ODM_PROXY_FORWARDED_HEADERS` |
| `Proxy:TrustedProxies` | empty | addresses / CIDR ranges of trusted proxies; **required** with `Proxy:ForwardedHeaders` (the server does not start without it) | `ODM_TRUSTED_PROXY` |
| `Proxy:TrustAllProxies` | `false` | believe `X-Forwarded-*` from every peer instead; only if ODM is reachable through the proxy alone (Helm sets it when no ranges are given) | `ODM_PROXY_TRUST_ALL` |
| `Tls:CertificateFile` / `KeyFile` / `Port` | / / 8443 | HTTPS directly at ODM with PEM files (in Compose: file names in `./certs`) | `ODM_TLS_CERT`, `ODM_TLS_KEY`, `ODM_TLS_PORT` |
| `Notifications:*` | off | see [Notifications](#notifications) | `ODM_NOTIFY_*`, `ODM_SMTP_*`, `ODM_WEBHOOK_*` |
| retention settings | | see [Housekeeping and retention](#housekeeping-and-retention) | `ODM_RETENTION_*` |
| `Logging:LogLevel:Default` / `Odm` | `Information` | log level of everything / of ODM's own components | `ODM_LOG_LEVEL`, `ODM_LOG_LEVEL_ODM` |

## Kubernetes (Helm)

The chart lives in `deploy/helm/odm`. It deploys ODM as a single instance (background services and the agent CA volume
require exactly one replica), a Service, an optional Ingress, a PVC for the agent CA and, optionally, a bundled
single-node PostgreSQL. The package store (S3) is always external (AWS S3, MinIO, RustFS, ...).

```bash
docker build -t registry.example.org/odm:1.0 . && docker push registry.example.org/odm:1.0

helm install odm deploy/helm/odm -n odm --create-namespace \
  --set image.repository=registry.example.org/odm --set image.tag=1.0 \
  --set publicUrl=https://odm.example.org \
  --set ingress.enabled=true --set ingress.host=odm.example.org --set ingress.tls.secretName=odm-tls \
  --set secrets.jwtSecret="$(openssl rand -base64 48)" \
  --set secrets.secretsKey="$(openssl rand -base64 32)" \
  --set secrets.adminPassword='...' \
  --set database.connectionString='Host=pg.example.org;Database=odm;Username=odm;Password=...' \
  --set s3.serviceUrl=https://s3.example.org --set s3.accessKey=odm --set s3.secretKey='...'
```

- **Secrets:** pass them as values (the chart creates a Secret) or set `existingSecret` to a Secret you manage
  (keys: `jwtSecret`, `secretsKey`, `adminPassword`, `databaseConnectionString`, `s3SecretKey`, optional `ldapBindPassword`,
  `oidcClientSecret`). Do not keep real secrets in a values file in Git.
- **Database:** set `database.connectionString` (and `database.provider` for SQL Server), or `postgresql.enabled=true` with
  `postgresql.password` for the bundled instance (evaluation and small setups only).
- **Ingress:** the default annotations target ingress-nginx and raise the upload limit; adjust `proxy-body-size` to
  `software.maxPackageMegabytes`. With the ingress enabled the chart switches on ASP.NET Core forwarded headers
  (`Proxy__ForwardedHeaders`) so client address and scheme are taken from the proxy; restrict it with `proxy.trustedProxies`.
- **Other settings:** LDAP has its own values block; everything else (OIDC, Kestrel HTTPS, ...) goes through `extraEnv` using
  the keys of the [configuration reference](#configuration-reference).
- **mTLS** needs TLS terminated by ODM itself or an ingress that forwards the client certificate
  (`mtls.forwardedCertificateHeader`, `proxy.trustedProxies`; see [agent.md](agent.md#mtls-behind-a-reverse-proxy), untested on Kubernetes).
  Otherwise keep `mtls.mode=Off`.
- **Upgrades:** `helm upgrade` replaces the pod (strategy `Recreate`); migrations run at startup.

## SQL Server instead of PostgreSQL

ODM runs on PostgreSQL (default) or Microsoft SQL Server 2019 or newer. Select it by configuration only:

```json
"Database": {
  "Provider": "SqlServer",
  "ConnectionString": "Server=db.example.org;Database=odm;User Id=odm;Password=…;Encrypt=True"
}
```

(Environment variables: `Database__Provider`, `Database__ConnectionString`.)

- The SQL Server migration set lives in `src/Odm.Migrations.SqlServer`; see [development.md](development.md) for how
  to create it. ODM refuses to start with the SQL Server provider if it is missing.
- ODM creates the database on first start if the account may do so (`sa`, `dbcreator`). Otherwise create an empty
  database first; the account needs `db_owner` on it.
- The driver requires TLS. With a certificate from a trusted CA `Encrypt=True` is enough; add `TrustServerCertificate=True`
  only for development with self-signed certificates.
- SQL Server compares case-insensitively by default, PostgreSQL case-sensitively. ODM stores user and target names in
  lower case, so the application behaves the same on both.
- Docker: `COMPOSE_FILE=docker-compose.yml:deploy/docker-compose.mssql.yml` in `.env` starts ODM with SQL Server 2022
  (Developer edition; production needs a licence). `ODM_DB_PASSWORD` is the `sa` password there and must satisfy SQL Server
  rules (8+ characters, upper/lower case, digit, no `;`). In production use a dedicated `db_owner` account through
  `ODM_DB_CONNECTION`. Installations that used the former standalone file (`docker compose -f deploy/docker-compose.mssql.yml`)
  ran as Compose project `deploy`; set `COMPOSE_PROJECT_NAME=deploy` in `.env` to keep using those containers and volumes.
- There is no data migration between the two databases.

## TLS and reverse proxy

### HTTPS with Docker Compose

```bash
# .env: COMPOSE_FILE=docker-compose.yml:deploy/docker-compose.https.yml
#       ODM_DOMAIN=odm.example.org   (optional: ODM_CADDY_TLS, see below)
docker compose up -d --build
```

This adds a [Caddy](https://caddyserver.com) reverse proxy (`deploy/Caddyfile`) on ports 80/443 (`ODM_HTTP_PORT`,
`ODM_HTTPS_PORT`) and removes the direct ports 8080/8443. Certificates (`ODM_CADDY_TLS`):

| Value | Result |
|---|---|
| empty | certificate from Let's Encrypt, renewed automatically; needs a public DNS name and reachable ports 80 and 443 |
| `internal` | Caddy's own CA for private networks; browsers and agents must trust its root certificate (`caddy-data` volume, `pki/authorities/local/root.crt`) |
| `/certs/fullchain.pem /certs/key.pem` | your own certificate, placed in `./certs` |

The variant sets the forwarded headers (trusted: only the proxy network `ODM_PROXY_SUBNET`), the public address from
`ODM_DOMAIN` and Caddy's upload limit from `ODM_MAX_PACKAGE_MB`. **Agent client certificates (mTLS):** set
`ODM_PROXY_CADDYFILE=./deploy/Caddyfile.mtls` and `ODM_MTLS_MODE=Optional` (or `Required`); Caddy then forwards the agent
certificate to ODM in the `X-Client-Cert` header, see [agent.md](agent.md#mtls-behind-a-reverse-proxy).

### General notes

- Agents refuse plain HTTP by default. Terminate TLS either in front of ODM (reverse proxy) or in ODM itself
  (`ODM_TLS_CERT` / `ODM_TLS_KEY`: PEM files in `./certs`, served on port 8443).
- **Behind a reverse proxy:** set `Proxy:ForwardedHeaders=true` and `Proxy:TrustedProxies` to the proxy addresses (Docker
  Compose: `ODM_PROXY_FORWARDED_HEADERS=true` and `ODM_TRUSTED_PROXY=<address or range>` in `.env`, e.g. `172.18.0.0/16` for a
  proxy in a Docker network), otherwise rate limits apply per proxy instead of
  per client; set `Agents:PublicBaseUrl`; raise the proxy upload limit to match `Software:MaxPackageMegabytes`. Once all
  agents use the proxy, publish port 8080 only locally (`ODM_BIND=127.0.0.1` in `.env` when the proxy runs on the same host)
  or restrict it in the firewall, so nobody can bypass the proxy.
- **Agent client certificates (mTLS)** need either TLS terminated by ODM itself or a trusted proxy that forwards the
  certificate in a header. See [agent.md](agent.md#client-certificates-mtls).

## Notifications

ODM sends alerts by e-mail and/or webhook. Alerts are off by default; changes need a restart.

| Setting | Default | Meaning |
|---|---|---|
| `Notifications:Enabled` | `false` | master switch |
| `Notifications:CheckMinutes` | 5 | how often new alerts are collected and sent |
| `Notifications:Events` | all | list of `installation-failed`, `device-silent`, `update-run-failed`, `certificate-expiring` |
| `Notifications:SilentHours` | 24 | a device without heartbeat for this long raises `device-silent` (devices that never reported are ignored) |
| `Notifications:CertificateDays` | 7 | agent certificate expiring within this many days (agents renew themselves earlier, so this signals a problem) |
| `Notifications:Email:Host` / `Port` / `Security` | / 587 / `StartTls` | SMTP server; `Security` is `StartTls` or `None` (local relay). Implicit TLS on port 465 is not supported |
| `Notifications:Email:UserName` / `Password` | | SMTP credentials (optional) |
| `Notifications:Email:From` / `To:0`, `To:1`, ... | | sender and recipients |
| `Notifications:Webhook:Url` / `Authorization` | | JSON `POST { "text": "...", "alerts": [...] }`; optional `Authorization` header value |
| `Notifications:MaxPerMessage` | 50 | alerts per message; the rest follows in the next round |
| `Notifications:RetentionDays` | 180 | how long sent-alert records are kept |

Rules: new alerts of one round go into **one** message per channel. An alert is sent **once**: the key contains the occurrence
(installation and time of failure, last heartbeat, certificate expiry), so a new failure alerts again but the same one does
not. If every channel fails, the alerts are retried in the next round. Alerts only look at the last 7 days of failures, so
switching notifications on does not flood you with old history; silent devices and expiring certificates are reported as they are now.

Administrators see the setup, can send a test message and browse the recently sent alerts in the UI (*Notifications*, menu *System*). The same is available through `GET /api/notifications` (channels, enabled events, the last 100 alerts, no secrets) and
`POST /api/notifications/test` (sends a test message and reports the result per channel).

Docker Compose: `ODM_NOTIFY_ENABLED`, `ODM_NOTIFY_EVENTS`, `ODM_SMTP_*` and `ODM_WEBHOOK_*` in `.env` (see `.env.example`);
several recipients or events are comma-separated, e.g. `ODM_SMTP_TO=it@example.org, oncall@example.org`.

## Housekeeping and retention

A daily background job deletes old data. Finished work is removed only after its retention period; pending or running
jobs are never touched. `0` keeps the data forever.

| Setting | `.env` | Default | Deletes |
|---|---|---|---|
| `Agents:JobRetentionDays` | `ODM_RETENTION_JOBS_DAYS` | 90 | finished (succeeded / failed) agent jobs including their output |
| `Agents:TokenRetentionDays` | `ODM_RETENTION_TOKENS_DAYS` | 30 | used or expired registration tokens |
| `OsUpdates:RunRetentionDays` | `ODM_RETENTION_OS_UPDATES_DAYS` | 365 | finished OS update runs and their update history |
| `Audit:RetentionDays` | `ODM_RETENTION_AUDIT_DAYS` | 365 | audit log entries |
| `Notifications:RetentionDays` | `ODM_RETENTION_NOTIFICATIONS_DAYS` | 180 | records of sent alerts |

The current state of devices, installations and assignments is not affected; only the history (job output, run records) goes.

## Backup and restore

What to back up:

1. the database (PostgreSQL dump or SQL Server backup),
2. the package bucket (S3),
3. `ODM_SECRETS_KEY` (without it stored secrets cannot be decrypted; keep it apart from the other backups),
4. the CA directory (`odm-pki` volume / Kubernetes PVC `<release>-odm-pki`, `ca.crt` / `ca.key`) when mTLS is used. If the CA is lost, all agents must be re-registered. It contains the CA private key: protect the backup.

**Docker Compose with PostgreSQL:** the scripts do the work.

```bash
scripts/backup.sh                       # backup/<timestamp>/ with odm.dump and pki/
scripts/backup.sh /mnt/backup --with-packages   # also copies the bundled S3 store
scripts/restore.sh backup/20261031-120000       # replaces the database, CA and (if present) packages
```

Without the scripts: `docker compose exec -T db pg_dump -U odm -d odm -Fc > odm.dump` and
`docker compose exec -T db pg_restore -U odm -d odm --clean --if-exists --no-owner < odm.dump`.
Use the bucket tools of your S3 service (e.g. `mc mirror`) for an external store.

**SQL Server:** `BACKUP DATABASE odm TO DISK = '...' WITH COMPRESSION, CHECKSUM` and the matching `RESTORE DATABASE`.

**Kubernetes:** dump the database with `kubectl exec ... pg_dump` (or your operator's backup feature) and snapshot the CA
volume, or copy it with `kubectl cp`.

**Restore order:** stop ODM, restore database, bucket and CA directory, set the same `ODM_SECRETS_KEY`, start ODM (pending
migrations are applied on start). Test a restore before you need it.

## Inventory

Agents (version 0.7.0 or later) report hardware and installed programs every 6 hours (`Agent:InventoryHours` in the agent
configuration). Until an agent has updated itself no inventory is shown for its device. API: `GET /api/inventory/devices/{id}`,
`GET /api/inventory/software/missing?name=` (devices without a program), `GET /api/software/compliance?reason=` (deviations from the assignments; each report also as `.csv`),
`GET /api/inventory/software/summary?name=`, `GET /api/inventory/software?name=&version=` and `.../software.csv`.

## Audit log

Administrators see the audit log in the UI (*Audit log*, menu *System*) or via `GET /api/audit` (filters `from`, `to`, `actor`,
`outcome`, `text`; at most 500 entries per request). It records:

- every state-changing API request (POST, PUT, PATCH, DELETE) with time, user and role, route, affected path, outcome
  (`success`, `failed`, `denied`, `error`) and client address,
- sign-ins and failed sign-ins (local, LDAP, OIDC), agent registrations and certificate requests.

Request bodies are never stored, so passwords, tokens and secret values do not appear. Agent traffic such as heartbeats is
not recorded. The application offers no way to change or delete single entries; the retention job removes old ones
(`Audit:RetentionDays`). For tamper resistance beyond that, restrict database access or ship the table to a log system.

## Version

Administrators see the server version in the top bar of the UI (tooltip: .NET runtime and database provider); the same data is available at
`GET /api/system/info`. The version is set in `src/Odm.Host/Odm.Host.csproj` (`<Version>`). Agents report their own version, shown on the device page.

## Security notes

- Passwords: PBKDF2-HMAC-SHA256 (600 000 iterations, random salt); sign-in is rate limited (10 attempts per minute and IP,
  and 10 failures per 15 minutes and account) with uniform response time for unknown users.
- Sessions of local users end at once when the user is deactivated, demoted or deleted or the password changes (security
  stamp checked on every request); changing the own password ends all other sessions. LDAP and OIDC sessions end when the
  token expires (`Jwt:ExpiryMinutes`).
- Agent registration: token (single-use by default, optionally reusable for provisioning; stored as SHA-256 hash, limited validity, use counter consumed atomically), rate limited.
- Agent operation: random 256-bit secret per agent, stored as a hash on the server; an operator-allowed reinstall revokes the
  old credentials. A registration never takes over an existing device without that permission (see [agent.md](agent.md)).
- Every response carries a Content-Security-Policy (resources only from the server itself, no framing), `nosniff`,
  `Referrer-Policy: no-referrer` and, over HTTPS, HSTS.
- The web UI keeps its token in `sessionStorage` (per tab).
- Secrets are stored encrypted (AES-256-GCM) and never shown again; see [software-distribution.md](software-distribution.md#secrets).
