# DevHub security review — 30 October 2026

This review covers the available API and frontend source, dependency advisories, the production Nginx configuration, public response headers, and the deployment process. Reproduction and rejection tests used local applications and disposable SQL databases. Production checks were limited to configuration inspection and ordinary availability requests; no production flood or load attack was performed.

**Status: deployed and verified.** The initial security rollout was release `20261030102346-222b95c3`, including its final managed-assembly corrections. The five pending migrations completed, and a verified encrypted pre-migration BACPAC was retained on the server. Production frontend, API, origin checks, input limits, and a database-backed invalid-login check passed. The subsequent [workshop limits rollout](workshop-limits-2026-10-30.md), release `20261030133610-3879df12`, is now live and records the latest verification and quota settings.

## Findings and fixes

| Finding | Assessment | Change |
| --- | --- | --- |
| Login, registration, refresh, and most API traffic had no request budgets. | High: credential attempts and repeated database operations could monopolize resources. | Added per-IP authentication limits, API read/write budgets, bounded in-flight requests, creation/invitation limits, and layered Nginx limits. |
| Realtime messages bypass HTTP rate limits after a socket opens. | High: an authenticated client could repeatedly invoke chat or database-backed collaboration methods. | Added per-user message budgets shared across sockets, a 20-connection user limit, a 200-upgraded-connection server limit, concurrent-work limits of four per user and 16 across the server, and automatic closure at authentication expiry. |
| Large request and upload work could run concurrently without a shared upload budget. | High resource-exhaustion risk on the small production VPS. | Added a 1 MB default API body limit, retained explicit document/upload allowances, capped authentication bodies at 16 KB, and limited parallel multipart requests to two. |
| Authentication cookies lasted as long as refresh tokens and renewed automatically. | High impact if a cookie was copied: refresh-family revocation did not bound that independent cookie to the access-token lifetime. | Authentication cookies now expire with access tokens, do not slide, and reject legacy tickets older than the configured access lifetime. Renewal uses the existing rotating refresh token. |
| Origin checks were absent for unsafe API methods and realtime handshakes. | Defense gap; existing SameSite cookies and CORS already provided partial protection. This review did not establish a working cross-site exploit against a real session. | Added explicit configured-origin checks, rejection of cross-site Fetch Metadata without Origin, and API no-store responses. Non-browser clients may omit Origin. |
| Unknown accounts skipped password-hash work. | Medium: coarse timing differences could reveal account existence. | Missing/passwordless accounts now perform a dummy verification, and login input lengths are bounded. Signup conflict responses still reveal whether an address is registered. |
| Production had no CSP, frame restriction, HSTS, or proxy abuse limits; query strings were logged. | Browser and operational defense gaps. | Added CSP, frame denial, HSTS, Permissions-Policy, redacted access logs, request/connection budgets, timeouts, hidden-file rejection, and hidden server version details. Moved the theme bootstrap to an external same-origin script. |
| The development dependency graph contained three high-severity advisories. | Development tooling exposure, not a production Node server: production serves static frontend assets and a .NET API. | Updated the lockfile, including Vite 8.3.5 and systeminformation 5.33.18; the affected body-parser dependency was removed. Production npm dependencies report no known advisories. |

The Linux API release uses .NET and ASP.NET Core runtime 10.0.14. The database backup helper explicitly uses patched cryptography package 10.0.14; package auditing reports no known advisories for that helper or the API.

## Default application limits

| Traffic | Limit |
| --- | --- |
| API requests | 300/minute per IP |
| Mutating requests | 120/minute per authenticated user, or IP for anonymous traffic |
| Login | 10/minute per IP |
| Registration | 5/hour per IP |
| Refresh | 30/minute per IP |
| Organization, team, project, and conversation creation | 10/minute per user |
| Invitation creation and acceptance | 5/minute per user |
| Ordinary in-flight requests | 32; realtime endpoints are excluded from this lease |
| Multipart uploads | 2 concurrently |
| Realtime chat | Five-message burst, replenished at 30/minute per user |
| Selection updates | Burst 20, replenished each second per user; normal client publishes at most 10/second |
| Other realtime invocations | Burst 10, replenished every 10 seconds per user |
| Pending realtime work | 4 operations per user, 16 across the server |

General application budgets can be adjusted through `Security:Limits`. Nginx independently limits proxy requests to 15/second per IP with a burst allowance, login/signup to 10/minute with a burst allowance, and public submissions to 1/minute with a small burst. In-process budgets reset when the API restarts; Nginx remains an independent layer. Multiple API instances need shared/distributed account budgets. Review legitimate shared-NAT and multi-tab usage before tightening these values.

