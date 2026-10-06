# Platform Update Management Implementation Plan

> **For agentic workers:** Implement each checkbox in order. This plan is scoped to the Docker Compose self-update path.

**Goal:** Let administrators detect a successfully built GitHub image and update the deployed application without replacing its database or uploaded files.

**Architecture:** GitHub Actions publishes SHA-tagged GHCR images. An admin-only ASP.NET API proxies fixed commands to a host updater over a Unix Socket. The updater backs up persistent content, changes only the application image, and checks Docker health.

**Tech Stack:** GitHub Actions, GHCR, Docker Compose, Python 3 standard library, ASP.NET Core 10, React 19/Mantine.

---

### Task 1: Publish installable images

- [x] Add `.github/workflows/self-update-image.yml` for `main` pushes and manual runs.
- [x] Publish `sha-<commit>` and `main` tags to GHCR with an OCI revision label.
- [x] Keep this workflow independent of upstream Docker Hub and Aliyun credentials.

### Task 2: Provide persistent Compose deployment

- [x] Add `deploy/self-update/compose.example.yml` and `.env.example` with a PostgreSQL volume, host upload directory, fixed image variable, and updater socket/secret mounts.
- [x] Add installation and recovery instructions, including private-image authentication and the fact that database migrations may prevent image-only rollback.

### Task 3: Implement host updater

- [x] Add `deploy/self-update/updater.py` using a local Unix HTTP socket and token authentication.
- [x] Query the successful GitHub workflow run; compare its SHA with the running image label.
- [x] Pull and verify the target image, stop the application, back up PostgreSQL and uploads, atomically set the image, recreate only the application, and wait for health.
- [x] Persist task state and retain backup paths for operator recovery. Accept only a fixed repository and a validated SHA.

### Task 4: Connect admin API and page

- [x] Add an admin-only ASP.NET endpoint and Unix Socket client, with an audit action on update requests.
- [x] Add an administrator “更新管理” page showing status, workflow link, progress, and a confirmed update action.
- [x] Show a clear setup state when the updater is unavailable.

### Task 5: Validate without changing live data

- [x] Run Python syntax validation, frontend type checking, backend compilation, Compose/JSON parsing, and `git diff --check`.
- [x] Inspect the final diff for unrelated files and report installation steps that still require the actual deployment host.
