NenFocus — Project Brief
Overview
Windows desktop focus tool (Avalonia/.NET 9). Core features: app/website blocking, 4-step ritual session setup (Ten, Zetsu, Ren, Hatsu), and calendar-based planning.

Architecture & Patterns
Clean Architecture: UI (Avalonia) → Application (CQRS) ← Infrastructure (EF Core/WinAPI) → Domain (Entities).

CQRS: Commands for mutations, Queries for reads. No mixed service classes.

MVVM: CommunityToolkit.Mvvm. ViewModels never reference Views.

Events: Domain events published via IInProcessDomainEventPublisher.

Value Objects: Record types with constructor validation (e.g., SessionGoal, BlockedApp).

Project Structure
NenFocus.Domain/: Entities (Session, FocusBlock), Aggregates (BlockProfile), ValueObjects, Enums, Events.

NenFocus.Application/: Interfaces, DTOs, UseCases (Commands/Queries), Validators (FluentValidation), EventHandlers.

NenFocus.Infrastructure/: Persistence (EF Core + SQLite), WindowsEnforcementService, WindowsNotificationService.

NenFocus/: Program.cs, ViewModels (Shell, Ritual, Main), Views (XyzView), Converters.

Implementation State

Done
- Full domain model: Session, FocusBlock, BlockProfile, value objects, domain events (NenFocus.Domain).
- Application layer: CQRS handlers (commands/queries), FluentValidation validators, DTOs, event handlers, interfaces (ISessionRepository, IBlockProfileRepository, IPlannedBlockRepository, IEnforcementService, INotificationService, IDomainEventPublisher, ICommandHandler/IQueryHandler), DependencyInjection.
- Persistence: EF Core + SQLite, DbContext + migrations, repositories (SessionRepository, BlockProfileRepository, PlannedBlockRepository).
- In-process domain event publisher implementation (InProcessDomainEventPublisher).
- Windows enforcement: WindowsEnforcementService (process-kill loop + hosts-file editing).
- Windows notifications: WindowsNotificationService.
- Session persistence and restore: SessionPersistenceService + Program.cs host/startup sequence (EnsureDatabase, session restore).
- Ritual wizard UI and wiring: Ten, Zetsu, Ren, Hatsu pages and ViewModels; full ritual flow wired.
- Block Profile CRUD: UI (BlockProfilePageView/ViewModel) and persistence/repository.
- Break support (backend): StartBreakCommand, ResumeFromBreakCommand, BreakStartedEvent, DB migration for break fields, domain StartBreak/Resume methods and handlers present.
- Post-session reflection: SaveReflectionCommand, PostSessionView/ViewModel.
- Blocked-attempt tracking: BlockedAttemptTracker implementation in Infrastructure.
- General UI scaffolding: Views, ViewModels, converters, ViewLocator, Program.cs, DI files across projects.

Partially implemented
- Planner: PlannerPageView/ViewModel and planned-block repository/queries exist, but the calendar UI and full planner UX are stubs.
- Insights: InsightsPageView exists but the analytics/stats/distraction-log features are stubs.
- Settings: SettingsPageView exists but many settings (strict mode, notification filters, sounds) are not wired.
- Break/Extend UI controls: backend commands/events present, but visible UI controls for starting/resuming/extending breaks are not fully wired or exposed.

Not yet built / remaining work
- Planner: full calendar UI, planned-period wizard, richer planner UX.
- Planned session activation: automatic activation of planned blocks (auto-triggering ritual and enforcement).
- Insights: complete session history, stats, milestones, distraction logging UI & analysis.
- Settings: persistent settings UI for strict mode, notification filters, sounds.
- Checklist persistence for Main page checklist.
- Full allowlist website enforcement (requires DNS proxy — hosts-file approach insufficient).
- Daily ritual summary, end-of-week recap, export/import JSON for profiles.
- Strict Mode: full implementation of any stricter enforcement rules beyond current behavior.

Key Rules
Sessions: One focus session at a time. Profile locked mid-session.

Adjustments: "Ren" page adjustments are temporary and session-only.

Policy: Apps are always blocklist (union). Websites: intersection if any block is Allowlist; otherwise union of blocklists.

Enforcement: 2s polling process kill; hosts file modification for sites. Windows only (net9.0-windows).

UI Conventions
Theme: Accent Gold #B8922A. Sidebar 210px. Min size 900x580.

Navigation: XyzViewModel → NenFocus.Views.XyzView.

Rituals: Full-screen overlay; gold active dots; 450ms CrossFade transition.

Dev Notes
Migrations: dotnet ef migrations add <Name> --project NenFocus.Infrastructure --startup-project NenFocus

DI: InProcessDomainEventPublisher creates a new scope per handler.

Startup: Host build → DB Ensure → Session restore → Resolve ShellViewModel.
