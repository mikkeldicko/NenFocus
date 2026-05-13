# NenFocus — Architect Governance

**This is the Constitution. Read this at the start of every session.**

For detailed vocabulary, see `DOMAIN_DICTIONARY.md`.
For implementation details and build state, see `TECHNICAL_SPEC_INFRA.md`.
For UI rules and ViewModel contracts, see `UI_UX_STANDARDS.md`.
For module and layer guardrails, see `MODULE_STANDARDS.md`.
For TDD workflow and test patterns, see `TESTING_GUIDE.md`.

---

## High-Level Development Strategy

### 1. Strategic Design & The Design Gap

**Principle:** Bridge the Design Gap before writing any new features.

Before implementing anything, ask as many questions as necessary to reach a shared design concept and understand the full dependency tree. Do not write implementation code until the design is agreed upon. This means: interface first, implementation second.

### 2. Ubiquitous Language (DDD)

**Principle:** Use a shared vocabulary to eliminate communication overhead.

Always refer to the terms defined in `DOMAIN_DICTIONARY.md` (Ritual, Session, Enforcement, Policy Slice, Ritual Block, Background Block, SessionOrigin, Quest, Deep Module, etc.). If a new concept needs to be introduced, define and name it together before writing any code.

**Product invariant:** A manual enforcement session cannot exist without a completed Ritual. This is a product invariant enforced by design, not by validation code. The full Nen Ritual (Ten → Zetsu → Ren → Hatsu) is the only path to creating an Impromptu Session. Abandoning the Ritual at any point produces no saved record anywhere.

### 3. TDD — Headlight Management

**Principle:** Never outrun the headlights.

Take small, deliberate steps. When possible, follow a TDD workflow: write a failing test, make it pass, then refactor. Avoid generating large blocks of unverified code. Each increment must be provably correct before the next one begins.

### 4. Deep Modules — Cognitive Load Management

**Principle:** Complexity must be hidden behind simple interfaces.

Aim for Deep Modules — classes and services that deliver significant functionality through a narrow, obvious API. Concretely:

- **ViewModels** must be kept shallow: no timers, no math, no string formatting. Delegate all tactical logic (countdowns, progress calculation, time formatting) to Infrastructure or Application services.
- **Services** are gray boxes: the human architect should be able to understand and compose them from their interface alone, without reading the implementation.
- When a ViewModel grows beyond "display data + dispatch commands," that is a signal to extract a new service.

---

## What This App Is

NenFocus is a Windows desktop focus tool built with Avalonia (.NET 9). It combines app/website blocking, intentional session creation via a 4-step ritual wizard, calendar-based focus planning, and session insights.

The name and ritual terminology (Ten, Zetsu, Ren, Hatsu) are inspired by the concept of "Nen" from Hunter × Hunter — each ritual step has a thematic purpose that maps to the session setup flow.

---

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
- **CQRS**: `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` — all mutations are commands, all reads are queries. No service classes that do both. All four base interfaces (`IQuery<>`, `IQueryHandler<,>`, `ICommand<>`, `ICommandHandler<,>`) are consolidated in `NenFocus.Application/Interfaces/IHandlers.cs` to eliminate namespace ambiguity and enforce consistent generic constraints.
- **Repository pattern**: interfaces in Application, EF Core implementations in Infrastructure.
- **Domain events**: raised inside entities/aggregates, published via `IDomainEventPublisher`, handled by Application-layer event handlers. Commands do not directly call enforcement or notifications — event handlers do.
- **MVVM**: CommunityToolkit.Mvvm (`ObservableObject`, `RelayCommand`). ViewModels never reference Views.
- **Value objects**: `record` or `sealed class` types with validation — `SessionGoal`, `SessionDuration`, `BreakSettings`, `BlockedApp`, `BlockedSite`, `ActivePolicySlice`.
- **Domain Services**: Stateless logic that spans multiple aggregates lives in `NenFocus.Domain/Services/`. `IPolicyEngine` / `PolicyEngine` is the sole current domain service — it encapsulates all policy merge computation (app union, website policy precedence, `ActiveBlockType` priority). No merge logic exists in query handlers or Infrastructure services.
- **Vertical Slice Architecture**: Every use case lives in its own feature folder (e.g., `UseCases/Sessions/StartSession/`) containing exactly four files: `Command`, `CommandHandler`, `Result`, `Validator`. Validators are auto-registered via `AddValidatorsFromAssembly`.
- **Enforcement pipeline — Source of Truth flow**: `Database → PolicyEngine (Domain) → ActivePolicySlice (Value Object) → WindowsEnforcementService (OS)`. `ScheduledEnforcementService` (heartbeat, 5s interval) calls `ComputeActivePolicySliceQuery`, which fetches entities from repositories and delegates all merge logic to `PolicyEngine.ComputeSlice()`. The resulting `ActivePolicySlice` Domain Value Object is passed directly to `WindowsEnforcementService`, which uses it to manage the hosts file, DNS flushing, and process-tree termination. No DTO crosses the Application→Infrastructure boundary for enforcement.

