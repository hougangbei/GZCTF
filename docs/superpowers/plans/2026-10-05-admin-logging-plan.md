# Admin Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add structured audit records for every administrator mutation and user container start/stop, retain all authenticated Flag attempts with 30-day recoverable plaintext, and provide a compact three-tab administrator log console.

**Architecture:** Keep `LogModel` as the system-status source. Add `AuditEvent` and `FlagAttemptLog` tables, a shared audit action catalog and request-aware writer, a Flag-attempt writer protected with ASP.NET Data Protection, and server-paginated admin APIs. The UI fetches one page at a time and fetches sensitive Flag details only when expanded.

**Tech Stack:** .NET 10 / ASP.NET Core MVC / EF Core / PostgreSQL / xUnit integration tests / React / Mantine / TypeScript / Vitest / Playwright.

---

## File map and invariants

- `src/GZCTF/Features/Auditing/Domain/AuditEvent.cs`, `FlagAttemptLog.cs`: focused persistent records. No request body or expected Flag in either type; only Flag-attempt ciphertext may contain a submitted value.
- `src/GZCTF/Features/Auditing/Application/AuditWriter.cs`, `AuditActionCatalog.cs`, `FlagAttemptWriter.cs`: one event per attempted action, explicit category/action names, safe failure reasons, encryption and expiry policy.
- `src/GZCTF/Features/Auditing/Api/AdminAuditController.cs`, `AdminFlagAttemptsController.cs`: admin-only list/detail APIs with 20-item default page, total count and bounded filters.
- `src/GZCTF/Features/Auditing/Infrastructure/AuditTargetResolver.cs`, `AuditActionFilter.cs`: target snapshot and common success/failure capture. Apply metadata to state-changing administrator actions; `GET` actions that create draft state are included. An inventory test compares all state-changing actions with the catalog.
- `src/GZCTF/Models/AppDbContext.cs`, `src/GZCTF/Migrations/*`, `src/GZCTF/Extensions/Startup/ServicesExtension.cs`, `src/GZCTF/Extensions/Startup/AppExtensions.cs`: persistence, indexes, services and request boundary.
- `src/GZCTF/Features/ChallengeRuntime/Api/ChallengeSubmissionsController.cs`, `ChallengeInstancesController.cs`: capture all logged-in attempts before early rejection and record user-initiated container actions.
- `src/GZCTF/Services/CronJob/RuntimeCronJobs.cs`: daily UTC ciphertext cleanup.
- `src/GZCTF/Repositories/LogRepository.cs`, `src/GZCTF/Models/Request/Admin/LogMessageModel.cs`: add total count, stable `(TimeUtc, Id)` sort, source and exception fields for system status.
- `src/GZCTF/ClientApp/src/pages/admin/Logs.tsx`, nearby focused components, `src/GZCTF/ClientApp/src/locales/{zh-CN,en-US}/admin.json`: three tabs, collapsed rows, page controls and filters.
- `src/GZCTF.Integration.Test/Tests/Auditing/*`, `src/GZCTF/ClientApp/tests/e2e/admin-logs.spec.ts`: meaningful backend and browser verification.

Database migrations are additive. Preserve `Role.Admin`, its existing edit control and existing `ChallengeSubmissions.SubmittedFlagHash`. Never append raw submitted Flag, password, token or request body to Serilog, operation audit, browser console or notifications. Use UTC for timestamps and expiry checks. Do not classify automatic container expiry as user stop.

## Task 1: Add persistent records and schema

**Files:** Create the two domain types and a new EF migration; modify `AppDbContext.cs`.

