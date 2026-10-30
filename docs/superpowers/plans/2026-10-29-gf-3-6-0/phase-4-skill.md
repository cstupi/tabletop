# Phase 4 — The rulebook skill: two references, one method (design step 5)

Index: `index.md`. Spec: `2026-10-29-rule-set-versions-design.md` → "The rulebook skill". Group **D**: independent of every other phase; one worktree; 4.2–4.4 may run in parallel after 4.1 (they write disjoint page files). Phase 5's citations are checked against these pages, so D should merge before phase 5's review.

Facts the executor needs: the skill is tracked (`git ls-files .claude/skills/licensed` → 73 files; plugin `licensed`, `.claude/skills/licensed/.claude-plugin/plugin.json`). A page file is `# Grimdark Future v3.5.1 — page N` then the page's text, plain, one line per printed line, transcribed from `pypdf` output (`./venv/bin/python`, `pypdf.PdfReader(path).pages[N-1].extract_text()`; `r.pages[N]` is printed page `N+1`). The 3.6.0 PDF is `/mnt/c/Users/clays/Downloads/GF_-_Advanced_Rulebook_v3_6_0.pdf` (74 pages; verified readable). Diagram pages are measured as `docs/handoff.md` → Environment says (`pypdf` image extraction and Pillow; no `pdftotext`, no headless Chrome). The 3.6.0 mission-card images (pp.69–74) are identical to 3.5.1's pp.66–71 (modules diff → page map), so their transcriptions are copied with the header changed. Deployment diagrams (3.6.0 pp.21–23) are on a 6" grid without inch labels; the modules diff gives M1 (Side Battle 36"/48") and M2 (Tactical Push bump 24" tall, 12" from each end) as the only geometry changes; the other eleven match 3.5.1's labels.

---

### Task 4.1: `v3.5.1/` and the edition step in `SKILL.md` — **Sonnet**

**Files:**
- Move (`git mv`): `.claude/skills/licensed/skills/grimdark-future-rules/pages/` → `.../v3.5.1/pages/`
- Modify: `.claude/skills/licensed/skills/grimdark-future-rules/SKILL.md`: title "Grimdark Future rules (v3.5.1 and v3.6.0)"; a new first step under "Answering a rules question": "0. Which edition: the game's `game_rules` — `grimdark-future` is v3.5.1 (`v3.5.1/pages/`), `grimdark-future-3.6` is v3.6.0 (`v3.6.0/pages/`). A bare `p.N` in code or docs means v3.5.1; a v3.6.0 citation is written `3.6.0 p.N`. When the question names no game, answer for both when they differ."; the grep path in step 1 gains the edition folder; the page index and special-rules list move under a heading "v3.5.1 page index" (4.4 adds the 3.6.0 one); "Mistakes to avoid" gains "Citing a 3.6.0 page as a bare `p.N`, or reading a 3.6.0 page for a 3.5.1 game."
- Modify: every reference to `grimdark-future-rules/pages/` in the repo: `docs/handoff.md:431-436`, `AGENTS.md`, `server/game/AGENTS.md`, `docs/design/client/rulings-and-references.md` (grep `skills/grimdark-future-rules/pages`).

- [ ] **Step 1:** `git mv`, edit SKILL.md, `grep -rn "grimdark-future-rules/pages" --exclude-dir=node_modules --exclude-dir=obj .` → every hit updated.
- [ ] **Step 2: Verify**: `ls .claude/skills/licensed/skills/grimdark-future-rules/v3.5.1/pages | wc -l` → 71; the grep prints nothing stale; invoke the skill (`Skill` tool, `licensed:grimdark-future-rules`) and confirm it loads.
- [ ] **Step 3:** commit `git commit -am "Rulebook skill: v3.5.1 pages in their own folder; the edition step"`.

---

### Task 4.2: Transcribe v3.6.0 pp.1–20 — **Sonnet**

**Files:**
- Create: `.claude/skills/licensed/skills/grimdark-future-rules/v3.6.0/pages/p01.md` … `p20.md`
- Create (scratchpad, not committed): a Python script that writes each page's `extract_text()` under the header `# Grimdark Future v3.6.0 — page N`, then hand-correct: hyphenation across lines, column order, headings in capitals as the PDF has them, bullet glyphs; keep one printed line per file line as the v3.5.1 files do (compare `v3.5.1/pages/p13.md` with `pages[12].extract_text()` to see the house style).

