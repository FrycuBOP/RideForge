# S-05 — Rider auth + anonymous generation quota Implementation Plan

## Overview

Add the minimal auth scaffold FR-008 asks for — a rider creates an account, logs in, and the
session survives an app restart — and pair it with a server-enforced generation quota for
anonymous riders (2 per hour). The quota is what gives the account a reason to exist: it is the
first moment a rider gets something concrete for signing in.

The backend independently verifies Supabase-issued JWTs against the project's public JWKS, so the
trust boundary is proven here rather than inherited untested by S-06 (`save-route`). Anonymous
route generation keeps working exactly as it does today, up to the quota.

## Current State Analysis

- **Auth is absent everywhere.** No authentication middleware in
  [api/Program.cs](api/Program.cs), no DbContext/ORM/migrations on the backend, and no auth,
  crypto, or persistent-storage packages in [package.json](package.json).
- **The client transport layer is a clean extension point.** [src/api/client.ts](src/api/client.ts)
  is the single owner of transport, timeout, and error normalization; every endpoint module
  ([health.ts](src/api/health.ts), [route.ts](src/api/route.ts)) builds on `request()`. There is
  no place to attach an `Authorization` header today, and no per-request header customization.
- **Errors are already normalized.** [src/api/errors.ts](src/api/errors.ts) gives `ApiError` with a
  `kind` discriminant, and [src/app/(tabs)/index.tsx](src/app/(tabs)/index.tsx) establishes the
  pattern of mapping a failure to rider-facing copy (`planErrorMessage`, `geocodeErrorMessage`).
  `planErrorMessage` has no `429` branch.
- **The backend has a fail-fast configuration convention.** `Program.cs` throws at startup on a
  misconfigured stitching provider rather than degrading silently at request time — with a comment
  explaining that a wrong-output failure is harder to spot than a boot failure. New config in this
  plan follows that precedent.
- **The test harness is ready to extend.**
  [api/RideForgeApi.Tests/RouteGenerateEndpointTests.cs](api/RideForgeApi.Tests/RouteGenerateEndpointTests.cs)
  boots the real pipeline through `WebApplicationFactory<Program>` and already overrides
  configuration via `UseSetting(...)`. No frontend test runner exists, deliberately
  ([test-plan.md §4](context/foundation/test-plan.md)).
- **The provider decision was assumed, never made.**
  [infrastructure.md:159](context/foundation/infrastructure.md:159) lists auth provider selection
  as explicitly out of its scope, and [infrastructure.md:94](context/foundation/infrastructure.md:94)
  carries a H/M risk for a Supabase JWT secret drifting between Railway and local config.
- **Tab siblings have drifted.** [app-tabs.tsx](src/components/app-tabs.tsx) uses `name="index"`
  while [app-tabs.web.tsx](src/components/app-tabs.web.tsx) uses `name="home"` and still renders an
  "Expo Starter" brand label. A new tab means editing both files.

## Desired End State

A rider opens the Account tab, signs up with an email and password, and is signed in immediately.
Killing and relaunching the app leaves them signed in. The Account screen shows an identity the
**backend** confirmed, not just one the client believes — proving the JWT actually validates
server-side. Signing out returns them to the form.

A signed-out rider can generate two routes per hour. The third attempt shows a message explaining
the limit and pointing at sign-in. A signed-in rider has no limit. Generation itself is otherwise
unchanged for everyone.

Verified by: `dotnet test` (auth-boundary and quota tests), `npx tsc --noEmit`, `npm run lint`, and
a manual pass on an EAS dev build covering sign-up → restart → sign-out and the quota wall.

### Key Discoveries:

- **Asymmetric signing keys remove the flagged risk entirely.** Supabase exposes a JWKS endpoint at
  `{project-url}/auth/v1/.well-known/jwks.json` when asymmetric signing keys are enabled. Verifying
  against public keys means no shared secret ever enters Railway's vault or `appsettings`, so the
  pre-mortem's 6-hour-401 scenario becomes structurally impossible rather than merely avoided.
  The endpoint returns no keys while the project is still on legacy HS256 — enabling asymmetric
  keys in the dashboard is a hard prerequisite, not a nicety.
