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

Before implementing anything, ask as many questions as necessary to reach a shared design concept and understand the full dependency tree. Do not write implementation code until the design is agreed upon. This means: interface first, implementation second.

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

* **CQRS**: `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` — all mutations are commands, all reads are queries. No service classes that do both. **Exception:** an Application service that only coordinates a Domain decision into an outbound port — it reads the decision internally and acts on it, never serving reads to the UI — is allowed (e.g. `IEnforcementSynchronizer`, Gap 62). The rule forbids services that mix UI-facing reads with writes.
* **Repository pattern**: interfaces in Application, EF Core implementations in Infrastructure.
* **Domain events**: raised inside entities/aggregates, published via `IInProcessDomainEventPublisher`, handled by Application-layer event handlers. Commands do not directly call enforcement or notifications — event handlers do.
* **MVVM**: CommunityToolkit.Mvvm (`ObservableObject`, `RelayCommand`). ViewModels never reference Views.
* **Value objects**: `record` types with validation in the constructor — `SessionGoal`, `SessionDuration`, `BreakTimer`, `BlockedApp`, `BlockedSite`.
* **Vertical Slice Architecture**: Every use case lives in its own feature folder (e.g., `UseCases/Sessions/StartSession/`) containing exactly four files: `Command`, `CommandHandler`, `Result`, `Validator`. Validators are auto-registered via `AddValidatorsFromAssembly`.
* **Enforcement pipeline**: `ScheduledEnforcementService` (heartbeat, 5s interval), the session event handlers and `BreakTransitionHandler` (break boundaries, reported by `ActiveSessionMonitor`) → `IEnforcementSynchronizer` (sole caller that turns policy into an apply/remove) → `ComputeActivePolicySliceQueryHandler` / `PolicyEngine` (what should be blocked) → `WindowsEnforcementService` (kills processes, rewrites hosts file, exposes local HTTP API).

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
7. **Enforcement interaction:** Does this feature interact with `ScheduledEnforcementService` or `WindowsEnforcementService`?
8. **UI contract:** What observable properties and commands does the ViewModel need to expose? What XAML bindings does the View need?
9. **Failure modes:** What happens if the underlying operation fails? How does the UI surface the error?
10. **Rollback / undo:** Is there a compensating action? (e.g., if session start fails after enforcement is applied)

This interrogation happens in conversation. Code is not typed until every answer is agreed upon.

### Step 2 — Interface First

The first code artifact for any new feature is **always** the interface, placed in `NenFocus.Application/Interfaces/`. No handler, service, or ViewModel code is written until the interface is:

* Reviewed and approved by the architect
* Consistent with the Deep Module test (understandable in under 30 seconds)
* Free of infrastructure types in its signature

### Step 3 — Thin Vertical Slice

Once the interface is approved, implement the thinnest possible vertical slice:

* Domain entity changes (if any)
* One command or query handler
* One ViewModel binding
* No parallel work on adjacent features

### Step 4 — Verify Before Expanding

Each slice must compile and produce the correct output before the next slice begins. If the project has an active test suite (`NenFocus.Tests`), a passing test is required before moving on. This is the "headlight management" rule: never implement further than you can verify.

\---

## Open Gaps

Gaps that have been identified but not yet closed. Each is here because deferring it is a deliberate choice, not because it has been forgotten. **Full write-ups live in `OPEN_GAPS.md`** (not auto-loaded — read it before working on, or designing anything adjacent to, one of these). References elsewhere to "`ARCHITECT_GOVERNANCE.md` → Open Gap X" resolve there. When a gap closes, its write-up moves to `DESIGN_GAP_HISTORY.md` and its line below is removed.

- **A — Usage Ceiling & Universal Usage Tracking** — interrogated 2026-07-07; not yet built.
- **B — Crash Resilience: Session Recovery After Unexpected Shutdown** — direction sketched, not interrogated; deferred.
- **C — Nen Contract Motivation & Scoring Enhancements** (C1 Day-Met %, C2 streaks, C3 deficit digest) — interrogated 2026-07-07; not yet built.
- **D — Spontaneous Redemption Path** — dropped 2026-07-08; reopened 2026-09-27 as a "Spontaneous Redemption Block" (real redemption block created at Hatsu Start) — parked, not re-interrogated.
- **E — Recurring Blocks** — interrogated 2026-07-15; not yet built.
- **G — Nen Contract Recap Data for Insights** — interrogated 2026-08-12, widened 2026-08-30; not yet built.
- **J — System Clock Tampering: Detect and Nudge, Not Block** — raised 2026-08-20; parked, not interrogated.
- **K — Enforcement Has No Life Independent of the GUI Process** (Windows Service split) — interrogated 2026-09-07; deliberately not started yet.
- **O — Cross-Platform Suite: iOS App, Multi-Device Sync, "Hardcore iOS Lockdown"** — raised 2026-09-27; parked until the desktop app is finished, not interrogated.
- **P — Break Flexibility** (end/skip a break, break budget, extend session, edit at Hatsu, live Hatsu, focus-only floor) — raised 2026-09-27 during Gap 63; parked, not interrogated.
- **Q — Contract Activation/Completion Uses the UTC Date** — bug found 2026-09-28; fix agreed, open questions listed; next up (to close as Gap 65).
- **R — Contract Membership: Only Committed and Redemption Blocks Count Toward the Floor** — interrogated 2026-09-28; plan agreed, open questions listed (to close as Gap 66).
- **S — Drafting Adopts the Calendar; Seal Guards** — interrogated 2026-09-28; plan agreed, depends on R (to close as Gap 67).
- **T — Spontaneous Sessions as Planner Tiles** — raised 2026-09-28; direction agreed (read-side view of Session), not interrogated.
- **U — Extending a Contract** — raised 2026-09-28; direction agreed (draft mode for added days only), not interrogated.
- **V — Always-On Background Presets** — raised 2026-09-28; parked, not interrogated.- **R — Contract Membership: Only Committed and Redemption Blocks Count Toward the Floor** — interrogated 2026-09-28; plan agreed, open questions listed (to close as Gap 66).- **S — Drafting Adopts the Calendar; Seal Guards** — interrogated 2026-09-28; plan agreed, depends on R (to close as Gap 67).- **T — Spontaneous Sessions as Planner Tiles** — raised 2026-09-28; direction agreed (read-side view of Session), not interrogated.- **U — Extending a Contract** — raised 2026-09-28; direction agreed (draft mode for added days only), not interrogated.- **V — Always-On Background Presets** — raised 2026-09-28; parked, not interrogated.

\---

## Minor Cleanups

Trivial style or consistency fixes that don't rise to the level of a Design Gap. Deferred until the relevant file is next edited for other reasons. Items here should be deletable in a single line change.

*(No outstanding items.)*

