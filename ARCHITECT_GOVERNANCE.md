# NenFocus — Architect Governance

**This is the Constitution. Read this at the start of every session.**

For detailed vocabulary, see `DOMAIN\\\_DICTIONARY.md`.
For implementation details and build state, see `TECHNICAL\\\_SPEC\\\_INFRA.md`.
For UI rules and ViewModel contracts, see `UI\\\_UX\\\_STANDARDS.md`.
For module and layer guardrails, see `MODULE\\\_STANDARDS.md`.
For TDD workflow and test patterns, see `TESTING\\\_GUIDE.md`.
For historical record of closed design gaps, see `DESIGN\\\_GAP\\\_HISTORY.md`.

\---

## High-Level Development Strategy

### 1\. Strategic Design \& The Design Gap

**Principle:** Bridge the Design Gap before writing any new features.

Before implementing anything, ask as many questions as necessary to reach a shared design concept and understand the full dependency tree. Do not write implementation code until the design is agreed upon. This means: contract first, implementation second.

### 2\. Ubiquitous Language (DDD)

**Principle:** Use a shared vocabulary to eliminate communication overhead.

Always refer to the terms defined in `DOMAIN\\\_DICTIONARY.md` (Ritual, Session, Enforcement, Policy Slice, Focus Block, Background Block, SessionOrigin, Quest, Deep Module, etc.). If a new concept needs to be introduced, define and name it together before writing any code.

**Product invariant:** A manual enforcement session cannot exist without a completed Ritual. This is a product invariant enforced by design, not by validation code. The full Nen Ritual (Ten → Zetsu → Ren → Hatsu) is the only path to creating a Spontaneous Session. Abandoning the Ritual at any point produces no saved record anywhere.

### 3\. TDD — Headlight Management

**Principle:** Never outrun the headlights.

Take small, deliberate steps. When possible, follow a TDD workflow: write a failing test, make it pass, then refactor. Avoid generating large blocks of unverified code. Each increment must be provably correct before the next one begins.

### 4\. Deep Modules — Cognitive Load Management

**Principle:** Complexity must be hidden behind simple interfaces.

Aim for Deep Modules — classes and services that deliver significant functionality through a narrow, obvious API. Concretely:

* **ViewModels** must be kept shallow: no timers, no math, no string formatting. Delegate all tactical logic (countdowns, progress calculation, time formatting) to Infrastructure or Application services.
* **Services** are gray boxes: the human architect should be able to understand and compose them from their interface alone, without reading the implementation.
* When a ViewModel grows beyond "display data + dispatch commands," that is a signal to extract a new service.

\---

## What This App Is

NenFocus is a Windows desktop focus tool built with Avalonia (.NET 9). It combines app/website blocking, intentional session creation via a 4-step ritual wizard, calendar-based focus planning, and session insights.

The name and ritual terminology (Ten, Zetsu, Ren, Hatsu) are inspired by the concept of "Nen" from Hunter × Hunter — each ritual step has a thematic purpose that maps to the session setup flow.

\---

## Architecture

Clean Architecture with 4 code projects + 1 test project, plus one non-.NET sibling:

```
NenFocus.Domain          → Entities, value objects, domain events, enums. No dependencies.
NenFocus.Application     → Use cases (CQRS commands/queries), interfaces, validators, DTOs.
NenFocus.Infrastructure  → EF Core + SQLite, Windows enforcement, toast notifications, DI wiring.
NenFocus                 → Avalonia UI: ViewModels, Views, converters. Entry point.
NenFocus.Tests           → xUnit unit tests for Domain and Application layers.
NenFocusExtension        → Chrome/Edge/Brave MV3 browser extension (JS, not .NET). Talks to
                            WindowsEnforcementService's local HTTP API (localhost:5005) as an
                            external client — same relationship the UI has to Application, just
                            across a process/language boundary. Deliberately not nested inside
                            NenFocus.Infrastructure: it isn't compiled into that assembly, has its
                            own manifest/packaging/deployment story, and nesting it there would
                            only be cosmetic while implying a build relationship that doesn't exist.
```

Dependencies only flow inward: UI → Application ← Infrastructure → Domain. `NenFocusExtension` sits outside this flow entirely — it depends on nothing but Infrastructure's local HTTP contract (`/check`, `/report`).

**Key patterns:**