- **`expo-secure-store` cannot hold a Supabase session.** It rejects values over 2048 bytes and a
  session commonly exceeds that. AsyncStorage is the adapter the current Supabase Expo quickstart
  uses; the encrypted alternative requires a hand-maintained AES wrapper class.
- **React Native needs explicit refresh wiring.** `supabase.auth.startAutoRefresh()` /
  `stopAutoRefresh()` must be driven from `AppState`, registered once, and skipped on web.
- **Bearer tokens keep the CORS TODO closed.** The warning at [api/Program.cs:8](api/Program.cs:8)
  that `AllowAnyOrigin` cannot combine with credentials applies to cookies. An `Authorization`
  header is not a credential in the CORS sense, so the existing policy stays valid unchanged.
- **.NET 10 ships partitioned rate limiting in the box.** `AddRateLimiter` with a
  `PartitionedRateLimiter` and `FixedWindowLimiter` covers the quota with no new dependency and no
  external store.

## What We're NOT Doing

- **No password reset, email verification, or account deletion.** All three need deep-link handling
  and email templates; deferred until a rider actually needs them.
- **No OAuth / social sign-in.** Adding any third-party social login triggers the App Store
  requirement to also implement Sign in with Apple.
- **No database, ORM, or migrations.** Supabase owns the user records. Persistence arrives with
  S-06 (`save-route`), which is where it belongs.
- **No saving or listing of routes.** That is S-06 and S-07.
- **No frontend test runner.** [test-plan.md §4](context/foundation/test-plan.md) excludes one for
  MVP on purpose; reversing that is its own change, not a rider along with auth.
- **No rate limit on `POST /route/stitch`.** Scoped to `/route/generate` as requested. See Open
  Risks — the stitch endpoint is also a billed provider call and stays open.
- **No persistent or distributed quota counters.** In-memory, single instance, accepted for MVP.
- **No changes to the route generation algorithm or the Plan screen's inputs.**

## Implementation Approach

Three surfaces move, in an order that puts each one's dependency ahead of it:

1. **Client session foundation** — the Supabase client, storage adapter, and a session context the
   rest of the app reads from.
2. **Client auth UI** — an Account tab that exercises the foundation and is independently verifiable
   without any backend change.
3. **Backend trust boundary** — JWT bearer validation against JWKS plus one protected endpoint,
   tested hermetically with a local signing key so no test touches the network.
4. **Joining the two** — the client attaches its token and renders the backend's answer, which is
   the first proof that the boundary holds end-to-end.
5. **The quota** — enforced server-side, keyed by installation identifier with an IP fallback,
   skipped entirely for authenticated riders, and surfaced on the Plan screen as a reason to sign in.

Phases 1–2 are client-only and phase 3 is backend-only, so either can be verified in isolation
before phase 4 couples them.

## Critical Implementation Details

**Middleware ordering.** `UseRateLimiter()` must come **after** `UseAuthentication()`, because the
partition function reads `HttpContext.User` to decide whether the caller is exempt. Reversed, every
request looks anonymous and signed-in riders get limited too — a bug that passes every unit test and
only shows up under a real token. `UseForwardedHeaders()` must come first in the pipeline, before
anything reads the client IP, or the IP fallback partitions every anonymous rider onto Railway's
proxy address and the quota becomes global.

**Session restore is asynchronous.** `supabase.auth.getSession()` resolves after a storage read, so
on cold start the app briefly knows nothing about the session. The session context must expose a
distinct "restoring" state and the Account screen must render neither the sign-in form nor the
signed-in view during it — otherwise a signed-in rider sees a login form flash on every launch.

**Sign-out must clear cached backend responses.** React Query holds the `/me` result keyed
independently of auth state; without an explicit cache clear on sign-out, the next rider to sign in
on the same device sees the previous rider's identity until the query refetches.

**In-memory counters are shared across tests.** `WebApplicationFactory` reuses one host per
`IClassFixture`, so quota tests that reuse an installation identifier will interfere with each
other. Each test needs a unique identifier, or its own factory.

---

## Phase 1: Supabase foundation + session state

### Overview

