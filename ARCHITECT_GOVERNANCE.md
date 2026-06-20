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

Always refer to the terms defined in `DOMAIN\\\_DICTIONARY.md` (Ritual, Session, Enforcement, Policy Slice, Ritual Block, Background Block, SessionOrigin, Quest, Deep Module, etc.). If a new concept needs to be introduced, define and name it together before writing any code.

**Product invariant:** A manual enforcement session cannot exist without a completed Ritual. This is a product invariant enforced by design, not by validation code. The full Nen Ritual (Ten → Zetsu → Ren → Hatsu) is the only path to creating an Impromptu Session. Abandoning the Ritual at any point produces no saved record anywhere.

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

Clean Architecture with 4 code projects + 1 test project:

```
NenFocus.Domain          → Entities, value objects, domain events, enums. No dependencies.
NenFocus.Application     → Use cases (CQRS commands/queries), interfaces, validators, DTOs.
NenFocus.Infrastructure  → EF Core + SQLite, Windows enforcement, toast notifications, DI wiring.
NenFocus                 → Avalonia UI: ViewModels, Views, converters. Entry point.
NenFocus.Tests           → xUnit unit tests for Domain and Application layers.
```

Dependencies only flow inward: UI → Application ← Infrastructure → Domain.

**Key patterns:**

* **CQRS**: `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` — all mutations are commands, all reads are queries. No service classes that do both.
* **Repository pattern**: interfaces in Application, EF Core implementations in Infrastructure.
* **Domain events**: raised inside entities/aggregates, published via `IInProcessDomainEventPublisher`, handled by Application-layer event handlers. Commands do not directly call enforcement or notifications — event handlers do.
* **MVVM**: CommunityToolkit.Mvvm (`ObservableObject`, `RelayCommand`). ViewModels never reference Views.
* **Value objects**: `record` types with validation in the constructor — `SessionGoal`, `SessionDuration`, `BreakSettings`, `BlockedApp`, `BlockedSite`.
* **Vertical Slice Architecture**: Every use case lives in its own feature folder (e.g., `UseCases/Sessions/StartSession/`) containing exactly four files: `Command`, `CommandHandler`, `Result`, `Validator`. Validators are auto-registered via `AddValidatorsFromAssembly`.
* **Enforcement pipeline**: `ScheduledEnforcementService` (heartbeat, 5s interval) → `ComputeActivePolicySliceQueryHandler` (aggregates PlannedBlocks + active Sessions) → `WindowsEnforcementService` (kills processes, rewrites hosts file, exposes local HTTP API).

\---

## Project Structure