* **CQRS**: `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` — all mutations are commands, all reads are queries. No service classes that do both. **Exception:** an Application service that only coordinates a Domain decision into an outbound port — it reads the decision internally and acts on it, never serving reads to the UI — is allowed (e.g. `IEnforcementSynchronizer`, Gap 62). The rule forbids services that mix UI-facing reads with writes. A command reads first, then changes, and never runs a query after changing — a query sees the database, not the command's unsaved changes; reactions to what a command did go in event handlers, which run after the commit (Gap 110).
* **Repository pattern**: interfaces in Application, EF Core implementations in Infrastructure. A repository is a collection — `Add`, `Remove(aggregate)` and reads, no update or save; the unit of work around every command saves what changed, and saves nothing when the command fails (Gap 110; `MODULE_STANDARDS.md` → Repositories are collections).
* **Domain events**: raised inside entities/aggregates, published via `IInProcessDomainEventPublisher`, handled by Application-layer event handlers. Commands do not directly call enforcement or notifications — event handlers do.
* **MVVM**: CommunityToolkit.Mvvm (`ObservableObject`, `RelayCommand`). ViewModels never reference Views.
* **Value objects**: `record` types with validation in the constructor — `SessionGoal`, `SessionDuration`, `BreakTimer`, `BlockedApp`, `BlockedSite`.
* **Vertical Slice Architecture**: Every use case lives in its own feature folder (e.g., `UseCases/Sessions/StartSession/`) containing exactly four files: `Command`, `CommandHandler`, `Result`, `Validator`. Validators are auto-registered via `AddValidatorsFromAssembly`.
* **Enforcement pipeline**: `HeartbeatService` (the one heartbeat, 5s interval, one `now` per tick — Gap 89), the session event handlers and `BreakTransitionResponder` (break boundaries, reported by `ActiveSessionMonitor`) → `IEnforcementSynchronizer` (sole caller that turns policy into an apply/remove) → `ComputeActivePolicySliceQueryHandler` / `PolicyEngine` (what should be blocked) → `WindowsEnforcementService` (kills processes, rewrites hosts file, exposes local HTTP API).

\---

## Project Structure