Provision the Supabase project correctly and stand up the client-side session: a configured
supabase-js client, persistent storage, foreground token refresh, and a context the app reads
auth state from. No UI yet.

### Changes Required:

#### 1. Supabase project configuration (dashboard, not in-repo)

**Intent**: Create the project and put it in the shape the rest of the plan assumes.

**Contract**: In the Supabase dashboard — enable **asymmetric JWT signing keys** (RS256/ES256) so
the JWKS endpoint serves public keys; **disable "Confirm email"** under Auth providers so sign-up
returns a live session with no deep link; leave email/password enabled. Record the project URL and
the anon/publishable key. Verify by fetching
`{project-url}/auth/v1/.well-known/jwks.json` and confirming a non-empty `keys` array — an empty
array means the project is still on HS256 and phase 3 will fail.

#### 2. Dependencies

**File**: `package.json`

**Intent**: Add the Supabase client and its React Native prerequisites.

**Contract**: Install with `npx expo install` (not bare npm) so versions match SDK 56:
`@supabase/supabase-js`, `@react-native-async-storage/async-storage`, `react-native-url-polyfill`.

#### 3. Environment configuration

**File**: `.env` (local, git-ignored) and the EAS build environment

**Intent**: Supply the project URL and anon key the same way the backend URL is already supplied.

**Contract**: `EXPO_PUBLIC_SUPABASE_URL` and `EXPO_PUBLIC_SUPABASE_ANON_KEY`, mirroring the
`EXPO_PUBLIC_API_URL` convention documented in [src/api/config.ts](src/api/config.ts) — inlined at
build time, so changing them requires restarting the dev server with `--clear`. Unlike the API URL,
these get **no hardcoded fallback**: a missing value should fail loudly at startup rather than
silently pointing at nothing.

#### 4. Supabase client

**File**: `src/lib/supabase.ts`

**Intent**: Create the single shared client, configured for React Native session persistence.

**Contract**: Exports `supabase`, created with `createClient(url, anonKey, { auth: { storage:
AsyncStorage, autoRefreshToken: true, persistSession: true, detectSessionInUrl: false } })`.
Imports `react-native-url-polyfill/auto` at the top. Registers a single `AppState` listener that
calls `supabase.auth.startAutoRefresh()` on `active` and `stopAutoRefresh()` otherwise, guarded by
`Platform.OS !== 'web'` — both the storage adapter and the listener are native-only, and the web
build must not receive either.

#### 5. Session context

**File**: `src/components/session-provider.tsx`, `src/hooks/use-session.ts`

**Intent**: Expose auth state to any screen without each one subscribing to Supabase itself.

**Contract**: `useSession()` returns `{ session, user, isRestoring }`. The provider seeds state from
`supabase.auth.getSession()` and keeps it current via `supabase.auth.onAuthStateChange(...)`,
unsubscribing on unmount. `isRestoring` is `true` until the initial `getSession()` settles — see
Critical Implementation Details.

#### 6. Mount the provider

**File**: `src/app/_layout.tsx`

**Intent**: Make session state available app-wide.

