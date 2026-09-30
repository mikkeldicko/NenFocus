# NenFocus — Technical Specification & Infrastructure Notes

Tactical implementation details, current build state, and platform-specific gotchas. Read this when you need to understand *how something works*, not *why it exists* (that lives in `ARCHITECT_GOVERNANCE.md` and `DOMAIN_DICTIONARY.md`).

---

## Current Implementation State

### Done

- Full domain model (Session, BlockPreset, FocusBlock, BackgroundBlock, all value objects and events)
- (break methods superseded by Gap 63 — breaks are a stored schedule, not state) `Session` entity has `Rating (int?)` and `Reflection (string?)` properties; `ResumeFromBreak()`, `SetReflection(string, int)`, and `Complete()` methods
- All Application commands, queries, validators, and event handlers — fully refactored to Vertical Slice Architecture (feature folders)
- `SystemSafetyConstants.UnkillableProcesses` safety shield prevents blocking critical Windows processes (validated in command validators via normalised process name)
- EF Core + SQLite persistence (all three repositories); `BlockedAttemptTracker` uses SQLite with a 2-second debounce threshold for accurate attempt counting
- Windows enforcement: process kill loop (`p.Kill(entireProcessTree: true)`) + hosts file rewriting + DNS flush + IPv6 (`::`) entries
- `ScheduledEnforcementService`: BackgroundService heartbeat running every 5 seconds; delegates policy computation and state management to `WindowsEnforcementService`
- `WindowsEnforcementService`: exposes a local HTTP API on `http://localhost:5005` — `/check` (returns `{ "blocked": true/false }` based on active policy) and `/report` (logs web block attempts from extension); uses `SemaphoreSlim` to prevent hosts file collisions
- Browser extension (`NenFocusExtension/`): intercepts navigation (`background.js`), queries `/check`, redirects to `blocked.html` if blocked, calls `/report` to increment the web block counter. A tab already open when a block activates is caught too (2026-09-02, see `DESIGN_GAP_HISTORY.md` Gap 46) — `poll.js`, a content script injected into every page, self-polls `/check` every 2s independent of navigation; `background.js` also backfills `poll.js` into every already-open tab on extension install/update/browser startup (`chrome.scripting.executeScript`), since Chrome's declarative `content_scripts` only auto-inject on *future* page loads.
- Registry enforcement: disables DNS-over-HTTPS (DoH) in Chrome, Edge, and Brave via `BuiltInDnsClientEnabled = 0` registry key, preventing browsers from bypassing the hosts file
- Admin manifest (`requireAdministrator`): required for hosts file writes, registry key edits, and high-privilege process tree termination
- Windows toast notifications via `WindowsNotificationService.ShowAsync`, called only by `Notifier` (Gap 84; the unused break-reminder methods were deleted then). Toast failures are logged via `ILogger` as warnings (VS Output / Event Viewer), never thrown. Gotcha: Windows Do Not Disturb suppresses every toast without any error — check it first when "no notification appears."
- Session rehydration on startup: `SessionStateService.InitializeAsync()` queries DB for the active session and maps domain entities to `SessionDto`; `SessionStateService` injects `IBlockPresetRepository` to resolve the real preset name from its ID
- Session persistence across app restarts (`%AppData%\NenFocus\active_session.json`)
- Post-session reflection: `SaveReflectionCommand` persists `Rating (1–5)` and `Reflection (string)` to the `Session` entity
- Distraction logging: app kill attempts tracked via `IBlockedAttemptTracker.RecordBlocked`; website block attempts tracked via `RecordWebBlocked(domain)`; `EndSessionCommandHandler` groups logs by `ResourceName` and `AttemptType`, serialises to a `historyData` JSON field on the session
- `ActivePolicySlice` (Domain Value Object, `NenFocus.Domain/ValueObjects/ActivePolicySlice.cs`): the canonical enforcement contract produced by `PolicyEngine.ComputeSlice()`. Carries `WebsiteFilterTypeMode` (`Blocklist`/`Allowlist`), `BlockedApps`, `BlockedSites`, `AllowedSites`, `ActiveSessionId`, and `SliceStart`/`SliceEnd` — blocking only; the Ritual fields it used to carry were removed in Gap 62. `IsEmpty` is `true` only in Blocklist mode with no blocked apps or sites. Consumed directly by `WindowsEnforcementService` via `EnforcementSynchronizer` — no DTO crosses the Application→Infrastructure enforcement boundary. Replaces the deleted `PolicySliceDto`.
- `PolicyEngine` / `IPolicyEngine` (`NenFocus.Domain/Services/`): stateless Domain service that accepts raw `BlockPreset`, `Session`, and `PlannedBlock` collections and computes the `ActivePolicySlice`. All merge logic (app union, allowlist/blocklist precedence, what a break lifts) is encapsulated here. `ComputeActivePolicySliceQueryHandler` is now a pure orchestrator — it fetches, then delegates.
- **Blocklist mode fixed**: `PolicyEngine` correctly populates `BlockedSites` in Blocklist mode (was always an empty set in the old `ComputeActivePolicySliceQueryHandler`, causing the browser extension to block nothing)
- Ritual wizard (Ten → Zetsu → Ren → Hatsu) — fully wired end-to-end
- Main page: active session panel, countdown timer, progress bar (with break markers — see Gap 61 below), "USE NEN" button, end session (breaks follow the session's own `BreakSchedule` with no buttons — Gap 63; end-break-early is parked in Open Gap P)
- Shell navigation structure (sidebar with nav items, page switching)
- Block Presets page: full CRUD (create, edit, delete, duplicate), tabbed modal (Websites / Apps tabs), file picker for app selection (opens to `C:\Program Files`), click-to-edit preset cards
- App blocking works correctly — `NormalizeProcessName` strips only `.exe` suffix (not `Path.GetFileNameWithoutExtension`) so dotted names like `kcc_10.0.1` are preserved
- **Enforcement gap fixed** (superseded by Gap 62 — the handler now just calls `IEnforcementSynchronizer.SyncAsync`): `SessionStartedEventHandler` now injects `IEnforcementService` and `IQueryHandler<ComputeActivePolicySliceQuery, ActivePolicySlice>`; computes the current policy slice (a Domain Value Object) and calls `ApplyPolicyAsync` before sending the session-start notification. Enforcement is event-driven: `StartSessionCommandHandler` publishes `SessionStartedEvent` → `InProcessDomainEventPublisher` → `SessionStartedEventHandler` applies the policy
- Planner use cases fully refactored to Vertical Slice Architecture: `CreatePlannedBlock/`, `UpdatePlannedBlock/`, `DeletePlannedBlock/` — each with Command, Result, CommandHandler, Validator; `DeletePlannedBlock` returns `DeletePlannedBlockResult` (not `bool`); handlers apply `.ToUniversalTime()` before any DB/overlap logic
- (break half superseded by Gap 63 — the monitor now only reads the session's `BreakSchedule`) `IActiveSessionMonitor` / `ActiveSessionMonitor`: singleton countdown engine that owns the `System.Timers.Timer`, subscribes to `ISessionStateService.SessionChanged`, auto-ends sessions and auto-resumes breaks by calling command handlers via `IServiceScopeFactory`
- (superseded by Gap 63 — both commands deleted) `StartBreakCommandHandler` and `ResumeFromBreakCommandHandler` now call `_sessionState.SetActive(current with { ... })` after saving — break UI state propagates correctly
- (superseded by Gap 63 — now maps the session's `BreakSchedule`) `SessionStateService.MapToDto` maps `BreakTimer` domain value object to `BreakTimerDto` (was previously always null on DB restore; `BreakTimer` replaced the retired `BreakSettings`, see Gap 61)
- BreakTimerPreset (Pomodoro-style timer presets: Classic/Deep Work/Flow, cycles + long break, automatic work/break transitions, progress-bar break markers, optional enforcement pause during a break) — see `DESIGN_GAP_HISTORY.md` Gap 61. No Settings-page CRUD UI yet.
- `Session` entity stores `TemporaryBlockedApps` and `TemporaryBlockedSites` (`IReadOnlyList<string>`, JSON-serialised to TEXT columns via EF Core value converter); `PolicyEngine.ComputeSlice()` unions these into every policy computation so session-level Ren page additions are enforced immediately. Persisted to the DB so Insights can display per-session temporary additions alongside the base preset
- `StartSessionCommandHandler` now fetches `PresetName` from `IBlockPresetRepository` and includes it in the `SessionDto` passed to `ISessionStateService.SetActive`; preset name is no longer blank on the Main page after session start
- `StartSessionCommandHandler` publishes `SessionStartedEvent` via `IDomainEventPublisher` after saving the session and setting state, completing the event-driven enforcement chain
- `ScheduledEnforcementService` asks `GetFocusBlockAwaitingRitualQuery` each heartbeat cycle and calls `_sessionState.SetRitualRequired(true, blockId)` when it names a block, triggering the Ritual overlay automatically at the Focus Block's designated start time (Gap 62)
- `SessionOrigin` enum added (`Enums/SessionOrigin.cs`); values: `Spontaneous`, `Planned`. The previous `Impromptu` value was renamed to `Spontaneous` — a data migration renames existing rows in the `Sessions` table
- `Session.Origin (SessionOrigin)` and `Session.OriginatingBlockId (Guid?)` added to the `Session` entity; set by `StartSessionCommandHandler` at session creation time; never changed after the session starts
- `FocusBlock.RitualCompletedAt (DateTime?)` added; `FocusBlock.MarkRitualCompleted(DateTime completedAt)` method stamps completion at the exact moment the session is created; called by `StartSessionCommandHandler` when a Planned session starts; allows Insights to detect missed blocks from the entity alone without a join to Session
- `StartSessionCommandHandler` updated: when `OriginatingBlockId` is provided, fetches the `FocusBlock`, calls `MarkRitualCompleted(DateTime.UtcNow)`, and saves it; sets `Session.Origin = Planned`; when `OriginatingBlockId` is null, sets `Session.Origin = Spontaneous`. Also accepts `AdditionalBlockedSites` and `AdditionalBlockedApps` (raw strings from the Ren page) and writes them to `Session.TemporaryBlockedSites`/`TemporaryBlockedApps`
- **DDD entity refactors (Gap 4 closure)**: `BaseEntity` abstract class deleted — `Id (Guid)` is now declared directly on each entity/aggregate with a private setter; `CreatedAt` removed from all entities and from every DB table. Each aggregate (`Session`, `BlockPreset`) owns a private `_domainEvents` list; `DomainEvents` and `ClearDomainEvents()` expose it. Domain events are appended inside entity state-transition methods (`Session.Start()`, `Session.Complete()`, `Session.StartBreak()`, `BlockPreset.Create()`, etc.) — not inside command handlers. Handlers call `entity.DomainEvents` and dispatch via `IDomainEventPublisher.PublishAllAsync`, then clear with `entity.ClearDomainEvents()`.
- `Session.BlockedAttemptsSerialized` and any related `SetBlockedAttempts()` method removed. `BlockedAttempt` rows (append-only, keyed by `SessionId`) are the sole persistence path for attempt data. Migration `20260512000001_RemoveBlockedAttemptsSerialized` drops the `BlockedAttempts` column from the `Sessions` table.
- **Entity hierarchy refactored**: `FocusBlock` entity deleted. `PassiveBlock` renamed to `BackgroundBlock` (`NenFocus.Domain/Entities/BackgroundBlock.cs`). `FocusBlock` is now a first-class entity at `NenFocus.Domain/Entities/FocusBlock.cs` (extends `PlannedBlock`; owns `RitualCompletedAt (DateTime?)` and `MarkRitualCompleted(DateTime)`). `PlannedBlock` no longer carries `Type (BlockPresetType)`, `IsPlanned (bool)`, or `StartTime`/`EndTime` aliases — it is now a minimal base with `Id`, `BlockPresetId`, `Start`, `End`, `Title`.
- **`BlockPresetType` enum**: `{ Focus, Background }` (`NenFocus.Domain/Enums/BlockPresetType.cs`). **Corrected 2026-08-18** — this line previously claimed the values were renamed `Focus`→`Ritual`/`Passive`→`Background`; that does not match the live enum (confirmed directly, and `PolicyEngine`/`ScheduledEnforcementService` both check `BlockPresetType.Focus` throughout), so the `Ritual` rename either never actually landed or was reverted without a doc update. `DESIGN_GAP_HISTORY.md` Gap 3's closure note made the same now-incorrect claim and should be read in light of this correction.
- **`BlockedAttempt` entity refactored**: `Timestamp (DateTimeOffset)` renamed to `OccurredAt (DateTime)`. Public constructor removed; replaced with static factory `BlockedAttempt.Record(Guid id, Guid sessionId, string resourceName, BlockedAttemptType attemptType, DateTime occurredAt)`. The caller supplies the UTC timestamp — the entity never reads the clock. DB index changed from `IX_BlockedAttempts_Timestamp` to `IX_BlockedAttempts_OccurredAt`.
- **CQRS interface files split**: `IHandlers.cs` deleted. Each interface now lives in its own file in `NenFocus.Application/Interfaces/`: `ICommand<TResult>` (`ICommand.cs`), `ICommandHandler<TCommand,TResult>` (`ICommandHandler.cs`), `IQuery<TResult>` (`IQuery.cs`), `IQueryHandler<TQuery,TResult>` (`IQueryHandler.cs`). The `ICommandHandler<TCommand>` shorthand (returning `bool`) is removed — all commands declare an explicit result type.
- **`DeleteBlockPresetResult` added**: `DeleteBlockPresetCommand` now returns `DeleteBlockPresetResult` (not `bool`). Factory methods: `DeleteBlockPresetResult.Ok()` and `DeleteBlockPresetResult.Fail(string)`. This establishes the pattern going forward: no command returns a bare primitive.
- **Standalone `Validators/` directory removed**: the six validator classes in `NenFocus.Application/Validators/` (e.g. `CreateBlockPresetCommandValidator`, `CreatePlannedBlockCommandValidator`) have been deleted. Per-use-case validators that already lived inside feature folders (`*Validator.cs`) remain. New validators must be co-located inside the use-case folder — not in a top-level `Validators/` directory.
- **DI registration refactored** (`Application/DependencyInjection.cs`): Scrutor dependency removed from the registration code (hand-rolled reflection scan used instead — iterates assembly types, matches `ICommandHandler<,>` and `IQueryHandler<,>` by generic type definition, registers with `AddTransient`; types whose name ends in `EventHandler` are registered with `AddTransient` as self). **The `Scrutor` package reference itself is still sitting in `NenFocus.Application.csproj`, unused** — flagged 2026-08-18, worth deleting next time that file is touched. `IPolicyEngine` is registered as `services.AddScoped<IPolicyEngine, PolicyEngine>()` — arguably should be `AddSingleton` since it's stateless, a minor known inefficiency, not a correctness issue.
- **`TimeSpanToStringConverter` hardened**: now wraps `ts.ToString(format)` in a try/catch; if a `FormatException` is thrown because the format string contains an unescaped `:`, the converter auto-escapes the colon and retries before falling back to `hh\:mm\:ss`. No converter call-site changes required.
- **`PlannedBlockDto.BlockPresetType`** property changed from `string` to `BlockPresetType` domain enum. `PlannerPageViewModel.ParseBlockPresetType(string)` helper deleted — the DTO now carries the typed value directly.
- **`SessionDto.Status`** changed from `string` to `SessionStatus` enum. `MainPageViewModel` updated: `session.Status == "OnBreak"` → `session.Status == SessionStatus.OnBreak`.
- **`SessionDto`** gains `Origin (SessionOrigin)` and `OriginatingBlockId (Guid?)`; `PresetId (string)` removed (preset is identified by name only in the DTO).
- (policies superseded by Gap 65 — a contract now stores its days) **Nen Contract — Domain layer, per-day floor redesign.** `DailyFloorPolicy`/`WorkHoursPolicy` — see `DESIGN_GAP_HISTORY.md` Gap 6.
- **Nen Contract — Application/Infrastructure/UI, first vertical slice (2026-07-13).** Seal/Extend/Break/lifecycle sweep — see `DESIGN_GAP_HISTORY.md` Gap 7.
- **Nen Contract — supporting queries** (deficits, completed hours, planned-hours preview, batch overlap) and Contract Preset CRUD — see `DOMAIN_DICTIONARY_NEN_CONTRACT.md`.
- **Contract Presets get a Presets-page authoring UI** (2026-08-21) — see `DESIGN_GAP_HISTORY.md` Gap 27.
- **Contract Wizard UI, drafting mode, full-screen overlay** (2026-07-08 – 2026-08-11) — see `DOMAIN_DICTIONARY_NEN_CONTRACT.md`.
- **Sealed-contract enforcement guards on planned blocks** (Strictness Rule gates Delete/Update) (2026-08-11) — see `DOMAIN_DICTIONARY_NEN_CONTRACT.md`.
- **Active-contract banner, week-scoped, then widened to multi-contract** (2026-08-11, rebuilt 2026-09-06) — see `DESIGN_GAP_HISTORY.md` Gap 55.
- **Contract cancellation reachable from the UI** (2026-08-11) — Cancel button on the popover's Active card.
- **Drafting-mode placement constraints, contract date-range overlap validation** (2026-08-11) — see `DOMAIN_DICTIONARY_NEN_CONTRACT.md`.
- **Nen Contract activate/complete notifications** (2026-08-12) — closed the notification half of Open Gap G (`ARCHITECT_GOVERNANCE.md`).
- **`EndedEarly` Focus Block outcome & deficit-ledger fix** (2026-08-12) — see `DESIGN_GAP_HISTORY.md` Gap 22.
- **Nen Contract management popover** (list/edit/delete Upcoming, Active card) (2026-08-12) — see `DOMAIN_DICTIONARY_NEN_CONTRACT.md`.
- **Drafting-mode Work Hours enforcement, plus 3 bugs found testing it** (`WorkHours.ContainsRange` midnight bug, a redundant tile rebuild, a swallowed `FormError`) (2026-08-18) — see `DOMAIN_DICTIONARY_NEN_CONTRACT.md`'s Work Hours entry.
- **Per-day floor badges under the Planner's day headers, drafting and sealed alike** (2026-08-18) — see `DOMAIN_DICTIONARY_NEN_CONTRACT.md`.
- **`AllowCancellation` not enforced anywhere, interim** (2026-08-18) — see `DOMAIN_DICTIONARY_NEN_CONTRACT.md`'s Nen Contract section for the full rationale.
- **Day-header floor badges widened to 7 states + live 3s polling** (2026-08-18) — `DayTiming { Past, Today, Future }` enum, `#E8A93A` in-progress amber.
- **Fixed: Ritual overlay not appearing for an already-active Focus Block at app startup** (2026-08-18) — startup race between `ScheduledEnforcementService`'s first heartbeat tick and `ShellViewModel` construction; fixed with a constructor-time catch-up check.
- **"You're late" modal on the Ritual shell for an overdue Focus Block** (2026-08-18) — reuses `FocusBlockOutcomeClassifier.GracePeriodMinutes`; lives on `RitualShellViewModel`, not a step page.
- **Fixed: Ritual overlay briefly flashing on startup with an active Session already running** (2026-08-18) — a second startup race (heartbeat vs. `SessionStateService.InitializeAsync()`); fixed by awaiting rehydration before `AppHost.StartAsync()`.
- **Fixed: post-session "Planned" duration could drift from the block's actual window** (2026-08-18, corrected same day) — settled on `Planned = block's full nominal length`, `Duration` credited via grace period, `Planned ≥ Duration` always holds by construction.
- **Block Splitting for sealed Daily-Flexibility contracts** (2026-08-19) — see `DESIGN_GAP_HISTORY.md` Gap 17.
- **Daily Flexibility floor-protection + Period Time Budget's `ContractFloor`** (2026-08-19) — see `DESIGN_GAP_HISTORY.md` Gap 18.
- **Daily floor's blank-vs-zero ambiguity closed** (2026-08-19) — see `DESIGN_GAP_HISTORY.md` Gap 19.
- **Fixed: four `PlannedBlockRepository` read methods never excluded cancelled blocks** (2026-08-19) — `GetForRangeAsync`, `GetUnresolvedFocusBlocksAsync`, `HasFutureBlocksForPresetAsync`, `HasActiveFocusBlockAsync` all gained the same `CancelledAt == null` filter `GetOverlappingAsync` already had.
- **Debug clock overlay for manual testing** (2026-08-19) — dev tooling, `Ctrl+Shift+T`; `ISystemClock`/`IDebugClockController` offset added to real `UtcNow`, resets on restart, no persisted state.
- **Preset name uniqueness enforced at the DB level** (2026-08-20) — unique index on `ContractPresets.Name`, composite `(Name, PresetType)` on `BlockPresets`; `TransactionalCommandHandler` translates the constraint violation to a clean `Fail(...)`.
- **Fixed a long-standing 3-test failure baseline** (2026-08-20) — a stale test fixture, not a real em-dash/Unicode bug; all tests green afterward.
- **Focus Block Outcome credit cap, Planner tile display, badge scheme** (2026-08-20) — see `DESIGN_GAP_HISTORY.md` Gap 20.
- **Missed-block detection now survives a large unwatched time gap** (2026-08-20) — see `DESIGN_GAP_HISTORY.md` Gap 21.
- **Live Post-Session popup + Planner refresh for background-swept Missed blocks** (2026-08-20) — see `DESIGN_GAP_HISTORY.md` Gap 21's Revisited note.
- **Three same-day follow-up fixes to the sweep above** — a TOCTOU race, a boundary-width mismatch, a missing pre-Ritual guard (2026-08-20) — see `DESIGN_GAP_HISTORY.md` Gap 21's second/third/fourth addenda.
- **Missed and Completed-but-undismissed Post-Session screens unified, auto-surface at startup** (2026-08-18) — `Session.PostSessionAcknowledgedAt`, `GetUnacknowledgedPostSessionsQuery`.
- **Redemption mechanism gets a real UI, plus 3 bugs found testing it** (2026-08-21) — see `DESIGN_GAP_HISTORY.md` Gap 25.
- **Redemption toggle replaced with automatic attribution the same day** (2026-08-21) — see `DESIGN_GAP_HISTORY.md` Gap 26.
- **Contract Presets get a Presets-page authoring UI, wizard's Advanced mode narrowed to picking one** (2026-08-21) — see `DESIGN_GAP_HISTORY.md` Gap 27.
- **Planner "done block" snapshot, read-only view replacing the greyed-out edit form** (2026-08-21) — see `DESIGN_GAP_HISTORY.md` Gap 28.
- **Redemption block could generate a fresh deficit of its own — Rule 13 finally enforced** (2026-08-21) — see `DESIGN_GAP_HISTORY.md` Gap 29.
- **`BlockPreset` lock only protected the Blocklist direction** (2026-08-21) — see `DESIGN_GAP_HISTORY.md` Gap 30.
- **A Planned Session could hang forever at `00:00:00`** (2026-08-21) — see `DESIGN_GAP_HISTORY.md` Gap 31.
- **"Application Handler Forbidden Zone" rule written, then used to audit and fix 3 more instances of the same bug shape** (2026-08-21) — see `DESIGN_GAP_HISTORY.md` Gap 32.
- **Planner's six time-picker fields rebuilt from `AutoCompleteBox` to a hand-owned control** (2026-09-02) — see `DESIGN_GAP_HISTORY.md` Gap 47.
- **Split dialog: duration labels, live overlap + Work Hours validation** (2026-09-02) — see `DESIGN_GAP_HISTORY.md` Gap 48.
- **Redemption Placement Policy — Cover Deficit stopped requiring same-day-as-missed** (2026-09-04) — see `DESIGN_GAP_HISTORY.md` Gap 49.
- **Debug-only seed fixtures for the redemption placement policy** (2026-09-04) — `HostExtensions.EnsureDatabase()`, `#if DEBUG`-gated, five test contracts.
- **`NenContract.Extend` removed entirely** (2026-09-04) — deliberate scope decision, not a bug fix; see `DOMAIN_DICTIONARY_NEN_CONTRACT.md`'s "No mid-contract extension" note.
- **Daily-floor badges made live instead of a Seal-time snapshot; genuine Bonus split** (2026-09-04) — see `DESIGN_GAP_HISTORY.md` Gap 52.
- **Three bugs surfaced testing Gap 52, fixed same day** — see `DESIGN_GAP_HISTORY.md` Gap 53.
- **Defense-in-depth preset-existence checks at the Application layer** (2026-09-04) — see `DESIGN_GAP_HISTORY.md` Gap 54.
- **`CreditedWindow` now accrues progressively for a still-live block** (2026-09-04) — distinguishes a live query (`sessionEndedAt == null`) from a resolved one.
- **`CompleteEarly` renamed to `CompleteNenContract` throughout** (2026-09-04) — completing via the Planner button is the normal path, not an edge case.
- **Open Gap L fully closed** (2026-09-08) — see `DESIGN_GAP_HISTORY.md` Gap 59.
- **Multi-contract weeks fixed; Planner banner gains a contract switcher** (2026-09-06) — see `DESIGN_GAP_HISTORY.md` Gap 55.
- **Enforcement Synchronizer; breaks and session end no longer drop all blocking; Ritual question moved out of the Policy Slice** (2026-09-25) — see `DESIGN_GAP_HISTORY.md` Gap 62.
- **Time-driven breaks: a session owns its `BreakSchedule`; `OnBreak`, `StartBreak`/`ResumeFromBreak`, the break events and `IBreakScheduleCalculator` removed; session end capped at its planned end** (2026-09-27) — see `DESIGN_GAP_HISTORY.md` Gap 63. Gotcha: `Session`'s breaks are an EF `OwnsMany` on the private `_breaks` field (table `SessionBreaks`); `Session.BreakSchedule`/`BreaksTaken` are computed and explicitly `Ignore`d.
- **Contract rule "Keep blocking during breaks"** (renamed from `PauseEnforcementDuringBreak`, inverted; set on Contract Presets, copied Seal → Session at Start; `PolicyEngine` no longer takes contracts) (2026-09-27) — see `DESIGN_GAP_HISTORY.md` Gap 64. Gotcha: a `bool` with `HasDefaultValue(false)` must have a DB default of `false` too — EF omits a CLR-default value from INSERTs and lets the column default apply, so renaming the old `DEFAULT 1` column would have silently stored `true`.
- **Contract days (Gap 65):** a contract is its days (`ContractDay`/`ContractCalendar`, `DailyFloorRule`); activation/completion at exact local moments; `WorkHoursPolicy`/`DailyFloorPolicy` removed — see `DESIGN_GAP_HISTORY.md` Gap 65.
- **Notifications through one door (Gap 84):** `INotifier` owns all wording; blocks announce starting soon / started / ended; the never-firing "Block Starting Soon" warning fixed; `INotificationService` reduced to `ShowAsync` — see `DESIGN_GAP_HISTORY.md` Gap 84.
- **Cancellation folded into `PlannedBlock.EffectiveEnd` (Gap 85):** a cancelled block's own `IsActive` says no; the cancelled filters in `PolicyEngine`, `ComputeActivePolicySliceQueryHandler`, `IsAwaitingRitual` and the markers removed — see `DESIGN_GAP_HISTORY.md` Gap 85.
- **`Classify(FocusBlock, asOf)` the only way to classify (Gap 86):** the block's own stamps are the one source; the five-argument form is private; the recap no longer loads sessions — see `DESIGN_GAP_HISTORY.md` Gap 86.
- **Cover Deficit suggestions decide in local time (Gap 87):** `GetSuggestedRedemptionPlacementsQueryHandler` no longer mixed UTC "now" with local Work Hours; `ISystemClock.ToUtc` added — see `DESIGN_GAP_HISTORY.md` Gap 87.
- **Recap Bonus block count is Focus-only (Gap 94):** matches the set Bonus hours come from — see `DESIGN_GAP_HISTORY.md` Gap 94.
- **Spontaneous sessions matched on local dates; Background tiles never "Bonus" (Gap 96)** — see `DESIGN_GAP_HISTORY.md` Gap 96.
- **Shutdown asks the synchronizer (Gap 101):** closing leaves blocking as `PolicyEngine` says for that moment, instead of lifting a running Background block — see `DESIGN_GAP_HISTORY.md` Gap 101. Gotcha: Avalonia doesn't await an `async` `ShutdownRequested` handler, so the process can exit mid-way; hold the first request with `e.Cancel = true`, finish, then call `desktop.Shutdown()` behind a flag so the second request passes.
- **Architecture tests (Gap 98):** `NenFocus.Tests/ArchitectureTests/` turns `MODULE_STANDARDS.md` rules into tests that fail on drift, with shrink-only exceptions lists — see `DESIGN_GAP_HISTORY.md` Gap 98. Gotcha: a source-scan rule reads `MODULE_STANDARDS.md` and the `.cs` files from the repo root (found by walking up to `NenFocus.slnx`), so the tests need the source tree, not just the build output. Gotcha: `ISessionRepository.GetHistoryAsync(DateOnly, DateOnly)` reads its dates as **UTC** days — a caller holding local dates must fetch a day either side and filter by `ISystemClock.ToLocalDate` (Gap 97).
- **Avalonia 11.3.22 (Gap 102):** all Avalonia packages and `Avalonia.Headless.XUnit` moved together; dependency scan clean with no pin — see `DESIGN_GAP_HISTORY.md` Gap 102. Gotcha: since 11.1–11.3 a TextBox with a selection handles Down itself, so a plain `KeyDown +=` handler never sees it — key handling that must win over the TextBox listens with `AddHandler(KeyDownEvent, …, RoutingStrategies.Tunnel)` (the Planner's time fields do).
- **Planner End list built on every form open (Gap 104):** a first block at 09:00 no longer opens an empty End dropdown — see `DESIGN_GAP_HISTORY.md` Gap 104.
- **One source for time (Gap 88, with Gaps 90 and 99):** `Moment`, `UserTimeZone`, `TimeSlot` held by blocks and sessions, started blocks locked, DST-correct days, and `BlockPlacementChecker` as the one placement answer — see `DESIGN_GAP_HISTORY.md` Gap 88. Gotcha: `Moment` is stored through an EF value converter to `DateTime`, so SQL range comparisons and `ORDER BY` still run in SQLite, with no migration (`DateTimeOffset` would have pushed them into memory).

### Not Yet Built

| Area | Notes |
|------|-------|
| Insights page | Not built. `InsightsPageView.axaml` is a static "Coming soon" placeholder backed by a generic stub in `StubPageViewModels.cs` — no query, no handler, no DTO exist yet. Also the intended home for Gap C1 (weighted Day-Met percentage day-strip) — `NenContract.PercentOfFloorMet` is not yet built, only designed. |
| Universal usage tracking + Usage Ceiling | See `OPEN_GAPS.md` → Open Gap A. Domain shape settled; not yet built. |
| Planned Period / Strict Mode | Superseded by Nen Contract — kept only as a historical cross-reference; do not build a second, parallel feature for this. |
| Recurring Blocks | See `OPEN_GAPS.md` → Open Gap E. Domain shape settled; not yet built. |
| Settings page | Stub only — theme, strict mode toggle, notification filter, sounds, daily adjustment window constraints. |
| Checklist / Quest persistence | UI present on Main page. No storage wired up. |
| Daily Ritual Summary | Morning popup with today's scheduled blocks and streak. Not built. |
| End-of-week recap | Not built. |
| Export/import JSON for presets | Not built. |
| Allowlist full enforcement | Browser extension handles HTTP/S traffic. Hosts file alone cannot enforce default-deny. |

---

## Key Behaviours & Rules

### Session Rules

- Only one Focus session at a time (`HasActiveSessionAsync` check on start).
- Once a session starts, the preset is **locked** — it cannot be edited mid-session.
- Session-only adjustments (Ren page) are not persisted to the preset; they apply only for the duration.
- Users can **extend** the timer (make longer). Ending early is also currently possible — `MainPageViewModel.EndSession()` has no guard beyond `EndSessionCommandHandler`'s own `Status == Active || OnBreak` check, so any active or on-break session can be ended at any point (this doc previously claimed the opposite; corrected 2026-08-12, drift found and fixed against `DOMAIN_DICTIONARY.md`'s already-accurate note). Ending early stamps the originating `FocusBlock.SessionEndedAt` (closed 2026-08-12, `DESIGN_GAP_HISTORY.md` Gap 22) rather than mutating `End` — the original schedule is preserved for deficit/credited-hours accuracy, and the Planner tile is shortened only for display (`GetPlannedBlocksForDayQueryHandler`). A future Strict Mode Settings toggle (already tracked under Not Yet Built) will prevent ending early when enabled — see `DOMAIN_DICTIONARY.md`'s Session section.
- Background Blocks may overlap with anything; Ritual + Ritual overlap is rejected at creation time.

### Session Adjustment Rules (Ren page)

| Session type | Apps | Website (Blocklist preset) | Website (Allowlist preset) |
|-------------|------|---------------------------|-----------------------------|
| Spontaneous | Add or temporarily remove | Add or remove allowed | Add only; no removals |
| Planned | Add only | Add only | Apps: Add only; Websites: No changes allowed |

### Policy Computation (`ComputeActivePolicySliceQuery` + `PolicyEngine`)

Full merge rules and current behavior — see `DOMAIN_DICTIONARY.md`'s "Policy Slice" and "PolicyEngine" sections (kept there as the single source, not duplicated here).

### Enforcement Implementation Details

- **App blocking**: `WindowsEnforcementService` kill loop, `p.Kill(entireProcessTree: true)`, driven by `ScheduledEnforcementService` (5s heartbeat). Process handles are disposed in a `finally` block. No browser force-kill — browser tab blocking is handled exclusively by the `NenFocusExtension` via the `/check` redirect.
- **Ritual trigger**: each heartbeat, after `SyncAsync(now)`, `ScheduledEnforcementService` asks `GetFocusBlockAwaitingRitualQuery(now)`. If it returns a block id, it calls `SetRitualRequired(true, id)`; otherwise `SetRitualRequired(false)`. The rule lives on `FocusBlock.IsAwaitingRitual` plus the query's "no session running" check, so the heartbeat holds no rule of its own (Gap 62).
- **Website blocking — hosts file**: writes `0.0.0.0 <domain>` and `:: <domain>` entries in `C:\Windows\System32\drivers\etc\hosts` between `# NEN_FOCUS_START` / `# NEN_FOCUS_END` markers (case-sensitive, underscores). Calls `ipconfig /flushdns` after each write. Domains are normalised (strips `https://`, `www.`, paths/ports) before insertion.
- **Website blocking — browser extension**: `NenFocusExtension` intercepts navigations (`background.js`), asks `WindowsEnforcementService` at `http://localhost:5005/check`, and redirects blocked tabs to `blocked.html`. Reports attempts via `/report`. An already-open tab is also caught, independent of navigation — see the Done list above (Gap 46).
- **DoH bypass prevention**: registry keys `BuiltInDnsClientEnabled = 0` are written for Chrome, Edge, and Brave to ensure browsers cannot route around the hosts file via DNS-over-HTTPS.
- **Allowlist mode**: `ActivePolicySlice.WebsiteFilterTypeMode == Allowlist` → extension enforces default-deny; intersection of all active allowlists is the permitted set. Hosts file alone cannot enforce this.
- **Local HTTP API** (`http://localhost:5005`): hosted by `WindowsEnforcementService`. `/check` returns `{ "blocked": true/false }`. `/report` increments the blocked-attempt counter for the current domain. The API starts when enforcement is applied and shuts down when it is removed.

---

## Development Notes

### Platform & Admin

- **Platform**: Windows only (`net9.0-windows`). Enforcement and notification services use Windows APIs. Do not attempt to make these cross-platform without explicit instruction.
- **Admin required**: The app manifest sets `level="requireAdministrator"`. This is mandatory for hosts file writes, registry key edits, and killing high-privilege process trees.

### Database

- **SQLite via EF Core**. Add migrations: `dotnet ef migrations add <Name> --project NenFocus.Infrastructure --startup-project NenFocus`
- `HostExtensions.EnsureDatabase()` calls `context.Database.Migrate()` on startup — no bypass of migration history.
- SQLite cannot do `ORDER BY` or `>` comparisons on `DateTimeOffset` in SQL — fetch to memory first, then filter/sort in C#. **Possibly obsolete (flagged 2026-08-18):** no `DateTimeOffset` field exists anywhere in `NenFocus.Domain/Entities` today — every timestamp is a plain `DateTime` (see `ARCHITECT_GOVERNANCE.md` → Open Gap E, which independently confirmed the same for `PlannedBlock`). Kept here rather than deleted since several "fetch to memory, sort in C#" repository methods described elsewhere in this doc may still exist for other reasons (e.g. general SQLite querying limitations) even if this specific `DateTimeOffset` trigger no longer applies — worth confirming and pruning next time a repository in this area is touched.
- `BlockPreset.Tags` is stored as a serialised string in the DB. `GetByTagsAsync` filters in-memory. Known workaround.

### Known Workarounds

- **FocusBlock update**: There is no `FocusBlock.Update()` method — `UpdatePlannedBlockCommand` uses delete + recreate.
- **Hosts file markers**: must be exactly `# NEN_FOCUS_START` and `# NEN_FOCUS_END` (case-sensitive, underscores). Any deviation causes duplicate entries on next write.
- (removed in Gap 84 — the unused `ScheduleBreakReminderAsync` family was deleted) **Break reminders**: breaks are announced by `BreakTransitionResponder` from the session's `BreakSchedule` (Gap 63).
- **No index on `PlannedBlocks.Start`/`End`**: confirmed 2026-07-15 while sizing the Recurring Blocks materialization horizon (see `ARCHITECT_GOVERNANCE.md` → Open Gap E) — every range query (`GetForDayAsync`, `GetForRangeAsync`, `GetOverlappingAsync`, etc.) is currently a full table scan. Not a problem at current or foreseeable single-user scale; worth adding an index on `(Start, End)` before row counts climb into the tens of thousands, independent of any specific feature.
- **Two time frames — never compare across them.** Stored times (block and session slots, every timestamp, `AsOf` arguments) are `Moment`s, always UTC; contract days, Work Hours and "today" are the user's local calendar (Gap 65). Since Gap 88 the types keep them apart: a `Moment` has no calendar members, so a day or hour goes through `ISystemClock.TimeZone` (`UserTimeZone.ToCalendarTime` in, `ToMoment` back), never `ToLocalTime()`/`ToUniversalTime()` (untestable). Gap 87 was a handler comparing UTC "now" with a local Work Hours close.

### DI & Registration

- **DI scope**: `InProcessDomainEventPublisher` creates a new DI scope per event handler to avoid `DbContext` concurrency issues.
- **Validator registration**: `services.AddValidatorsFromAssembly(assembly)` in `DependencyInjection.cs` auto-registers all `AbstractValidator<T>` classes in the Application assembly. Validators must be co-located inside their use-case folder (e.g. `StartSession/StartSessionValidator.cs`). The top-level `Validators/` directory has been deleted — do not recreate it.
- **Handler registration** (`Application/DependencyInjection.cs`): Scrutor has been removed. Handlers are discovered via a hand-rolled reflection scan over the Application assembly. All `ICommandHandler<,>` and `IQueryHandler<,>` implementations are registered with `AddTransient` (not `AddScoped`). Event handler types are registered with `AddTransient` as self (concrete type). This replaces the previous Scrutor `.AsImplementedInterfaces().WithScopedLifetime()` pattern.
- **Singleton + scoped pattern**: `ActiveSessionMonitor` (singleton) calls scoped command handlers by creating short-lived scopes via `IServiceScopeFactory`. This is the correct pattern; do not inject scoped services directly into singletons.
- **ViewModels never inject `IQueryHandler<,>`/`ICommandHandler<,>` directly (2026-08-30, `DESIGN_GAP_HISTORY.md` Gap 40).** `App.axaml.cs` resolves `ShellViewModel` from the DI root with no scope (`Program.AppHost!.Services.GetRequiredService<ShellViewModel>()`), and `Host.CreateDefaultBuilder` only enables `ValidateScopes` in the `"Development"` environment — which nothing in this app ever sets. A directly-injected handler on a long-lived ViewModel therefore shares one `DbContext` for the app's entire runtime, exactly the captive-dependency hazard the "Singleton + scoped pattern" bullet above already warns against, just one layer further out. Every ViewModel instead takes `IQueryDispatcher`/`ICommandDispatcher` (`Application/Interfaces/`, implemented in `Infrastructure/Services/` as `ScopedQueryDispatcher`/`ScopedCommandDispatcher`, registered `AddSingleton`) — each `QueryAsync`/`DispatchAsync` call creates its own fresh `IServiceScopeFactory.CreateScope()` internally, resolves the closed `IQueryHandler<,>`/`ICommandHandler<,>` type via the same `MakeGenericType`/reflection approach `RegisterDecoratedCommandHandlers` already uses, and disposes the scope after. `CompositionRootTests.AddInfrastructure_ShellViewModel_ResolvesFromRootProviderLikeAppAxamlDoes` is the permanent regression guard — it resolves `ShellViewModel` exactly the way `App.axaml.cs` does, under `ValidateScopes: true`, and fails immediately if a directly-injected handler is ever reintroduced anywhere in the ViewModel tree.
- **`NenFocusDbContext` registration: factory first, Scoped derived from it — never `AddDbContextFactory` + `AddDbContext` independently (2026-08-30, Gap 40).** Registering both separately for the same context type silently breaks resolving `NenFocusDbContext` from any real DI scope under `ValidateScopes: true` (an EF Core internal quirk in how the factory's options-building captures the root provider) — `CompositionRootTests` didn't catch this originally because it only builds the container, never resolves from it. Correct pattern: `services.AddDbContextFactory<NenFocusDbContext>(...)` once, then `services.AddScoped<NenFocusDbContext>(sp => sp.GetRequiredService<IDbContextFactory<NenFocusDbContext>>().CreateDbContext())` — verified to preserve true Scoped semantics (same instance within one scope, different instances across scopes) and leaves the factory itself unchanged for its existing singleton consumers (`ScheduledEnforcementService`, `SessionStateService`, `ActiveSessionMonitor`, `NenContractHeartbeatService`).
- **App startup sequence** (`Program.cs`): build host → ensure DB → `SessionStateService.InitializeAsync()` (rehydrate session from DB) → resolve `ShellViewModel` → show window.
- **NuGet alignment**: all `Microsoft.CodeAnalysis` packages pinned to `5.3.0` to avoid NU1608 version conflicts.
