# HBC-style GZCTF Frontend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reshape the GZCTF frontend around the approved HaoBaChang visual structure while preserving all existing routes, APIs, and permissions.

**Architecture:** Keep Mantine, SWR, and React Router. Replace the shared sidebar shell with a horizontal responsive header. Use dedicated landing and account layouts, then carry the same tokens into existing pages.

**Tech Stack:** React 19, TypeScript, Mantine 9, CSS modules, Vitest, Playwright.

---

### Task 1: Shared navigation and theme

**Files:** `src/GZCTF/ClientApp/src/components/{AppHeader,AppNavbar,WithNavbar}.tsx`, their CSS modules, `src/GZCTF/ClientApp/src/styles/App.css`, `src/GZCTF/ClientApp/src/utils/ThemeOverride.ts`, `src/GZCTF/ClientApp/tests/e2e/shared-shell.spec.ts`.

- [x] Add a browser test requiring a horizontal desktop header and a keyboard accessible mobile menu; run it and confirm the old sidebar fails.
- [x] Implement the header, mobile menu, shared tokens, and purple theme. Preserve existing routes, account actions, language selection, theme switching, `WithNavBar` props, and skip link.
- [x] Run targeted browser tests and `pnpm check`.

### Task 2: Landing page

**Files:** `src/GZCTF/ClientApp/src/pages/Index.tsx`, `src/GZCTF/ClientApp/src/styles/pages/Index.module.css`, localizations as needed, and browser test.

- [x] Add a failing browser test for the landing hero, real destination links, and latest-post state.
- [x] Implement the hero, four route cards, decorative terminal, and latest-post section using the existing API and `PageState`.
- [x] Verify desktop and mobile screenshots with a real backend and test keyboard navigation.

### Task 3: Account pages and global fit

**Files:** `src/GZCTF/ClientApp/src/components/AccountView.tsx`, its CSS module, `AppFooter.tsx` and CSS, About CSS, browser test.

- [x] Add a failing test for the split login layout and responsive form.
- [x] Implement the shared account layout and footer. Recheck public pages and existing admin shell behavior.
- [x] Run `pnpm check`, `pnpm test:unit`, `pnpm build`, browser tests, and real-data visual checks at 390px and 1440px.