```
📄 ARCHITECT\\\_GOVERNANCE.md    ← Constitution (this file)
📄 DOMAIN\\\_DICTIONARY.md       ← Ubiquitous language + technical mappings
📄 MODULE\\\_STANDARDS.md        ← Layer rules, ViewModel Forbidden Zone, naming conventions
📄 TECHNICAL\\\_SPEC\\\_INFRA.md    ← Build state, enforcement details, dev notes
📄 TESTING\\\_GUIDE.md           ← TDD workflow, NSubstitute patterns, test inventory
📄 UI\\\_UX\\\_STANDARDS.md         ← Colours, layout, ViewLocator, time formatting
📄 OPEN\\\_GAPS.md              ← Full write-ups of open, deferred gaps (read on demand)
📄 DESIGN\\\_GAP\\\_HISTORY.md      ← Record of closed design gaps (reference only)
📁 NenFocusExtension/          ← Browser extension (JS/MV3), own folder, not part of the .NET solution



## The Design Gap Protocol

This protocol is **mandatory** before any implementation code is written for a new feature. Its purpose is to close the Design Gap — the distance between what is currently visible in the codebase and what is needed to implement the feature correctly.

### Step 1 — The Interrogation (Grill the Architect)

Before writing any implementation code, Claude MUST ask at least 5–10 clarifying questions covering:

1. **Trigger:** What event, user action, or system condition initiates this feature?
2. **Dependency tree:** Which existing interfaces, entities, and services will this feature touch or depend on?
3. **New contracts:** Will this feature require a new `ICommandHandler`, `IQueryHandler`, or service interface? What does the signature look like?
4. **State ownership:** Which layer owns the state this feature introduces? Domain entity, Application service, or UI observable?
5. **Edge cases:** What happens if the feature is triggered while a Session is active? While on break? At app startup? At shutdown?
6. **Persistence:** Does this feature require a new DB column, table, or migration?
7. **Enforcement interaction:** Does this feature interact with `HeartbeatService` or `WindowsEnforcementService`?
8. **UI contract:** What observable properties and commands does the ViewModel need to expose? What XAML bindings does the View need?
9. **Failure modes:** What happens if the underlying operation fails? How does the UI surface the error?
10. **Rollback / undo:** Is there a compensating action? (e.g., if session start fails after enforcement is applied)

This interrogation happens in conversation. Code is not typed until every answer is agreed upon.

### Step 2 — Contract First

The first code artifact for any new feature is **always** its contract: an interface in `NenFocus.Application/Interfaces/` for a port, or a Domain service's `sealed class` signatures (bodies not yet written) in `NenFocus.Domain/Services/` — see `MODULE_STANDARDS.md` → Contract-First Rule (Gap 111). No handler, service, or ViewModel code is written until the contract is:

* Reviewed and approved by the architect
* Consistent with the Deep Module test (understandable in under 30 seconds)
* Free of infrastructure types in its signature

### Step 3 — Thin Vertical Slice

Once the contract is approved, implement the thinnest possible vertical slice:

* Domain entity changes (if any)
* One command or query handler
* One ViewModel binding
* No parallel work on adjacent features

### Step 4 — Verify Before Expanding

Each slice must compile and produce the correct output before the next slice begins. If the project has an active test suite (`NenFocus.Tests`), a passing test is required before moving on. This is the "headlight management" rule: never implement further than you can verify.

### Step 5 — Guard Before Closing (added 2026-09-29)

Before any gap is closed — every gap, not only the Architecture box — ask: **"Did this gap reveal a rule a test could guard?"** Answer it explicitly in the close-out, and record the answer in the gap's `DESIGN_GAP_HISTORY.md` entry:

* **Yes →** add the rule to `NenFocus.Tests/ArchitectureTests/` (or a parity/regression test when it's a single case), probe it red with a deliberate violation, and write it into `MODULE_STANDARDS.md` if it isn't already there. Follow the Guard Hierarchy: if the compiler can own the rule, prefer that, and note which gap will.
* **The gap fixed something a test lists →** delete that exception entry (the test stays red until you do).
* **No →** say why in one line (e.g. a judgment call no test can see, or a one-off already pinned by a regression test).

**If the gap changed Domain code, also run Stryker on the files it changed (added 2026-10-08, Gap 113):** every surviving behaviour change gets a test, or a one-line reason it can't be tested — wording-only and equivalent mutants are ignored. Record the result in the history entry. At the end of each box, run it over the whole Domain and record the score. How-to: `TESTING_GUIDE.md` → Mutation Testing.

Why: written rules alone didn't stop drift; every rule a gap makes true but leaves unguarded can quietly come back. How-to: `TESTING_GUIDE.md` → Architecture Tests.

\---

## Open Gaps

Gaps that have been identified but not yet closed. Each is here because deferring it is a deliberate choice, not because it has been forgotten. **Full write-ups live in `OPEN_GAPS.md`** (not auto-loaded — read it before working on, or designing anything adjacent to, one of these). References elsewhere to "`ARCHITECT_GOVERNANCE.md` → Open Gap <letter>" resolve there via the bracketed letter. When a gap closes, its write-up moves to `DESIGN_GAP_HISTORY.md` under the same number and its line below is removed.

**Naming and order are separate (2026-09-28).** A gap gets the next free number the moment it is raised and keeps it forever, open or closed — the number is a name, not a rank. Priority is this list's order alone: position 1 is next up; reprioritising or slotting in an urgent gap means moving lines, never renumbering. Gaps raised before 2026-09-28 carry their old letter in brackets so older references still resolve; no new letters are issued.

Grouped into six boxes, worked top to bottom (agreed 2026-09-28; architecture box added first 2026-09-29). Order within a box is a suggestion; the box order is the rule.

**1. Architecture — one source of truth, deep modules, DDD (added 2026-09-29).** Not new features: making the existing code scalable before more is built on it. Rules for this work: `MODULE_STANDARDS.md` → Where a Rule Lives and Design Review Rules. **Every gap in this box ends with an architecture test for the rule it made true** (Gap 98, closed 2026-09-29 — tests in `NenFocus.Tests/ArchitectureTests/`, how-to in `TESTING_GUIDE.md`) — a gap isn't done until its rule is guarded by a test that fails on drift, and the exception entries it retires are deleted.
1. **Gap 89 — Architecture Audit, Areas 2–5** (PlannerPageViewModel, two heartbeats' clocks, in-memory session vs database, Nen Contract queries) — raised 2026-09-28; areas 3 and 4 read 2026-09-29 (Pass 2: F1–F6; F2, the running tile unlocked after a restart, fixed in Gap 93 — the Planner reads its locks from the block's `Allowances`; area 4's F1, F3 and refresh direction built in Gap 92, closed 2026-10-07). Moved here from box 3 on 2026-09-29; area 5 (Nen Contract queries) largely investigated as Gap 95. Finished in four slices (2026-10-25): **A — one heartbeat** (area 3) built 2026-10-25; then B (ViewModel audit), C (F7, session DTO `TimeSlot`s), D (R3, preset lock).
2. **Gap 114 — Walk-in Blocks: Every Session Belongs to a Block** (a spontaneous run creates a walk-in `FocusBlock`; a miss is the block's stored outcome, not a `Session`; absorbs the missed-sweep findings, Gap 79 and Gap 67's session half) — raised 2026-10-08; direction agreed, not interrogated. After Gap 110 (closed 2026-10-13: its new saving code is written in the collection style once) and before Gap 107, whose overlays these records feed; before box 2, whose contract gaps build on it.
3. **Gap 115 — Query Handler Tests on Real SQLite** (the 21 query test files stop mocking repositories, as Gap 110 did for command handlers; `TestDoubleRuleTests`' shrink-only list counts what's left) — raised 2026-10-08 during Gap 110's interrogation; direction agreed, not built. After Gap 114, which rewrites several of those queries.
4. **Gap 107 — One Owner for What the Shell Shows Next** (`ShellViewModel` decides which overlays are owed — post-session, contract recap, Ritual — their order and the missed-sessions triage rule, at startup and live) — raised 2026-10-01 during Gap 97's interrogation; findings recorded, not interrogated. Finding 5 done in Gap 92 (the Shell no longer marks blocks missed); the relays it will own wait in the interim `IRecapRelay`.

**2. Contracts — now**
1. **Gap 66 (W) — Contract Wizard: "When" Step and One-Click Date/Time Fields** — interrogated 2026-09-28; plan agreed.
2. **Gap 67 (R) — Contract Membership: Only Committed and Redemption Blocks Count Toward the Floor** — interrogated 2026-09-28; plan agreed, open questions listed. Block half built in Gap 97 (closed 2026-10-02); the session half (spontaneous sessions become Bonus, stop counting toward the floor, may not run across a contract's start or end) is folded into Gap 114 (2026-10-08).
3. **Gap 68 (S) — Drafting Adopts the Calendar; Seal Guards** — interrogated 2026-09-28, revised same day (adopted tiles are theoretical while drafting); plan agreed, depends on Gap 67. Also carries Gap 88's phase 9 (drafting's placement check onto the Domain, delete `WorkHours.ContainsRange`), moved in 2026-09-30.
4. **Gap 82 (X) — Per-Day Hours and Floors in Drafting** (Outlook-style draggable grey hours per day; click a day's badge to change its floor or rest it) — raised 2026-09-28; not interrogated; builds on Gap 68's drafting mode.
5. **Gap 80 (U) — Extending a Contract** — raised 2026-09-28; direction agreed (draft mode for added days only), not interrogated.
6. **Gap 72 (D) — Spontaneous Redemption Path** — dropped 2026-07-08; reopened 2026-09-27 as a "Spontaneous Redemption Block" (real redemption block created at Hatsu Start) — not re-interrogated.
7. **Gap 71 (C) — Nen Contract Motivation & Scoring Enhancements** (C1 Day-Met %, C2 streaks, C3 deficit digest; C4 an untouched contract reads as "Completed", raised 2026-10-08) — C1–C3 interrogated 2026-07-07; not yet built. Leans toward Insights; may move to box 5.
8. **Gap 103 — A New Contract May Start on the Day Another Was Broken** (today a broken contract's break day still counts as taken — Gap 59's choice — so cancel-and-restart the same day is refused) — raised 2026-09-29 during Gap 102; **interrogated 2026-09-30:** a contract records the moment it ended; broken → a new one may start right after that moment, completed (incl. early) → the next day; not built.
9. **Gap 105 — No Contract Changes While Focus Time Is Running** (Make/Edit contract refused while a session runs; a contract may not start inside Focus time still taken — reverses the "not gated on session-active" decision) — raised and interrogated 2026-09-30 during Gap 88's manual testing; not built.
10. **Gap 108 — Breaking a Contract Asks What Happens to Its Bonus Blocks** (keep them as plain blocks, or cancel them with the rest; the rule itself is built in Gap 97, this adds the prompt and one command field) — raised 2026-10-01 during Gap 97's interrogation; direction agreed, not built. Gap 97 and Gap 93 are both closed, so nothing blocks it.
11. **Gap 109 — When a Contract's Blocks May Be Changed** (the old "Change Window" idea; e.g. change the coming week on Sunday, locked after) — raised 2026-10-07 during Gap 92, which deletes the unused `ChangeWindow` type; parked for much later, not interrogated.

**3. Planner & Presets to MVP**
1. **Gap 73 (E) — Recurring Blocks** — interrogated 2026-07-15; not yet built.
2. **Gap 79 (T) — Spontaneous Sessions as Planner Tiles** — raised 2026-09-28; folded into Gap 114 (2026-10-08), which reverses its read-side direction.
3. **Gap 81 (V) — Always-On Background Presets** — raised 2026-09-28; not interrogated.
4. **Gap 69 (A) — Usage Ceiling & Universal Usage Tracking** — interrogated 2026-07-07; not yet built. Largest in the box; touches enforcement.
5. **Gap 116 — Site Exceptions** (block a domain except some of its subdomains; most specific match wins) — raised 2026-10-13 during Gap 112; low priority, not interrogated.
6. **Gap 117 — Attempts Know What Blocked Them** (Post-Session splits attempts by the session's preset vs a Background block; both → Focus; the slice answers `BlockerOf`) — raised 2026-10-13 during Gap 112; direction agreed, not interrogated.

**4. Settings to MVP**
1. **Gap 100 — One Home for User-Facing Text** (validators return typed reasons instead of sentences, wording built in one place; desktop text into resource files) — raised 2026-09-29 during Gap 98; direction agreed, not interrogated. Placement and permission validators convert inside Gaps 88/93; this gap does the rest. After the Architecture box so text moves once; before Gap 83 and Gap 77, its consumers.
2. **Gap 83 — Settings Page MVP** — raised 2026-09-28; not interrogated.
3. **Gap 78 (P) — Break Flexibility** (end/skip a break, break budget, extend session, edit at Hatsu, live Hatsu, focus-only floor) — raised 2026-09-27 during Gap 63; not interrogated; parts need Gap 83.

**5. Insights — after boxes 3 and 4 reach MVP**
1. **Gap 74 (G) — Nen Contract Recap Data for Insights** — interrogated 2026-08-12, widened 2026-08-30; not yet built.

**6. Resilience — last, once features are basically done**
1. **Gap 70 (B) — Crash Resilience: Session Recovery After Unexpected Shutdown** — direction sketched, not interrogated.
2. **Gap 75 (J) — System Clock Tampering: Detect and Nudge, Not Block** — raised 2026-08-20; not interrogated.
3. **Gap 76 (K) — Enforcement Has No Life Independent of the GUI Process** (Windows Service split; first step since 2026-09-29: close to tray and refuse Quit while enforcing — "always active like other blockers") — interrogated 2026-09-07; not yet built.
4. **Gap 77 (O) — Cross-Platform Suite: iOS App, Multi-Device Sync, "Hardcore iOS Lockdown"** — raised 2026-09-27; not interrogated.

Next free number: **119** (118 built directly 2026-10-14; 116 and 117 raised 2026-10-13 during Gap 112; 115 raised 2026-10-08 during Gap 110; 114 raised 2026-10-08; 112 and 113 raised 2026-10-08; 111 built directly 2026-10-08; 109 and 110 raised 2026-10-07; 107 and 108 raised 2026-10-01; 105 and 106 raised 2026-09-30; 84–87 used 2026-09-28 by fixes built directly; 88–89 raised as open gaps the same day; 90–93, 95, 97, 98, 99, 100, 101, 102 and 103 raised 2026-09-29; 94, 96 and 104 fixes built directly the same day; 91 dropped the same day — the one-source questions are investigated one by one as their own gaps instead; 90 and 99 folded into 88 the same day — their write-ups live under Gap 88 in `DESIGN_GAP_HISTORY.md` since it closed 2026-10-01, so older references to them still resolve).

\---

## Minor Cleanups

Trivial style or consistency fixes that don't rise to the level of a Design Gap. Deferred until the relevant file is next edited for other reasons. Items here should be deletable in a single line change.

*(No outstanding items.)*

