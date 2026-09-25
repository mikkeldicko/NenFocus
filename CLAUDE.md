# NenFocus — Claude Working Agreement

## Authoritative documents
These files govern every decision. Read them, do not override them, surface drift instead of silently fixing it.

- @ARCHITECT_GOVERNANCE.md — constitution, Design Gap Protocol, one-line open-gap index
- `OPEN_GAPS.md` — full write-ups of every open gap; not auto-loaded, read on demand before working on or designing near one
- @DOMAIN_DICTIONARY.md — canonical domain vocabulary, current-state technical mappings, kept lean (2026-09-10 split, see below)
- `DOMAIN_DICTIONARY_NEN_CONTRACT.md` — Nen Contract deep-dive (Redemption Rules, Strictness Rule mechanics, build history); not auto-loaded, read on demand like `DESIGN_GAP_HISTORY.md`
- @MODULE_STANDARDS.md — layer rules, Deep Module test, ViewModel Forbidden Zone, naming
- `TECHNICAL_SPEC_INFRA.md` — current implementation state
- `TESTING_GUIDE.md` — TDD workflow
- `UI_UX_STANDARDS.md` — colors, typography, layout, ViewLocator convention, Planner drag-interaction mechanics
- `DESIGN_GAP_HISTORY.md` — record of closed gaps, for precedent

## Process (governs how you work, not the code)
- **Architecture decisions belong to Mikkel.** Claude executes; does not silently change design.
- **Design Gap Protocol is mandatory before any new feature.** Interrogate design first, produce a written plan, get sign-off, then implement. Never skip to code.
- **Surface drift, don't fix silently.** If code contradicts governance docs, flag it and ask.
- **Clean Architecture + DDD, CQRS marker interfaces, ViewModel Forbidden Zone all apply.** See `MODULE_STANDARDS.md` and `DOMAIN_DICTIONARY.md` for specifics before proposing changes in those areas.
- **Git commits are authored by Mikkel, not Claude.** Never add `Co-Authored-By: Claude`, "🤖 Generated with Claude Code", "Assisted by Claude", or any similar attribution to commit messages or PR descriptions. Write commit messages as if Mikkel wrote them.
- **"Update MD file" routes by content type, not by habit.** A completed fix/feature has two different things worth recording, and they go in different files — check both, most real changes need both:
  - The *narrative* (what broke, why, rejected alternatives, principle demonstrated) → a new dated entry in `DESIGN_GAP_HISTORY.md`.
  - The *current-state fact* (what a concept/method/class does right now) → the matching technical-mapping row in `DOMAIN_DICTIONARY.md` (or `DOMAIN_DICTIONARY_NEN_CONTRACT.md` if it's Nen Contract detail). Terse — what it is, current behavior, one line pointing back to the Gap number for the story. Never re-tell the narrative inline.
- **Keep `DOMAIN_DICTIONARY.md` lean — it's auto-loaded every session.** A not-yet-built feature gets a one-line pointer to its `ARCHITECT_GOVERNANCE.md` Open Gap, never a duplicated design writeup. Pixel-level UI/interaction mechanics belong in `UI_UX_STANDARDS.md`, not here. If a section starts accumulating a full build history rather than current vocabulary, split it into its own file (see `DOMAIN_DICTIONARY_NEN_CONTRACT.md` for precedent) rather than letting the core file grow back.
- **`TECHNICAL_SPEC_INFRA.md`'s "Done" list is not a third place for the narrative.** It already suffered the same duplication problem once (2026-09-10 audit — ~130 of 235 lines were retelling `DESIGN_GAP_HISTORY.md`/`DOMAIN_DICTIONARY.md` a second or third time, plus stale renamed-type references nothing caught). A "Done" entry there gets one line — what shipped, a pointer to the Gap number — never the story. Genuinely unique content (platform gotchas, SQLite quirks, DI registration traps — things that answer "how," not "why") stays as-is; that's this file's real job.

## Style
- Compact answers. One question at a time when interrogating design.
- Plain-English analogies for abstract concepts.
- Never assume; ask if the governance docs don't cover a case.
- **All output in English.** Every response, code comment, commit message, thinking step, and internal note must be written in English, even when the underlying model prefers another language. If a non-English phrase appears in a response, rewrite that response in English before finalizing.