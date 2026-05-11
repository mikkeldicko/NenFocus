# NenFocus — CLAUDE.md

## What This App Is

NenFocus is a Windows desktop focus tool built with Avalonia (.NET 9). It combines app/website blocking, intentional session creation via a 4-step ritual wizard, calendar-based focus planning, and session insights.

The name and ritual terminology (Ten, Zetsu, Ren, Hatsu) are inspired by the concept of "Nen" from Hunter × Hunter — each ritual step has a thematic purpose that maps to the session setup flow.

---

## Architecture

Clean Architecture with 4 projects:

```
NenFocus.Domain          → Entities, value objects, domain events, enums. No dependencies.
NenFocus.Application     → Use cases (CQRS commands/queries), interfaces, validators, DTOs.
NenFocus.Infrastructure  → EF Core + SQLite, Windows enforcement, toast notifications, DI wiring.
NenFocus                 → Avalonia UI: ViewModels, Views, converters. Entry point.
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

## Domain Language

Use these terms consistently across code, comments, and UI:

| Term | Meaning |
|------|---------|
| **Session** | An active focus period created by the user via the ritual |
| **Ritual** | The 4-step wizard (Ten → Zetsu → Ren → Hatsu) that starts a session |
| **Block Profile** | A reusable set of blocked apps and a website policy (blocklist or allowlist) |
| **Focus Block** | A calendar entry that schedules enforcement during a time window |
| **Passive Block** | A calendar entry that logs activity without enforcing anything |
| **Policy Slice** | The computed effective blocking state at a given instant (merged from all active blocks) |
| **Impromptu session** | A session started from the Main Page (more permissive adjustment rules) |
| **Planned session** | A session started because a scheduled Focus Block activated (stricter rules) |
| **Quest** | A named task scoped to a day, week, or month. Created in the Planner, checked off on the Main page, reviewed in Insights. |


---

## Project Structure

```
📄 NenFocus.slnx
📄 CLAUDE.md
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
│  │  │  ├─📄 CreateBlockProfileCommand.cs
│  │  │  ├─📄 CreateBlockProfileCommandHandler.cs
│  │  │  ├─📄 CreateBlockProfileResult.cs
│  │  │  └─📄 CreateBlockProfileValidator.cs
│  │  ├─📁 DeleteBlockProfile
│  │  │  ├─📄 DeleteBlockProfileCommand.cs
│  │  │  └─📄 DeleteBlockProfileCommandHandler.cs
│  │  ├─📁 DuplicateBlockProfile
│  │  │  ├─📄 DuplicateBlockProfileCommand.cs
│  │  │  ├─📄 DuplicateBlockProfileCommandHandler.cs
│  │  │  └─📄 DuplicateBlockProfileResult.cs
│  │  └─📁 UpdateBlockProfileCommand
│  │     ├─📄 UpdateBlockProfileCommand.cs
│  │     ├─📄 UpdateBlockProfileCommandHandler.cs
│  │     ├─📄 UpdateBlockProfileResult.cs
│  │     └─📄 UpdateBlockProfileValidator.cs
│  ├─📁 Planner
│  │  ├─📁 CreatePlannedBlock
│  │  │  ├─📄 CreatePlannedBlockCommand.cs
│  │  │  ├─📄 CreatePlannedBlockCommandHandler.cs
│  │  │  ├─📄 CreatePlannedBlockResult.cs
│  │  │  └─📄 CreatePlannedBlockValidator.cs
│  │  ├─📁 DeletePlannedBlock
│  │  │  ├─📄 DeletePlannedBlockCommand.cs
│  │  │  ├─📄 DeletePlannedBlockCommandHandler.cs
│  │  │  ├─📄 DeletePlannedBlockResult.cs
│  │  │  └─📄 DeletePlannedBlockValidator.cs
│  │  └─📁 UpdatePlannedBlock
│  │     ├─📄 UpdatePlannedBlockCommand.cs
│  │     ├─📄 UpdatePlannedBlockCommandHandler.cs
│  │     ├─📄 UpdatePlannedBlockResult.cs
│  │     └─📄 UpdatePlannedBlockValidator.cs
│  └─📁 Sessions
│     ├─📁 EndSession
│     │  ├─📄 EndSessionCommand.cs
│     │  ├─📄 EndSessionCommandHandler.cs
│     │  └─📄 EndSessionResult.cs
│     ├─📁 ResumeFromBreak
│     │  ├─📄 ResumeFromBreakCommand.cs
│     │  ├─📄 ResumeFromBreakCommandHandler.cs
│     │  └─📄 ResumeFromBreakResult.cs
│     ├─📁 SaveReflection
│     │  ├─📄 SaveReflectionCommand.cs
│     │  ├─📄 SaveReflectionCommandHandler.cs
│     │  └─📄 SaveReflectionResult.cs
│     ├─📁 StartBreak
│     │  ├─📄 StartBreakCommand.cs
│     │  ├─📄 StartBreakCommandHandler.cs
│     │  └─📄 StartBreakResult.cs
│     └─📁 StartSession
│        ├─📄 StartSessionCommand.cs
│        ├─📄 StartSessionCommandHandler.cs
│        ├─📄 StartSessionResult.cs
│        └─📄 StartSessionValidator.cs
├─📁 Validators
│  ├─📄 CreateBlockProfileCommandValidator.cs
│  ├─📄 CreatePlannedBlockCommandValidator.cs
│  ├─📄 DeleteBlockProfileCommandValidator.cs
│  ├─📄 StartSessionCommandValidator.cs
│  ├─📄 UpdateBlockProfileCommandValidator.cs
│  └─📄 UpdatePlannedBlockCommandValidator.cs
└─📄 DependencyInjection.cs
│
📁 NenFocus.Infrastructure
├─📁 Events
│  └─📄 InProcessDomainEventPublisher.cs
├─📁 Migrations
│  ├─📄 20260510213709_InitialCreate.cs
│  ├─📄 20260510213709_InitialCreate.Designer.cs
│  └─📄 NenFocusDbContextModelSnapshot.cs
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
│  ├─📄 BlockedAttemptTracker.cs
│  ├─📄 ScheduledEnforcementService.cs
│  ├─📄 SessionPersistenceService.cs
│  ├─📄 SessionStateService.cs
│  └─📄 WindowsEnforcementService.cs
├─📄 DependencyInjection.cs
└─📄 HostExtensions.cs
│
📁 NenFocus  (UI — entry point)
├─📁 Assets
│  └─📄 avalonia-logo.ico
├─📁 ViewModels
│  ├─📄 BlockProfilePageViewModel.cs
│  ├─📄 BoolToDotFillConverter.cs
│  ├─📄 BoolToOpacityConverter.cs
│  ├─📄 ChecklistItemViewModel.cs
│  ├─📄 CountToBoolConverter.cs
│  ├─📄 CountToVisibilityConverter.cs
│  ├─📄 HatsuPageViewModel.cs
│  ├─📄 HourLabelViewModel.cs
│  ├─📄 IntEqualConverter.cs
│  ├─📄 MainPageViewModel.cs
│  ├─📄 NavItemViewModel.cs
│  ├─📄 PlannedBlockTileViewModel.cs
│  ├─📄 PlannerPageViewModel.cs
│  ├─📄 PostSessionViewModel.cs
│  ├─📄 RenPageViewModel.cs
│  ├─📄 RitualShellViewModel.cs
│  ├─📄 ShellViewModel.cs
│  ├─📄 StringEqualConverter.cs
│  ├─📄 StubPageViewModels.cs
│  ├─📄 TenPageViewModel.cs
│  ├─📄 ViewModelBase.cs
│  ├─📄 WeekDayHeaderViewModel.cs
│  └─📄 ZetsuPageViewModel.cs
├─📁 Views
│  ├─📄 BlockProfilePageView.axaml / .axaml.cs
│  ├─📄 HatsuPageView.axaml / .axaml.cs
│  ├─📄 InsightsPageView.axaml / .axaml.cs
│  ├─📄 MainPageView.axaml / .axaml.cs
│  ├─📄 MainWindow.axaml / .axaml.cs
│  ├─📄 PlannerPageView.axaml / .axaml.cs
│  ├─📄 PostSessionView.axaml / .axaml.cs
│  ├─📄 RenPageView.axaml / .axaml.cs
│  ├─📄 RitualShellView.axaml / .axaml.cs
│  ├─📄 SettingsPageView.axaml / .axaml.cs
│  ├─📄 TenPageView.axaml / .axaml.cs
│  └─📄 ZetsuPageView.axaml / .axaml.cs
├─📄 App.axaml / App.axaml.cs
├─📄 app.manifest
├─📄 Program.cs
└─📄 ViewLocator.cs
│
📁 NenFocusExtension  (browser extension — solution root)
├─📄 background.js
├─📄 blocked.html
└─📄 manifest.json
```

---

## Current Implementation State

### Done
- Full domain model (Session, BlockProfile, FocusBlock, all value objects and events)
- `Session` entity has `Rating (int?)` and `Reflection (string?)` properties; `ResumeFromBreak()`, `SetReflection(string, int)`, and `Complete()` methods
- All Application commands, queries, validators, and event handlers — fully refactored to Vertical Slice Architecture (feature folders)
- `SystemSafetyConstants.UnkillableProcesses` safety shield prevents blocking critical Windows processes (validated in command validators via normalized process name)
- EF Core + SQLite persistence (all three repositories); `BlockedAttemptTracker` uses SQLite with a 2-second debounce threshold for accurate attempt counting
- Windows enforcement: process kill loop (`p.Kill(entireProcessTree: true)`) + hosts file rewriting + DNS flush + IPv6 (`::`) entries
- `ScheduledEnforcementService`: BackgroundService heartbeat running every 5 seconds; delegates policy computation and state management to `WindowsEnforcementService`
- `WindowsEnforcementService`: exposes a local HTTP API on `http://localhost:5005` — `/check` (returns `{ "blocked": true/false }` based on active policy) and `/report` (logs web block attempts from extension); uses `SemaphoreSlim` to prevent hosts file collisions
- Browser extension (`NenFocusExtension/`): intercepts navigation, queries `/check`, redirects to `blocked.html` if blocked, calls `/report` to increment the web block counter
- Registry enforcement: disables DNS-over-HTTPS (DoH) in Chrome, Edge, and Brave via `BuiltInDnsClientEnabled = 0` registry key, preventing browsers from bypassing the hosts file
- Admin manifest (`requireAdministrator`): required for hosts file writes, registry key edits, and high-privilege process tree termination
- Windows toast notifications + scheduled break reminders (in-process Task.Delay)
- Session rehydration on startup: `SessionStateService.InitializeAsync()` queries DB for active/on-break sessions and maps domain entities to `SessionDto`; `SessionStateService` injects `IBlockProfileRepository` to resolve the real profile name from its ID
- Session persistence across app restarts (`%AppData%\NenFocus\active_session.json`)
- Post-session reflection: `SaveReflectionCommand` persists `Rating (1–5)` and `Reflection (string)` to the `Session` entity
- Distraction logging: app kill attempts tracked via `IBlockedAttemptTracker.RecordBlocked`; website block attempts tracked via `RecordWebBlocked(domain)`; `EndSessionCommandHandler` groups logs by `ResourceName` and `AttemptType`, serializes to a `historyData` JSON field on the session
- `PolicySliceDto`: uses `WebsitePolicyMode` enum (`Blocklist`/`Allowlist`); `IsEmpty` is never true in Allowlist mode so enforcement stays active even with a small allowlist
- Ritual wizard (Ten → Zetsu → Ren → Hatsu) — fully wired end-to-end
- Main page: active session panel, countdown timer, progress bar, "USE NEN" button, end session, take a break, resume from break (auto-resume when break timer expires, restores correctly across app restarts)
- Shell navigation structure (sidebar with nav items, page switching)
- Block Profiles page: full CRUD (create, edit, delete, duplicate), tabbed modal (Websites / Apps tabs), file picker for app selection (opens to `C:\Program Files`), click-to-edit profile cards
- App blocking works correctly — `NormalizeProcessName` strips only `.exe` suffix (not `Path.GetFileNameWithoutExtension`) so dotted names like `kcc_10.0.1` are preserved
- Browser force-kill on session start for any site covered by the active block profile
- `SessionStartedEventHandler` fixed — no longer overwrites enforcement with an empty policy; now only sends the session-start notification (enforcement is applied by the command handler)
- Planner use cases fully refactored to Vertical Slice Architecture: `CreatePlannedBlock/`, `UpdatePlannedBlock/`, `DeletePlannedBlock/` — each with Command, Result, CommandHandler, Validator; `DeletePlannedBlock` returns `DeletePlannedBlockResult` (not `bool`); handlers apply `.ToUniversalTime()` before any DB/overlap logic

