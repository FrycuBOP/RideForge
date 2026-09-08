---
change_id: s-01
title: Generate route from start + length, show on map (north-star S-01 / generate-route-preview)
status: implemented
created: 2026-08-24
updated: 2026-09-08
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

**Phase 3 note (2026-09-07):** the free OpenRouteService tier is unreliable under load —
after a burst of calls the directions endpoint slows past the 10s ceiling and the backend
returns 504 (verified: the same 40 km request went 0.62s → >10s timeout within minutes). The
client handles it correctly (504 → error message). This is Risk #5 (infra): production needs a
paid ORS tier or a self-hosted OSRM/GraphHopper. Phase-3 flow testing therefore used the `fake`
provider; the real-road ±20% checks (2.4–2.6) passed earlier while ORS was responsive.

**Phase 4 note (2026-09-08):** the first end-to-end run drew a straight-line octagon and
reported 12.5 km for a 20 km request — the Railway deploy was serving the `fake` stitcher.
`api/appsettings.json` commits `Provider: "fake"` on purpose (local/CI need no ORS key), so the
deploy must override it with `RouteStitching__Provider=openrouteservice` +
`RouteStitching__ApiKey`. Symptom signature worth remembering: 9 geometry points and a distance
of exactly `distanceKm / DetourFactor` means fake, not a generator bug. After the flip: 640
points, 19.06 km (-4.7%), 0.16s.