---

## Project Structure

```
📄 ARCHITECT_GOVERNANCE.md   ← Constitution (this file)
📄 DOMAIN_DICTIONARY.md      ← Ubiquitous language + technical mappings
📄 MODULE_STANDARDS.md       ← Layer rules, ViewModel Forbidden Zone, naming conventions
📄 TECHNICAL_SPEC_INFRA.md   ← Build state, enforcement details, dev notes
📄 TESTING_GUIDE.md          ← TDD workflow, NSubstitute patterns, test inventory
📄 UI_UX_STANDARDS.md        ← Colours, layout, ViewLocator, time formatting
📄 NenFocus.slnx
│
📁 NenFocus.Domain
├─📁 Aggregates
│  └─📄 BlockProfile.cs
├─📁 Constants
│  └─📄 SystemSafetyConstants.cs
├─📁 Entities
│  ├─📄 PlannedBlock.cs
│  ├─📄 RitualBlock.cs
│  ├─📄 BackgroundBlock.cs
│  ├─📄 BlockedAttempt.cs
│  └─📄 Session.cs
├─📁 Enums
│  ├─📄 BlockedAttemptType.cs
│  ├─📄 BlockType.cs
│  ├─📄 SessionOrigin.cs
│  ├─📄 SessionStatus.cs
│  └─📄 WebsitePolicy.cs
├─📁 Events
│  ├─📄 BlockProfileCreatedEvent.cs
│  ├─📄 BreakStartedEvent.cs
│  ├─📄 DomainEvent.cs
│  ├─📄 SessionEndedEvent.cs
│  └─📄 SessionStartedEvent.cs
├─📁 Services
│  ├─📄 IPolicyEngine.cs
│  └─📄 PolicyEngine.cs
├─📁 Utils
│  └─📄 ProcessUtils.cs
└─📁 ValueObjects
   ├─📄 ActivePolicySlice.cs
   ├─📄 BlockedApp.cs
   ├─📄 BlockedSite.cs
   ├─📄 BreakSettings.cs
   ├─📄 SessionDuration.cs
   └─📄 SessionGoal.cs
│
📁 NenFocus.Application
├─📁 DTOs
│  ├─📄 BlockProfileDto.cs
│  ├─📄 BreakSettingsDto.cs
│  ├─📄 PlannedBlockDto.cs
│  ├─📄 PostSessionDto.cs
│  ├─📄 SessionDto.cs
│  └─📄 SessionSummaryDto.cs
├─📁 EventHandlers
│  ├─📄 BlockProfileCreatedEventHandler.cs
│  ├─📄 BreakStartedEventHandler.cs
│  ├─📄 SessionEndedEventHandler.cs
│  └─📄 SessionStartedEventHandler.cs
├─📁 Interfaces
│  ├─📄 IActiveSessionMonitor.cs
│  ├─📄 IBlockedAttemptTracker.cs
│  ├─📄 IBlockProfileRepository.cs
│  ├─📄 IDomainEventPublisher.cs
│  ├─📄 IEnforcementService.cs
│  ├─📄 IHandlers.cs                          ← IQuery<>, IQueryHandler<,>, ICommand<>, ICommandHandler<,>
│  ├─📄 INotificationService.cs
│  ├─📄 IPlannedBlockRepository.cs
│  ├─📄 ISessionRepository.cs
│  └─📄 ISessionStateService.cs
├─📁 Queries
│  ├─📁 ComputeActivePolicySlice
│  │  └─📄 ComputeActivePolicySliceQuery.cs   ← query + handler; delegates to IPolicyEngine
│  ├─📄 GetActiveSessionQuery.cs
│  ├─📄 GetAllBlockProfilesQuery.cs
│  ├─📄 GetInsightsCalendarQuery.cs
│  ├─📄 GetPlannedBlocksForDayQuery.cs
│  └─📄 GetSessionHistoryQuery.cs
├─📁 UseCases
│  ├─📁 BlockProfiles
│  │  ├─📁 CreateBlockProfile
│  │  ├─📁 DeleteBlockProfile
│  │  ├─📁 DuplicateBlockProfile
│  │  └─📁 UpdateBlockProfileCommand
│  ├─📁 Planner
│  │  ├─📁 CreatePlannedBlock
│  │  ├─📁 DeletePlannedBlock
│  │  └─📁 UpdatePlannedBlock
│  └─📁 Sessions
│     ├─📁 EndSession
│     ├─📁 ResumeFromBreak
│     ├─📁 SaveReflection
│     ├─📁 StartBreak
│     └─📁 StartSession
└─📄 DependencyInjection.cs
│
📁 NenFocus.Infrastructure
├─📁 Events
│  └─📄 InProcessDomainEventPublisher.cs
├─📁 Migrations
├─📁 Notifications
│  └─📄 WindowsNotificationService.cs
├─📁 Persistence
│  ├─📄 NenFocusDbContext.cs
│  ├─📄 NenFocusDbContextFactory.cs
│  └─📁 Repositories
│     ├─📄 BlockProfileRepository.cs
│     ├─📄 PlannedBlockRepository.cs
│     └─📄 SessionRepository.cs
├─📁 Services
│  ├─📄 ActiveSessionMonitor.cs
│  ├─📄 BlockedAttemptTracker.cs
│  ├─📄 ScheduledEnforcementService.cs
│  ├─📄 SessionPersistenceService.cs
│  ├─📄 SessionStateService.cs
│  └─📄 WindowsEnforcementService.cs
├─📄 DependencyInjection.cs
└─📄 HostExtensions.cs
│
📁 NenFocus  (UI — entry point)
├─📁 ViewModels
│  ├─📄 BlockProfilePageViewModel.cs
│  ├─📄 HatsuPageViewModel.cs
│  ├─📄 MainPageViewModel.cs
│  ├─📄 RitualShellViewModel.cs
│  ├─📄 ShellViewModel.cs
│  ├─📄 TimeSpanToStringConverter.cs
│  └─📄 [other converters and stubs]
├─📁 Views
│  ├─📄 MainPageView.axaml / .axaml.cs
│  ├─📄 RitualShellView.axaml / .axaml.cs
│  └─📄 [other views]
├─📄 App.axaml / App.axaml.cs
├─📄 app.manifest
├─📄 Program.cs
└─📄 ViewLocator.cs
│
📁 NenFocus.Tests
├─📁 Application
│  └─📄 StartSessionCommandHandlerTests.cs
└─📁 Domain
   └─📄 SessionTests.cs
│
📁 NenFocusExtension  (browser extension — solution root)
├─📄 background.js
├─📄 blocked.html
└─📄 manifest.json
```

