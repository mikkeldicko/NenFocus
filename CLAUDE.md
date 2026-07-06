# NenFocus — Claude Working Agreement

## Authoritative documents
These files govern every decision. Read them, do not override them, surface drift instead of silently fixing it.

- @ARCHITECT_GOVERNANCE.md — constitution, Design Gap Protocol, open gaps
- `DOMAIN_DICTIONARY.md` — canonical domain vocabulary
- `MODULE_STANDARDS.md` — layer rules, Deep Module test, ViewModel Forbidden Zone, naming
- `TECHNICAL_SPEC_INFRA.md` — current implementation state
- `TESTING_GUIDE.md` — TDD workflow
- `UI_UX_STANDARDS.md` — colors, typography, layout, ViewLocator convention
- `DESIGN_GAP_HISTORY.md` — record of closed gaps, for precedent

## Process (governs how you work, not the code)
- **Architecture decisions belong to Mikkel.** Claude executes; does not silently change design.
- **Design Gap Protocol is mandatory before any new feature.** Interrogate design first, produce a written plan, get sign-off, then implement. Never skip to code.
- **Surface drift, don't fix silently.** If code contradicts governance docs, flag it and ask.
- **Clean Architecture + DDD, CQRS marker interfaces, ViewModel Forbidden Zone all apply.** See `MODULE_STANDARDS.md` and `DOMAIN_DICTIONARY.md` for specifics before proposing changes in those areas.
- **Git commits are authored by Mikkel, not Claude.** Never add `Co-Authored-By: Claude`, "🤖 Generated with Claude Code", "Assisted by Claude", or any similar attribution to commit messages or PR descriptions. Write commit messages as if Mikkel wrote them.

## Style
- Compact answers. One question at a time when interrogating design.
- Plain-English analogies for abstract concepts.
- Never assume; ask if the governance docs don't cover a case.
- **All output in English.** Every response, code comment, commit message, thinking step, and internal note must be written in English, even when the underlying model prefers another language. If a non-English phrase appears in a response, rewrite that response in English before finalizing.