- [ ] **Step 1: Write failing persistence tests.** Add `AuditSchemaTests.cs` under `src/GZCTF.Integration.Test/Tests/Auditing/` that resolves `AppDbContext`, inserts one audit event and one Flag-attempt row, and asserts both can be queried after a new scope. Assert composite time/ID indexes through EF metadata.
- [ ] **Step 2: Run the focused test.** `dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~AuditSchemaTests`; expect missing domain/DbSet failures.
- [ ] **Step 3: Define exact records.** `AuditEvent` has `Id: Guid`, `OccurredAtUtc: DateTimeOffset`, `ActorId: Guid?`, `ActorName: string`, `ActorKind`, `Category`, `Action`, `TargetType`, `TargetId: string?`, `TargetName: string?`, `Succeeded: bool`, `HttpStatus: int`, `ErrorCode: string?`, `ErrorReason: string?`, `RequestId: string`, `AffectedCount: int?`. `FlagAttemptLog` has `Id`, `OccurredAtUtc`, `UserId`, `UserName`, `ChallengeId`, `ChallengeName?`, `SubmissionId?`, `Outcome`, `RejectionCode?`, `ProtectedSubmittedFlag?`. Use bounded lengths for codes/names and `text` for ciphertext.
- [ ] **Step 4: Add DbSets, indexes and an additive migration.** Create indexes for `(OccurredAtUtc, Id)`, `(Category, OccurredAtUtc, Id)`, `(ActorId, OccurredAtUtc, Id)`, `(Outcome, OccurredAtUtc, Id)` and `(ChallengeId, OccurredAtUtc, Id)` as applicable. Use this project's EF migration style and update `AppDbContextModelSnapshot`.
- [ ] **Step 5: Run focused tests and commit.** Expect schema tests pass; commit only the schema and test files.

## Task 2: Add operation-audit capture and cover administrator mutations

**Files:** Create `AuditWriter.cs`, `AuditActionCatalog.cs`, `AuditActionFilter.cs`, `AuditTargetResolver.cs`; modify startup registration, administrator controllers and user container controller.

- [ ] **Step 1: Write failing writer and route-inventory tests.** Verify writer rejects or strips secret-bearing data by accepting only explicit fields. Enumerate MVC actions protected by `[RequireAdmin]` or `[RequireAdminOrToken]`; every state-changing action must have a catalog entry, including a GET that materializes a draft, while read-only POST search is explicitly excluded. Verify two admins may grant the existing `Admin` role and both can read audit endpoints.
- [ ] **Step 2: Run focused tests.** `dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~Audit`; expect failures identifying uncovered routes.
- [ ] **Step 3: Implement an explicit catalog and request scope.** Use fixed category/action codes and target type per action. Capture actor from the current authenticated user or API token identity, route/request ID, target ID and pre-mutation target name where needed. The writer stores one success or failure record after result status is known; map 400/401/403/404/409/500 to controlled failure reasons. Do not serialize action arguments or request bodies.
- [ ] **Step 4: Instrument all administrator side effects.** Cover `AdminController`, `ApiTokenController`, `AssetsController`, `EditPostsController`, admin challenge/lesson/tree/category/instance/settings/dashboard/cohort/import controllers and admin writeup review. Include persistent changes made by draft retrieval and external QQ test sends. Capture created target ID/name from returned domain result, and capture deleted target name before deletion. Batch actions store batch ID or target type plus affected count. Add user container start/stop; exclude automatic expiry and read-only requests.
- [ ] **Step 5: Verify outcomes and coverage.** Integration tests must assert create/delete, role grant, validation failure, missing target, authorization rejection, bulk operation and container success/failure each create one correctly labeled record. Inspect audit rows for absence of Flag, password and token values. Run route-inventory test until no uncovered action remains.
- [ ] **Step 6: Commit the audit layer and coverage.** Keep unrelated files and generated assets out of this commit.

## Task 3: Record Flag attempts and clear submitted plaintext after 30 days

**Files:** Create `FlagAttemptWriter.cs`; modify `ChallengeSubmissionsController.cs` and `RuntimeCronJobs.cs`; add `FlagAttemptTests.cs` and `FlagRetentionTests.cs`.

- [ ] **Step 1: Write failing tests.** Submit correct and incorrect Flags, an empty value, a limit-exhausted value and an inaccessible challenge value as a logged-in user. Assert exactly one attempt event per request, accepted/incorrect attempts link an existing submission ID, rejected attempts still exist, and the expected Flag never appears in audit or Serilog records. Assert unauthenticated requests do not create a Flag event.
- [ ] **Step 2: Run the focused tests.** `dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~FlagAttempt`; expect missing event failures.
- [ ] **Step 3: Implement protected original storage.** Use ASP.NET Data Protection with a dedicated purpose string and protect the exact user-supplied value; do not trim or normalize it. The writer stores metadata and ciphertext once a logged-in request begins, updates the outcome after access validation and judging, and uses safe rejection codes. A failure to write must be reported in system status, without rerunning a completed submission.
- [ ] **Step 4: Add expiry at read and at rest.** The Flag-detail API returns no original once `OccurredAtUtc + 30 days <= UtcNow`, regardless of cleanup timing. A daily cron job executes a set-based update that nulls only `ProtectedSubmittedFlag` for expired rows and can run repeatedly.
- [ ] **Step 5: Run focused tests and commit.** Verify 29-day original decrypts, 30-day original does not, cleanup preserves metadata and submission hash, and existing scoring tests still pass.

