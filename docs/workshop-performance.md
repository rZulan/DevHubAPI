# Workshop performance and scalability review

Reviewed and optimized 2026-09-09. Existing unrelated edits were preserved. This is a code and local functional review, not a production capacity certification.

## Changes implemented

| Area | Previous cost / behavior | Updated behavior |
| --- | --- | --- |
| Connections | One workshop socket per organization plus another for the open workshop | One shared workshop socket per signed-in browser tab, joining all organization groups |
| Status and token changes | Recreated sockets, repeated joins and snapshots | Update status over the existing socket; token factory reads the current token; unchanged statuses are suppressed |
| HTTP presence | Fallback could become stuck after a successful connection followed by a failed join | Join failures stop the transport before retry; jittered exponential retry, sequential fallback requests, timeout and cancellation |
| Authentication | Presence fetch bypassed the shared token-refresh path | Fallback uses the existing API client and coordinated token refresh |
| Presence rendering | Duplicate events and snapshots allocated new maps | Equal snapshots and duplicate events preserve references; events carry the organization ID |
| Fallback/live consistency | HTTP changes did not notify live clients; expired fallback users could stay online indefinitely | Broadcast only changed public statuses; sweep expired leases every 30 seconds; reclaim status entries; transfer fallback lease on reconnect |
| Server contention | One lock across every organization | 64 fixed lock shards; disconnect releases every organization joined by that socket |
| Presence authorization | Loaded members, role assignments, roles, teams and team members | Indexed membership existence query; authorization remains checked on every join, status request and heartbeat |
| Organization selector | Loaded organization child collections just to count them | Database projection returns organization summaries and counts |
| Dashboard reads | Loaded the whole organization aggregate | Authorized, untracked organization read without child collections |
| Member reads | Loaded teams and repeatedly scanned membership for each user | Membership-only read and dictionary lookup for response mapping |
| Dashboard rendering | Rebuilt member data and widget placement on clock updates, dialog changes and presence changes | Reuses layout data; memoizes widget normalization and placement |
| Member sidebar | Scanned every member for every role; filtered and sorted all roles for each member | Group members in one pass; choose primary role using an ID map |
| Organization lookup | Linear search returned a new object on every layout render | Memoized ID map returns stable organization references |
| Initial JavaScript | Workshop layout and provider were eager route imports | Both layouts load when their routes are used |

RTK Query already shares identical requests between mounted subscribers. Removing the dashboard's extra subscriptions chiefly removes duplicate transformation work; it is not claimed as a reduction from two network requests to one.

## What belongs on each transport

- **SignalR:** presence status transitions, offline notifications, and small dashboard-publication notifications. Use a snapshot when joining or reconnecting, then apply deltas. Dashboard events invalidate the organization's cached REST response; publication bursts are coalesced.
- **Heartbeat:** temporary HTTP presence renewal and snapshots only while the workshop connection is unavailable. Normal interval is randomized between 40 and 50 seconds after each fallback round; each request has a 10-second timeout. No recurring application heartbeat runs on a healthy socket. Transport-level SignalR keepalives still operate.
- **REST and the query cache:** authoritative reads and durable mutations for organizations, membership, roles, teams, projects, TODOs and dashboards. Fallback refreshes subscribed dashboard data while the page is visible; reconnect also refreshes it to recover missed notifications.
- **Future collaborative updates:** publish scoped entity/version invalidations after committed changes. Do not send full project/member/task lists or broadcast unchanged data on every heartbeat. Do not broadcast project-private changes to a whole organization without checking project visibility.

## Remaining scaling limits

1. **Multiple API instances are not supported by the in-memory presence and subscription stores.** A SignalR Redis backplane or Azure SignalR handles message delivery, but it does not distribute WorkshopPresenceTracker or WorkshopSubscriptions state. Before scaling horizontally, use shared per-organization/user/connection leases with expiry and atomic status changes, and propagate subscription revocation to every instance, plus the appropriate SignalR backplane/service. Verify load-balancer affinity for the selected transport. No distributed infrastructure was provisioned in this change.
2. **Large organizations still return complete lists.** Add cursor pagination and server-side search for members, teams, projects and TODO/task lists, and use windowed list rendering. The TODO list currently includes every nested task. Preserve aggregate counts and selection behavior when replacing these contracts; silently truncating arrays would break the existing UI.
3. **Presence fanout and snapshots remain proportional to organization size.** Healthy unchanged sessions no longer broadcast, but a real status change still reaches the organization's subscribers. Joining and fallback still return an active-member snapshot for each organization. At high membership counts, separate global self-presence from subscribing to visible organization/member presence, and batch fallback renewals. Sequential fallback bounds concurrent traffic but long rounds can outlive leases for users in very many organizations.
4. **Some read paths still use full aggregates.** Role/team/project permission evaluation and TODO authorization still load organization membership and roles. Introduce dedicated permission projections and paged read models next; avoid caching authorization without a revocation strategy.
5. **Durable changes beyond dashboard publishing are not currently synchronized across clients.** Entity-specific invalidation events need authorization-aware groups, reconnect reconciliation and reliable post-commit delivery. Membership revocation also needs explicit removal of existing sockets from groups. Presence is ephemeral and currently best-effort; high concurrency would benefit from event versions to reject reordered status/expiry notifications.
6. **Production limits remain unmeasured.** No browser paint/profile baseline, sustained socket load, production network simulation, SQL execution-plan benchmark or multi-server failover test was performed. Static CSS remains a shared asset. Do not infer an overall latency percentage or supported user count from local smoke timings.

Microsoft references: [SignalR hosting and scaling](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0), [Redis backplane configuration](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0).

## Validation

- Client production build and lint.
- Five deterministic client lifecycle tests using the actual presence hook with controlled hooks, transport and timers: one socket/no healthy heartbeat, status/token updates without reconnect, organization isolation/duplicate suppression, fallback recovery, failed joins, membership changes and cleanup. These are not browser integration tests.
- API build with warnings treated as errors.
- Eighteen presence assertions covering multi-organization sockets, duplicate joins/disconnects, multiple tabs, invisible status, fallback delta suppression, expiry, lease transfer and concurrent cleanup. A 2,000-organization concurrent lifecycle smoke took approximately 25–30 ms locally; it does not measure socket/network/database capacity.
- Seven read-only checks against the locally configured database: member authorization/nonmember rejection, projected counts matching the original aggregate, dashboard and membership reads omitting unrelated collections, and unauthorized reads returning no data.

Run from client: `npm run build`, `npm run lint`, `node --test tests/workshop-presence.test.mjs`.

Run from api: `dotnet build DevHub.sln`, `dotnet run --project tests/DevHub.WorkshopSmoke -c Release`. Add `-- --database` from api to run the read-only database checks using local configuration.

For deployment validation, exercise 100 / 1,000 / 10,000 members, multiple organizations and tabs per user, normal traffic and reconnect storms. Record active sockets, heartbeat RPS, broadcast rate, payload bytes, database command count and p95 latency, process memory/GC, and browser render duration. Verify steady-state HTTP heartbeat traffic is zero with healthy sockets, memory returns near baseline after disconnect/expiry, and permissions remain enforced after membership changes.