---

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

- Reviewed and approved by the architect
- Consistent with the Deep Module test (understandable in under 30 seconds)
- Free of infrastructure types in its signature

### Step 3 — Thin Vertical Slice

Once the interface is approved, implement the thinnest possible vertical slice:
- Domain entity changes (if any)
- One command or query handler
- One ViewModel binding
- No parallel work on adjacent features

### Step 4 — Verify Before Expanding

Each slice must compile and produce the correct output before the next slice begins. If the project has an active test suite (`NenFocus.Tests`), a passing test is required before moving on. This is the "headlight management" rule: never implement further than you can verify.

---

## Design Gap Closure Log

A running record of gaps that were identified, interrogated, and closed. Each entry is a concrete demonstration of the protocol working correctly.

### Gap 1 — Enforcement Gap on Session Start *(closed)*

**The gap:** `StartSessionCommandHandler` saved the session and updated UI state but never applied OS-level enforcement. `SessionStartedEventHandler` had been stripped of its enforcement call and only sent a notification. Sessions could start with no blocking in effect. The bug was silent — no test caught it because `IEnforcementService` was never injected into the handler.

**The closure:** Event-driven chain restored. `Session.Start()` raises `SessionStartedEvent` internally → handler dispatches via `PublishAllAsync(session.DomainEvents)` → `SessionStartedEventHandler` computes the current policy slice via `ComputeActivePolicySliceQuery` and calls `IEnforcementService.ApplyPolicyAsync`. `IEnforcementService` remains a simple two-method interface; the orchestration complexity lives in the event handler. Three new tests in `SessionStartedEventHandlerTests` now guard this path permanently.

