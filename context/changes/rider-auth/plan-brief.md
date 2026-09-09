# S-05 — Rider auth + anonymous generation quota — Plan Brief

> Full plan: `context/changes/rider-auth/plan.md`

## What & Why

Riders can create an account and log in (FR-008), and anonymous riders get a server-enforced quota of
two generations per hour. Auth on its own is a nice-to-have nobody asks for; the quota is what makes
an account worth creating, so the two ship together. The backend verifies Supabase tokens
independently, which means S-06 (`save-route`) inherits a trust boundary that has been proven rather
than assumed.

## Starting Point

Auth is absent on both sides: no middleware, DbContext, or migrations in the backend, and no auth or
persistent-storage packages in the Expo app. `POST /route/generate` is open and unlimited.
`src/api/client.ts` centralises transport and error normalization but has no way to attach headers
per request. The backend was chosen with auth in mind — `tech-stack.md` names Supabase — but
`infrastructure.md:159` states the provider was never actually evaluated.

## Desired End State

A rider signs up on the Account tab and is signed in immediately; relaunching the app keeps them
signed in; the screen shows an identity the backend confirmed, not just one the client believes.
Signed out, they get two route generations per hour, and the third attempt explains the limit and
points at sign-in. Signed in, there is no limit. Anonymous generation otherwise behaves exactly as
it does today.

## Key Decisions Made

| Decision | Choice | Why (1 sentence) |
| --- | --- | --- |
| Auth provider | Supabase Auth | Keeps the database out of this slice entirely and matches the provider both foundation docs already assume. |
| Slice scope | Client auth + backend verification + one protected endpoint | Proves the trust boundary here rather than letting S-06 discover it is broken on top of persistence work. |
| Sign-in methods | Email + password, email confirmation off | No deep links, redirect URLs, or email templates — the whole flow is two screens. |
| Session storage | AsyncStorage | The current Supabase Expo quickstart adapter; the encrypted alternative is a hand-maintained AES class guarding a list of motorcycle routes. |
| Entry point | Third "Account" tab | Leaves the Plan screen untouched, so anonymous generation is provably unaffected, and gives S-06/S-07 a home. |
| Token verification | Asymmetric keys + JWKS | No shared secret ever reaches Railway, which makes `infrastructure.md`'s H/M secret-drift risk structurally impossible instead of merely mitigated. |
| Error surfacing | Reuse `ApiError` + a Supabase error mapper | Adds no new error vocabulary for one endpoint and follows the `planErrorMessage` pattern already in the codebase. |
| Test depth | xUnit on the auth boundary + manual client smoke | Automates the boundary that fails silently, without reversing `test-plan.md`'s deliberate no-frontend-runner decision. |
| Quota identity | Installation UUID header, IP as fallback | No collisions between riders sharing a carrier IP; the fallback stops a missing header from being a free pass. |
| Quota counters | In-memory, fixed 1-hour window | Zero infrastructure and zero cost, versus a database that would undo the main reason Supabase Auth was chosen. |
| Authenticated limit | None; 429 invites sign-in | Turns the wall into the clearest possible argument for creating an account. |

## Scope

**In scope:** Sign-up, sign-in, sign-out, persistent sessions, an Account tab, backend JWT
validation, one protected `GET /me`, the anonymous generation quota with its 429 UX, backend tests
for both the auth boundary and the quota, and a PRD/roadmap sync for the new requirement.

**Out of scope:** Password reset, email verification, account deletion, OAuth/social sign-in, any
database or migrations, saving or listing routes (S-06/S-07), a frontend test runner, a rate limit on
`/route/stitch`, and persistent or distributed quota counters.

## Architecture / Approach

The Expo app talks to Supabase directly for sign-up and sign-in, holding the session in AsyncStorage
with `AppState`-driven token refresh. It sends the resulting JWT to the RideForge API as a bearer
token; the API validates it against Supabase's public JWKS, so no secret is shared between the two.
The quota lives in .NET's built-in partitioned rate limiter, keyed on an installation UUID the client
sends as a header (falling back to the caller's IP), and skipped entirely when the request carries a
valid token — which is why the limiter must run after the authentication middleware.

## Phases at a Glance

| Phase | What it delivers | Key risk |
| --- | --- | --- |
| 1. Supabase foundation | Configured project, supabase-js client, persistent session, session context | Project left on HS256 serves an empty JWKS and silently breaks phase 3 |
| 2. Account tab + screens | Sign-up, sign-in, sign-out, mapped provider errors | Async session restore flashing a login form at signed-in riders |
| 3. Backend JWT + `/me` | JWKS validation, one protected endpoint, hermetic auth tests | OIDC discovery document may be absent, forcing a direct JWKS resolver |
| 4. Client ↔ backend wiring | Bearer token on requests, backend-verified identity on screen | Stale `/me` cache leaking one rider's identity into the next session |
| 5. Generation quota | Install-id header, partitioned limiter, 429 UX, quota tests, doc sync | Middleware ordering — a limiter before auth silently limits signed-in riders too |

**Prerequisites:** F-01 (done), an EAS dev build (Expo Go cannot run this app), a Supabase account,
and Railway access to set `Supabase__ProjectUrl`.
**Estimated effort:** ~4–5 after-hours sessions; phases 1–2 and 3 are independently verifiable.

## Open Risks & Assumptions

- The installation identifier is client-controlled — clearing app data resets the allowance. A speed
  bump, not a security control, and accepted as such.
- The IP fallback collides under carrier NAT, so riders behind one mobile egress IP could share an
  allowance; it applies only to requests missing a valid header.
- In-memory counters reset on every redeploy, so the limit is looser than 2/hour during development.
- `POST /route/stitch` remains unlimited despite also being a billed provider call — out of scope
  here, worth closing before any public launch.
- The quota is not in the PRD or roadmap. Phase 5 syncs both, but the requirement originated in
  planning rather than upstream, so it deserves a second look before it hardens.

## Success Criteria (Summary)

- A rider signs up, closes the app, reopens it, and is still signed in — with the backend, not just
  the client, confirming who they are.
- A signed-out rider gets two generations per hour and a third attempt that explains the limit and
  offers sign-in; a signed-in rider is never limited.
- Generation still works without an account, exactly as US-01 requires.
