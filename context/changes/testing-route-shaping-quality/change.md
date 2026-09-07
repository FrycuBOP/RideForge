---
change_id: testing-route-shaping-quality
title: Route-shaping quality harness — curviness/variety invariants (Risk #1; test-plan §3 Phase 2)
status: preparing
created: 2026-08-24
updated: 2026-08-24
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

Opened by `/10x-research` for test-plan Risk #1 (test-plan §3 Phase 2, "Route-shaping
quality harness"; covers Risks #1 and #4). Research finding: the **subject under test —
the curviness shaping algorithm — does not exist in the codebase yet.** `api/Routing/` is
exclusively the stitching boundary (F-02); no waypoint generation, no curviness input, no
variety metric. See `research.md`.

**Decision 2026-08-24 (user):** stop at research. No tests/metrics this session — the
oracle and the two-faces analysis in `research.md` stand as the record. Revisit Phase 2
when roadmap S-01 (`generate-route-preview`) and S-02 (`curviness-shaping`) land, at which
point Face-A property tests (monotonicity, monotony floor, ±20% duration) become writable.
