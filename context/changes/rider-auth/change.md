---
change_id: rider-auth
title: Rider auth + anonymous generation quota
status: implementing
created: 2026-09-08
updated: 2026-09-09
archived_at: null
---

## Notes

Scope beyond roadmap S-05: generation stays available without an account, but anonymous riders are
capped at 2 generations per hour (server-enforced). Signing in removes the cap — that is what makes
the account worth creating. The quota is not in the PRD or the roadmap yet; plan phase 5 syncs both.