### Not Yet Built
| Area | Notes |
|------|-------|
| Planner page | Stub only — calendar, block creation, Planned Period wizard (use cases are built; UI is not) |
| Insights page | Stub only — session history, stats, milestones, distraction log |
| Settings page | Stub only — theme, strict mode, notification filter, sounds |
| Checklist persistence | UI present on Main page; no storage wired up |
| Allowlist full enforcement | Browser extension handles it for HTTP/S traffic; hosts file alone can't do default-deny |
| Planned session activation | Scheduled blocks don't yet auto-trigger the ritual or apply enforcement at start time |
| Daily Ritual Summary | Morning popup with today's scheduled blocks and streak — not built |
| End-of-week recap | Not built |
| Export/import JSON for profiles | Not built |
| Strict Mode | Not built |


---

## Key Behaviours & Rules

### Quests
- Quests are named tasks scoped to a **Daily**, **Weekly**, or **Monthly** period.
- Created and managed in the Planner page.
- The Main page displays today's active quests (daily quests for today, weekly quests covering this week, monthly quests covering this month).
- In the Zetsu ritual step, the user can optionally link the session to a specific quest from today's active list.
- Quest completion is recorded in Insights (completion rate, streaks, session time per quest).
- A day/week/month can have at most one quest list of each scope type.

