---
project: RideForge
version: 1
status: draft
created: 2026-06-09
context_type: greenfield
product_type: mobile
target_scale:
  users: small
  qps: low
  data_volume: small
timeline_budget:
  mvp_weeks: 3
  hard_deadline: null
  after_hours_only: true
---

# RideForge

## Vision & Problem Statement

A recreational motorcyclist wants to ride for pleasure, but map apps optimize for speed — twisty roads get avoided, not sought. When a rider has free time but no destination, there is no tool that takes their constraints (duration, curviness preference, pace character) and generates a ready-to-use route. Existing routing tools require the rider to already know the roads or manually draw the route; they are editors, not generators.

The insight: no current tool combines ride-character input (how long, how twisty, fast vs touristic) with automatic GPX generation and a description of what the ride will feel like — including interesting points to stop along the way (viewpoints, villages, cafés). The rider shows up with a plan, not a blank map.

## User & Persona

**Primary persona: The recreational motorcyclist**
Any individual rider — regardless of bike type or country — who rides for pleasure, not for commuting or delivery. They have free time and a bike. They want a ride that matches their mood and energy, not just the shortest path. They are comfortable loading a GPX file into their device or navigation app. Their frustration: planning a good ride takes too long, and generic apps don't understand what makes a route fun.

## Success Criteria

### Primary
- A rider enters a starting location, ride duration, curviness level, and pace (fast vs touristic); hits "Generate"; sees a route drawn on a map; and downloads a GPX file that loads correctly in standard navigation apps.

### Secondary
- Logged-in riders can save generated routes and revisit past rides from their account.

### Guardrails
- The generated GPX file must be valid and loadable in mainstream navigation apps (Garmin, Google Maps, OsmAnd) — a broken GPX is a hard regression.
- The route must depart from the rider's specified starting location — a route generated from a wrong or random location invalidates the product.

## User Stories

### US-01: Rider generates a twisty route from their location

- **Given** a rider who has opened RideForge and entered a starting location, a ride duration, a curviness level, and a pace style
- **When** they hit "Generate"
- **Then** they see a route drawn on a map that departs from their starting location, and a "Download GPX" button becomes available

#### Acceptance Criteria
- Route departs from the specified starting location
- Route duration is within ±20% of the requested duration
- Downloaded GPX file opens correctly in Garmin, Google Maps, and OsmAnd
- Generation completes without requiring a login

## Functional Requirements

### Route generation

- FR-001: Rider can enter a starting location. Priority: must-have
  > Socrates: Counter-argument considered: "starting location alone isn't enough — riders need to specify a region or area to stay within." Resolution: valid gap. A radius/area constraint is not captured in any current FR. Flagged as Open Question 4 — likely a must-have addition before v1 ships.

- FR-002: Rider can specify ride length as either a duration (hours) or a distance (km). Priority: must-have
  > Socrates: Counter-argument considered: "duration is too abstract — riders who know roads think in km, not hours." Resolution: both inputs are valid rider models. FR-002 updated to offer duration OR distance as the length constraint.

- FR-003: Rider can set a curviness preference via a slider. Priority: must-have
  > Socrates: Counter-argument considered: "a slider is misleading if the routing engine can't guarantee curviness — the output may not match the input." Resolution: kept as must-have; the slider represents an optimization preference, not a guarantee. The UI must communicate this clearly. Implementation must validate the routing API's actual curviness support before shipping.
  > Update 2026-08-11: curviness is now RideForge's **own algorithm**, not an external API feature (see Open Question 2 resolution). The slider still represents a preference, not a guarantee; the "validate external curviness support" caveat no longer applies.

- FR-004: Rider can set the ride pace style (fast or touristic). Priority: must-have
  > Socrates: Counter-argument considered: "'fast vs touristic' is vague without a definition — riders will interpret it differently." Resolution: kept as must-have; but both terms must be defined in the UI (e.g. via tooltip or label examples) before launch. See Open Question 1.

- FR-005: Rider can trigger route generation from their inputs. Priority: must-have
  > Socrates: Counter-argument considered: "a single Generate button with no feedback is a dead end if generation is slow or fails." Resolution: kept as must-have; visible loading and error states are required companions. The 30-second generation limit (see Non-Functional Requirements) bounds the maximum wait.

- FR-013: Anonymous riders are limited to 2 route generations per hour; signed-in riders have no limit. Priority: must-have
  > Added during S-05 implementation (`rider-auth`), not from upstream discovery. Each generation is a billed third-party directions call on a publicly reachable endpoint, so an unbounded anonymous endpoint is an open tab against the project's budget. Enforced server-side (client-side would be one devtools away from gone), counted per install with the caller's IP as the fallback for requests that carry no usable install id.
  > This does not violate the US-01 acceptance criterion "Generation completes without requiring a login": generation still needs no account, and 2/hour is above what a rider planning one ride actually uses. The limit is what gives FR-008 a reason to exist — signing in is the thing that removes it, which is the first concrete benefit an account confers.
  > Numbers are configuration (`GenerationQuota__PermitLimit`, `GenerationQuota__WindowMinutes`), not constants, so tuning needs no code change. Counters are in-memory and single-instance for the MVP: they reset on redeploy, and the limit is looser than 2/hour during active development.