**Contract**: `<SessionProvider>` wraps the existing tree inside `QueryClientProvider` (phase 4
reads the session from inside a query), outside `ThemeProvider`. No change to the `<Stack>` shape.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npx tsc --noEmit`
- Linting passes: `npm run lint`

#### Manual Verification:

- JWKS endpoint returns a non-empty `keys` array
- App launches on an EAS dev build with no redbox and no missing-env crash
- Web build (`npm run web`) still loads — the native-only storage adapter and AppState listener are correctly skipped

**Implementation Note**: After completing this phase and all automated verification passes, pause
here for manual confirmation before proceeding.

---

## Phase 2: Account tab + auth screens

### Overview

Give the rider a place to sign up, sign in, and sign out. Verifiable end-to-end against Supabase
with no backend change.

### Changes Required:

#### 1. Account screen

**File**: `src/app/(tabs)/account.tsx`

**Intent**: Render the sign-in form when signed out and the account summary when signed in.

**Contract**: Signed out — email and password inputs plus **Sign up** and **Sign in** actions
calling `supabase.auth.signUp` / `signInWithPassword`, with a disabled/pending state while in
flight and an inline error message. Signed in — the rider's email and a **Sign out** action calling
`supabase.auth.signOut()`. While `isRestoring`, render neither. Follows the existing screen
conventions: `ThemedView`/`ThemedText`, `Spacing` constants, `MaxContentWidth`, and
`useSafeAreaInsets` with `BottomTabInset` as in [index.tsx](src/app/(tabs)/index.tsx).

#### 2. Supabase error mapper

**File**: `src/lib/auth-errors.ts`

**Intent**: Turn provider error codes into rider-facing copy instead of leaking `AuthApiError`
strings into the UI.

**Contract**: A single function mapping a Supabase `AuthError` to a message, covering at minimum:
invalid credentials, email already registered, weak/short password, and an unmatched fallback.
Mirrors the shape of `planErrorMessage` in [index.tsx](src/app/(tabs)/index.tsx).

#### 3. Native tab registration

**File**: `src/components/app-tabs.tsx`

**Intent**: Add the Account tab to the native tab bar.

**Contract**: A third `NativeTabs.Trigger name="account"` with a label and a
`NativeTabs.Trigger.Icon`, following the existing two triggers. Requires a new icon asset at
`assets/images/tabIcons/` alongside `home.png` and `explore.png`.

#### 4. Web tab registration

**File**: `src/components/app-tabs.web.tsx`

**Intent**: Keep the web build's tab list in sync.

**Contract**: A third `TabTrigger name="account" href="/account"` wrapped in the existing
`TabButton`. Note the pre-existing naming drift between the two files (`index` vs `home`) — match
the local convention of each file rather than unifying them here.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npx tsc --noEmit`
- Linting passes: `npm run lint`

#### Manual Verification:

- Sign-up with a new email lands directly in the signed-in state (no inbox round-trip)
- Force-quitting and relaunching the app leaves the rider signed in
- Sign out returns to the form, and signing back in works
- A wrong password shows the mapped message, not a raw provider string
- The Plan tab still generates a route while signed out — anonymous generation is unaffected

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 3: Backend JWT verification + protected `GET /me`

### Overview

Stand up the trust boundary: the API validates Supabase tokens against the project's public keys and
exposes one endpoint that requires a valid one. Covered by hermetic tests that never touch the
network.

### Changes Required:

#### 1. Authentication package

**File**: `api/RideForgeApi.csproj`

**Intent**: Add JWT bearer support.

**Contract**: `Microsoft.AspNetCore.Authentication.JwtBearer`, version-aligned with the `net10.0`
target (matching the `10.0.x` line the test project already uses for `Mvc.Testing`).

#### 2. Supabase options

**File**: `api/Auth/SupabaseAuthOptions.cs`

**Intent**: Make the auth configuration bindable and overridable by tests, following the
`RouteStitchingOptions` precedent.

**Contract**: `SectionName = "Supabase"`; `ProjectUrl` (string) and `Audience` (string, default
`"authenticated"`). Startup **throws** when `ProjectUrl` is blank, matching the existing fail-fast
convention for the stitching provider.

#### 3. JWT bearer registration

**File**: `api/Program.cs`

**Intent**: Validate incoming bearer tokens against Supabase's published keys, with no shared secret.

**Contract**: `AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)` with
`Authority = "{ProjectUrl}/auth/v1"`, `Audience` from options, and `TokenValidationParameters`
validating issuer, audience, signing key, and lifetime. `AddAuthorization()` alongside.

> The handler derives its key set by fetching `{Authority}/.well-known/openid-configuration`. If
> Supabase does not serve that discovery document for the project, wire
> `TokenValidationParameters.IssuerSigningKeyResolver` against
> `{ProjectUrl}/auth/v1/.well-known/jwks.json` directly (via a cached
> `ConfigurationManager<JsonWebKeySet>`) rather than falling back to a shared secret — that fallback
> is the risk this design exists to remove.

#### 4. Pipeline wiring

**File**: `api/Program.cs`

**Intent**: Put the auth middleware in the request pipeline.

**Contract**: `app.UseAuthentication(); app.UseAuthorization();` after `app.UseCors()` and before
endpoint mapping. Phase 5 inserts `UseForwardedHeaders` ahead of all of it and `UseRateLimiter`
after — see Critical Implementation Details.

