# NenFocus — Technical Specification & Infrastructure Notes

Tactical implementation details, current build state, and platform-specific gotchas. Read this when you need to understand *how something works*, not *why it exists* (that lives in `ARCHITECT_GOVERNANCE.md` and `DOMAIN_DICTIONARY.md`).

---

## Current Implementation State

### Done

- Full domain model (Session, BlockProfile, FocusBlock, all value objects and events)
- `Session` entity has `Rating (int?)` and `Reflection (string?)` properties; `ResumeFromBreak()`, `SetReflection(string, int)`, and `Complete()` methods
- All Application commands, queries, validators, and event handlers — fully refactored to Vertical Slice Architecture (feature folders)
- `SystemSafetyConstants.UnkillableProcesses` safety shield prevents blocking critical Windows processes (validated in command validators via normalised process name)
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
- Distraction logging: app kill attempts tracked via `IBlockedAttemptTracker.RecordBlocked`; website block attempts tracked via `RecordWebBlocked(domain)`; `EndSessionCommandHandler` groups logs by `ResourceName` and `AttemptType`, serialises to a `historyData` JSON field on the session
- `PolicySliceDto`: uses `WebsitePolicyMode` enum (`Blocklist`/`Allowlist`); `IsEmpty` is never true in Allowlist mode so enforcement stays active even with a small allowlist
- Ritual wizard (Ten → Zetsu → Ren → Hatsu) — fully wired end-to-end
- Main page: active session panel, countdown timer, progress bar, "USE NEN" button, end session, take a break, resume from break (auto-resume when break timer expires, restores correctly across app restarts)
- Shell navigation structure (sidebar with nav items, page switching)
- Block Profiles page: full CRUD (create, edit, delete, duplicate), tabbed modal (Websites / Apps tabs), file picker for app selection (opens to `C:\Program Files`), click-to-edit profile cards
- App blocking works correctly — `NormalizeProcessName` strips only `.exe` suffix (not `Path.GetFileNameWithoutExtension`) so dotted names like `kcc_10.0.1` are preserved
- Browser force-kill on session start for any site covered by the active block profile
- `SessionStartedEventHandler` fixed — no longer overwrites enforcement with an empty policy; now only sends the session-start notification (enforcement is applied by the command handler)
- Planner use cases fully refactored to Vertical Slice Architecture: `CreatePlannedBlock/`, `UpdatePlannedBlock/`, `DeletePlannedBlock/` — each with Command, Result, CommandHandler, Validator; `DeletePlannedBlock` returns `DeletePlannedBlockResult` (not `bool`); handlers apply `.ToUniversalTime()` before any DB/overlap logic
- `IActiveSessionMonitor` / `ActiveSessionMonitor`: singleton countdown engine that owns the `System.Timers.Timer`, subscribes to `ISessionStateService.SessionChanged`, auto-ends sessions and auto-resumes breaks by calling command handlers via `IServiceScopeFactory`
- `StartBreakCommandHandler` and `ResumeFromBreakCommandHandler` now call `_sessionState.SetActive(current with { ... })` after saving — break UI state propagates correctly
- `SessionStateService.MapToDto` maps `BreakSettings` domain value object to `BreakSettingsDto` (was previously always null on DB restore)

### Not Yet Built

| Area | Notes |
|------|-------|
| Planner page | Stub only — calendar, block creation, Planned Period wizard (use cases are built; UI is not) |
| Insights page | Stub only — session history, stats, milestones, distraction log |
| Settings page | Stub only — theme, strict mode, notification filter, sounds |
| Checklist / Quest persistence | UI present on Main page; no storage wired up |
| Allowlist full enforcement | Browser extension handles it for HTTP/S traffic; hosts file alone can't do default-deny |
| Planned session activation | Scheduled blocks don't yet auto-trigger the ritual or apply enforcement at start time |
| Daily Ritual Summary | Morning popup with today's scheduled blocks and streak — not built |
| End-of-week recap | Not built |
| Export/import JSON for profiles | Not built |
| Strict Mode | Not built |

---

## Key Behaviours & Rules

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

### Policy Computation (`ComputeActivePolicySliceQuery`)

