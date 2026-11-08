# Platform visual theme refinement

User approved scope: whole platform (learning and administration), neutral light/dark surfaces, readable controls, personal accent preserved. Existing vertical challenge workflow stays.

- [x] Central neutral surface/text/border/control tokens shared by Mantine resolver and CSS; test WCAG text4.5:1 and critical control3:1 on actual background layers.
- [x] Light background#f4f5f7, white cards, soft inset#eceef1; dark charcoal#141618, cards#202326, inset#292d31. Tune final numeric values to measured contrast; subdued but readable muted text.
- [x] Align Mantine body/card/default/control/placeholder/modal/menu/table/disabled states with tokens. Separate filled accent background from readable accent text; custom palettes choose accessible shade across all surfaces. Preserve platform/custom/default/system providers.
- [x] Remove admin and per-page neutral palette forks. Audit hardcoded nonsemantic text/backgrounds and legacy theme leakage; semantic status colors/chart series remain meaningful.
- [x] Review watermark contrast/readability without disabling identity/security behavior.
- [x] Browser coverage: light/dark/custom/managed/theme switching at homepage, directory, challenge, lesson, library/editor, authoring and ranking; input/placeholder/border/disabled/focus; mobile overflow and screenshots.
- [x] Unit/type/build, related browser regressions and independent visual/code review. Record limits, local commit; no automatic production deployment.

Validation:75frontendunit;59relatedbrowserchecks allpass;16theme/layoutfocus followup checks cover finalfocus treatment. Typecheck/productionbuild/focusedlintpass. Independentvisual+codereviewaccepted after fixing semanticcontrols, priorityshadowing, customhovercontrast and interactiveborders. No backend/migration/productionchanges.
