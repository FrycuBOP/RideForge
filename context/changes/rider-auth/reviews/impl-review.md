<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: S-05 — Rider auth + anonymous generation quota

- **Plan**: `context/changes/rider-auth/plan.md`
- **Scope**: Full plan — Phases 1–5 of 5
- **Date**: 2026-09-10
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 4 warnings, 5 observations

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | WARNING |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

All 30 "Changes Required" items across the five phases were verified against the code and match
their stated Contract. The "What We're NOT Doing" list is clean: no password reset, no OAuth, no
DbContext/EF/migrations, no route saving, no frontend test runner, no limiter on `/route/stitch`,
and the generator and Plan-screen inputs are untouched.

Automated criteria re-run at review time: `npx tsc --noEmit` PASS, `npm run lint` PASS,
`dotnet test api/RideForgeApi.slnx` 67/67 PASS.

Three changes shipped that no phase lists: `auth: true` on `generateRoute`
(`src/api/route.ts`), the extracted `RideForgeApiFactory` test base plus the rewrite of
`RouteGenerateEndpointTests`, and `KnownIPNetworks.Clear()` / `KnownProxies.Clear()` in the
forwarded-headers config. All three are load-bearing rather than scope creep, but none is recorded
in the plan — hence the Scope Discipline warning.

Not a finding: `roadmap.md` still shows S-05 as `in-progress`. That is correct — flipping it to
`done` belongs to `/10x-archive`, and this change is not archived yet.

## Findings

### F1 — Rotating the install id both defeats the quota and grows the limiter dictionary

- **Severity**: ⚠️ WARNING
- **Impact**: 🔬 HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: api/Program.cs:315-326, api/Program.cs:175-184
- **Detail**: `AnonymousPartitionKey` returns `install:{guid}` whenever the header parses as a GUID,
  so a caller sending a fresh random GUID per request receives a brand-new 2-permit partition every
  time — unmetered billed provider calls, and the IP fallback never engages. This session's own
  5.8 verification is the proof: two freshly minted GUIDs each got `200, 200, 429`. The same
  behaviour that makes the quota per-rider is what makes it bypassable.

  The plan's Open Risks does accept the client-controlled identifier as "a speed bump, not a
  security control", so the *behaviour* is per-plan. Two things are not:

  1. The XML comment at Program.cs:315-326 claims the GUID parse means "the cheapest way to defeat
     the quota is also the most annoying one". That is false — a well-formed random GUID is both
     the cheapest bypass and never reaches the IP fallback. The comment overstates the protection.
  2. Memory. Each distinct key caches its own limiter; a `FixedWindowRateLimiter` that has consumed
     a permit is not evicted until its window replenishes, so with `WindowMinutes = 60` live
     entries are bounded by request-rate × window rather than by anything an operator controls.
     Unbounded client-supplied partition keys are the documented DoS shape for
     `PartitionedRateLimiter`. The plan's Open Risks does not mention this at all.
- **Fix A ⭐ Recommended**: Chain a coarse per-IP limiter alongside the per-install one (e.g. 20/hour
  per IP), so a rotating header still meets a ceiling and the dictionary stays bounded by attacker
  count rather than request count.
  - Strength: Closes the billing bypass and the memory growth with one mechanism, and finally makes
    the IP fallback do the job its own comment claims. `AddRateLimiter` already supports chaining.
  - Tradeoff: Carrier NAT means a shared egress IP shares the coarse ceiling — the plan already
    flags this collision for the existing fallback, and 20/hour is loose enough to hide it.
  - Confidence: MED — the mechanism is standard, but the right ceiling is a product guess that
    wants one real traffic sample before being fixed.
  - Blind spot: No measurement of what a legitimate busy hour looks like; the number is chosen
    blind.
- **Fix B**: Keep the behaviour, correct the comment, and add the memory-growth risk to the plan's
  Open Risks.
  - Strength: Honest about what shipped at zero code risk; the accepted design is already written
    down, only incompletely.
  - Tradeoff: Leaves a zero-cost path to unmetered ORS spend on a publicly reachable endpoint.
  - Confidence: HIGH — purely documentary.
  - Blind spot: Depends on nobody finding the endpoint before a real cap exists at the provider.