Given multiple active blocks at time `t`:
- **Apps**: union of all blocked apps across all active blocks (always blocklist).
- **Websites**: if any active block uses Allowlist mode → default-deny, allowed = intersection of all allowlists. Otherwise → block the union of all blocklisted sites.

### Enforcement Implementation Details

- **App blocking**: `WindowsEnforcementService` kill loop, `p.Kill(entireProcessTree: true)`, driven by `ScheduledEnforcementService` (5s heartbeat). Process handles are disposed in a `finally` block.
- **Website blocking — hosts file**: writes `0.0.0.0 <domain>` and `:: <domain>` entries in `C:\Windows\System32\drivers\etc\hosts` between `# NEN_FOCUS_START` / `# NEN_FOCUS_END` markers (case-sensitive, underscores). Calls `ipconfig /flushdns` after each write. Domains are normalised (strips `https://`, `www.`, paths/ports) before insertion.
- **Website blocking — browser extension**: `NenFocusExtension` intercepts navigations, asks `WindowsEnforcementService` at `http://localhost:5005/check`, and redirects blocked tabs to `blocked.html`. Reports attempts via `/report`.
- **DoH bypass prevention**: registry keys `BuiltInDnsClientEnabled = 0` are written for Chrome, Edge, and Brave to ensure browsers cannot route around the hosts file via DNS-over-HTTPS.
- **Allowlist mode**: `PolicySliceDto.WebsitePolicyMode == Allowlist` → extension enforces default-deny; intersection of all active allowlists is the permitted set. Hosts file alone cannot enforce this.
- **Local HTTP API** (`http://localhost:5005`): hosted by `WindowsEnforcementService`. `/check` returns `{ "blocked": true/false }`. `/report` increments the blocked-attempt counter for the current domain. The API starts when enforcement is applied and shuts down when it is removed.

---

## Development Notes

### Platform & Admin

- **Platform**: Windows only (`net9.0-windows`). Enforcement and notification services use Windows APIs. Do not attempt to make these cross-platform without explicit instruction.
- **Admin required**: The app manifest sets `level="requireAdministrator"`. This is mandatory for hosts file writes, registry key edits, and killing high-privilege process trees.

### Database

- **SQLite via EF Core**. Add migrations: `dotnet ef migrations add <Name> --project NenFocus.Infrastructure --startup-project NenFocus`
- `HostExtensions.EnsureDatabase()` calls `context.Database.Migrate()` on startup — no bypass of migration history.
- SQLite cannot do `ORDER BY` or `>` comparisons on `DateTimeOffset` in SQL — fetch to memory first, then filter/sort in C#.
- `BlockProfile.Tags` is stored as a serialised string in the DB. `GetByTagsAsync` filters in-memory. Known workaround.

### Known Workarounds

- **FocusBlock update**: There is no `FocusBlock.Update()` method — `UpdatePlannedBlockCommand` uses delete + recreate.
- **Hosts file markers**: must be exactly `# NEN_FOCUS_START` and `# NEN_FOCUS_END` (case-sensitive, underscores). Any deviation causes duplicate entries on next write.
- **Break reminders**: Implemented as `Task.Delay` fire-and-forget tasks tracked by `CancellationTokenSource`. They do not survive app restarts. A persistent scheduler is a future concern.

### DI & Registration

- **DI scope**: `InProcessDomainEventPublisher` creates a new DI scope per event handler to avoid `DbContext` concurrency issues.
- **Validator registration**: `services.AddValidatorsFromAssembly(assembly)` in `DependencyInjection.cs` auto-registers all `AbstractValidator<T>` classes. No manual registration needed.
- **Singleton + scoped pattern**: `ActiveSessionMonitor` (singleton) calls scoped command handlers by creating short-lived scopes via `IServiceScopeFactory`. This is the correct pattern; do not inject scoped services directly into singletons.
- **App startup sequence** (`Program.cs`): build host → ensure DB → `SessionStateService.InitializeAsync()` (rehydrate session from DB) → resolve `ShellViewModel` → show window.
- **NuGet alignment**: all `Microsoft.CodeAnalysis` packages pinned to `5.3.0` to avoid NU1608 version conflicts.
