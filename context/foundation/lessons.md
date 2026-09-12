# Lessons Learned

> Append-only register of recurring rules and patterns. Re-read at start by /10x-frame, /10x-research, /10x-plan, /10x-plan-review, /10x-implement, /10x-impl-review.

## A native module in a leaf utility takes the whole app down with it

**Context:** `src/lib/install-id.ts` imported `expo-crypto` at module scope for one UUID.
That util sits in the chain `install-id → api/client → api/index → use-me-query → account.tsx`.

**Problem:** On a dev build without the native module, `requireNativeModule('ExpoCrypto')`
threw during module evaluation. Every module downstream failed to evaluate, both route files
lost their default export, and the app died on `Cannot read property 'ErrorBoundary' of
undefined` — an error naming neither the module nor the feature. Typecheck, lint and the
backend suite were all green; nothing catches this before a device runs it.

**Rule:** A native dependency pulled in at module scope by a leaf utility escalates a missing
module into a total boot failure. Before adding one, ask what breaks if it is absent — and if
the answer is "more than this feature", either load it lazily behind a try/catch with a
fallback, or place it where its absence is contained. Adding a native module also always means
a new dev build; say so unprompted when a phase introduces one.

**Applies to:** any `expo-*` package with a native side, imported from `src/lib/` or `src/api/`.

## Settle a native navigator's structure before it mounts

- **Context**: Any phase that changes the structure of a native navigator at
  runtime — the set of `NativeTabs.Trigger`s, a tab's `hidden`, or screens added
  to or removed from a `Stack` — driven by async app state such as a restoring
  session.
- **Problem**: S-07 hid the Saved tab with `hidden={session === null}`. Session
  restore resolves a few frames after launch, so the trigger set changed exactly
  as Android's `TabsContainer` was attaching; it flushed a pending tab update and
  committed a fragment transaction from inside the activity's own resume
  transaction — `FragmentManager is already executing transactions`, dead on
  launch. Typecheck, lint and the web preview were all green throughout.
- **Rule**: Never let a native navigator's structure change underneath an
  attached instance. Settle it *before* the instance mounts: gate the layout on
  the async state it depends on, then key the navigator on that state so a change
  swaps in a fresh instance instead of mutating a live one. And treat such a
  phase as unverified until it has run on a device — typecheck, lint and the web
  bundle cannot see this class of fault.
- **Applies to**: plan, implement, impl-review