- FR-006: Rider can view the generated route on a map. Priority: must-have
  > Socrates: Counter-argument considered: "a map preview adds a heavy third-party dependency before the routing even works." Resolution: kept as must-have — seeing the route before downloading is the minimum trust signal. Use the lightest viable map library (e.g. Leaflet + OpenStreetMap) to minimize dependency weight.

- FR-007: Rider can download the generated route as a GPX file. Priority: must-have
  > Socrates: Counter-argument considered: "GPX format is not universally familiar — riders won't know how to load it." Resolution: kept as must-have; GPX file should be auto-named meaningfully (e.g. RideForge-2h-twisty-2026-06-09.gpx) and a brief 'how to use' hint should appear near the download button.

### Account & saved routes

- FR-008: Rider can create an account and log in. Priority: nice-to-have
  > Socrates: No counter-argument; keep as written. Auth ships if time allows, does not block v1.

- FR-009: Logged-in rider can save a generated route. Priority: nice-to-have
  > Socrates: No counter-argument; keep as written.

- FR-010: Logged-in rider can view a list of their saved routes. Priority: nice-to-have
  > Socrates: No counter-argument; keep as written.

- FR-011: Rider can include points of interest (cafés, viewpoints, villages) as physical waypoints on the generated route. Priority: nice-to-have

- FR-012: Rider can optionally set a maximum radius from the starting point to constrain the riding area. Priority: nice-to-have

## Non-Functional Requirements

- Route generation (from input submission to route visible on map) completes within 30 seconds for any valid input combination.

## Business Logic

RideForge generates a motorcycle route from a rider's starting point by selecting roads that match their curviness and pace preferences, routing through real points of interest as natural stopping points, and exporting the result as a GPX file.

**Inputs (user-facing):** starting location, ride length (duration in hours or distance in km), curviness level (slider: straight → very twisty), pace style (fast or touristic), and optionally a toggle to include POI waypoints.

**Output:** a route drawn on a map, downloadable as a GPX file. When POI waypoints are enabled, the GPX includes waypoints at real places (cafés, viewpoints, villages) that fall naturally on or near the route.

**How the rider encounters it:** they fill in the generation form, hit Generate, and see the result on a map within 30 seconds. They review it visually and download the GPX if they like it. No knowledge of the roads is required up front — the product makes the routing decision for them.

## Access Control

Anonymous users can generate a ride and download the GPX file without creating an account — no login barrier on the core value. Generation is rate-limited for them (FR-013), which is a ceiling on a billed call, not a login barrier: the feature is available, and signing in lifts the ceiling.

Registered users (flat model — all accounts equal) can additionally save named routes and revisit past generated rides.

No role separation. No admin tier in the MVP.

## Non-Goals

- **No turn-by-turn navigation.** RideForge generates and exports routes; it is not a GPS navigator. Riders load the GPX into their existing navigation app. Real-time guidance is permanently out of scope, not deferred.
- **No social or community features.** No route sharing, no public library, no following other riders, no likes or comments. v1 is a personal tool. Community features are a deliberate non-goal, not a v2 candidate.

## Open Questions

1. **What does "fast" vs "touristic" mean concretely?** — Needs a UI definition before launch (e.g. tooltips or label examples). Owner: user. Block: yes (UI clarity required before FR-004 is shippable).
2. **~~Which routing API supports curviness optimization?~~ RESOLVED 2026-08-11 — hybrid, own algorithm.** Curviness optimization is now RideForge's **own algorithm**, not an external feature: the backend generates candidate waypoints / route shape from the curviness + pace inputs, and a commodity directions / map-matching API only stitches those waypoints into a road-following route. This removes the dependency on any provider's curviness support. Remaining sub-question (non-blocking): which directions API does the stitching — GraphHopper Directions, OpenRouteService, Mapbox Directions, or self-hosted OSRM. Any road-following directions API works since curviness is no longer sourced externally. Owner: user. Block: no.
3. **Which POI data source should be used for FR-011?** — Candidates: OSM Overpass API (free, global), Google Places (paid), Foursquare. Owner: user. Block: no (FR-011 is nice-to-have).
4. **Riding area / radius constraint** — FR-001 Socrates round flagged that a starting location alone may be insufficient; riders may need to constrain the riding area to a radius around the start point. No current FR captures this. If must-have, a new FR must be added before v1 ships. Block: pending decision.
