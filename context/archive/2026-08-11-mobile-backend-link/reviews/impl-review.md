<!-- IMPL-REVIEW-REPORT -->
# Implementation Review: Mobile ↔ backend link (F-01)

- **Plan**: context/changes/mobile-backend-link/plan.md
- **Scope**: Phase 1 + Phase 2 (full plan, all phases complete)
- **Date**: 2026-08-16
- **Verdict**: NEEDS ATTENTION
- **Findings**: 0 critical, 2 warnings, 1 observation

## Verdicts

| Dimension | Verdict |
|-----------|---------|
| Plan Adherence | PASS |
| Scope Discipline | WARNING |
| Safety & Quality | PASS |
| Architecture | PASS |
| Pattern Consistency | WARNING |
| Success Criteria | PASS |

## Findings

### F1 — Backend modified despite "No backend changes" guardrail

- **Severity**: ⚠️ WARNING
- **Impact**: 🔎 MEDIUM — real tradeoff; pause to reason through it
- **Dimension**: Scope Discipline
- **Location**: api/Program.cs:4 (CORS), api/Program.cs:15 (host binding)
- **Detail**: The plan's "What We're NOT Doing" states "No backend changes." But `api/Program.cs` gained (a) a default CORS policy (`AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()`) and (b) host-binding logic (bind `localhost` locally, `0.0.0.0` when `PORT` is set). The CORS addition is not scope creep in spirit — the Phase 2 success criterion "app boots on web (`npm run web`) … indicator transitions to connected" is impossible cross-origin without it, so the guardrail itself was wrong. The host-binding change is a dev-ergonomics tweak (avoids the Windows firewall prompt) genuinely unrelated to F-01. Security note: `AllowAnyOrigin` is acceptable for this public, unauthenticated read API, and the code already annotates scoping it when auth (FR-008) lands.
- **Fix A ⭐ Recommended**: Document both backend edits as a plan addendum and correct the "No backend changes" line to "No new endpoints; CORS added to enable the web target."
  - Strength: Fixes the source of truth so the guardrail matches reality; the CORS change is legitimately required for the verified web round-trip.
  - Tradeoff: Plan becomes a slightly moving target.
  - Confidence: HIGH — the web success criterion provably depends on CORS.
  - Blind spot: None significant.
- **Fix B**: Keep only CORS under F-01; split the host-binding tweak into its own infra note/commit.
  - Strength: Keeps F-01 scoped strictly to what connectivity required.
  - Tradeoff: Bookkeeping for a 2-line, already-merged change.
  - Confidence: MED — depends on whether you track infra tweaks separately.
  - Blind spot: Haven't checked if other envs rely on the old bind-all behavior.
- **Decision**: FIXED (Fix A) — plan guardrail corrected + Addendum added documenting CORS & host-binding.

### F2 — Unplanned refactor of use-color-scheme.web.ts

- **Severity**: ⚠️ WARNING
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Scope Discipline
- **Location**: src/hooks/use-color-scheme.web.ts:1
- **Detail**: Rewritten from a `useState` + `useEffect` hydration flag to `useSyncExternalStore`. It's a genuine improvement (no setState-in-effect on hydration) but nothing in F-01 called for touching the color-scheme hook. `tsc` + `lint` pass and it's isolated, so risk is low — it's simply outside the change's declared surface.
- **Fix**: Note it in the plan addendum (one line), or record a brief lessons entry. No code change needed.
- **Decision**: FIXED — one-line note added to plan Addendum.

### F3 — Hardcoded error color in dev status pill

- **Severity**: 🔷 OBSERVATION
- **Impact**: 🏃 LOW — quick decision; fix is obvious and narrowly scoped
- **Dimension**: Pattern Consistency
- **Location**: src/components/backend-status.tsx:28
- **Detail**: The component pulls every color from `useTheme()` except the error state, which hardcodes `#E5484D`. `src/constants/theme.ts` has no danger/error token, so this is a gap-fill rather than a deviation for its own sake. Dev-only surface, so impact is negligible.
- **Fix**: Leave as-is for now, or add a `danger` color to `Colors` in src/constants/theme.ts and reference `theme.danger`.
- **Decision**: SKIPPED — dev-only surface, negligible impact; revisit if a danger token is added.
