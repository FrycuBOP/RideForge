---
change_id: s-01
title: Generate route from start + length, show on map (north-star S-01 / generate-route-preview)
status: implementing
created: 2026-08-24
updated: 2026-08-24
archived_at: null
---

## Notes

<!-- Free-form notes for this change: links, ad-hoc context, decisions that don't belong in research/frame/plan. -->

Roadmap north-star slice S-01 (`generate-route-preview`). Prerequisites **F-01 + F-02
are both done**, so the slice is now unblocked (roadmap Backlog Handoff still says "no —
czeka na F-01+F-02", now stale). Research 2026-08-24 (`research.md`): the F-01 input shell
+ F-02 stitching backend exist, but the **generation core (waypoint algorithm +
`/route/generate`), the map preview (`react-native-maps` not even installed), and the
end-to-end wiring are all absent.** Six decisions listed in `research.md` §Open Questions
need answering before `/10x-plan`.
