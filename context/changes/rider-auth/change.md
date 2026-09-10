---
change_id: rider-auth
title: Rider auth + anonymous generation quota
status: implementing
created: 2026-09-08
updated: 2026-09-10
archived_at: null
---

## Notes

Phase 2 item 2.7 (anonymous generation still works) was blocked by a defect outside this change:
`POST /route/generate` answered 422 for starts inside pedestrian zones, because the provider's
350 m snap radius could not reach a drivable road. Pre-existing S-01 behaviour — no `api/` file is
touched by this change. Fixed separately in `a93a57f`, after which 2.7 verified clean.

Scope beyond roadmap S-05: generation stays available without an account, but anonymous riders are
capped at 2 generations per hour (server-enforced). Signing in removes the cap — that is what makes
the account worth creating. The quota is not in the PRD or the roadmap yet; plan phase 5 syncs both.

Gap found during phase 5 manual verification: a signed-in rider was still capped after two
generations. The limiter was correct — the client was not sending the token. Phase 4 made `auth`
an opt-in flag on `request()` and gave it only to `/me`; phase 5 specified the
`X-RideForge-Install` header but never said `/route/generate` should authenticate, so the
backend saw every caller as anonymous and the signed-in exemption was unreachable from the app.
Fixed by opting `generateRoute` into `auth: true`.

No automated test could have caught this: the backend's exemption test passes because it presents a
token itself, and there is no frontend test runner (deliberate, per `test-plan.md` §4). The gap is
in the wiring between the two, which nothing in the current harness exercises.