#### 5. Protected endpoint

**File**: `api/Program.cs`

**Intent**: Give the client something that proves the token validated server-side.

**Contract**: `app.MapGet("/me", ...).RequireAuthorization()` returning the `sub` claim as `id` and
the `email` claim, in the same camelCase wire style the client already consumes. A missing or
invalid token yields 401 from the middleware, not from handler code.

#### 6. Configuration defaults

**File**: `api/appsettings.json`, Railway environment

**Intent**: Declare the setting in-repo and supply the real value out of band.

**Contract**: A `Supabase` section with an empty `ProjectUrl` committed; the real value set on
Railway as `Supabase__ProjectUrl`. **No secret is involved** — the project URL is public.

#### 7. Auth-boundary tests

**File**: `api/RideForgeApi.Tests/MeEndpointTests.cs`

**Intent**: Pin the behaviour that fails silently and expensively when misconfigured.

**Contract**: Uses `WebApplicationFactory<Program>` with `UseSetting("Supabase:ProjectUrl", ...)` to
satisfy startup, and `ConfigureTestServices` to override `JwtBearerOptions` with a locally generated
signing key — clearing `Authority`/`MetadataAddress` so no network call occurs. Cases: no token →
401; malformed token → 401; token signed by the wrong key → 401; expired token → 401; valid token →
200 with `id` and `email`. Plus a regression case: `POST /route/generate` **without** a token still
returns 200.

### Success Criteria:

#### Automated Verification:

- Backend tests pass: `dotnet test api/RideForgeApi.slnx`
- New auth-boundary cases all present and passing (401 for no/malformed/wrong-key/expired token, 200 for valid)
- Anonymous `POST /route/generate` regression test still passes

#### Manual Verification:

- Deployed Railway `GET /me` returns 401 with no token
- Deployed Railway `GET /me` returns 200 and the correct email for a real token copied from a signed-in dev build
- Startup fails loudly when `Supabase__ProjectUrl` is unset, rather than booting and 500-ing later

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 4: Client ↔ backend auth wiring

### Overview

Attach the token to outgoing requests and show the rider an identity the backend confirmed. This is
the first end-to-end proof that the boundary holds.

### Changes Required:

#### 1. Authenticated requests

**File**: `src/api/client.ts`

**Intent**: Let an endpoint opt into sending the current access token, without changing any existing
call site.

**Contract**: `RequestOptions` gains `auth?: boolean` (default false). When true, `request()` reads
the current session and attaches `Authorization: Bearer <access_token>`. If there is no session, the
request proceeds without the header and the backend answers 401 — one uniform failure path rather
than two. Existing unauthenticated callers are untouched.

#### 2. Me endpoint

**File**: `src/api/me.ts`

**Intent**: Type the protected endpoint.

**Contract**: `MeResponse = { id: string; email: string | null }` and `getMe(): Promise<MeResponse>`
calling `request<MeResponse>('/me', { auth: true })` with a short timeout in the style of
`HEALTH_TIMEOUT_MS` — this is a fast identity check, not a generation call, and must not sit on the
30s budget.

#### 3. Me query hook

**File**: `src/hooks/use-me-query.ts`

**Intent**: Fetch the backend identity only when it can succeed.

**Contract**: `useQuery<MeResponse, ApiError>` keyed on `['me']`, `enabled` only when a session
exists, following the [use-health-query.ts](src/hooks/use-health-query.ts) template.

#### 4. Account screen shows verified identity

**File**: `src/app/(tabs)/account.tsx`

**Intent**: Display what the backend says, not only what the client believes.

**Contract**: When signed in, render the `/me` result with loading and error states. A `401`
(`kind: 'http'`, `status: 401`) gets its own message — the session is stale and the rider should
sign in again — distinct from network/timeout copy.

#### 5. Clear cached identity on sign-out

**File**: `src/app/(tabs)/account.tsx` (or the session provider)

**Intent**: Prevent one rider's identity leaking into the next session on a shared device.

**Contract**: On a `SIGNED_OUT` transition, remove the `['me']` query from the React Query cache.