- [ ] **Step 1:** generate the 20 raw files. **Step 2:** correct each against the PDF text (p.17 Fast-Play is new; pp.11–12 terrain are reordered: placement on p.11, types on pp.11–12). **Step 3: Verify**: for each page, the word multiset of the file (header line excluded) equals the word multiset of `extract_text()` after stripping hyphenation and page furniture (the script prints per-page differences; zero differences, or only the page-footer words). **Step 4:** commit `git commit -m "Rulebook skill: v3.6.0 pp.1-20"`.

---

### Task 4.3: Transcribe v3.6.0 pp.21–23 (deployment diagrams) — **Opus**

**Files:**
- Create: `v3.6.0/pages/p21.md`, `p22.md`, `p23.md`

- [ ] **Step 1:** extract the three pages' images with `pypdf` (`page.images`) into the scratchpad; measure each zone on the 6" grid with Pillow (count grid squares; the table is 72" × 48" = 12 × 8 squares) as the modules diff did.
- [ ] **Step 2:** write each diagram as a sentence per zone in the v3.5.1 house style (see `v3.5.1/pages/p22.md`): e.g. "Side Battle: zone A is the right triangle in the bottom-left corner with legs 36" up the short edge and 48" along the long edge; zone B is its mirror in the top-right corner." Tactical Push: "zone A is 18" deep with a 24"-tall, 24"-wide bump 12" from each short edge" — confirm the widths by measurement; M2 in the modules diff names only the bump height and offsets.
- [ ] **Step 3: Verify**: M1 and M2 measurements match the modules diff; every other style's numbers match the v3.5.1 transcription's. Note any further difference found in the page file and in the modules diff (a real finding, not a plan item).
- [ ] **Step 4:** commit `git commit -m "Rulebook skill: v3.6.0 pp.21-23 deployment diagrams"`.

---

### Task 4.4: Transcribe v3.6.0 pp.24–74; the v3.6.0 index — **Sonnet**

**Files:**
- Create: `v3.6.0/pages/p24.md` … `p74.md` (pp.69–74: copy `v3.5.1/pages/p66.md`…`p71.md`, header changed; verify the card images are identical by comparing the extracted image bytes' hashes between the two PDFs)
- Modify: `SKILL.md`: a "v3.6.0 page index" table from the page map in `2026-10-29-gf-3-6-0-rules-diff.md` → Page map (core 4–15; packs 16; Fast-Play 17; advanced actions 18–20; deployment 21–23; missions 24–28; terrain 29–33; casting and relic wargear 34–37; battlefield effects 38–41; command points, twists 42–45; fog of war, psychology, brutal damage 46–48; Political Intrigue 49; boarding 50; Meat Grinder 51; multiplayer … LOS variants 52–58 (Hybrid True LOS p.58); solo 59–60; Horde Mode 61–62; campaign 63–68; mission cards 69–74) and a "v3.6.0 special rules (pp.13–15)" list: p.13 Aircraft, Ambush, AP(X), Artillery, Assault, Bane, Blast(X), Caster(X), Counter; p.14 Cumbersome, Deadly(X), Fast, Fear(X), Fearless, Flying, Furious, Hero, Immobile, Impact(X), Indirect, Limited, Regeneration, Relentless, Rending (verify the p.14/p.15 break against the PDF: p.15 begins at Scout); p.15 Scout, Shred, Slow, Spell Amplifier, Stealth, Strider, Surge, Takedown, Thrust, Tough(X), Transport(X), Unstoppable.

- [ ] **Step 1:** generate and correct as in 4.2. **Step 2:** the index and list. **Step 3: Verify**: 74 files; the word-multiset check passes for every page; `grep -c "Grimdark Future v3.6.0 — page" v3.6.0/pages/*.md` → 74 ones; the special-rules list's page breaks agree with the files. **Step 4:** commit `git commit -m "Rulebook skill: v3.6.0 pp.24-74 and the v3.6.0 index"`.
