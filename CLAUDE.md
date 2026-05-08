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
NenFocus.Domain/
  Entities/           BaseEntity, Session, FocusBlock
  Aggregates/         BlockProfile (aggregate root)
  ValueObjects/       SessionGoal, SessionDuration, BreakSettings, BlockedApp, BlockedSite
  Enums/              SessionStatus, BlockType, WebsitePolicy
  Events/             DomainEvent (base), SessionStartedEvent, SessionEndedEvent,
                      BreakStartedEvent, BlockProfileCreatedEvent

NenFocus.Application/
  Interfaces/         ISessionRepository, IBlockProfileRepository, IPlannedBlockRepository,
                      IEnforcementService, INotificationService, ISessionStateService,
                      IDomainEventPublisher, ICommandHandler, IQueryHandler, IBlockedAttemptTracker
  DTOs/               SessionDto, SessionSummaryDto, BlockProfileDto, PlannedBlockDto,
                      PolicySliceDto, BreakSettingsDto
  UseCases/
    Sessions/         StartSessionCommand, EndSessionCommand, StartBreakCommand,
                      ResumeFromBreakCommand, ExtendSessionCommand
    BlockProfiles/    CreateBlockProfileCommand, UpdateBlockProfileCommand,
                      DeleteBlockProfileCommand, DuplicateBlockProfileCommand
    Planner/          CreatePlannedBlockCommand, UpdatePlannedBlockCommand,
                      DeletePlannedBlockCommand
  Queries/            GetActiveSessionQuery, GetAllBlockProfilesQuery,
                      GetPlannedBlocksForDayQuery, GetSessionHistoryQuery,
                      ComputeActivePolicySliceQuery
  Validators/         FluentValidation validators for every command
  EventHandlers/      SessionStartedEventHandler, SessionEndedEventHandler,
                      BreakStartedEventHandler, BlockProfileCreatedEventHandler
  DependencyInjection.cs

NenFocus.Infrastructure/
  Persistence/
    NenFocusDbContext.cs
    Repositories/     BlockProfileRepository, SessionRepository, PlannedBlockRepository
  Events/             InProcessDomainEventPublisher
  Services/           WindowsEnforcementService, SessionPersistenceService
  Notifications/      WindowsNotificationService
  DependencyInjection.cs
  HostExtensions.cs   (EnsureDatabase)

NenFocus/
  Program.cs          (host setup, migration, session restore, DI)
  App.axaml(.cs)      (FluentTheme, shutdown handler)
  ViewLocator.cs      (convention: XyzViewModel → XyzView)
  ViewModels/
    ShellViewModel, RitualShellViewModel
    TenPageViewModel, ZetsuPageViewModel, RenPageViewModel, HatsuPageViewModel
    MainPageViewModel
	
NavItemViewModel, QuestItemViewModel    StubPageViewModels (BlockProfiles, Planner, Insights, Settings)
    Converters: BoolToDotFillConverter, BoolToOpacityConverter,
                CountToVisibilityConverter, StringEqualConverter, IntEqualConverter
  Views/
    MainWindow.axaml
    MainPageView, RitualShellView
    TenPageView, ZetsuPageView, RenPageView, HatsuPageView
    (stub views for BlockProfiles, Planner, Insights, Settings)
```

---

## Current Implementation State

### Done
- Full domain model (Session, BlockProfile, FocusBlock, all value objects and events)
- All Application commands, queries, validators, and event handlers
- EF Core + SQLite persistence (all three repositories)
- Windows enforcement: process kill loop (2s polling) + hosts file rewriting + DNS flush
- Windows toast notifications + scheduled break reminders (in-process Task.Delay)
- Session persistence across app restarts (`%AppData%\NenFocus\active_session.json`)
- Ritual wizard (Ten → Zetsu → Ren → Hatsu) — fully wired end-to-end
- Main page: active session panel, countdown timer, progress bar, "USE NEN" button, end session, take a break, resume from break (auto-resume when break timer expires, restores correctly across app restarts)
- Shell navigation structure (sidebar with nav items, page switching)
- Block Profiles page: full CRUD (create, edit, delete, duplicate), tabbed modal (Websites / Apps tabs), file picker for app selection (opens to `C:\Program Files`), click-to-edit profile cards
- App blocking works correctly — `NormalizeProcessName` strips only `.exe` suffix (not `Path.GetFileNameWithoutExtension`) so dotted names like `kcc_10.0.1` are preserved
- Browser force-kill on session start for any site covered by the active block profile
- `SessionStartedEventHandler` fixed — no longer overwrites enforcement with an empty policy; now only sends the session-start notification (enforcement is applied by the command handler)

### Not Yet Built
| Area | Notes |
|------|-------|
| Planner page | Stub only — calendar, block creation, Planned Period wizard |
| Insights page | Stub only — session history, stats, milestones, distraction log |
| Settings page | Stub only — theme, strict mode, notification filter, sounds |
| Checklist persistence | UI present on Main page; no storage wired up |
| Allowlist full enforcement | Hosts-file approach can't do default-deny; needs DNS proxy layer |
| Planned session activation | Scheduled blocks don't yet auto-trigger the ritual or apply enforcement at start time |
| Daily Ritual Summary | Morning popup with today's scheduled blocks and streak — not built |
| Distraction logging | App kill attempts tracked via IBlockedAttemptTracker and shown on post-session screen; website attempt tracking not yet built |
| Post-session reflection | No reflection prompt or storage |
| End-of-week recap | Not built |
| Export/import JSON for profiles | Not built |
| Strict Mode | Not built |
| Checklist persistence | UI present on Main page; no storage wired up |


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
- App blocking: process kill loop, 2-second poll interval (`WindowsEnforcementService`).
- Website blocking: writes/removes entries in `C:\Windows\System32\drivers\etc\hosts` between `# NenFocus Start` / `# NenFocus End` markers.
- Full allowlist (default-deny) is not yet supported via hosts file — requires a DNS proxy.

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
- **Database**: SQLite via EF Core. Run `dotnet ef migrations add <Name> --project NenFocus.Infrastructure --startup-project NenFocus` to add migrations. `HostExtensions.EnsureDatabase()` runs them on startup automatically.
- **Tags storage**: `BlockProfile.Tags` is stored as a serialized string in the DB. `GetByTagsAsync` filters in-memory. This is a known workaround.
- **FocusBlock update**: There is no `FocusBlock.Update()` method — `UpdatePlannedBlockCommand` uses delete + recreate with the same ID as a workaround.
- **Break reminders**: Implemented as `Task.Delay` fire-and-forget tasks tracked by `CancellationTokenSource`. They do not survive app restarts. A persistent scheduler is a future concern.
- **DI scope**: `InProcessDomainEventPublisher` creates a new DI scope per event handler to avoid `DbContext` concurrency issues.
- **App startup sequence** (`Program.cs`): build host → ensure DB → try restore session → resolve `ShellViewModel` → show window.
