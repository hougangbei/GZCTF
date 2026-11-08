# Challenge flags, administrator test sessions and reader layout

> **For agentic workers:** Use subagent-driven-development; no Astra. Prior import-purpose/publication-feedback commit is the base and must remain included.

**Goal:** Fix literal dynamic template injection, preserve imported runtime/Flag semantics during editing, stop editor-owned tests when leaving, and refine the learner challenge workspace after layout preference confirmation.

**Architecture:** Keep content identity and active student instance snapshots intact. Generate dynamic Flags with the existing GZCTF template engine (`[GUID]`, existing GZCTF markers), using the same path for native and imported challenges; remove `{userId}` substitution and explicitly reject obsolete templates on new starts/publication. Preserve runtime origin settings and static metadata on editor save. Bind administrator tests to a unique per-editor session and exact instance ID. Use same-origin keepalive release on unmount/pagehide and short server lease + existing expiry cleanup as fallback. Do not stop student instances or source CTFd containers.

**Tech stack:** .NET10/EF/PostgreSQL, Docker, React/Mantine/SWR, Vitest/Playwright.

## Backend

- [x] Regression: GUID template is never literal, source-aware hash works, stale `{userId}` rejects, random CTFd/default generation and static validation remain intact.
- [x] Unify runtime generation; maintain source provenance and Whale FLAG alias even for previously edited CTFd imports. No silent static Flag rewriting.
- [x] Add administrator-only test session lifecycle under existing instances API: start query `testSessionId` UUID, POST `test-heartbeat` and `test-stop` bodies `{instanceId,testSessionId}`. Require role, owning user, active exact instance ID and adminTest/session marker. Stale requests cannot stop/revive replacements.
- [x] Heartbeat renews <=90sec expiry (instance and container); normal student lifetime/extend stays unchanged; keep old API compatibility for clients without session during transition.
- [x] Test lease expiry/renewal, wrong user/role/session, stale IDs, start/close races, legacy tests, existing student instance protection and admin no-score behavior.

## Frontend

- [x] Failing editor roundtrip tests: preserve FlagMode/ImportSourceInstance, attachments and static IgnoreCase; normalize network mode casing; unified template hint/default `[GUID]`, reject old `{userId}` in editable dynamic form.
- [x] Preserve origin metadata without copying stale editable aliases; keep source attachment references/Flags intact on unrelated edits.
- [x] Per-editor session hook starts with UUID (works over intranet HTTP), heartbeat while mounted, guarded stop on pagehide/unmount/context/account change. If start resolves after unmount, stop its exact instance; server lease covers abrupt browser exit.
- [x] Browser regression for navigation/close, cancelled dirty navigation, late start resolution and student workspace isolation.
- [x] Layout waits for user selection: proposed vertical description/compact instance bar/submission area/collapsible help. Keep personal theme/light/dark, keyboard, mobile, copy/time/extend/stop/WP features.

## Delivery

- [x] Typecheck, unit + integration sequential .NET, focused browser flow and production build.
- [x] Independent spec/quality review and fix findings.
- [x] Record validation/limits; commit locally. No automatic production deployment or fresh release unless requested.

## Delivery notes

User confirmed vertical layout. Backend365unit/297integration, frontend66unit,57relatedbrowsercases plus final6sessionchecks passed. Build/typecheckpassed; existinglintanddependencywarningsremain. Independentreviewpassed after attachmentmetadata and BFCache/late-save race fixes. No migration, image release or production deployment. Detailed operational limitations: docs/superpowers/specs/2026-11-08-challenge-flags-test-sessions.zh-CN.md.