#### 6. Export the new surface

**File**: `src/api/index.ts`

**Intent**: Keep the barrel export complete.

**Contract**: Re-export `getMe` and `MeResponse` alongside the existing entries.

### Success Criteria:

#### Automated Verification:

- Type checking passes: `npx tsc --noEmit`
- Linting passes: `npm run lint`

#### Manual Verification:

- Signed-in Account screen shows the email returned by `/me`, not only the local session value
- Sign out, sign in as a different account — the displayed identity changes, with no stale value
- Airplane mode on the Account screen shows the network message and no crash
- Generation from the Plan tab still works while signed out

**Implementation Note**: Pause for manual confirmation before proceeding.

---

## Phase 5: Anonymous generation quota

### Overview

Enforce two generations per hour for anonymous riders, exempt signed-in riders entirely, and turn
the wall into the moment sign-in makes sense. Includes syncing the PRD and roadmap, since this is a
requirement neither document currently carries.

### Changes Required:

#### 1. Installation identifier

**File**: `src/lib/install-id.ts`

**Intent**: Give each install a stable identity to count against.

**Contract**: `getInstallId(): Promise<string>` returning a persisted UUID, created on first call
via `expo-crypto`'s `randomUUID()` and stored in AsyncStorage (already a dependency from phase 1)
under a stable key. Generated once, cached in memory thereafter. Requires
`npx expo install expo-crypto`.

#### 2. Send the identifier

**File**: `src/api/client.ts`

**Intent**: Let the backend partition anonymous callers.

**Contract**: `request()` attaches `X-RideForge-Install: <install id>` to every request. The
existing CORS policy already allows any header, so no backend CORS change is needed.

#### 3. Quota options

**File**: `api/RateLimiting/GenerationQuotaOptions.cs`

**Intent**: Keep the numbers configurable rather than hardcoded, so tests can drive them and the
limit can be tuned without a code change.

**Contract**: `SectionName = "GenerationQuota"`; `PermitLimit` (default `2`) and `WindowMinutes`
(default `60`).

#### 4. Rate limiter registration

**File**: `api/Program.cs`

**Intent**: Enforce the quota where it cannot be bypassed by the client.

**Contract**: `AddRateLimiter` with a named policy backed by a `PartitionedRateLimiter` whose
partition function returns: `NoLimiter` when `HttpContext.User.Identity?.IsAuthenticated == true`;
otherwise a `FixedWindowLimiter` partition keyed on the `X-RideForge-Install` header **when it
parses as a GUID**, and on the caller's remote IP otherwise — a missing or malformed header must
never be a free pass. `PermitLimit` and `Window` from options, `QueueLimit = 0`,
`RejectionStatusCode = 429`.

#### 5. Pipeline ordering and forwarded headers

**File**: `api/Program.cs`

**Intent**: Make the IP fallback meaningful behind Railway's proxy and let the limiter see the
authenticated user.

**Contract**: Final order — `UseForwardedHeaders` (with `ForwardedHeaders.XForwardedFor`) → `UseCors`
→ `UseAuthentication` → `UseAuthorization` → `UseRateLimiter` → endpoints. Apply
`.RequireRateLimiting(...)` to `POST /route/generate` only; `/health`, `/me`, and `/route/stitch`
stay unlimited.

#### 6. Quota message on the Plan screen

**File**: `src/app/(tabs)/index.tsx`

**Intent**: Explain the wall and give the rider the action that removes it.

**Contract**: `planErrorMessage` gains a `429` branch under `kind: 'http'`, telling the rider they
have used their free rides for this hour and that signing in removes the limit. Pair it with a way
to reach the Account tab from that message rather than leaving the rider to find it.

#### 7. Quota tests

**File**: `api/RideForgeApi.Tests/GenerationQuotaTests.cs`

**Intent**: Pin the quota's actual behaviour, including the bypass paths.

**Contract**: Cases — two anonymous generations with one installation id succeed and the third
returns 429; a different installation id has an independent allowance; an authenticated request is
not limited after the anonymous allowance is exhausted; a request with **no** installation header is
still limited (IP fallback), not waved through. Each test uses a unique installation id or its own
factory — counters are process-wide, see Critical Implementation Details.