**Principle demonstrated:** Deep Modules — `IEnforcementService` stayed narrow. The event handler became the "orchestrator" layer, not the command handler and not the infrastructure service.

---

### Gap 2 — BlockType Promotion: From Leaky Boolean to Domain Enum *(closed)*

**The gap:** The system could not distinguish between a Ritual Block (which must trigger the Ritual) and a Background Block (which enforces silently) after computing a policy slice. A boolean `RequiresRitual` flag would have been the shallow solution — a boolean with no domain meaning that every caller must interpret differently.

**The closure:** `PolicySliceDto` now carries `ActiveBlockType (BlockType?)` — a domain enum from `NenFocus.Domain.Enums`. `ComputeActivePolicySliceQueryHandler` sets it to the type of the highest-priority active planned block (Ritual > Background). `ScheduledEnforcementService` reads this field and calls `SetRitualRequired(true)` only when `ActiveBlockType == Ritual` with no active session. The decision is expressed once, in the domain's language, in one place.

**Principle demonstrated:** Ubiquitous Language — the difference between a Ritual Block and a Background Block was already a domain concept. Promoting it to the `PolicySliceDto` made the contract self-documenting and eliminated a class of future bugs where callers would have needed to re-derive the distinction themselves.

**Side fix included:** Blocklist mode in `ComputeActivePolicySliceQueryHandler` was previously broken — `BlockedSites` was always an empty set (missing `case WebsitePolicy.Blocklist:`). Fixed in the same slice. The browser extension was receiving an empty blocklist for all Blocklist-mode profiles.

---

### Gap 3 — Impromptu Sessions, Insights Architecture, and the Block Hierarchy *(closed)*

**The gap:** The domain had no way to distinguish a planned Session from a manually-started one, making the planned Insights page impossible to build correctly. Additionally, the block hierarchy was being reconsidered — a proposed `InstantRitualBlock` class and a rename to Ritual Block / Background Block were both under evaluation. The `BlockedAttempt` entity was also being considered for conversion to a Value Object.

**The interrogation revealed:**
- Insights requires a unified calendar view showing Ritual Blocks (completed and missed), Background Blocks, and Impromptu Sessions
- Insights requires analytics including session quality comparison between Planned and Impromptu sessions and block profile follow-through rates
- Background Blocks must appear in Insights using scheduled time as canonical truth regardless of machine state
- A Ritual Block where the user missed the Ritual must appear in Insights as "missed" — the `RitualBlock` row is the record
- An Impromptu Session that did not complete the Ritual must not be saved anywhere
- The ONLY manual path to enforcement is through the full Nen Ritual — this is a product invariant, not a validation rule
- Browsing time tracking on all websites is a desired future feature requiring extension changes
- Planned Period / Strict Mode is a desired future feature