```
📄 ARCHITECT\\\_GOVERNANCE.md    ← Constitution (this file)
📄 DOMAIN\\\_DICTIONARY.md       ← Ubiquitous language + technical mappings
📄 MODULE\\\_STANDARDS.md        ← Layer rules, ViewModel Forbidden Zone, naming conventions
📄 TECHNICAL\\\_SPEC\\\_INFRA.md    ← Build state, enforcement details, dev notes
📄 TESTING\\\_GUIDE.md           ← TDD workflow, NSubstitute patterns, test inventory
📄 UI\\\_UX\\\_STANDARDS.md         ← Colours, layout, ViewLocator, time formatting
📄 DESIGN\\\_GAP\\\_HISTORY.md      ← Record of closed design gaps (reference only)



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

Gaps that have been identified but not yet closed. Each is here because deferring it is a deliberate choice, not because it has been forgotten.

### Open Gap A — Persistence Boundary: Per-Repository SaveChanges vs. Unit of Work

**The gap:** Repositories currently call `SaveChangesAsync` internally on each `SaveAsync`. `StartSessionCommandHandler` therefore performs two separate database round-trips when a Ritual Block triggers a session: one to stamp `RitualBlock.RitualCompletedAt`, one to insert the new `Session` row. If the second save fails, the database is left with a Ritual Block marked completed but no corresponding Session — violating the invariant in `DOMAIN\\\_DICTIONARY.md` that a completed Ritual Block always has an associated Planned Session.

The same fragmentation pushes domain-event dispatching out to every handler: each must remember to call `PublishAllAsync` and `ClearDomainEvents` in the right order relative to `SaveAsync`. The rule "save, then publish, then clear" is repeated in every handler instead of being encapsulated once — a Deep Modules violation.

**Why deferred:** The consistency bug only manifests on the Planned Session path. Per the dictionary, `ShellViewModel` (which detects Ritual Block activation and triggers the Ritual overlay) is "not yet built." The fragile path is not yet reachable from the UI.

**Direction (subject to interrogation when closed):** Repositories register changes without calling `SaveChangesAsync`. `NenFocusDbContext.SaveChangesAsync` is overridden to commit the transaction, walk the change tracker for aggregates with pending `DomainEvents`, publish them via `IDomainEventPublisher`, and clear them. Command handlers shrink to: validate → mutate aggregate → `SaveChangesAsync` → return result.

**Must be closed before:** `ShellViewModel` is built and the Ritual Block activation path becomes reachable.

### Open Gap B — Time Budget Feature (three-slice implementation)

**The feature:** A `TimeBudget` is an optional daily cap on a `BlockedSite` entry within a `BlockProfile`. A budgeted site is accessible until its daily cap is consumed, at which point it becomes blocked for the rest of the day. Usage is reported by the browser extension via a `/report` endpoint and written through `IBudgetUsageTracker` into `DailyBudgetUsage` rows — it is not inferred from wall-clock time. See `DOMAIN\_DICTIONARY.md` → Time Budget and Budget Window for full definitions and ubiquitous language rules.

**Why three slices:** The feature crosses three independent implementation layers: the domain model and UI (Slice 1), the extension and HTTP reporting path (Slice 2), and the enforcement wiring (Slice 3). Slice 2 must complete before Slice 3 begins because the enforcement engine cannot read usage data until the reporting pipeline exists. Slice 1 has no prerequisite and can be built immediately.

**Slice 1 — Domain model + UI** *(no prerequisite; can begin immediately)*
- Add `TimeBudget` value object to `NenFocus.Domain/ValueObjects/`
- `BlockedSite` gains an optional `TimeBudget` field
- Add `DailyBudgetUsage` entity to `NenFocus.Domain/Entities/`
- Add `IBudgetUsageTracker` interface to `NenFocus.Application/Interfaces/`
- Block Profile editor UI: add per-site budget cap input

**Slice 2 — Extension + `/report` endpoint** *(no prerequisite; must complete before Slice 3)*
- `background.js` gains tab focus tracking (active tab time accumulation)
- `WindowsEnforcementService` gains `/report` handler that receives usage events from the extension and writes through `IBudgetUsageTracker`
- `IBudgetUsageTracker` implementation persists `DailyBudgetUsage` rows

**Slice 3 — Enforcement wiring** *(requires Slice 2 complete)*
- `PolicyEngine.ComputeSlice()` reads `DailyBudgetUsage`; exhausted sites are added to the blocked set
- `ScheduledEnforcementService` triggers re-evaluation when a budget is exhausted mid-block
- Warning notification fires N minutes before exhaustion (N is a user-wide preference in Settings, not per-profile)

**New artifacts introduced:**
- `TimeBudget` — value object (Domain); carries the daily cap only
- `DailyBudgetUsage` — append-only entity (Domain); source of truth for consumed time per site per calendar day per profile entry
- `IBudgetUsageTracker` — interface (Application); the write contract for usage events; analogous to `IBlockedAttemptTracker`
- Budget Window — derived concept only; `[PlannedBlock.Start, PlannedBlock.End)` while `IsActive()` is `true`; no stored field, no migration

**Existing artifacts touched:**
- `BlockedSite` value object — gains optional `TimeBudget` field (Slice 1)
- `PolicyEngine` — gains budget exhaustion logic in `ComputeSlice()` (Slice 3)
- `WindowsEnforcementService` — gains `/report` handler for usage reporting (Slice 2)
- `ScheduledEnforcementService` — gains re-evaluation trigger on budget exhaustion (Slice 3)
- `background.js` — gains tab focus tracking (Slice 2)

**Must be closed before:** Slice 3 must be closed before Time Budget enforcement is considered complete. Slice 1 may be closed and verified independently.

**Design decision recorded:** Option B (heartbeat inference — deriving usage from the enforcement heartbeat's wall-clock timing rather than extension-reported events) was explicitly considered and rejected during the Design Gap interrogation. Usage tracking must be accurate: only time the user actively spends on a site counts, which requires the extension to report it directly. Do not reopen this question.

\---

## Minor Cleanups

Trivial style or consistency fixes that don't rise to the level of a Design Gap. Deferred until the relevant file is next edited for other reasons. Items here should be deletable in a single line change.

* **`StartSessionCommandHandler.cs` — extract minutes-available calc to domain.** The line `(int)(conflictingBlock.Start - now).TotalMinutes` computes "how long until the next conflicting Ritual Block" inline. Replace with a `PlannedBlock.MinutesUntilStart(asOf)` method. Ubiquitous language; no behavior change.
* **`EndSessionCommandHandler.cs` — use existing helper for elapsed time.** `(int)(session.EndedAt.Value - session.StartedAt.Value).TotalMinutes` should use `session.GetElapsedTime(session.EndedAt.Value)`. The helper already exists on `Session`.

