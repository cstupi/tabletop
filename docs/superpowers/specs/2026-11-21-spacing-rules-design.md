# Spacing rules

Owner, 2026-11-21: *"Lets set some rules and add to our agents.md … add spacing and hints around spacing to improve all our uis."* Source:
Ayush S. Mathur, "Spacing & Alignment in UI: Creating Visual Rhythm and Breathing Room" (Design Systems Collective, 2025-11-13). Applies to
the Godot client, the web app and the viewer. Owner: the client is the priority; the browser needs no settings.

## Rules

1. **One scale.** Every padding, margin, gap and separation is a step of the scale: 4, 8, 16, 24, 32, 48 px. 4 is the half-step for
   icon-to-label and dense list rows only. No 2, 3, 5, 6, 10, 12, 14, 18, 20, 22, 28 (borders and outlines are not spacing).
2. **Inside smaller than outside.** A container's padding is smaller than the gap between it and its siblings (card: 16 inside, 24
   between cards). Items that belong together sit closer than items that do not: a label and its value 4–8, rows of a group 8, groups 16–24,
   sections 32–48.
3. **Shared edges.** Titles, text, controls and buttons in a panel start on one left edge. Text is left-aligned; centre only a short heading
   or a lone call to action; right-align only numbers in tables and key caps in buttons.
4. **Even padding.** A panel's padding is the same on all four sides unless a rule names why (a band's left edge).
5. **Micro and macro.** Micro: padding inside a button, gap between icon and text (4–8). Macro: between sections, cards, columns (24–48).
6. **More space, less relation.** Space carries grouping before lines or boxes do; add a divider only where space cannot.
7. **When in doubt, more space** on full-screen pages (Home, lobby, Options, the web app). The in-game Hud over the table stays compact
   (the scale's lower steps) because padding there hides the table.
8. **Named, never literal.** Code names a step, never a number:
   - Godot: `Space.XS` 4, `Space.S` 8, `Space.M` 16, `Space.L` 24, `Space.XL` 32, `Space.XXL` 48 (`client/src/Theme/Space.cs`).
   - Web and viewer: `--space-1` 4px, `--space-2` 8px, `--space-3` 16px, `--space-4` 24px, `--space-5` 32px, `--space-6` 48px
     (`web/src/themes.css`, `viewer/src/app.css`).
   A literal spacing number in a UI file is a defect; `./scripts/check-client` reports one in `client/src/{Hud,Panels,Screens,Theme}`.

## Where it goes

- Root `AGENTS.md` → Rules for every module: one line pointing here.
- `client/AGENTS.md`, `web/AGENTS.md`, `viewer/AGENTS.md` → a "Spacing" recipe: the constant or variable names, the file, the check.
- Every existing spacing literal in the three UIs moves to the nearest step that keeps the rules (inside < outside); where a layout breaks,
  the layout changes, not the scale. Fixed control sizes (a 220 px button, a 48 px swatch) are sizes, not spacing, and stay.

## Left to the builder

Which step each existing value maps to, screen by screen, judged by rule 2; checked on screen at 1280×720 and 1920×1080.