**The closure:**
- `InstantRitualBlock` rejected: an Impromptu Session is not a Block. It has no calendar identity, no life outside its Session, and no state transitions. Creating an entity to make the schema look symmetric would violate DDD and fail the Deep Module test.
- `BlockedAttempt` kept as append-only fact table: burying it in JSON would make Insights analytics queries require full in-memory deserialisation across hundreds of sessions.
- `FocusBlock` renamed to `RitualBlock`, `PassiveBlock` renamed to `BackgroundBlock`, `BlockType` enum values updated to `Ritual` and `Background`. Rename is complete at every layer: entity, enum, DTO, handler, service, dictionary.
- `Session.Origin (SessionOrigin)` added: explicit `Impromptu` / `Planned` tag. Does not rely on null-checking `OriginatingBlockId` so it remains unambiguous when future origin types are added.
- `Session.OriginatingBlockId (Guid?)` added: closes the known gap from `TECHNICAL_SPEC_INFRA.md`. Written by `StartSessionCommandHandler` when a Ritual Block triggered the session.
- `RitualBlock.RitualCompletedAt (DateTime?)` added: allows Insights to detect missed blocks from the entity alone without a join to Session.
- `GetInsightsCalendarQuery` stubbed: single query handler that unifies three sources behind one interface. The ViewModel never touches the individual tables.

**Principle demonstrated:** The asymmetry between Planned and Impromptu in the schema (Block + Session vs Session alone) is correct and intentional. It reflects a real asymmetry in the domain — a planned block exists before and independently of any Session; an impromptu block does not. Hiding this asymmetry with a synthetic entity would be lying with code. `GetInsightsCalendarQuery` is the correct place to unify the three sources — complexity is hidden behind a narrow interface, not erased from the domain.

---

### Gap 4 — Domain Events Raised by Handlers, Not Entities; `BlockedAttemptsSerialized` Contradiction *(closed)*

**The gap:** Two DDD violations were found during a doc/code audit. First, domain events (`SessionStartedEvent`, `SessionEndedEvent`, `BreakStartedEvent`, `BlockProfileCreatedEvent`) were being constructed and published directly inside command handlers, not inside the entities and aggregates that own the state transitions. The governance states "domain events: raised inside entities/aggregates" — the code violated this. Second, `EndSessionCommandHandler` and `StartBreakCommandHandler` were calling `IEnforcementService` and `INotificationService` directly, bypassing the event handler chain the governance explicitly requires. Third, `Session.BlockedAttemptsSerialized` persisted blocked attempt data as a JSON blob on the Session row — contradicting the Gap 3 decision to keep `BlockedAttempt` as an append-only fact table.

**The closure:**
- `Session` and `BlockProfile` now own a private `_domainEvents` list. Each state-transition method (`Start()`, `StartBreak()`, `Complete()`) appends the appropriate event internally. Handlers call `entity.DomainEvents` and dispatch via `IDomainEventPublisher.PublishAllAsync`, then clear with `entity.ClearDomainEvents()`. The handler no longer knows which events a domain operation produces.
- `EndSessionCommandHandler` and `StartBreakCommandHandler` had their `IEnforcementService` and `INotificationService` injections removed. All enforcement and notification side-effects now flow exclusively through `SessionEndedEventHandler` and `BreakStartedEventHandler`. `BreakStartedEventHandler` also gained the missing `IEnforcementService.RemoveEnforcementAsync` call it lacked.
- `Session.BlockedAttemptsSerialized` and `Session.SetBlockedAttempts()` removed. `BlockedAttempt` rows (append-only, keyed by `SessionId`) are the sole persistence path for attempt data. Migration `20260512000001_RemoveBlockedAttemptsSerialized` drops the now-redundant `BlockedAttempts` column from the `Sessions` table.

**Principle demonstrated:** Deep Modules and the governance rule "commands do not directly call enforcement or notifications — event handlers do." The command handlers are now thin: validate → mutate domain → save → publish events → return result. All side-effects (enforcement, notifications) are encapsulated in event handlers with narrow, self-documenting interfaces.