## Verification

- 40 dedicated local security checks passed: throttling and retry headers, origin rejection before side effects, request size limits, real MVC login-record validation, upload permit release, old and fresh cookies, password verification, realtime spam isolation, global and per-user work budgets, and socket permit release.
- Existing workshop permission and membership smoke checks passed.
- Project document, TODO, bug-report HTTP/SQL, and ownership migration tests passed against disposable local databases.
- 359 frontend unit tests passed.
- Eight browser checks passed under enforced CSP, including syntax highlighting and rejection of an injected inline script.
- API and frontend release builds passed. One existing Fast Refresh lint warning remains in `tests/fixtures/erd-overlap.tsx`.
- Production checks returned the expected statuses: homepage/theme script 200, anonymous/private and nonexistent-account login 401, cross-origin API/realtime 403, oversized login body 413, invalid password length 400, and hidden repository/environment paths 404. The production mobile login rendered without runtime or CSP errors.
- An isolated loopback Nginx listener accepted its permitted burst and returned 429 thereafter; a forged forwarding header did not bypass it. The corrected test explicitly isolates all temporary paths and verifies production temporary-directory ownership remains unchanged.

## Remaining risks and work

1. **Upstream DDoS protection:** DNS points directly at the VPS. Application and Nginx limits cannot absorb a network-volume attack. Put the site behind an appropriate CDN/WAF/DDoS service, then restrict direct origin access while preserving certificate renewal and administration.
2. **Distributed bot signup:** IP limits slow individual sources. Email verification, account-risk controls, and a challenge service would provide stronger protection against distributed registration and submission farms.
3. **Immediate session revocation:** Existing access tokens and copied access-lifetime cookies can remain valid until their short expiration. Immediate global logout requires a server-side session/revocation design; the refresh family is already revocable.
4. **Development CLI dependency:** Three low-severity audit entries remain in the `shadcn` → `deepmerge` development-tool chain. The suggested automatic fix downgrades shadcn to an incompatible major version. Do not run the CLI against untrusted registries while this remains unresolved; the package is not a production server dependency.
5. **Host administration:** Password SSH authentication is disabled and the API binds only to loopback under a sandboxed service account. Root SSH remains enabled and UFW is inactive. Review named administrator access, cloud firewall policy, OS updates, and monitoring separately.
6. **Files and recovery:** Attachment limits, filename sanitization, content validation, and forced downloads are present. Malware scanning and periodic recovery drills would strengthen these controls.

No confirmed SQL injection or stored-script execution was found in the reviewed EF and Markdown paths. That is a scoped review result, not proof that every vulnerability has been found.

## Deployment and recovery

Proxy configuration is in `deploy/security` at the workspace root. Its installer validates Nginx before reload and retains the previous files under `/var/backups/devhub/security-*`.

The release process can export a BACPAC before migrations while the API is stopped, validate the archive, encrypt it with AES-GCM, verify decryption, and retain only the authenticated ciphertext. Backups are root-only under `/var/backups/devhub/<release>/`; the key is root-only at `/etc/devhub/backup-aes.key`. Preserve that key in the operator's secure backup process. The helper's `decrypt <input> <new-output>` mode produces a private BACPAC for an explicitly planned restore; normal deployment never restores data automatically.

Application deployment preserves production configuration and data-protection keys and retains previous binaries and frontend files. File rollback does not automatically reverse database migrations. The pending migrations for this release are additive except for normalization of owner flags/owner-role assignments to the recorded canonical owner, which was tested in a disposable database.

The initial activation failed a service-account execution check and rolled back. The deployment script now assigns explicit root-owned, group-readable permissions and executable mode 750 to the app host, and checks service-account access before startup. The subsequent activation and managed updates passed. Buffered-request directory ownership was restored after a test-isolation error; the proxy test now confines all five Nginx temporary paths to its own directory. Request-size exceptions retain their 4xx status through the exception handler, and login validation is attached to record constructor parameters as required by MVC.

The production SQL connection timeout was raised from 30 to 90 seconds after a cold/post-login connection timeout was observed. Other configuration values and ownership were preserved, with a root-only settings backup retained. The final database-backed invalid-login check returned 401. This does not replace database-capacity monitoring or upstream availability protection.

## References

- [ASP.NET Core rate limiting and DDoS limitations](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [Kestrel security considerations](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/security-considerations?view=aspnetcore-10.0)
- [Nginx request limits](https://nginx.org/en/docs/http/ngx_http_limit_req_module.html)
- [Cryptography denial-of-service advisory](https://github.com/advisories/GHSA-846f-f764-76mx)