#### 8. Documentation sync

**File**: `context/foundation/prd.md`, `context/foundation/roadmap.md`

**Intent**: Stop the documents from drifting away from shipped behaviour.

**Contract**: PRD — add a functional requirement under *Route generation* for the anonymous
generation quota, noting it does not violate the US-01 criterion that generation needs no login, and
record the chosen numbers. Roadmap — note the added scope in the `S-05` body so the slice's outcome
matches what shipped.

### Success Criteria:

#### Automated Verification:

- Backend tests pass: `dotnet test api/RideForgeApi.slnx`
- Quota tests cover the third-request 429, per-install independence, the authenticated exemption, and the missing-header fallback
- Type checking passes: `npx tsc --noEmit`
- Linting passes: `npm run lint`

#### Manual Verification:

- Signed out on a dev build, the third generation within an hour shows the quota message
- Signing in and retrying immediately succeeds
- A signed-in rider can generate more than twice in an hour
- Deployed on Railway, the quota counts per rider rather than globally — two devices on different networks each get their own allowance
- PRD and roadmap reflect the new requirement

---

## Testing Strategy

### Unit / integration tests (backend, xUnit):

- Auth boundary on `/me`: no token, malformed token, wrong-key signature, expired token, valid token
- Anonymous `/route/generate` still succeeds without a token (regression against over-broad auth)
- Quota: allowance exhaustion, per-install isolation, authenticated exemption, missing-header fallback

All hermetic — a locally generated signing key and the committed fake stitcher, no network calls,
consistent with the fake-`HttpMessageHandler` precedent in
[test-plan.md §4](context/foundation/test-plan.md).

### Manual testing steps:

1. Fresh install on an EAS dev build → Account tab → sign up → lands signed in
2. Force-quit and relaunch → still signed in
3. Account screen shows the email from `/me`
4. Sign out → form returns; sign in as a second account → identity updates with no stale value
5. Signed out, generate three routes within an hour → third shows the quota message
6. Sign in from that message → generate again → succeeds
7. Second device on a different network, signed out → gets its own two generations
8. Web build loads and the Account tab renders

## Performance Considerations

The quota check is an in-memory dictionary lookup and adds no measurable latency to the 30-second
NFR-01 budget. JWT validation is a local signature check; the JWKS fetch happens once per process on
first use and is cached by the handler, costing one outbound call on cold start. `/me` uses a short
timeout so an identity check never sits on the generation budget.

## Migration Notes

No data migration — there is no existing data. Existing anonymous riders are unaffected except for
the new quota, which applies from deploy. Rollback is a code revert plus unsetting
`Supabase__ProjectUrl` on Railway; because startup fails fast on a blank value, a partial rollback
that reverts the code but leaves the variable set is harmless, while the reverse fails loudly rather
than silently.

## Open Risks & Assumptions

- **The installation identifier is client-controlled.** Clearing app data, reinstalling, or sending a
  different header value resets the allowance. This is a deliberate choice — a speed bump, not a
  security control. If abuse appears, the escalation path is the IP fallback or a real identity.
- **The IP fallback collides under carrier NAT.** Riders sharing a mobile carrier's egress IP share
  one allowance. It only applies to requests missing a valid installation header, so the blast radius
  is small, but a rider hitting it will see an unexplained limit.
- **Counters reset on redeploy.** In-memory state dies with the process, and a solo developer may
  redeploy several times a day, so the effective limit is looser than 2/hour during active
  development.
- **`POST /route/stitch` stays unlimited.** It is also a billed provider call and is publicly
  reachable. Out of scope here by decision; worth closing before any public launch.
- **Asymmetric signing keys must be enabled before phase 3.** A project still on HS256 serves an
  empty JWKS and every token validation fails with no obvious cause.
- **Supabase free-tier limits are unverified against expected usage.** Not expected to bind at MVP
  scale, but unmeasured.
- **This slice exceeds its roadmap scope.** The quota is not in the PRD or the roadmap's S-05.
  Phase 5 syncs both, but the requirement itself was decided in planning, not upstream.

## References