---

### Gap 5 — DTO-to-Domain-Value-Object Promotion: `PolicySliceDto` → `ActivePolicySlice` *(closed)*

**The gap:** Enforcement logic was orchestrated using `PolicySliceDto`, a plain Application-layer DTO. The merge rules for apps, websites, and `ActiveBlockType` lived inline inside `ComputeActivePolicySliceQueryHandler` — an Application orchestrator holding domain logic. `WindowsEnforcementService` (Infrastructure) consumed the DTO, which inverted the correct dependency: Infrastructure was coupled to an Application-layer artifact, and domain merge logic was stranded in a query handler that had no business owning it. Additionally, CQRS base interfaces were scattered across multiple namespaces, causing `IQueryHandler<ComputeActivePolicySliceQuery, PolicySliceDto>` vs `IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice>` registration conflicts during the migration.

**The closure:**
- `PolicySliceDto` removed entirely. `ActivePolicySlice` promoted to a Domain Value Object at `NenFocus.Domain/ValueObjects/ActivePolicySlice.cs`: a `sealed class` with `init`-only properties carrying `BlockedApps`, `BlockedSites`, `AllowedSites`, `WebsitePolicyMode`, `ActiveBlockType`, `ActiveSessionId`, and `IsEmpty`.
- `IPolicyEngine` / `PolicyEngine` added to `NenFocus.Domain/Services/`: a stateless domain service that accepts raw `BlockProfile`, `Session`, and `PlannedBlock` aggregates and produces an `ActivePolicySlice`. All merge logic (app union, allowlist/blocklist precedence, `ActiveBlockType` priority) is now Domain code — unit-testable with plain collections, no mocks required.
- `ComputeActivePolicySliceQueryHandler` reduced to a pure orchestrator: fetches entities from three repositories, collects unique profile IDs, loads `BlockProfile` aggregates, then delegates entirely to `_policyEngine.ComputeSlice()`. No merge logic remains in the handler.
- `IEnforcementService` and `WindowsEnforcementService` updated to accept `ActivePolicySlice` directly, completing the Source of Truth flow: `Database → PolicyEngine (Domain) → ActivePolicySlice (VO) → WindowsEnforcementService (OS)`.
- All four CQRS base interfaces (`IQuery<>`, `IQueryHandler<,>`, `ICommand<>`, `ICommandHandler<,>`) consolidated into `NenFocus.Application/Interfaces/IHandlers.cs`, eliminating the namespace ambiguity that caused DI registration conflicts.
- `ComputeActivePolicySliceQuery` and its handler moved from a flat file (`Queries/ComputeActivePolicySliceQuery.cs`) to a dedicated subfolder (`Queries/ComputeActivePolicySlice/ComputeActivePolicySliceQuery.cs`) consistent with the Vertical Slice pattern used by all other queries.

**Principle demonstrated:** Domain-Driven Design — domain logic belongs in the Domain layer. Moving the merge rules into `PolicyEngine` makes them testable without a database and shareable with any future consumer that needs policy computation (e.g., a Planner "preview what would be blocked" query). The DTO was always a leaky abstraction; `ActivePolicySlice` is the correct expression of the domain concept it was approximating.

---

## Minor Cleanups

Trivial style or consistency fixes that don't rise to the level of a Design Gap. Deferred until the relevant file is next edited for other reasons. Items here should be deletable in a single line change.

- **`StartSessionCommandHandler.cs` — extract minutes-available calc to domain.** The line `(int)(conflictingBlock.Start - now).TotalMinutes` computes "how long until the next conflicting Ritual Block" inline. Replace with a `PlannedBlock.MinutesUntilStart(asOf)` method. Ubiquitous language; no behavior change.
- **`EndSessionCommandHandler.cs` — use existing helper for elapsed time.** `(int)(session.EndedAt.Value - session.StartedAt.Value).TotalMinutes` should use `session.GetElapsedTime(session.EndedAt.Value)`. The helper already exists on `Session`.