## Task 4: Add safe server-paginated administrator APIs

**Files:** Create `AdminAuditController.cs`, `AdminFlagAttemptsController.cs`, query services/DTOs; modify `LogRepository.cs`, `ILogRepository.cs`, `LogMessageModel.cs`, `AdminController.cs` for system status paging.

- [ ] **Step 1: Write failing API tests.** Seed 25 records with ties in timestamp. Assert default 20 rows, page 2 has 5, total count is 25, tie sort uses ID, category/result/name-or-ID filters work, and invalid page/count values return 400. Assert ordinary and downgraded users get 403, anonymous users get 401; Flag original is absent from list JSON and appears only in authorized detail before expiry.
- [ ] **Step 2: Run focused tests.** `dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj --filter FullyQualifiedName~AuditApi`; expect missing route or payload failures.
- [ ] **Step 3: Implement three bounded queries.** Use `AsNoTracking()`, SQL filtering, `CountAsync`, `OrderByDescending(Time).ThenByDescending(Id)`, `Skip`, `Take`; default 20 and cap 100. Return `{ items, total, page, pageSize }`. Search actor/target by snapshot name and ID. Audit and Flag details return one record by ID; do not include protected payload on list. System status details include source and exception, retaining the current level selector.
- [ ] **Step 4: Test, inspect OpenAPI and commit.** Run focused API tests; verify ordinary users cannot receive Flag plaintext through any endpoint. Generate/update the client API shape using the repository's `pnpm genapi` flow when an app server is available; otherwise create a focused typed client wrapper and keep `Api.ts` generated file untouched.

## Task 5: Build the three-tab collapsed log console

**Files:** Modify `src/GZCTF/ClientApp/src/pages/admin/Logs.tsx`; create small `AuditLogPanel.tsx`, `FlagAttemptPanel.tsx`, `SystemLogPanel.tsx` and shared `LogPager.tsx` under `src/GZCTF/ClientApp/src/components/admin/logs/`; modify `zh-CN/admin.json` and `en-US/admin.json`.

- [ ] **Step 1: Write browser behavior tests.** In `admin-logs.spec.ts`, assert default tab, 20 compact rows, per-row expand, category/result filters reset page, page 2 loads only its own records, Flag plaintext arrives only after expansion, expired original shows unavailable, and a new SignalR system log raises a refresh indicator without prepending to the current page.
- [ ] **Step 2: Run the browser test against the existing page.** `pnpm --dir src/GZCTF/ClientApp test:e2e admin-logs.spec.ts`; expect missing tabs/rows failures.
- [ ] **Step 3: Implement fetch and view state.** Keep each tab's filter, page, loading and error state isolated. Render summary rows inside accessible buttons or disclosure controls with `aria-expanded`; fetch Flag detail on open. Render error and retry instead of an empty success state. Translate result/category labels, and display time in the selected locale.
- [ ] **Step 4: Replace live prepend.** Keep the existing SignalR connection for system status, count unseen records and show a “new records” button. Clicking it resets to page 1 and refetches; do not log received content to `console`.
- [ ] **Step 5: Verify and commit.** Run `pnpm --dir src/GZCTF/ClientApp check`, `pnpm --dir src/GZCTF/ClientApp test:unit`, and focused Playwright tests. Check desktop and narrow layouts for clipped disclosure controls.

## Task 6: Release verification

- [ ] **Step 1: Run focused and existing backend suites.** `dotnet test src/GZCTF.Test/GZCTF.Test.csproj` and `dotnet test src/GZCTF.Integration.Test/GZCTF.Integration.Test.csproj`; if native `dotnet` is unavailable, use a compatible .NET 10 SDK container with the repository mounted and Docker daemon available.
- [ ] **Step 2: Run frontend checks.** `pnpm --dir src/GZCTF/ClientApp check`, `pnpm --dir src/GZCTF/ClientApp test:unit`, and focused admin log Playwright test.
- [ ] **Step 3: Review diff against the approved spec.** Confirm no role model change, no raw Flag in generic logs or list responses, every administrator side effect in the action catalog, 30-day gating plus cleanup, three pages with server pagination, and no unrelated files staged.
- [ ] **Step 4: Report results and limits.** State exact passing commands and any environment-limited tests; do not claim unrun tests passed.