### Session Rules
- Only one Focus session at a time (`HasActiveSessionAsync` check on start).
- Once a session starts, the profile is **locked** — it cannot be edited mid-session.
- Session-only adjustments (Ren page) are not persisted to the profile; they apply only for the duration.
- Users can **extend** the timer (make longer) but cannot shorten it or end early via the UI.
- Passive blocks may overlap with anything; Focus + Focus overlap is rejected at creation time.

### Session Adjustment Rules (Ren page)
| Session type | Apps | Website (Blocklist profile) | Website (Allowlist profile) |
|-------------|------|---------------------------|-----------------------------|
| Impromptu | Add or temporarily remove | Add or remove allowed | Add only; no removals |
| Planned | Add only | Add only | No changes allowed |

### Policy Computation (ComputeActivePolicySliceQuery)
Given multiple active blocks at time `t`:
- **Apps**: union of all blocked apps across all active blocks (always blocklist).
- **Websites**: if any active block uses Allowlist mode → default-deny, allowed = intersection of all allowlists. Otherwise → block the union of all blocklisted sites.

### Enforcement
- **App blocking**: `WindowsEnforcementService` kill loop, `p.Kill(entireProcessTree: true)`, driven by `ScheduledEnforcementService` (5s heartbeat). Process handles are disposed in a `finally` block.
- **Website blocking — hosts file**: writes `0.0.0.0 <domain>` and `:: <domain>` entries in `C:\Windows\System32\drivers\etc\hosts` between `# NEN_FOCUS_START` / `# NEN_FOCUS_END` markers (case-sensitive). Calls `ipconfig /flushdns` after each write. Domains are normalized (strips `https://`, `www.`, paths/ports) before insertion.
- **Website blocking — browser extension**: `NenFocusExtension` intercepts navigations, asks `WindowsEnforcementService` at `http://localhost:5005/check`, and redirects blocked tabs to `blocked.html`. Reports attempts via `/report`.
- **DoH bypass prevention**: registry keys `BuiltInDnsClientEnabled = 0` are written for Chrome, Edge, and Brave to ensure browsers cannot route around the hosts file via DNS-over-HTTPS.
- **Allowlist mode**: `PolicySliceDto.WebsitePolicyMode == Allowlist` → extension enforces default-deny; intersection of all active allowlists is the permitted set. Hosts-file alone cannot enforce this.