- Roadmap slice: `context/foundation/roadmap.md` → `### S-05: Konto i logowanie jeźdźca`
- PRD requirement: `context/foundation/prd.md` → FR-008, and the US-01 no-login criterion
- Assumed-provider note: `context/foundation/infrastructure.md:159`
- JWT secret risk this design removes: `context/foundation/infrastructure.md:94`
- Test stack and the deliberate no-frontend-runner decision: `context/foundation/test-plan.md` §4
- Client transport to extend: `src/api/client.ts`, `src/api/errors.ts`
- Error-copy pattern to follow: `src/app/(tabs)/index.tsx` (`planErrorMessage`)
- Test harness pattern to follow: `api/RideForgeApi.Tests/RouteGenerateEndpointTests.cs`
- Fail-fast config precedent: `api/Program.cs`

## Progress

> Convention: `- [ ]` pending, `- [x]` done. Append ` — <commit sha>` when a step lands. Do not rename step titles. See `references/progress-format.md`.

### Phase 1: Supabase foundation + session state

#### Automated

- [x] 1.1 Type checking passes: `npx tsc --noEmit` — 16e575f
- [x] 1.2 Linting passes: `npm run lint` — 16e575f

#### Manual

- [x] 1.3 JWKS endpoint returns a non-empty `keys` array — 16e575f
- [x] 1.4 App launches on an EAS dev build with no redbox and no missing-env crash — 16e575f
- [x] 1.5 Web build still loads with native-only wiring skipped — 16e575f

### Phase 2: Account tab + auth screens

#### Automated

- [x] 2.1 Type checking passes: `npx tsc --noEmit` — 642f2ef
- [x] 2.2 Linting passes: `npm run lint` — 642f2ef

#### Manual

- [x] 2.3 Sign-up with a new email lands directly in the signed-in state — 642f2ef
- [x] 2.4 Force-quit and relaunch leaves the rider signed in — 642f2ef
- [x] 2.5 Sign out returns to the form, and signing back in works — 642f2ef
- [x] 2.6 A wrong password shows the mapped message, not a raw provider string — 642f2ef
- [x] 2.7 The Plan tab still generates a route while signed out — 642f2ef

### Phase 3: Backend JWT verification + protected `GET /me`

#### Automated

- [x] 3.1 Backend tests pass: `dotnet test api/RideForgeApi.slnx` — d3a3431
- [x] 3.2 Auth-boundary cases present and passing (401 for no/malformed/wrong-key/expired token, 200 for valid) — d3a3431
- [x] 3.3 Anonymous `POST /route/generate` regression test still passes — d3a3431

#### Manual

- [x] 3.4 Deployed `GET /me` returns 401 with no token — d3a3431
- [x] 3.5 Deployed `GET /me` returns 200 and the correct email for a real token — d3a3431
- [x] 3.6 Startup fails loudly when `Supabase__ProjectUrl` is unset — d3a3431

### Phase 4: Client ↔ backend auth wiring

#### Automated

- [x] 4.1 Type checking passes: `npx tsc --noEmit`
- [x] 4.2 Linting passes: `npm run lint`

#### Manual

- [x] 4.3 Signed-in Account screen shows the email returned by `/me`
- [x] 4.4 Switching accounts updates the displayed identity with no stale value
- [x] 4.5 Airplane mode shows the network message and no crash
- [x] 4.6 Generation from the Plan tab still works while signed out

### Phase 5: Anonymous generation quota

#### Automated

- [ ] 5.1 Backend tests pass: `dotnet test api/RideForgeApi.slnx`
- [ ] 5.2 Quota tests cover 429 exhaustion, per-install independence, authenticated exemption, missing-header fallback
- [ ] 5.3 Type checking passes: `npx tsc --noEmit`
- [ ] 5.4 Linting passes: `npm run lint`

#### Manual

- [ ] 5.5 Signed out, the third generation within an hour shows the quota message
- [ ] 5.6 Signing in and retrying immediately succeeds
- [ ] 5.7 A signed-in rider can generate more than twice in an hour
- [ ] 5.8 Two devices on different networks each get their own allowance
- [ ] 5.9 PRD and roadmap reflect the new requirement
