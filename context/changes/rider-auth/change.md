---
change_id: rider-auth
title: Rider auth + anonymous generation quota
status: implementing
created: 2026-09-08
updated: 2026-09-09
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