- **Decision**: FIXED via Fix B — comment at `api/Program.cs` corrected to state that the GUID parse
  does not defend against a rotating well-formed GUID; both the unreachable-escalation-path
  correction and the partition-dictionary growth added to the plan's Open Risks.

  Considered and rejected during triage: deriving the install id from a device identifier
  (`expo-application`'s `getAndroidId` / `getIosIdForVendorAsync`) hashed with the app name. It does
  not close the bypass — the value still travels in a client-sent header, so an attacker sends any
  string regardless of how an honest client derives it. It would only harden against an honest user
  clearing app data, at the cost of another native module (repeating F8's failure shape), platform
  branching, a web fallback, and shipping a durable device identifier.

  Also considered and deferred: capping anonymous route length at 50 km. The stated rationale was
  cost, which does not hold here — `RouteGenerator.WaypointCount` is a constant 8 and
  `OpenRouteServiceStitcher` makes exactly one provider call per generation regardless of distance,
  so a shorter route costs the same. The other half of the argument does hold (a 50 km route is a
  less attractive prize, and it creates a real anonymous-vs-signed-in feature difference), but that
  is a new product requirement rather than a mitigation, so it does not belong in this change.

### F2 — Two awaits in `request()` sit outside the timeout and outside error normalization

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/api/client.ts:40, src/api/client.ts:45
- **Detail**: `await getInstallId()` (storage) and `await supabase.auth.getSession()` (which performs
  a **network token refresh** when the access token has expired) both run before the
  `AbortController` and `setTimeout` are armed on lines 50-51, and before the `try` that maps
  failures through `normalizeError`. Two consequences: a request documented as having a 30s budget
  can hang indefinitely on a flaky network during refresh, and because `index.tsx` derives `busy`
  from `generate.isPending`, the Plan button stays stuck on "Planning…" until the app restarts. A
  throw from either await also escapes as a raw error rather than the `ApiError` the JSDoc promises
  and that `useMeQuery` / `useGenerateRouteMutation` declare as their error type.
- **Fix**: Move both awaits inside the `try`, after the timeout is armed, so they share the deadline
  and the `normalizeError` path.
  - Strength: Restores the single documented failure contract the whole error UI is built on.
  - Tradeoff: The timeout now also covers a token refresh, so a slow refresh consumes generation
    budget — correct, but it changes what the 30s means.
  - Confidence: HIGH — the ordering is plainly visible and the fix is local to one function.
  - Blind spot: None significant.
- **Decision**: FIXED — header preparation extracted into `buildHeaders()` and raced against the
  request deadline via a new `withDeadline()` helper, inside the `try` so failures normalize.

  Moving the awaits alone would not have been enough: neither `getInstallId` nor
  `supabase.auth.getSession` accepts an `AbortSignal`, so they would have been normalized but still
  unbounded. `withDeadline` releases the caller when the controller aborts; the underlying work is
  not cancelled and its result is dropped, which the helper's doc comment states plainly.

  Same edit also closed the abort-listener leak listed under F9: the forwarding handler is now
  named and detached in the `finally`. Verified: `tsc` and `lint` pass, and the web build's
  `/health` call — which travels the same `request()` path — still reports "backend: connected".

### F3 — The quota suite pins the limits it claims to be guarding

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: api/RideForgeApi.Tests/GenerationQuotaTests.cs:50-53
- **Detail**: The comment reads "Assert against the shipped numbers rather than test-only ones: the
  value of this suite is that it fails when someone changes the default from 2 without meaning to" —
  and the next two lines call `UseSetting("GenerationQuota:PermitLimit", "2")` and
  `UseSetting("GenerationQuota:WindowMinutes", "60")`, supplying the very values under test.
  Changing `appsettings.json` or `GenerationQuotaOptions.PermitLimit` to 20 leaves the entire suite
  green. The stated regression is not caught.
- **Fix**: Drop both `UseSetting` calls so the suite reads committed configuration.
- **Decision**: FIXED — both `UseSetting` calls removed; the comment now states why their absence is
  deliberate. Verified by mutation rather than assertion: raising `appsettings.json`'s `PermitLimit`
  to 20 turned all 6 quota cases red, and restoring it returned the full suite to 67/67 green. The
  regression the comment promised is now actually caught.

### F4 — `.env.example` points at a hostname that does not exist

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: .env.example:7
- **Detail**: `.env.example` sets `EXPO_PUBLIC_API_URL=https://rideforge-api-production.up.railway.app`
  while the code's fallback in `src/api/config.ts:10` — and the host actually serving traffic — is
  `https://rideforge-production.up.railway.app`. A developer copying the example verbatim replaces a
  working default with a dead host, and because `EXPO_PUBLIC_*` is inlined at build time the failure
  surfaces as an opaque network error rather than a config error.
- **Fix**: Correct the hostname in `.env.example` to match `src/api/config.ts`.
- **Decision**: FIXED — hostname corrected to `rideforge-production.up.railway.app`; verified it
  matches `src/api/config.ts:10` and that the host answers `200` on `/health`.

  Left for the owner, not a defect: the comment above that line says the variable is *optional* and
  is meant for pointing at a local or staging backend, so the example now duplicates the production
  fallback verbatim. Commenting the line out, or showing a localhost example instead, would match
  the comment's stated intent better.

### F5 — `signUp` ignores `data`, so a confirmation-required project fails silently

- **Severity**: 📋 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/app/(tabs)/account.tsx:72-84
- **Detail**: The handler destructures only `error`. Supabase's `signUp` returns `{ error: null }`
  with **no session** when the project requires email confirmation; the code would take the success
  path, clear the password, and render nothing — the rider sees an unchanged form and concludes the
  button is broken.

  Latent, not live: plan phase 1 item 1 requires "Confirm email" to be disabled, and criterion 2.3
  was manually verified as landing directly in the signed-in state. The defect is the silent
  coupling — a dashboard toggle nothing in the repo enforces can break sign-up with no error
  anywhere, and `auth-errors.ts:50` already handles `email_not_confirmed`, which reads as if
  confirmations were expected.
- **Fix**: Inspect `data.session` after `signUp`; when it is null, show "Check your email to confirm
  your account" instead of falling through silently.
  - Strength: Makes the screen correct under either dashboard setting instead of depending on one.
  - Tradeoff: Adds a branch for a state the project is configured never to produce.
  - Confidence: HIGH — documented supabase-js behaviour.
  - Blind spot: None significant.
- **Decision**: FIXED — `submit()` now reads `data` and, when a sign-up returns no session, sets a
  separate `notice` state ("Account created. Check your email to confirm it, then sign in.")
  rendered in secondary text rather than error red, because this is a success that happens to leave
  the rider signed out. `notice` is cleared on every submit and on sign-out, so a stale message
  cannot survive into the next visit to the form.

  Unverifiable end-to-end here: the project has confirmations disabled by design, so this branch
  cannot be exercised without changing the Supabase dashboard. It is guarded by types and inspection
  only. Comment at the call site records why the branch exists despite being unreachable in the
  intended configuration.

### F6 — `POST /route/stitch` is still an unmetered path to the same billed provider

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: api/Program.cs:232
- **Detail**: `/route/stitch` calls the same `IRouteStitcher.StitchAsync` with caller-supplied
  waypoints, carries neither `RequireAuthorization` nor `RequireRateLimiting`, and
  `GenerationQuotaTests` deliberately pins it as unlimited. No client code calls it. This is a
  documented, accepted decision — the plan's "What We're NOT Doing" and Open Risks both name it —
  so it is restated here as still-open rather than raised as new. Worth pairing with F1: the cost
  argument that justifies the quota applies verbatim to this endpoint.
- **Fix**: Either apply `.RequireRateLimiting(GenerationQuotaPolicy)` to it as well, or delete the
  endpoint until a client needs it.
- **Decision**: FIXED — `.RequireRateLimiting(GenerationQuotaPolicy)` applied to `/route/stitch`,
  deliberately sharing one allowance with `/route/generate` rather than getting its own, because it
  makes the identical billed call and no shipped client calls it.

  The old `UnlimitedEndpoints_AreNotAffectedByAnExhaustedAllowance` test pinned the opposite
  behaviour, so it was replaced by three: `/health` stays reachable when the allowance is spent
  (a capped rider must not look offline), `/route/stitch` shares the generation allowance, and
  `/route/stitch` still answers while allowance remains — the last one exists so the first would
  not pass just as happily against a broken or deleted endpoint. Suite is 69/69.

  Note this closes the last unmetered provider path, which weakens F1's practical impact: the
  bypass there now costs the same allowance on both endpoints instead of having a free one beside it.

### F7 — Test factory duplication between the two auth-aware suites

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: api/RideForgeApi.Tests/GenerationQuotaTests.cs:41-91
- **Detail**: `QuotaTestFactory` re-declares the RSA key, the `ConfigureTestServices` block that
  nulls `Authority` / `MetadataAddress` / `ConfigurationManager` and swaps `IssuerSigningKey`, a
  near-identical `CreateToken`, and the `Dispose` override — all already present in
  `MeEndpointTests.AuthTestFactory:42-107`. `RideForgeApiFactory` exists precisely to hold shared
  boot config and already owns `Issuer`, `Audience` and `InstallHeaderName`. A future change to
  token minting has to land in two places, and only one of them carries the negative-case coverage
  that would catch a mistake.
- **Fix**: Lift the signing-key swap and `CreateToken` into `RideForgeApiFactory` (or an
  `AuthenticatedApiFactory` base) and derive both suites from it.
- **Decision**: FIXED — new `AuthenticatedApiFactory` in `RideForgeApiFactory.cs` owns the RSA key,
  the hermetic `JwtBearerOptions` override and `CreateToken`. `AuthTestFactory` now adds only the
  impostor key it alone needs (down from ~65 lines to 12), and `QuotaTestFactory` is a one-line
  derivation carrying just the doc comment explaining why it supplies no quota configuration.
  Orphaned `using` directives removed from both suites. Suite green at 69/69.

### F8 — A native module in a leaf utility takes down the entire app

- **Severity**: 📋 OBSERVATION
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Architecture
- **Location**: src/lib/install-id.ts:2
- **Detail**: Observed live this session, not inferred. `import * as Crypto from 'expo-crypto'`
  throws at module-evaluation time on a binary without the native module, and `install-id.ts` sits
  in the import chain `install-id → client → api/index → use-me-query → account.tsx`, so both route
  modules lost their default export and the app failed to boot with
  `Cannot read property 'ErrorBoundary' of undefined`. The blast radius is the whole application for
  a dependency whose only job is generating one identifier that the plan and FR-013 both classify as
  a non-security speed bump.

  Resolved for now by a new dev build, so this is about the shape, not the current state: any future
  native dependency added at a similar depth reproduces it exactly.
- **Fix**: Load `expo-crypto` lazily inside `getInstallId()` behind a try/catch, falling back to a
  JS-generated UUID, so a missing native module degrades the identifier instead of the app.
  - Strength: Removes a whole class of boot failure; identical behaviour whenever the module is
    present.
  - Tradeoff: Two code paths, and the identifier's randomness quality varies silently by build.
  - Confidence: MED — the mechanism is certain; whether it is worth two paths is a judgement call
    given a rebuild also fixes it.
  - Blind spot: No audit of whether other leaf utilities carry the same shape.
- **Decision**: FIXED + ACCEPTED-AS-RULE: "A native module in a leaf utility takes the whole app
  down with it" — recorded in `context/foundation/lessons.md` (file created; it did not exist).

  Code fix applied too: `expo-crypto` is now `require`d lazily inside a local `randomUUID()` helper
  behind a try/catch, falling back to a `Math.random`-based v4 UUID. The doc comment states plainly
  that the weaker randomness is acceptable *for this value specifically* — a partition key, where
  guessing another install's id gains an attacker nothing but a share of a spent allowance — and
  warns against reusing the helper for anything that must be unpredictable.

  Verified on the web build: clearing the stored id and reloading produced a fresh, well-formed v4
  UUID and `/health` still reported connected, so the lazy `require` resolves to the real
  implementation rather than silently landing in the fallback.

### F9 — Smaller hardening and comment-accuracy items

- **Severity**: 📋 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: several
- **Detail**: Consolidated; none individually worth a slot.
  - `api/Program.cs:105-118` — `TokenValidationParameters.ValidAlgorithms` is unset. Not reachable
    as algorithm confusion (IdentityModel refuses an HMAC algorithm against an RSA key, and a legacy
    Supabase project serves an empty JWKS rather than the HS256 secret), so this is hardening:
    setting `["RS256", "ES256"]` makes the intent explicit.
  - `api/Program.cs:20-24` — the CORS comment still says "Scope this to the real web origin(s) once
    auth/cookies land (FR-008)". Auth has landed; bearer tokens are not CORS credentials, so the
    policy is still sound. The comment should record that rather than leaving a reader unsure.
  - `src/api/client.ts:54-57` — the `abort` listener is removed only if it fires; a reused
    long-lived signal accumulates listeners. Latent, since React Query passes a per-request signal.
  - `src/lib/install-id.ts:39` — `cached ??= resolveInstallId()` memoises a rejected promise
    permanently, so a single failure would fail every later API call for the process lifetime.
  - `src/components/session-provider.tsx:34-39` — the `cancelled` flag guards unmount, not ordering,
    but its comment claims it prevents clobbering a fresher session from `onAuthStateChange`. The
    code is low risk; the comment is wrong.
  - `api/Program.cs:115-117` — `IssuerSigningKeyResolver` blocks a thread-pool thread on
    `GetAwaiter().GetResult()` with `CancellationToken.None` behind a default 100s HTTP timeout.
    Steady-state cost is nil, as the comment says; cold-start pathology is real but acceptable at
    MVP scale.
- **Fix**: Take them individually as convenient; none blocks anything.
- **Decision**: FIXED (all six).
  - `ValidAlgorithms = [RS256, ES256]` set, with a comment saying it is hardening rather than a hole
    being closed.
  - CORS comment rewritten to record that bearer-token auth keeps `AllowAnyOrigin` valid, and why —
    nothing is sent automatically to a hostile origin without the rider's own token — with the
    revisit condition narrowed to "only if cookie auth is ever introduced".
  - `install-id.ts` no longer caches a rejected promise: the memo is cleared on rejection so one
    transient failure cannot fail every later API call for the process lifetime.
  - `session-provider.tsx` comment corrected to say the `cancelled` flag guards unmount, not
    ordering, and to name the race that stays open and why it is tolerable.
  - Abort-listener leak was already closed as part of F2.

## Also verified clean

- **Secrets**: nothing committed. `.env` is gitignored, only `.env.example` is tracked,
  `appsettings.json` ships an empty `Supabase:ProjectUrl` and no signing secret exists by design.
  No token is logged on either side.
- **JWT validation**: issuer, audience, lifetime and signing key are all explicitly validated, each
  with a negative test case in `MeEndpointTests`.
- **Forwarded headers spoofability**: `RemoteIpAddress` is attacker-controlled, but
  `AnonymousPartitionKey` is its only consumer — no IP allowlist, no IP-based authz, no IP logging.
  Not exploitable beyond the caller's own rate-limit bucket.
- **Pattern compliance**: `me.ts` follows `health.ts`, `use-me-query.ts` follows
  `use-health-query.ts`, `account.tsx` follows `index.tsx`'s screen structure, and the account tab
  was added to both the native and web tab files with each file's local naming convention
  respected, as the plan instructed.