---

## UI Conventions

- **Navigation**: left sidebar (210px), `TransitioningContentControl` for page content. Ritual overlays the entire window via a full-screen `ContentControl` when `IsRitualActive = true`.
- **Ritual steps**: top bar shows 4 step-indicator dots; active dot is gold (`#B8922A`), inactive is `#DDDDE8`. `CrossFade` transition (450ms) between steps.
- **Accent colour**: gold (`#B8922A`) — used for active nav, step dots, and the "USE NEN" button text.
- **Window size**: 1100×720, minimum 900×580.
- **ViewLocator convention**: `XyzViewModel` → `NenFocus.Views.XyzView` (strip "ViewModel", add "View").
- Do not put business logic in Views or code-behind. Views bind to ViewModels only.
- ViewModels get dependencies (commands, queries, services) injected via constructor.

---

## Development Notes

- **Platform**: Windows only (`net9.0-windows`). The enforcement and notification services use Windows APIs. Do not attempt to make these cross-platform without explicit instruction.
- **Admin required**: The app manifest sets `level="requireAdministrator"`. This is mandatory for hosts file writes, registry key edits, and killing high-privilege process trees.
- **Database**: SQLite via EF Core. Run `dotnet ef migrations add <Name> --project NenFocus.Infrastructure --startup-project NenFocus` to add migrations. HostExtensions.EnsureDatabase() calls context.Database.Migrate() on startup to ensure the schema is up to date without bypassing the migration history.. SQLite cannot do `ORDER BY` or `>` comparisons on `DateTimeOffset` in SQL — fetch to memory first, then filter/sort in C#.
- **Tags storage**: `BlockProfile.Tags` is stored as a serialized string in the DB. `GetByTagsAsync` filters in-memory. This is a known workaround.
- **FocusBlock update**: There is no `FocusBlock.Update()` method — `UpdatePlannedBlockCommand` uses delete + recreate as a workaround.
- **Hosts file markers**: must be exactly `# NEN_FOCUS_START` and `# NEN_FOCUS_END` (case-sensitive, underscores). Any deviation causes duplicate entries on next write.
- **Break reminders**: Implemented as `Task.Delay` fire-and-forget tasks tracked by `CancellationTokenSource`. They do not survive app restarts. A persistent scheduler is a future concern.
- **DI scope**: `InProcessDomainEventPublisher` creates a new DI scope per event handler to avoid `DbContext` concurrency issues.
- **Validator registration**: `services.AddValidatorsFromAssembly(assembly)` in `DependencyInjection.cs` auto-registers all `AbstractValidator<T>` classes found in feature folders. No manual registration needed for validators.
- **App startup sequence** (`Program.cs`): build host → ensure DB → `SessionStateService.InitializeAsync()` (rehydrate session from DB) → resolve `ShellViewModel` → show window.
- **NuGet alignment**: all `Microsoft.CodeAnalysis` packages pinned to `5.3.0` to avoid NU1608 version conflicts.
