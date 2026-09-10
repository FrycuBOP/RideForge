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
