# Lesson workspace implementation plan

Goal: deliver approved A layout, Markdown editing/preview, lesson management and personal theme propagation.

Architecture: keep existing skill-tree URLs and lesson access checks; branch lesson rendering before the challenge modal. Reuse the platform Markdown renderer, asset upload API, publication selection and Mantine theme. Local recovery stores only unsaved editor content per account/lesson; server save and publication remain explicit.

1. Add failing browser checks for full-page lesson navigation, completion, heading anchors, Markdown editor preview/save/recovery, and theme inheritance. Extend unit checks for Markdown insertion and headings.
2. Create a lesson reader with collapsible tree navigation, heading TOC, previous/next links, completion state, mobile drawer and safe anchored headings. Preserve challenge modal behavior and existing access policies.
3. Create a focused Markdown editor with formatting insertion, edit/split/preview modes, safe image upload/paste/drop, matching rendered preview, keyboard save, failure state, before-unload/internal-navigation protection and opt-in local draft restore. Avoid automatic server writes.
4. Upgrade lesson administration index to searchable/filterable list with publication locations and create-then-edit. Keep localization data on edits; don't overwrite other locales.
5. Replace local fixed accent/surface overrides with the global theme variables. Honor personal Custom/Default/Managed choice, system/light/dark settings across relevant platform surfaces.
6. Run frontend unit tests/type checking/build and focused Playwright suites, inspect desktop/mobile screenshots, review changed files for content safety and data-loss behavior. Finish locally; publication/deployment is a later request.

Validation: use real React routes with mocked HTTP boundaries; assert no dialog on lesson route; switch content and check state isolation, duplicate/fenced-code headings, save errors, local restore, uploads, published-save warning, theme changes and narrow-screen overflow. Existing challenge/branding/theme tests must continue passing.

Completed: all six tasks implemented and reviewed. Verification: 30 frontend unit tests, 46 browser regressions plus the additional paste/drop/formatting case, the final reader/editor 11-case rerun, strict TypeScript check and production build. No backend/database/online deployment changes.
