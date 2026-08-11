---
id: mobile-backend-link
title: "Mobile ↔ backend link (F-01)"
status: planned
created: 2026-08-11
updated: 2026-08-11
roadmap_ref: F-01
prd_refs: [FR-005]
---

# Mobile ↔ backend link (F-01)

Foundation from `context/foundation/roadmap.md` (F-01). Establishes how the Expo app
reaches the Railway backend: a typed fetch client with a normalized `ApiError`, an
environment-based base URL, a TanStack Query provider, and a `/health` round-trip
surfaced in-app to prove connectivity and set the loading/error convention (FR-005)
that S-01 and later slices reuse.

Unlocks: S-01 (`generate-route-preview`) and every later backend-calling slice.
