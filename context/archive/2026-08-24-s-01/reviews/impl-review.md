<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: S-01 — Generate route from start + length, show on map

- **Plan**: context/changes/s-01/plan.md
- **Scope**: Full plan — Phases 1-4 of 4
- **Date**: 2026-09-08
- **Verdict**: REJECTED (deployment/security hygiene; the planned feature work itself is sound)
- **Findings**: 2 critical, 7 warnings, 1 observation
- **Post-triage (2026-09-08)**: 8 fixed, 1 accepted (F1 — mitigated in Google Cloud Console),
  1 skipped (F2 — exposure accepted at MVP stage). Backend suite 33 -> 48 tests; tsc and lint clean.
  The REJECTED verdict above records the state at review time and is left unedited; F2 is the one
  finding still standing, by decision.

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | WARNING |
| Scope Discipline | PASS |
| Safety & Quality | FAIL |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | WARNING |

Automated criteria were re-run from scratch during this review and all pass: `dotnet build` (0 errors,
0 warnings), `dotnet test` (33/33), `npx tsc --noEmit` (clean), `npm run lint` (clean), plus a live
`POST /route/generate` returning 640 road-following points at -4.7% of the requested 20 km in 0.16 s.

Scope discipline was checked against every line of the plan's "What We're NOT Doing" list and is clean:
curviness state is never read by `handlePlan` or sent to the API, `destination` is inert, there is no
duration input, geocoding is entirely client-side, and generation is a single `StitchAsync` call with no
retry (react-query's mutation retry default is 0; `retry: 1` in query-client.ts applies to queries only).
`src/components/ride-stats.tsx` and `src/lib/format-ride.ts` are not named in the plan but are shared by
two plan-mandated files (`result.tsx` and the `result.web.tsx` fallback) and introduce no new behaviour —
justified decomposition, not scope creep.

## Findings

### F1 — Live Google Maps API key committed to a public repository

- **Severity**: CRITICAL
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: app.json:50
- **Detail**: `"androidGoogleMapsApiKey": "AIzaSyDBbGuhuQ3F9A2Gh0tCwAVtLurwsHGJoJw"` sits in a tracked
  file. Verified: `https://api.github.com/repos/FrycuBOP/DocumentAIlyzer` returns HTTP 200
  unauthenticated (the repo is public) and `git show origin/dev:app.json` contains the key, so it is
  already published and in git history. The plan permitted this — plan.md:130-132 says "keep it out of
  source if the repo goes public (env-injected via app.config.js) — for the MVP dev build it may sit in
  app.json". The condition the plan attached has since been met, so this is the plan's own escape clause
  expiring, not implementer drift.
- **Fix**: Restrict the key in Google Cloud Console (Android app restriction: package `com.rideforge` +
  debug and release SHA-1 fingerprints; API restriction: Maps SDK for Android only). Rotate afterwards
  if you want the published value dead, which additionally needs `app.config.js` reading
  `process.env.GOOGLE_MAPS_API_KEY` plus an EAS secret.
  - Strength: Restriction is the only mitigation that actually works — an Android Maps key is compiled
    into the APK and extractable from any installed build, so it can never be secret. A restricted key
    is unusable by anyone else even though it is public.
  - Tradeoff: Console work outside the repo; the SHA-1 list must be updated when signing keys change.
  - Confidence: HIGH — this is Google's documented model for Android Maps keys.
  - Blind spot: Have not checked whether this key is also used by other Google APIs on the same
    project, which would widen the blast radius beyond Maps.
- **Decision**: ACCEPTED — user confirms the key is already restricted in Google Cloud Console. That is the effective mitigation (an Android Maps key ships in the APK regardless); the key stays in app.json for the MVP dev build.

### F2 — `/route/generate` is an open, unauthenticated, unthrottled proxy to a billed provider

- **Severity**: CRITICAL
- **Impact**: HIGH — architectural stakes; think carefully before deciding
- **Dimension**: Safety & Quality
- **Location**: api/Program.cs:9-13, api/Program.cs:96
- **Detail**: CORS is `AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()` globally, and the endpoint has
  no authentication, no authorization and no rate limiting (verified: no `AddRateLimiter`,
  `AddAuthentication` or `RequireAuthorization` anywhere in Program.cs). Every request triggers one
  outbound OpenRouteService call billed to the server-side `RouteStitching__ApiKey`. The backend URL is
  committed at src/api/config.ts:10 and the exact wire contract is public in RouteModels.cs:37, so a
  trivial loop drains the ORS free tier (2k/day) in minutes and bills directly on a paid tier. This
  compounds F1: two separately-billed provider credentials now reachable from a public repo — one
  embedded in the client, one behind an open server proxy.
- **Fix A (Recommended)**: Add ASP.NET Core rate limiting (a fixed-window per-IP limiter on
  `/route/generate` and `/route/stitch`) plus a daily ceiling, and scope CORS to the real web origin(s).
  - Strength: Removes the unbounded-cost property without waiting for the auth slice (S-05/FR-008), and
    per-IP limiting is a few lines of builder configuration with no client change.
  - Tradeoff: Per-IP limits are weak against a distributed caller and add a config surface to tune.
  - Confidence: HIGH — `AddRateLimiter` is first-party and the endpoints are already centralized in one
    file.
  - Blind spot: Have not checked whether Railway sits behind a proxy that would collapse client IPs into
    one, which would make a per-IP limiter behave as a global one.
- **Fix B**: Take the endpoint private until S-05 lands — a shared header secret, or Railway-side access
  control.
  - Strength: Closes the surface completely rather than shaping it.
  - Tradeoff: `EXPO_PUBLIC_*` values are inlined into the app bundle, so a client-held secret is friction
    rather than auth; it also blocks the web target you are currently building.
  - Confidence: MEDIUM — depends on how much you still need open access for device testing.
  - Blind spot: Have not surveyed what else already calls this backend.
- **Decision**: SKIPPED — accepted exposure at MVP stage; revisit when the endpoint carries real traffic or moves to a paid ORS tier.

### F3 — The generator's sizing test is a mirror test; `DetourFactor` is unfalsifiable

- **Severity**: WARNING
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Success Criteria
- **Location**: api/RideForgeApi.Tests/RouteGeneratorTests.cs:59-61
- **Detail**: The assertion computes its expected value from the implementation's own constant:
  `var targetMeters = distanceKm * 1000.0 / RouteGenerator.DetourFactor;` then
  `Assert.InRange(lengthMeters / targetMeters, 0.8, 1.2)`. Both sides move together. Reproduced the
  geometry independently in Node: the ratio is 1.0000 at `DetourFactor` 1.6, 0.1 and 10 alike, so the
  constant can be set to any value and all 33 tests still pass — and it is the single constant that
  decides whether US-01's +/-20% holds on real roads. The +/-20% band is decoration; actual deviation is
  0.0000. This is the Lesson-2 "mirror implementation" anti-pattern.

  Important context on intent: the plan was internally contradictory before any code was written. Phase 2
  change #4 contract (plan.md:267-268) says "stitched length via `fake` is within +/-20% of `distanceKm`",
  which is arithmetically impossible — the generator deliberately targets `distanceKm / 1.6`, i.e. 0.625
  of the request, so the test as literally specified would fail. Critical Implementation Details
  (plan.md:98-101) says the opposite: "xUnit asserts the *geometric* sizing against the fake; the +/-20%
  against real roads is a manual device check (Phase 4)". The implementer followed the second reading and
  documented it in a code comment pointing at manual step 2.5. So the oracle is real but lives only in a
  manual note. The contract text was never amended to match.

  Note what the test does still catch: `GeoMath.PathLengthMeters` is haversine, independent of the planar
  `Offset` placement, so a broken N-gon radius formula would move the ratio. But a +/-20% band is far too
  wide even for that — swapping the circumradius for the apothem, the classic N-gon error, shifts length
  by `cos(pi/8)` = 7.6%, comfortably inside the band.
- **Fix**: Tighten this test to `[0.99, 1.01]` against a hand-computed constant for one case (the
  straight-line geometry is fully deterministic), and add a separate guard that pins `DetourFactor`
  against the observed real-road ratio recorded in change.md, so changing it forces a deliberate test
  update rather than passing silently.
- **Decision**: FIXED — tightened the geometric band to `[0.99, 1.01]`, and added
  `GeometricSizing_InflatedByMeasuredRoadFactor_SatisfiesUs01Band`, whose expected value comes from
  US-01 (+/-20%) and the externally measured 1.45-1.71 road inflation rather than from
  `RouteGenerator.DetourFactor`. Mutation-verified: `DetourFactor` set to 10.0, 1.2 and 2.0 each now
  fails 3 tests, where all three passed before the change. Suite 33 -> 36 tests, green.

### F4 — Criterion 2.3 is filed under "Automated" but no automated test exercises the endpoint

- **Severity**: WARNING
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Success Criteria
- **Location**: context/changes/s-01/plan.md:489
- **Detail**: "2.3 `POST /route/generate` returns 200 with non-empty geometry (fake provider)" is checked
  off under `#### Automated`. Verified: `grep -rln "WebApplicationFactory|route/generate"` over
  api/RideForgeApi.Tests/ returns nothing. The directory holds only FakeRouteStitcherTests.cs,
  OpenRouteServiceStitcherTests.cs and RouteGeneratorTests.cs. The endpoint handler
  (api/Program.cs:96-121) — including its validation-to-400 path and the
  `RouteStitchException` to 422/504/502 mapping, duplicated verbatim from `/route/stitch` — has no
  coverage. The criterion was genuinely verified, but by hand (curl), which makes it a manual check
  wearing an automated label, and nothing will re-verify it on the next change.
- **Fix**: Either add a `WebApplicationFactory` test hitting `/route/generate` with the fake provider, or
  move 2.3 into the Manual subsection so the plan stops overstating the automated safety net.
- **Decision**: FIXED — added `api/RideForgeApi.Tests/RouteGenerateEndpointTests.cs`: 7 tests booting the
  real pipeline via `WebApplicationFactory<Program>` against the fake provider (200 + closed non-empty
  geometry, the camelCase wire contract the client reads, and the 400 paths for bad distance, missing
  start and out-of-range start). Required `Microsoft.AspNetCore.Mvc.Testing` and a `public partial class
  Program;` marker in Program.cs. Mutation-verified: removing the `MaxDistanceKm` ceiling fails 2 tests,
  switching the serializer to PascalCase fails 1. Criterion 2.3 is now genuinely automated. Suite 36 -> 43.

### F5 — The deployment fails open to the fake stitcher, silently

- **Severity**: WARNING
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: api/Program.cs:32-48, api/Routing/RouteStitchingOptions.cs:13
- **Detail**: The provider `switch` throws on a missing API key but falls through `default:` to
  `FakeRouteStitcher` for any unrecognized provider string, and `Provider` defaults to `"fake"`. A typo
  ("openrouteserivce") or an unset Railway variable therefore serves straight-line octagons at 62.5% of
  the requested distance, with nothing in the response and no startup log saying which provider answered.
  This is not hypothetical: it happened during Phase 4 manual verification in this very session — a 20 km
  request came back as a 12.5 km octagon, and diagnosing it required probing the endpoint and recognizing
  that 12500 is exactly `20000 / DetourFactor`.
- **Fix**: Make `default:` throw for any value that is not literally `"fake"`, and log the resolved
  provider once at startup.
  - Strength: Converts a silent wrong-output failure into a loud startup failure, matching the fail-fast
    treatment the missing-API-key branch already gets three lines above.
  - Tradeoff: A deploy with a typo'd variable now refuses to boot instead of serving degraded results.
    That is the point, but it is a behavior change for the deploy pipeline.
  - Confidence: HIGH — the fail-fast precedent is already in this same switch.
  - Blind spot: None significant.
- **Decision**: FIXED — `default:` now throws for any unrecognized provider name (`"fake"` became its own
  explicit case), and `app.Logger` reports the resolved provider at startup. Added
  `UnknownProviderName_FailsFastAtStartup` asserting the throw via a config-overriding
  `WebApplicationFactory`. Suite 43 -> 44, green.

  **Residual gap, stated deliberately:** the throw catches a *typo*, not the case that actually occurred —
  a deploy that never sets `RouteStitching__Provider` falls back to the committed `"fake"` in
  appsettings.json, which is a recognized value, so the app boots normally. Only the new startup log
  covers that path (diagnosis, not prevention). Real prevention would mean requiring an explicit provider
  when `ASPNETCORE_ENVIRONMENT=Production`; not done here, as it was outside what was approved.

### F6 — Unvalidated response shape crashes the results screen

- **Severity**: WARNING
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/api/route.ts:24, src/app/result.tsx:47
- **Detail**: `request<T>` ends in `return (await response.json()) as T` — an unchecked assertion, so
  `GeneratedRoute` is never verified at runtime. A 200 whose body lacks `geometry` (a proxy interstitial,
  a backend regression, a provider contract change) reaches `route.geometry.length` and throws
  `TypeError: Cannot read property 'length' of undefined`, crashing the screen instead of flowing into
  the FR-005 error UI that already exists. Same path at src/app/result.web.tsx:24.
  `formatDistanceKm(undefined)` degrades more quietly to the string "NaN km".
- **Fix**: Add a narrow runtime guard in `generateRoute` (`Array.isArray(r.geometry) && typeof
  r.distanceMeters === 'number'`) that throws `new ApiError('parse', ...)`, so a malformed body renders
  the existing parse-error message.
- **Decision**: FIXED — `generateRoute` is now async and runs the parsed body through an
  `isGeneratedRoute` type guard (geometry is an array of numeric lat/lng pairs; distance and duration
  are finite numbers), throwing `ApiError('parse', ...)` otherwise. A malformed 200 now renders the
  existing "unexpected response" message instead of crashing the map screen. tsc + lint clean.

### F7 — Double-submit race: the button stays enabled during geocoding

- **Severity**: WARNING
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: src/app/(tabs)/index.tsx:135-149
- **Detail**: `canPlan` includes `!generate.isPending`, but `handlePlan` awaits `geocodeAddress(origin)`
  before calling `mutate`. During that await the mutation has not started, so `isPending` is still false,
  the button is still enabled and still reads "Plan Route". A double-tap during a multi-second network
  geocode fires two geocodes, two mutations, two `setLastRoute` writes and two `router.push('/result')`
  calls — which also stacks two screens on the navigation stack. Given F2, it is also two billed provider
  calls.
- **Fix**: Add a `geocoding` state flag, set it around the await, and fold it into both `canPlan` and the
  button label.
- **Decision**: FIXED — added a `geocoding` flag set in a try/finally around the await and a derived
  `busy = geocoding || generate.isPending` feeding `canPlan`, `accessibilityState.busy` and the label
  (which now reads "Finding start…" during the geocode, then "Planning…"). tsc + lint clean.

### F8 — Geocoding failures are collapsed into one misleading message

- **Severity**: WARNING
- **Impact**: MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Safety & Quality
- **Location**: src/lib/geocode.ts:17-19, src/lib/geocode.web.ts:15-20
- **Detail**: Native `geocodeAddress` wraps everything in `catch { return null }`, and the caller renders
  a single message: "Couldn't find that starting point. Try a more specific address."
  `Location.geocodeAsync` throws for reasons that have nothing to do with the address — missing
  foreground permission, and no geocoder present on the device (common on AOSP/de-Googled devices and
  many emulators). This is a reachable path: `detectLocation` returns silently when permission is denied,
  the user then types an address, and the failure gets blamed on their typing. The web sibling has four
  further gaps in six lines: no `response.ok` check (a Nominatim 403/429 HTML body throws inside
  `.json()` and is swallowed as "not found"), no AbortController/timeout (unlike every call through
  src/api/client.ts, this fetch can hang forever with no pending UI), no identifying User-Agent despite
  the doc comment claiming Nominatim-policy compliance, and `parseFloat` on an unvalidated field — `NaN`
  serializes to JSON `null`, which fails binding into the non-nullable `Coord` and surfaces as a 400
  rendered as "check the distance", pointing at the wrong field.
- **Fix**: Return a discriminated result (`{ ok: false, reason: 'permission' | 'unavailable' |
  'not-found' }`) and branch the message; add `response.ok`, a timeout and a User-Agent to the web
  sibling, and reject non-finite parsed coordinates.
  - Strength: Turns the most likely first-run failure (permission denied, then a typed address) into an
    actionable message instead of one that sends the user to edit a correct address.
  - Tradeoff: Widens the geocode contract and touches the Plan screen's error rendering.
  - Confidence: MEDIUM — the reason taxonomy is inferred from Expo's documented exceptions, not from an
    observed failure on your devices.
  - Blind spot: Have not verified which exception type Expo SDK 56 actually surfaces on your test device.
- **Decision**: FIXED, with one recommendation deliberately dropped. `geocodeAddress` now returns
  `GeocodeResult` (`{ ok: true; point }` | `{ ok: false; reason: 'not-found' | 'permission' |
  'unavailable' }`) in both siblings, and the Plan screen branches the message per reason. The blind
  spot above is closed rather than papered over: instead of matching exception text, the native catch
  calls `Location.getForegroundPermissionsAsync()` and classifies on the real permission state, which
  is deterministic. Web sibling gained a `response.ok` check, a 10 s AbortController timeout, an array
  guard and a `Number.isFinite` check on the parsed coordinates.

  **Dropped: the User-Agent recommendation.** `User-Agent` is a forbidden header name — a browser
  `fetch` may not set it, so that advice is not implementable on web. Nominatim identifies browser
  traffic by `Referer`, which the browser sends on its own. The doc comment was corrected to claim only
  what the code actually does rather than deleting the claim.
  tsc + lint clean.

### F9 — The generator can emit out-of-domain coordinates; validation bounds distance, not latitude

- **Severity**: WARNING
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Safety & Quality
- **Location**: api/Routing/RouteGenerator.cs:59-64, api/Routing/RouteValidation.cs:21-31
- **Detail**: The math is correct where it applies — the N-gon radius inversion and the equirectangular
  offset both check out, and `MaxDistanceKm = 500` keeps the radius at ~51 km where equirectangular error
  stays around 1%. The unbounded axis is latitude: input validation accepts `|lat| <= 90`, and the
  generated waypoints are never re-validated before being handed to the stitcher. Reproduced
  independently in Node at 500 km: a start at lat 89.2 yields a waypoint at lat 90.118 (outside the
  domain); a start at lng 179.7 yields 180.63 with no antimeridian wrapping. The `1/cos(phi)` singularity
  is not where it first appears — a start exactly at the pole is harmless — it hits when the *circle
  centre* lands on the pole, i.e. start lat 89.541008 for a 500 km request, where longitude reaches
  7.5e15 degrees. Practical exposure for a motorcycle route app is essentially nil (89.5N is roughly 50 km
  from the North Pole), which is why this is a WARNING rather than a CRITICAL — but it is a real
  unvalidated-output path and the fix is one line.
- **Fix**: Constrain start latitude to roughly `|lat| <= 85` in `Validate(GenerateRequestDto)`, and run
  the generated waypoints through the existing range check before calling the stitcher.
- **Decision**: FIXED — added `RouteValidation.MaxGenerationLatitude = 85`, and `/route/generate` now
  re-validates the generated waypoints through the existing `Validate(StitchRequestDto)` before
  stitching (a 400 rather than an opaque provider 502). Added `NearPolarStart_IsInvalid` and a property
  test asserting every waypoint stays in domain across the accepted input surface (5 latitudes x 5
  longitudes at the 500 km ceiling).

  The property test immediately earned its place: it failed on first run at `lng = -180`, producing
  -183.4 — an antimeridian hole the latitude ceiling does not close. Fixed by wrapping longitude into
  [-180, 180] inside `RouteGenerator.Offset`. Suite 44 -> 48, green.

  **Residual:** wrapping keeps the coordinates legal, but a loop that actually crosses the antimeridian
  will still render badly client-side — `boundingRegion` in result.tsx:29 computes a `longitudeDelta`
  near 360 and zooms out to the whole globe. Listed under "Also noted" below rather than fixed here.

### F10 — Platform-sibling convention applied to forward geocoding but not the reverse geocoding beside it

- **Severity**: OBSERVATION
- **Impact**: LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/app/(tabs)/index.tsx:100-120, src/app/_layout.tsx:3
- **Detail**: CLAUDE.md is explicit: "When a component needs different behaviour on web, add a `.web.`
  sibling instead of branching on `Platform.OS` in the same file." This change introduced exactly that
  pattern for forward geocoding (geocode.ts / geocode.web.ts) while leaving an inline
  `Platform.OS === 'web'` branch for reverse geocoding in the same screen — and geocode.web.ts's own doc
  comment acknowledges the duplication rather than removing it. The Nominatim base URL, query
  construction and unvalidated result handling now live in two places with two different response types.
  The inline branch predates this change, but this change created the module that should absorb it.
  Separately, src/app/_layout.tsx:3 imports `useColorScheme` from `react-native` directly, bypassing the
  project's src/hooks/use-color-scheme.web.ts wrapper that exists specifically because the raw hook
  mis-renders during web static-render hydration — and `"output": "static"` is set in app.json.
- **Fix**: Move reverse geocoding into the same geocode.ts / geocode.web.ts pair, and import
  `useColorScheme` from `@/hooks/use-color-scheme` in the root layout.
- **Decision**: FIXED — `reverseGeocode(point)` added to both geocode siblings; the Plan screen's
  inline `Platform.OS === 'web'` branch, both Nominatim types and `formatAddress` are gone (the only
  remaining `Platform` use there is `Platform.select` for layout insets, which is styling, not a
  behavioural branch). The web reverse lookup now inherits this module's timeout, `response.ok` check
  and defensive `address` handling instead of the bare unguarded fetch it replaced — closing part of
  the "unvalidated external JSON" item listed below as a side effect. Root layout now imports
  `useColorScheme` from `@/hooks/use-color-scheme`. tsc + lint clean.

## Also noted, not filed as findings

Smaller items surfaced during review, recorded so they are not lost:

- `Math.min(...lats)` and three sibling spreads in src/app/result.tsx:19-24 run over an unbounded
  provider-supplied array; a 500 km ORS geometry is large enough to risk a stack overflow on Hermes.
- eas.json sets no per-profile `env`, so `development` and `preview` builds fall back to the production
  Railway URL in src/api/config.ts:10 — dev testing bills the production provider key.
- `docx@^9.7.1` is a runtime dependency in package.json with zero imports in `src/` or `scripts/`.
- The `destination` TextInput is bound to state and read by nothing; unlike the curviness card it carries
  no "coming soon" affordance, so a user who types a destination silently gets a loop.
- `distanceValid` has no upper bound client-side while the backend caps at 500 km, so an over-limit entry
  costs a round-trip to learn; `parseFloat('40,5')` also yields 40 on comma-decimal locales.
- `RouteValidation.Validate(StitchRequestDto)` enforces a minimum of 2 waypoints but no maximum.
- api/Program.cs has no `ILogger` call on any failure path.
- The `RouteStitchException` to status `switch` is duplicated verbatim at Program.cs:82-87 and 113-118.
- `EarthRadiusMeters` is declared twice in the same namespace (RouteGenerator.cs:11, GeoMath.cs:6).
- `use-generate-route-mutation.ts` performs navigation inside `onSuccess`, unlike the pure
  `use-health-query.ts` precedent; a screen unmounted mid-flight still triggers the push.
- CLAUDE.md's routing section still describes the pre-`(tabs)` layout, and package.json:2 still reads
  `"name": "bootstrap-scaffold"`.
