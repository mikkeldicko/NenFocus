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

Always refer to the terms defined in `DOMAIN_DICTIONARY.md` (Ritual, Session, Enforcement, Policy Slice, Focus Block, Quest, Deep Module, etc.). If a new concept needs to be introduced, define and name it together before writing any code.

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
- **CQRS**: `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` — all mutations are commands, all reads are queries. No service classes that do both.
- **Repository pattern**: interfaces in Application, EF Core implementations in Infrastructure.
- **Domain events**: raised inside entities/aggregates, published via `IInProcessDomainEventPublisher`, handled by Application-layer event handlers. Commands do not directly call enforcement or notifications — event handlers do.
- **MVVM**: CommunityToolkit.Mvvm (`ObservableObject`, `RelayCommand`). ViewModels never reference Views.
- **Value objects**: `record` types with validation in the constructor — `SessionGoal`, `SessionDuration`, `BreakSettings`, `BlockedApp`, `BlockedSite`.
- **Vertical Slice Architecture**: Every use case lives in its own feature folder (e.g., `UseCases/Sessions/StartSession/`) containing exactly four files: `Command`, `CommandHandler`, `Result`, `Validator`. Validators are auto-registered via `AddValidatorsFromAssembly`.
- **Enforcement pipeline**: `ScheduledEnforcementService` (heartbeat, 5s interval) → `ComputeActivePolicySliceQueryHandler` (aggregates PlannedBlocks + active Sessions) → `WindowsEnforcementService` (kills processes, rewrites hosts file, exposes local HTTP API).

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
│  ├─📄 BaseEntity.cs
│  ├─📄 BlockedAttempt.cs
│  ├─📄 FocusBlock.cs
│  ├─📄 PassiveBlock.cs
│  ├─📄 PlannedBlock.cs
│  └─📄 Session.cs
├─📁 Enums
│  ├─📄 BlockedAttemptType.cs
│  ├─📄 BlockType.cs
│  ├─📄 SessionStatus.cs
│  └─📄 WebsitePolicy.cs
├─📁 Events
│  ├─📄 BlockProfileCreatedEvent.cs
│  ├─📄 BreakStartedEvent.cs
│  ├─📄 DomainEvent.cs
│  ├─📄 SessionEndedEvent.cs
│  └─📄 SessionStartedEvent.cs
└─📁 ValueObjects
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
│  ├─📄 PolicySliceDto.cs
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
│  ├─📄 IHandlers.cs
│  ├─📄 INotificationService.cs
│  ├─📄 IPlannedBlockRepository.cs
│  ├─📄 ISessionRepository.cs
│  └─📄 ISessionStateService.cs
├─📁 Queries
│  ├─📄 ComputeActivePolicySliceQuery.cs
│  ├─📄 GetActiveSessionQuery.cs
│  ├─📄 GetAllBlockProfilesQuery.cs
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
├─📁 Validators
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
