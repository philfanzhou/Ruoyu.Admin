# Deployment and operations

Managed mistake assignment is off by default; before enabling it you must configure a valid HTTPS Mistake origin and existing trusted identities, and confirm that the real Student metadata and Mistake intake have been delivered. A wrong bool / missing HTTPS / missing required trust refuses startup before any business write; turning it off does not delete upstream history. For configuration, TLS/token wiring, and rollback boundaries, see the [managed integration contract](../Integration/ManagedAssignment.md).

## Build and startup

Newly published release images contain both `linux/amd64` and `linux/arm64` under the same
tag. Docker selects the matching platform automatically, including ARM64 when Colima
runs an `aarch64` VM on Apple Silicon. Existing version tags are unchanged; use a
version published after multi-platform support was added, or a new `edge` image where
that channel is available. `docker buildx imagetools inspect <image>:<tag>` lists
the platforms included in a published tag.

The Node and .NET SDK stages run on the build host and produce static web assets and
framework-dependent assemblies. The runtime base image and OS packages follow the
target architecture. CI builds both complete images without publishing on pull
requests, and publishing waits for that check as well as the existing tests.


- Integrated image: `backend/Admin.WebApi/Dockerfile`; it builds the Vue frontend first, then copies the output into the API's `wwwroot`. Build entry point `./scripts/build.sh`, producing `ruoyu.admin:${IMAGE_TAG}` (`IMAGE_TAG` defaults to `20260502`).
- Startup script: `start.sh` at the repository root.
- Container: `ruoyu-admin`, listening on 5020 in-container, mapped to host 5020 by default (host port equals container port, matching the mapping convention of the platform's other APIs; the monorepo-era 10901 is deprecated).
- The container uses the Docker default bridge and depends on neither `ruoyu-net` nor container-name resolution; cross-host dependencies are reached through the LAN addresses in Consul KV.
- The Dockerfile build context is the **repository root**, constrained by the root `.dockerignore`.
- Publishing images: after pushing a `vX.Y.Z-rc.N` (test) or `vX.Y.Z` (final) tag, CI (`.github/workflows/ci.yml`) publishes the image to GHCR: `ghcr.io/philfanzhou/ruoyu.admin`. Image tags carry no `v` prefix (keeping the `X.Y.Z` form), and the GitHub Release title is the tag itself. A final release also moves `X.Y` and `latest`; an rc keeps only the immutable version tag. Deployment hosts can skip building from source and `docker pull ghcr.io/philfanzhou/ruoyu.admin:<version>` directly.
- Historical note: the standalone frontend image inherited from the monorepo (`frontend/Dockerfile` + `scripts/build-web.sh`, producing `ruoyu.admin.web`) was removed on 2026-09-25 — the split frontend/backend mode was never used by any deployment; the product shape is the API+SPA single container.

### Service identity

When extracted from the monorepo, the service identity changed from `Ruoyu.Study.AdminPortal` to `Ruoyu.Admin`:

| Location | Key | Impact |
|------|-----|------|
| `Program.cs` / `AdminLoggingExtensions` | fixed `service=Ruoyu.Admin` | Loki log label. **Existing Grafana queries, dashboards, and alerts that filter on `service="Ruoyu.Study.AdminPortal"` must be renamed in sync**, otherwise the new logs cannot be found |

The Loki label is fixed in code; the old `Serilog` configuration overrides have been removed and can no longer change stream labels.

### Logging pipeline and fields

`ServiceMantle.Logging 0.3.0` (final release; #66 unified all direct ServiceMantle package references to the same version) provides Console + optional Loki, sharing an Information minimum level and two Warning overrides: `Microsoft.AspNetCore` and `Microsoft.EntityFrameworkCore.Database.Command`. `Logging:LogLevel` is a pre-filter for the framework ILogger; it can suppress events further but cannot break through the pipeline floor above.

`Loki:Uri` still comes from Consul / appsettings, with a repository fallback of `http://ruoyu-loki:3100`; an empty value disables the remote sink, and a malformed absolute URI fails startup with only a fixed error code. Intranet HTTP is trusted explicitly via `AllowInsecureHttp=true`; the full logs travel in plaintext and are for trusted networks only; crossing a trust boundary requires HTTPS. No authorization header is configured. When Loki is unreachable, an asynchronous bounded retry runs while business requests keep working; the bounded queue and the shutdown flush guarantee zero loss neither. The stream label is fixed `service=Ruoyu.Admin`, and the sink adds only the low-cardinality `level`; `{service="Ruoyu.Admin"}` queries are unchanged.

| Request log field | Old pipeline | Current pipeline |
|---|---|---|
| ServiceName | `Ruoyu.Admin` | `ruoyu-admin` |
| ServiceVersion | fixed `1.0.0` | entry assembly informational version |
| InstanceId | MachineName | per-host `ruoyu-admin-<guid>` |
| CorrelationId | none | same as the response `x-correlation-id` |
| MachineName / ThreadId | auto enrich | no longer attached automatically |

Identity fields of the request scope flow into the real Console/Loki pipeline; startup logs without a request scope do not promise automatic identity fields. Structured fields are forcibly masked, and startup diagnostics still go through `StartupDiagnosticsFormatter`; message-template literals/free text have no universal secret detection, and callers must not insert credentials or personal data directly.

## Configuration loading

Configuration reaches the running Admin API through a two-level model:

- a **static layer**, assembled once at startup from appsettings files, the Consul KV snapshot, environment variables, and the command line; and
- a **database dynamic layer** (#118): a small catalog of runtime-adjustable application settings persisted in the `AppSettings` table of the `ruoyu_admin` database and resolved per use (see [Database-hosted application settings](#database-hosted-application-settings-dynamic-layer)).

### Static configuration layers

Within the static layer, each layer overrides the one above it — the earlier a value arrives, the lower its precedence:

1. **Options-class defaults in code** — not an `IConfiguration` source at all. When no key binds, the owning options class supplies the value: `StudentServiceOptions` / `MistakeServiceOptions` / `HomeworkServiceOptions` (the local-development downstream URLs), `OssAuditScheduleOptions` (02:00 local time), `OssOptions` (structural S3 defaults; credentials default to empty), `TeacherPortalOptions` / `AssistantPortalOptions` (empty URL = proxy answers 503 "not configured"), `RuoyuConsulOptions` (see the Consul paragraph below), `AdminPortalOptions` (empty allowlist).
2. **appsettings files**. The image carries `appsettings.json` and `appsettings.Development.json` (both are published with the project), but the container runs in the Production environment, so only the base file loads there. `start.sh --config-file <path>` bind-mounts a replacement read-only over `/app/appsettings.json`. Locally (`dotnet run`, environment `Development`), `appsettings.Development.json` overlays the base file. The base file holds only environment-neutral keys (`Logging`, `Loki:Uri`, `AllowedHosts`, `Database:Name`, `AdminPortal`, `AdminOidc`, `AdminWeb`).
3. **Consul KV snapshot** under `config/ruoyu` (the `KvPrefix`; every JSON object below the prefix is flattened into configuration keys — `config/ruoyu/shared.json` is where `PostgreSql:*` arrives). A successful load is cached locally; when Consul is unreachable the existing cache is used; without a cache this layer is simply absent and startup continues on the lower layers. A missing database connection then fails later at the startup database gate rather than at configuration load.
4. **Environment variables and command line — always the highest precedence.** These sources are replayed after the Consul snapshot is applied, so they override Consul no matter how Consul was reached (live, cache, or absent). They never override each other's relative order (environment variables lose to command-line arguments, the framework default).

Only development placeholder values live in `appsettings.Development.json`: the local audit-database connection string, mock OSS credentials, and localhost downstream URLs (see [LocalSetup](LocalSetup.md#backend-configuration)). Production credentials — OSS keys, `IdentityService:AppId`/`AppSecret`, `PostgreSql:Password` — come only from Consul KV or environment variables and must never be written into any `appsettings*.json`.

The Consul client itself binds from the `Consul` configuration section plus the `CONSUL_*` environment variables; no `appsettings*.json` carries a `Consul` section anymore, so the effective values are the `RuoyuConsulOptions` defaults (`host.docker.internal:8500`, prefix `config/ruoyu`, cache enabled under `./data/consul`) overridden by `CONSUL_HTTP_ADDR`, `CONSUL_HOST`, `CONSUL_PORT`, `CONSUL_KV_PREFIX`, `CONSUL_TIMEOUT_MS`, `CONSUL_RETRY_COUNT`, `CONSUL_ENABLE_CACHE`, `CONSUL_CACHE_DIR`, and `CONSUL_TOKEN`. The `127.0.0.1` / `localhost` values in the repository are placeholder defaults and **are not the address of any real deployment**; at deployment time, point `CONSUL_HTTP_ADDR` at the actual Consul address.

### Key ownership

| Key | Normally arrives from | When absent |
|-----|----------------------|-------------|
| `IdentityService:Authority` | Consul KV or environment (`start.sh` `--authority` → `IdentityService__Authority`) | required — malformed/missing fails startup (`AdminOidcSettings.Read`) |
| `IdentityService:AppId`, `IdentityService:AppSecret` | environment or a restricted env-file (`start.sh` reads `IDENTITY_APP_ID` / `IDENTITY_APP_SECRET`); secrets never belong in files | mandatory — the host refuses to start without them |
| `AdminOidc:RedirectUri`, `AdminOidc:PostLogoutRedirectUri` | environment (`start.sh` `--redirect-uri` / `--post-logout-redirect-uri` → `AdminOidc__*`) | required with the exact fixed paths; same-origin pair |
| Legacy five `AdminOidc:Enabled` / `UseSessionForAdminApi` / `UseSessionForLogout` / `UseSessionForIdentityProxy` / `UseSessionForPortalProxies` | nowhere — remove them | omission passes; only omission, the canonical `true`, or a JSON boolean `true` passes migration validation — `false`, empty, or any malformed value rejects startup in every environment |
| `AdminPortal:AdminUserIds` | Consul KV or environment | empty list — an empty allowlist yields 403 for everyone |
| `AdminWeb:AllowedOrigins` | Consul KV or environment | empty list — CORS allows any origin |
| `IdentityService:Audience`, `IdentityService:RequireHttpsMetadata` | Consul KV or environment; Development placeholders in `appsettings.Development.json` | only consulted when `Mistake:ManagedAssignmentEnabled=true` (enablement gate requires them) |
| `IdentityService:ClockSkewSeconds` | Consul KV or environment | default 30; valid 0–300; a malformed or out-of-range value fails startup |
| `PostgreSql:Host` / `Port` / `Username` / `Password` | Consul KV `config/ruoyu/shared.json` | absent → the local `ConnectionStrings:AuditDb` fallback is used |
| `Database:Name` | base `appsettings.json`; `start.sh` always injects `Database__Name` (`DB_NAME`, default `ruoyu_admin`) | `ruoyu_admin` |
| `Database:AllowCreate` | Consul KV or environment | default `false` — a verifiably missing database is refused, never silently created |
| `ConnectionStrings:AuditDb` | `appsettings.Development.json` only (local development) | absent in production — production connects via the Consul `PostgreSql:*` synthesis |
| `StudentService:Url`, `MistakeService:Url`, `HomeworkService:Url` | Consul KV or environment in production; Development placeholders locally | options-class default (`http://localhost:5005` / `:5007` / `:5009`) |
| `MistakeService:UseSessionToken` | Consul KV, environment, or Development placeholder | `false`; must parse as a boolean or startup fails |
| `Mistake:ManagedAssignmentEnabled` | Consul KV or environment | `false`; a non-boolean value fails startup |
| `TeacherPortal:Url`, `AssistantPortal:Url` | Consul KV or environment | options-class default is empty — the proxy answers 503 "not configured" |
| `Oss:InternalEndpoint` / `InternalSecure` / `AccessKey` / `SecretKey` / `BucketName` / `PublicBaseUrl` | Consul KV or environment in production; mock values in `appsettings.Development.json` | `OssOptions` structural defaults (`localhost:8333`, bucket `ruoyu-study`); credentials have **no** code default — a missing credential fails at first use |
| `OssAudit:ScheduledHour`, `OssAudit:ScheduledMinute` | any static source may override, but these are database-layer keys resolved per cycle | options-class default 02:00 local time; see the dynamic layer below |
| `Loki:Uri` | Consul KV or a mounted file | base-file fallback `http://ruoyu-loki:3100`; empty disables the remote sink |
| `Logging:LogLevel:*` | any static source | base-file Information defaults |
| `Consul:*` (client binding) | `CONSUL_*` environment variables or a Consul-provided `Consul` section | `RuoyuConsulOptions` defaults |
| `StorageReferences:*` | Consul KV or environment; default-off | see [StorageAudit](../modules/OssAudit/StorageAudit.md) |
| `Kestrel:Endpoints:*` (optional HTTPS listener) | environment or a mounted file | none — the HTTP listener on hardcoded 5020 is the only default |

Plain environment variables outside `IConfiguration` section binding: `USE_LOCAL_OSS=1` switches `IOssService` to `LocalFileOssService` (directory from `OSS_LOCAL_PATH`, default `data/oss`); `APP_TITLE` titles the SPA shell; `TZ` is set by `start.sh`.

### start.sh injection surface

`start.sh` passes configuration into the container as environment variables and mounts — never as command-line arguments beyond `--validate-auth-config`:

- always: `TZ`, `APP_TITLE` (container name), `Database__Name` (from `DB_NAME`, default `ruoyu_admin`);
- when set: `IdentityService__AppId` / `IdentityService__AppSecret` / `IdentityService__Authority` (from `IDENTITY_APP_ID` / `IDENTITY_APP_SECRET` / `--authority`), `AdminOidc__RedirectUri` / `AdminOidc__PostLogoutRedirectUri` (from `--redirect-uri` / `--post-logout-redirect-uri`), and the `CONSUL_HTTP_ADDR` / `CONSUL_TOKEN` / `CONSUL_HOST` / `CONSUL_PORT` / `CONSUL_KV_PREFIX` / `CONSUL_TIMEOUT_MS` / `CONSUL_RETRY_COUNT` / `CONSUL_ENABLE_CACHE` passthrough;
- mounts: `--env-file` (docker env-file, the recommended home for secrets), `--config-file` (read-only bind mount over `/app/appsettings.json`), `--cache-dir` (sets `CONSUL_CACHE_DIR=/app/data/consul`; the preflight mounts it read-only, normal startup read-write).

### Database-hosted application settings (dynamic layer)

Issue #118 added a database-hosted settings layer for runtime-adjustable values. Its keys form a closed catalog (`AppSettingsRegistry` in `Admin.WebApi/Services`); the first-version catalog is the storage-audit schedule — `OssAudit:ScheduledHour` (integer 0–23, default 2) and `OssAudit:ScheduledMinute` (integer 0–59, default 0), defaults inherited from `OssAuditScheduleOptions` (02:00 local time). Keys without a registry definition are never read from or written to the `AppSettings` table.

Every registered key resolves through three layers, highest first:

1. **database override** (source `db`) — the row in `AppSettings`;
2. **valid static configuration value** (source `static`) — the same-name key from any static source (Consul KV, environment, appsettings); a static value that fails the value domain is treated as absent rather than surfacing an unusable setting;
3. **options-class default** (source `default`).

Deleting the database override falls back to the lower layers. Persistence is a last-writer-wins primary-key upsert (`UpdatedAt` / `UpdatedBy` recorded); there is no optimistic locking — this is a single-administrator surface.

The write surface is the management API `api/admin/settings` (`AppSettingsController`, behind the shared admin session and CSRF boundary): `GET` lists every registered key with its effective value and source; `PUT {key}` upserts the database override (invalid values are refused with a fixed 400 message that names the key and its constraint and never echoes the value); `DELETE {key}` removes the override and returns the post-delete effective value. Unregistered keys get 404 and never create rows. The admin SPA's settings page (issue #119) is a thin consumer of this API. Because `OssAuditWorker` re-resolves the schedule through the layered service on every cycle, a change takes effect on the next scheduling computation without a restart; if the settings store is unreachable, resolution falls back to the static/default layers (a warning is logged without exception details) and the loop keeps running — the audit invariants are untouched by schedule timing.

Credentials, downstream URLs, the administrator allowlist, CORS, and authentication keys deliberately stay out of the database layer: they belong to the static layer and the security boundary, and making them runtime-adjustable would be a behavior change outside this model. The `AppSettings` table itself is part of the migration chain (`20261017060000_AddAppSettings`); its structure, upgrade, and refusal rules are documented in [migrations](../database/migrations.md).

### Authentication read-only preflight and upgrade

`dotnet Admin.WebApi.dll --validate-auth-config` first reads the same appsettings/environment/command-line and effective Consul configuration as a normal startup, then reuses the same authentication validation; success prints the fixed `RUOYU_ADMIN_AUTH_CONFIG_VALID` to stdout with exit 0, and failure prints the fixed `RUOYU_ADMIN_AUTH_CONFIG_INVALID` to stderr with exit 2. It creates no host, migration, worker, listener, or session. Reading Consul/the existing cache is allowed; the preflight must not refresh or rewrite the cache, and configuration values never enter the output. Normal startup still caches the successfully obtained Consul configuration snapshot under the existing rules; when Consul is unreachable the existing cache is used, and without a cache the appsettings are used; environment variables and command-line configuration always override the snapshot.

First the deployment role obtains the existing registration code and secret, registers Code+PKCE and Logout with the exact URIs, confirms normal CA/SAN and same-origin HTTPS to the BFF; remove the five legacy keys and only then replace, after passing the preflight with the target integrated image. `start.sh` uses `--image` (or `IMAGE_NAME`) to select the concrete version and `--container` to select the exact instance; `--authority`, `--redirect-uri`, and `--post-logout-redirect-uri` supply the external URIs. Credentials are injected via the environment or a restricted `--env-file`; `--config-file` can mount an explicit appsettings file; `--cache-dir` can be omitted — when provided it points at an existing cache directory that the preflight mounts read-only while normal startup writes the original cache in the same directory. The script supports the system Bash 3.2, including startup without a cache. A missing directory or invalid path fails immediately; no directory is created. Before any old container is stopped/deleted, the preflight runs with the same target image and the same configuration set; on failure the original container is kept. The old container must carry this launcher's label or the original Ruoyu.Admin product image identity, to prevent accidentally swapping another application; on success only the explicit instance is replaced and 5020 is kept. This authentication preflight proves nothing about DB, downstream, network, or production platform registration readiness.

```bash
./start.sh --image ghcr.io/philfanzhou/ruoyu.admin:<version> --container ruoyu-admin \
  --env-file /private/admin.env --cache-dir /private/admin-consul-cache \
  --authority https://identity.example.com \
  --redirect-uri https://admin.example.com/api/auth/oidc/callback \
  --post-logout-redirect-uri https://admin.example.com/api/auth/oidc/logout/return
```

Replacement/restart invalidates existing in-memory tickets and does not restore revoked ones; rollback must use a previously explicitly working complete integrated image with its matching configuration — you cannot fall back to an identity mode with the old keys enabled. The real fixed head/image normal-TLS combination acceptance of #75 does not replace the production cutover; production rollout and shared platform registration are out of scope for the implementation PR.

### SignaCore hosted login (the only mode)

The current version runs only the server-session hosted login. You must configure `IdentityService:Authority/AppId/AppSecret`, `AdminOidc:RedirectUri/PostLogoutRedirectUri`, the current administrator allowlist and the ADMIN application audience, and complete real downstream trust plus exact Code/Logout registration. The five legacy keys `AdminOidc:Enabled/UseSessionForAdminApi/UseSessionForLogout/UseSessionForIdentityProxy/UseSessionForPortalProxies` no longer select the run mode: remove them; only omission or the canonical lowercase `true` passes migration validation, and an explicit `false`, empty, or malformed value is rejected at startup in every environment. The password entry point is permanently `410 legacy_login_disabled`; API/images/the three proxies/association queries always use the same administrator session and CSRF boundary; any inbound Authorization gets a fixed 401; there is no capability switch and no browser-JWT fallback mode.

Required: `IdentityService:Authority/AppId/AppSecret` and `AdminOidc:RedirectUri/PostLogoutRedirectUri`, with the exact registration `https://admin.example.com/api/auth/oidc/callback` (placeholder example). The URI must be byte-for-byte identical to the configuration, with a fixed path and no query/fragment/userinfo/wildcard; localhost is not allowed. Transport security is a deployment decision (issue #94, aligned with SignaCore #566): at the code level http and https are accepted equally, in any environment name (including Production) and any host form (numeric loopback, private/public IP, or domain) without differentiation, keeping only the structural URI rules; the same semantics apply to the login/logout callbacks, `IdentityService:Authority`, and `MistakeService:Url` when `MistakeService:UseSessionToken` is enabled. Public deployments should use TLS (reverse proxy / TLS termination / network segmentation — a recommendation, not an obligation, owned by the deployment side); rollback = changing the HTTPS URI configuration back and restarting, with no migration and no state transition, and switching the Cookie Secure derivation requires re-login. A 0.1.15 deployment that still carries the old intranet HTTP origin list keys can simply delete them: leftover keys are not read and do not block startup. SignaCore Host 0.1.16 removed the HTTP origin gate as well; earlier Host versions must admit this service's HTTP redirect origin under their own Testing gate (a deployment-side fact). After stripping the trailing `/`, the Authority must be exactly equal to the Discovery issuer; OIDC does not relax trust with JWT AdditionalValidIssuers. Wrong configuration fails startup, public output is a fixed safe code, and the configuration validation internals use key names only.

Register per [SignaCore HostedLogin](https://github.com/philfanzhou/SignaCore/blob/52c68c812335dd6a967cb90042ac91c21543d387/docs/integrations/HostedLogin.md) as Confidential, with PerApplication audience, the exact Redirect URI, and Code + openid profile + S256, without enabling refresh. Admin takes the authorization/token/JWKS endpoints from Discovery and sends no PAR, prompt, or other unsupported fields; redemption uses client_secret_basic (credentials go only in the Authorization header; 0.1.14+ also validates token lifetimes and future `iat` with strict defaults and zero clock skew, not relaxed by this repository), with the fixed external RedirectUri and the original verifier, and never trusts inbound Host/X-Forwarded-Host to build addresses. The Cookie Secure flag derives from the configured entry URI's actual scheme (same semantics on both sides since SignaCore.Client.AspNetCore 0.1.16): with an http entry, this repository's `adminCsrf` uses `CookieSecurePolicy.SameAsRequest` and the package's own Cookies carry no Secure; with an https entry, `Always`/Secure is kept. Public deployments should keep both the browser origin and the BFF's CSRF-handling request on HTTPS (a recommendation, not an obligation). The current service does not process arbitrary forwarded headers; an outer TLS termination that forwards plain HTTP to 5020 cannot then claim safety via `X-Forwarded-Proto`; production `CookieSecurePolicy.Always` rejects such a CSRF configuration. Keep the hardcoded HTTP 5020; you can add an internal HTTPS listener via the standard `Kestrel:Endpoints` configuration, with the proxy verifying the HTTPS upstream against the normal CA — the port contract is not changed and forwarded-header trust is not broadened.

Configuration example for a controlled HTTPS joint debug (ports and certificate paths are the deployment's choice; the password goes only into a restricted environment):

```text
Kestrel__Endpoints__AdminHttps__Url=https://0.0.0.0:5022
Kestrel__Endpoints__AdminHttps__Certificate__Path=/tls/server.pfx
Kestrel__Endpoints__AdminHttps__Certificate__Password=<private environment value>
```

A pure-intranet controlled network (no TLS termination facility, a VLAN-partitioned management plane) does not need this HTTPS listener: at the code level http and https are accepted equally; configure the URI directly as the intranet plaintext address (for example `http://192.168.55.10:5020/api/auth/oidc/callback` and the Authority `http://192.168.55.10:5002`), with no extra switch; whether to use TLS is the deployment's choice, and the complete URIs must still be registered exactly, one by one.

The proxy uses `proxy_pass https://admin:5022`, `proxy_ssl_verify on` with `proxy_ssl_trusted_certificate`, and sets `proxy_ssl_name` matching the certificate SAN. The certificate/PFX is mounted only to this instance, the CA trusts only this instance and its own browsers, with no global import and no verification skipping. The registered callback remains the browser's HTTPS same-origin address; 5020 is still kept; the HTTPS listener above does not replace the authentication-required configuration. Real joint verification must also confirm that both `/api/auth/csrf` and `/api/auth/oidc/csrf` return 200 — hosted login success must not be taken as CSRF availability.

The reverse proxy must keep the callback query (especially code) out of access logs/analytics and forward the original single-value query unchanged. Admin's protocol handler disables verbose framework logs that could contain tokens/URLs/Cookies; with OIDC enabled, the `Microsoft.AspNetCore.Hosting.Diagnostics` request logs that would record the raw query are additionally suppressed (including such logs for the other routes), the OIDC entry exports no ASP.NET Core trace; application callers still must not insert sensitive values into free text. The AppSecret goes only through environment variables/user-secrets/Consul secure configuration, never committed to configuration files.

Single-instance in-memory state lasts at most 5 minutes, with a 43-character random reference and a single atomic consumption; tickets expire absolutely after 8 hours without sliding, and all tokens stay server-side. Both stores hold at most 4096 items with per-minute reclamation; at capacity they fail fixed, they are lost on restart, and multiple replicas cannot share sessions. Data-protection keys, even if still present, do not restore lost tickets; there are no new tables/migrations. There is no refresh; prepared logout uses the existing server ID token and a dedicated logout CSRF — the exact boundaries are below. Do not treat frontend display state as proof of administrator authorization.

The server session is the only mode; illegal configuration fails at startup. A new session's administrator identity is the verified Authority's unique iss/sub plus the server stamp; an empty allowlist or current removal gives 403; inbound Authorization, missing ticket/server token, or an expired deadline gives a JSON 401. Unsafe management requests must first GET `/api/auth/csrf` and carry its separate `adminCsrf` Cookie and the single-value `X-CSRF-TOKEN`; errors are JSON 400, refused before business/OSS deletion. The CSRF Cookie is HttpOnly/Path=/, SameSite=Lax, with the Secure policy derived from the configured entry scheme (`SameAsRequest` for an http entry, Secure for an https entry); CORS is unchanged and no cross-origin credentials are added. For detailed responses see the [API contract](../api.md#admin-session-and-csrf-42).

In any configuration, password login is fixed `410 legacy_login_disabled` and no password is read/forwarded. The current SPA logout uses the dedicated logout CSRF; the server atomically revokes the local ticket first and then prepares the upstream logout; on failure/lost response it only queries local facts, never auto-replays, and never claims the upstream logout completed. Non-POST logout gives 405. Student association queries and the three proxies always use the server token boundary. The legacy keys cannot restore the browser credential path; a full-version rollback cannot resurrect revoked or lost tickets either.

Production capability activation also depends on Ruoyu.Study's application Code configuration and the downstream audience migration (IKJ8MO). This configuration document does not imply the production registration has been changed; the joint acceptance of 55 must pin the official Provider version and connect to the real three downstream — unit results from a test Authority are no substitute.

### OSS

```json
{
  "Oss": {
    "InternalEndpoint": "10.20.30.40:8333",
    "InternalSecure": false,
    "AccessKey": "<access key from Consul KV or the environment>",
    "SecretKey": "<secret key from Consul KV or the environment>",
    "BucketName": "ruoyu-study",
    "PublicBaseUrl": "https://oss.example.com/oss"
  }
}
```

- `InternalEndpoint/InternalSecure`: the real S3 connection used by the Admin backend for audit, cleanup, download, and migration assistance.
- `PublicBaseUrl`: the public signing address used by `GetPresignedUrlAsync`. When the address carries the `/oss` prefix, the public proxy removes only that deployment prefix and keeps the original bucket/key of the signed URL; appending the bucket again breaks the S3 signature. Verify real list thumbnails, previews, and original images — do not rely only on the proxy's health status.
- Browser access to `/oss/` is uniformly proxied by the User Web Nginx; the Admin Nginx maintains no SeaweedFS upstream address.
- With `USE_LOCAL_OSS=1`, `LocalFileOssService` is used, with the directory given by `OSS_LOCAL_PATH`, default `data/oss`.

### Downstream services

#### Mistake outbound session (IKJNXA)

`MistakeService:UseSessionToken` defaults to `false`; an explicit value must parse as a boolean. It selects only Admin's outbound transport to Mistake; the inbound management API permanently uses AdminSession. `false` keeps the original anonymous downstream, DTO, and exception-compatible behavior and does not restore the browser Bearer.

Before enabling, configure `MistakeService:Url=https://mistake.example.com` — only an explicit root API origin, with no userinfo, query, fragment, or subpath. Transport security is a deployment decision (issue #94): http and https are accepted equally (structural rules unchanged) while localhost is forbidden; public deployments should use HTTPS with a normal CA/SAN (a recommendation, not enforced), with no implicit fallback. The read-only `--validate-auth-config` validates these two keys together with normal startup; `start.sh` preflights with the same configuration set on the target image before stopping the old instance, refreshing no cache and creating no directories. The actual CLI name is kept; no `--validate-admin-oidc-config` alias is added.

`true` uses the dedicated handler for all 14 existing typed methods: every send re-authenticates from the current `HttpContext.RequestServices` and re-reads the server ticket, validating issuer/sub/stamp, the current allowlist, the token, and strict deadlines, including ticket rotation/revocation after the same-request framework authentication was cached. It sends only the current server Bearer to the configured origin's existing method/paths; Cookies, CSRF, inbound Authorization, AppSecret, ID/refresh tokens, or passwords are never forwarded. Without a current request, nothing is sent. The transport disables redirects, Cookie containers, client loggers, and outbound telemetry, and never attaches the Bearer to S3 or the browser.

Downstream 401/403/3xx, protocol/JSON errors, TLS/network/timeout, and unknown write results are uniformly a safe 502; legitimate business failures are safe 400/409, and a real detail-GET 404 and legitimate empty lists are preserved. Caller cancellation propagates directly instead of becoming a plain 500; each unsafe method sends the application at most once, with no refresh, retry, replay, or anonymous fallback. After failure/cancellation, no later Student or S3 writes happen, but an already-sent write's commit state cannot be proven. LegacyClean still uses `admin-legacy-cleanup`; a strict reviewer mismatch fails and must not lead to changing the reviewer or relaxing the SourceIntake/managed immutable/pins protections.

Enabling requires first verifying the official Hosted Code/PKCE, the real Mistake's exact issuer and ADMIN audience, the real Student, and normally signed image retrieval; a local stand-in is no substitute for joint acceptance. Disabling this outbound capability requires restoring the matching configuration, with the inbound mode unchanged; a full-version rollback can only use an explicitly working complete image and does not restore lost tickets or revoke committed writes.

| Dependency | Repository placeholder default | Protocol |
|------|----------|------|
| Student | `http://127.0.0.1:5005` | HTTP |
| Mistake | `http://127.0.0.1:5007` | HTTP |
| Identity | `http://127.0.0.1:5002` | HTTP |
| Teacher Portal | `http://127.0.0.1:5004` | HTTP |
| Assistant Portal | `http://127.0.0.1:5021` | HTTP |
| SeaweedFS | `Oss:InternalEndpoint` | S3 |

All values above are placeholders; real addresses always come from deployment configuration, and this repository records no real environment address.

The downstream addresses come respectively from Consul's `StudentService:Url`, `MistakeService:Url`, `IdentityService:Authority`, `TeacherPortal:Url`, and `AssistantPortal:Url`. Cross-host migration requires updating the live KV (syncing the seed KV when needed) and restarting Admin Portal before the new values load.

## Database startup gate

ServiceMantle 0.3.0's shared lifecycle gate runs before the WebHost and `OssAuditWorker.StartAsync`, fixing PostgreSQL MultiInstance, a real advisory lock, and 30-second lock-wait/preparation budgets. An existing database makes no maintenance connection and needs no CREATEDB; a missing database is refused by default, and only with `Database:AllowCreate=true` is it created with the same credentials by deriving a postgres maintenance connection and then re-checking connectivity. An invalid switch/unparsable connection is refused before the gate is registered, with no I/O; failures output safe codes only, and cancellation records no success. Unknown structures are still refused by the original `AuditMigrationExecutor`. For the full startup and the #66 error-code/receipt compatibility and rollback notes, see the [migration documentation](../database/migrations.md#66-release-compatibility-notes).

## Health checks

ServiceMantle health endpoints (added with #34; real readiness evidence sources since #54; anonymously reachable and unaffected by the SPA fallback):

```bash
curl -s http://localhost:5020/health/live
# 200 {"status":"live"}
curl -s http://localhost:5020/health/ready
# 200 {"status":"ready","phase":"completed","migrationStatus":"succeeded","databaseStatus":"reachable","errorCode":null}
```

- **`/health/live`**: always 200 while the process runs, reading no state; for container liveness probes. The response carries `x-correlation-id`.
- **`/health/ready` and the alias `/health` (usable for readiness probes and traffic gating since #54)**: ready = this process's shared `StartupDatabaseGate` succeeded (the singleton `StartupDatabaseReceipt` is Succeeded) **and** one 3-second bounded read-only AuditDb probe (a zero-row `SELECT` against the EF-mapped tables; reads no rows, runs no DDL) succeeded. Healthy returns the 200 above; failure gives 503 + a fixed safe `errorCode`: not started/running `ruoyu-admin.startup_incomplete`, startup failed `ruoyu-admin.startup_failed`, database unreachable `ruoyu-admin.database_unreachable`, mapped tables/columns unreadable `ruoyu-admin.schema_unavailable`, probe timeout `health.probe_timeout`, probe failure `health.probe_failed`. Responses and logs contain no connection strings, hosts, or exception text; NotStarted/Running/Failed mean zero database access, and only Succeeded uses the scoped EF Core snapshot. The probe proves only that mapped table columns are readable, not constraints, indexes, or data correctness. Every request re-samples; once the database recovers, the next request is ready again.
- **Downstream services (Student/Mistake/Homework/Identity/proxy targets) deliberately do not participate in readiness**, avoiding cascading removal from downstream jitter.
- Endpoints are anonymous: the library-mapped endpoints carry no authorization metadata themselves, and Admin exempts them from the global FallbackPolicy through the root route group's `AllowAnonymous` convention; `/api/*` still requires authentication (verified by the command below).

The traditional probing method still works:

```bash
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5020/
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5020/api/admin/students
```

The home page should return 200; accessing the protected Admin API without login credentials should return 401. Combine that with `docker inspect ruoyu-admin`'s running state, restart count, and startup logs to judge whether the service is stable.

## StorageReferences v1 report enablement

The dedicated keys, the three reference providers' HTTPS configuration, plus migration and fallback checks are in [StorageAudit](../modules/OssAudit/StorageAudit.md). The capability is off by default and enabled explicitly only in an isolated environment owned by the operator; upgrade the reference-providing services first, then deploy the new Admin. Startup no longer deletes old Homework review images or old bucket records; history is preserved and every `resolve` refuses deletion. Disabling the new capability must preserve the data — cleanup must not be performed by rolling back to an old Admin version that can delete objects.

## Database backup and recovery

```bash
docker exec ruoyu-postgres pg_dump -U postgres ruoyu_admin | gzip > backup_admin.sql.gz
gunzip -c backup_admin.sql.gz | docker exec -i ruoyu-postgres psql -U postgres -d ruoyu_admin
```

### Prepared logout (#46; owned by the SignaCore package since #83)

Logout always uses prepared logout, with the package-owned endpoint `POST /api/auth/oidc/logout`. The required `AdminOidc:PostLogoutRedirectUri` is same-origin with the RedirectUri, with the fixed path `/api/auth/oidc/logout/return` and no query/fragment/userinfo; the SignaCore application registration's PostLogout callback must be changed to this value by the maintainer. http and https are accepted equally (structural rules unchanged); public deployments should use HTTPS (a recommendation, not enforced). Non-POST logout is a fixed 405, and every POST attempt clears the old `adminAuthToken` Cookie.

The SPA first GETs `/api/auth/oidc/csrf` (publicly issuing `{"token":"..."}` and rotating the shared `adminCsrf` Cookie), then submits `__RequestVerificationToken` via a navigational form POST (or a single-value `X-CSRF-TOKEN` header). A missing/wrong token is a fixed `400 {"outcome":"csrf_rejected"}` without touching the session; logout is never blocked by access-token expiry or the administrator allowlist. Inside a gate serialized per session key, the package first removes the ticket and deletes the session Cookie, then sends HTTP Basic authentication + the server id_token_hint + the PostLogout URI + a one-time state to the Authority's `/oauth2/logout/requests`, without retrying or following redirects; success is a 302 to the validated same-origin logout_uri, failure/timeout/cancellation is a fixed `200 {"outcome":"local_only"}`. The return trip `/api/auth/oidc/logout/return?state=...` consumes atomically once with the binding Cookie (5 minutes); the correct case is a 302 to `/login`, everything else a fixed 400 HTML.


The success JSON `{success:true,upstreamLogout:true,logoutUrl:...}` carries only the unique URL that is same-origin or relative `/oauth2/logout?logout_handle=<43base64url>` after navigational validation; a relative URL resolves to an absolute URL same-origin with the Authority. Preparation failure, malformed/oversized response, wrong URL, timeout, cancellation, insufficient capacity, or a missing ID token all permanently keep the local logout — when the response is still writable, `{success:true,upstreamLogout:false}`; on disconnection the browser is not guaranteed to receive the result. Recovery re-queries the local state; remote success is never presumed. The overall send/read has a 10-second cap and the JSON at most 4096B; the private HTTP carries no browser Cookie/Authorization, logs, or redirects, and the protocol paths enter no telemetry.


### Token expiry and explicit reauthentication (#45)

In the only-session mode, when a valid administrator ticket hits a strict `expires_at` deadline, the business gate returns `401 {"error":"reauthentication_required"}`, with zero downstream/OSS deletion, no clearing of the server ID token, and no refresh or write replay. `GET /api/auth/session` returns `{authenticated,displayName,requiresReauthentication}`: valid token true/false; only a valid token at expiry false/true (display name kept); anonymous/revoked/8h-expired/missing token/bad deadline false/false with null display name. Non-admin 403 takes precedence; any Authorization 401. The browser only explicitly enters the controlled authorization at `/api/auth/oidc/start?returnUrl=...`; when the upstream session can be reused it returns immediately, otherwise the hosted login shows; cancel/failure keeps the existing fixed results, and the BFF never auto-challenges. Fake tests verify both return timings and before/exactly-at/after the deadline/8h plus concurrency, and cannot prove the production SignaCore timings; the real joint debugging still belongs to #41/IKJ8MO.

### Portal server-side credentials (#44)

The full Teacher/Assistant proxy prefixes always share the administrator/CSRF gate before authentication/authorization; a valid access token at expiry returns `401 reauthentication_required` with zero outbound, only the server Bearer is sent, and browser Cookie, Host, CSRF, and gateway headers plus downstream Set-Cookie are stripped. The admin/auth/other path mappings, query/body, 503/502, and downstream 401/403 are preserved; cancellation propagates without replay. Association queries share the same administrator boundary, forward only the server token, and reject before the Student/portal calls on failure. A single-sided business failure or malformed response keeps that side an empty list while the other side is normal; this aggregation guarantees neither failure visibility nor cross-portal consistency. Production rollout is not in #75's implementation scope; formal verification of the current combination uses the official Provider and real downstream. The dedicated `PortalSession_` and `AssociationsSession_` suites run on an isolated database/fake HTTP and never touch production.

### Identity proxy server-side session (#43)

The full `/api/identity` prefix (case, root, and trailing slash) passes the shared administrator and CSRF boundary before authentication/authorization. Any inbound Authorization is rejected; a valid access token at expiry returns `401 reauthentication_required` with zero outbound; only the valid in-ticket server access token is forwarded, and after stripping browser Cookie, Host, X-CSRF-TOKEN, and forged gateway headers, this service's own AppId/AppSecret is injected. Downstream Set-Cookie does not reach the browser; 401/403/structured 503 and Retry-After pass through as-is, network failure gives 502, and cancellation propagates without replay. AppSecret stripping always remains, and the three proxies share the same session boundary. Production platform registration is not performed by this item; the formal combination gate belongs to #75. The dedicated `FullyQualifiedName~IdentitySession_` tests use fake HTTP and an isolated database, with no production OSS deletion.

Identity proxy retirement of browser-credential compatibility (#73): the middleware's settings are a required dependency, and direct invocation lacking TrustedSessionKey/a valid server token is also rejected with 401. All paths map `/api` using the StartsWithSegments remainder; root/trailing slash/case/query/body are preserved; RequestAborted always applies, and pre-cancellation/cancellation mid-send propagate with no application-layer retry. The non-proxied `IdentityAccountsController` was out of scope for that change. Intermediate images are not deployed; the final must include #41/#75; rollback selects only a known-good old complete image and does not resurrect tickets.

Portal transport retirement of browser-credential compatibility (#74): the two middleware accept only a valid server token from TrustedSessionKey, settings are a required dependency, and direct invocation lacking a trusted item is also 401. The unified remainder uses the three admin/auth/other mappings, always applies RequestAborted, strips browser Authorization/Cookie/Host/CSRF/gateway, and isolates Set-Cookie; cancellation propagates with no application-layer retry. Association queries ride only with the API-session and use the server token even when the portal proxy switch is false; the current-account filter, one-side failure keeping the other side, no-association short circuit, and the Student failure response are preserved. This intermediate image is not deployed; the final must combine #41/#75; a full-version rollback does not restore this version's browser-credential path or resurrect revoked tickets.

## Internal HTTP correlation values (#85)

All direct ServiceMantle packages use the official `0.3.1-rc.1`. The existing Student, Mistake, Homework, IdentityService, TeacherPortal, AssistantPortal, StorageReferences, and the prepared-logout client with the fixed internal Authority explicitly mount the shared `AddServiceMantleCorrelationIdPropagation`; external S3, Consul, and the independent OIDC discovery/token backchannel do not mount it — there is no global policy. Destination and redirect trust remains the caller's responsibility, and the existing base addresses, timeouts, retries, logging disables, and sensitive-span filtering are unchanged.

The three proxies do not copy the raw inbound `x-correlation-id` as an explicit outbound value; the shared handler reads the middleware's private resolved slot. Normally accepted values stay the same, and rejected inputs map to the same generated value. Headers explicitly specified by the outbound call stay single-value; the Mistake session handler keeps only that non-credential metadata, continues clearing the other headers, and injects the server ticket token, with propagation sitting after the session handler. The original stripping of Authorization/Cookie/CSRF/Host/AppSecret and the downstream Set-Cookie isolation are unchanged. Pooled sends read the current context each time; without a context in the background no ID is invented; Mistake's existing no-session requests still get zero downstream rejection. The correlation value takes part in neither authentication nor idempotency.

`InstanceId.CreateRandom` keeps a per-Host-build random `ruoyu-admin-<Guid:N>`, stable within the same Host, writing no bootstrap or other state. No new configuration/data migration is needed; rollback deploys the old code and the matching same-version packages, and no database Down runs.
