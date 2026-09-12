# NenFocus — Domain Dictionary

This file is the canonical reference for every domain concept in NenFocus. It exists to eliminate ambiguity between conversations. When a term is used in code, comments, or a design discussion, its meaning here is authoritative.

---

## Quick Reference

| Term | One-line definition |
|------|---------------------|
| **Session** | A bounded focus period; lifecycle `NotStarted → Active → (OnBreak ↔ Active) → Completed` |
| **Ritual** | The four-step wizard (Ten → Zetsu → Ren → Hatsu) that creates a Session |
| **Block Preset** | A reusable template of blocked apps + website filter type; typed as Focus or Background at creation |
| **Planned Block** | Abstract base for `FocusBlock` and `BackgroundBlock`; carries `Start`, `End`, `BlockPresetId`, `RecurringSeriesId (Guid?)`, `IsActive()`, `OverlapsWith()` |
| **Focus Block** | A planned calendar entry that activates enforcement AND triggers the Nen Ritual at the designated time |
| **Background Block** | A planned calendar entry that activates enforcement at the designated time without triggering the Ritual |
| **Policy Slice** | The merged, computed enforcement state at a given instant; represented as `ActivePolicySlice` (Domain Value Object), produced by `PolicyEngine` |
| **PolicyEngine** | Stateless Domain service (`NenFocus.Domain/Services/`) that computes `ActivePolicySlice` from raw aggregates; sole owner of all merge rules |
| **Spontaneous Session** | A Session created by completing the Ritual manually from the Main Page; no Block entity exists; `Origin = Spontaneous` |
| **Planned Session** | A Session created via the Ritual triggered by a scheduled Focus Block; `Origin = Planned` |
| **SessionOrigin** | Enum (`Spontaneous` \| `Planned`) that tags every Session with how it was initiated |
| **BreakTimerPreset** | A saved, reusable work/break cadence (Classic, Deep Work, Flow ship as ordinary seeded rows, freely editable/deletable) — its own aggregate root, authored on the Settings page (not yet built), snapshotted by value into a Session at Hatsu; no locking of any kind |
| **BreakTimer** | Value object holding the frozen cadence a Session runs under — `WorkMinutes`, `ShortBreakMinutes`, `CyclesBeforeLongBreak`, `LongBreakMinutes` — copied from a `BreakTimerPreset` at Start; replaces the retired `BreakSettings` |
| **SessionPhase / SessionPhaseKind** | One entry (`Work` \| `ShortBreak` \| `LongBreak`) in the ordered phase timeline `IBreakScheduleCalculator` computes for a session's whole run, given its `BreakTimer` and total duration — the single source every consumer (automatic work/break transitions, Hatsu's fit-filtering, the Main Page progress bar's break markers) reads, rather than each re-deriving cycle state independently |
| **Blocked Attempt** | An append-only fact recording a user's attempt to access a blocked app or site during a Session |
| **Nen Contract** | A sealed, time-bound commitment over a contiguous date range that governs allowed changes to the Focus/Background blocks within it; own aggregate root, does not own `PlannedBlock`s |
| **Seal** | The act of finalising a Nen Contract; the only way a `NenContract` comes into existence — there is no persisted draft state in the domain |
| **Strictness Rule** | Enum (`Locked` \| `DailyFlexibility` \| `PeriodTimeBudget`) — one axis of post-seal mutability; interpretation of the rule against an edit's context is an Application-layer concern |
| **Daily Floor** | The minimum completed Focus hours required per day within a Nen Contract; Focus-only, hard-checked against planned hours at Seal, soft-tracked (scored, not blocked) thereafter |
| **Change Window** | An optional daily time-of-day window during which edits to *existing* blocks are permitted, with a configured per-day use limit; the use-count itself is tracked by the Application layer |
| **Day Met** | A scoring outcome: a Nen Contract day is "met" if that day's completed Focus hours ≥ the Daily Floor — full stop. A missed or late Focus block does not fail the day directly; it only costs hours that must be made up |
| **FocusBlockOutcome** | Enum (`CompletedOnTime` \| `StartedLate` \| `Missed` \| `EndedEarly`) classifying how a single Focus block resolved; a per-block display concept, separate from the day verdict |
| **Broken** | A Nen Contract the user cancelled before its end date; only reachable if `AllowCancellation` was set true at Seal |
| **Work Hours** | A daily time-of-day window defining where Focus/Background blocks may physically exist on the calendar; blocks cannot be placed, moved, or resized outside it. Distinct from Change Window (which gates *when edits are allowed*, not *where blocks can be*) |
| **Contract Preset** | A saved, reusable template of Nen Contract rules (strictness, daily floor, work hours, allow-cancellation) — a full Domain `AggregateRoot`, not just a saved configuration row. Authored and managed on the Presets page's "Contract Presets" tab; the Contract Wizard only picks one (applied as-is, no editing mid-wizard) — see the App Structure table below |
| **Simple Contract** | A quick-seal path in the Planner wizard: pick a named preset (Lockdown, Daily Flex, Period Study), schedule blocks, seal. No tuning knobs — the floor is derived from planned hours per day |
| **Advanced Contract** | The full-configuration path: every lever exposed (strictness, work hours, custom daily floor, rest days, allow-cancellation). User can save the configuration as a Contract Preset for reuse |
| **Redemption Block** | A Focus Block explicitly linked to one or more missed/late Focus Blocks of the same preset, covering all or part of their deficit; carries `RedemptionEntries` |
| **RedemptionEntry** | A value object tying a redemption block to one missed block: `{ MissedBlockId, MinutesCovered }` |
| **Deficit** | The gap between a missed or late Focus Block's scheduled duration and actual completed minutes; tracked per missed block, not per preset |
| **Enforcement** | OS-level mechanisms (process kill, hosts file, registry, extension) that apply a Policy Slice |
| **Post-Session** | The mandatory screen shown after a Session ends; displays stats, blocked attempts, and an optional reflection |
| **Post-Contract** | The one-time recap screen shown after a Nen Contract finishes (`Completed` or `Broken`); mirrors Post-Session — date range, committed-vs-completed hours, Bonus hours, completion grade, per-block outcome breakdown, day-strip, optional reflection. Built 2026-09-08, closing Open Gap L — see `DESIGN_GAP_HISTORY.md` Gap 59 |
| **Deep Module** | A module with significant functionality hidden behind a narrow, obvious API |
| **Design Gap** | The distance between visible code and what is needed to implement a feature correctly |
| **DailyUsage** | Append-only fact recording actual seconds spent on any site or app, per calendar day; the universal usage-tracking substrate, independent of ceilings, presets, or contracts |
| **UsageResourceType** | Enum (`App` \| `Website`) tagging a `DailyUsage` or `UsageCeiling` entry |
| **UsageCeiling** | An ambient, perpetual cap on daily or weekly access time for a specific site or app; created on the Presets page; deliberately independent of `BlockPreset` and `NenContract` |
| **CeilingPeriod** | Enum (`Daily` \| `Weekly`) — the aggregation window a `UsageCeiling`'s cap is measured against |
| **IUsageTracker** | Application-layer interface through which infrastructure records usage events; analogous in pattern to `IBlockedAttemptTracker` |
| **RecurrenceRule** | Value object describing a repeat schedule: interval, unit (Day/Week/Month), weekday mask, end condition — the single shape behind "Every day," "Specific weekdays," and "Custom" recurrence pickers |
| **RecurringSeries** | Entity owning a `RecurrenceRule` + block template (preset, time-of-day); generates and keeps topped-up the materialized `PlannedBlock` rows that belong to it |
| **SkippedOccurrence** | Append-only fact recording a recurring instance that couldn't be generated due to a conflict with another block; exists only while unresolved |

---

## App Structure

NenFocus has five pages accessible via the sidebar:

| Page | Purpose |
|------|---------|
| **Main Page** | Home screen; shows "Use Nen" button when idle, active session timer when a session is running |
| **Presets Page** | Top-level tab switcher (`PresetsPageViewModel`, built 2026-08-21) between **Block Presets** (CRUD, All/Focus/Background filter) and **Contract Presets** (CRUD — Name, Strictness, Daily Floor, Work Hours, Allow Cancellation). Contract Presets are now authored and managed exclusively here — the Contract Wizard no longer has a "Save as preset" option; it only picks from what's saved on this page |
| **Planner Page** | Calendar view for scheduling Focus Blocks, Background Blocks, and (future) Nen Contracts |
| **Insights Page** | Session history, block history, blocked attempt analytics, calendar view of past blocks, usage graphs |
| **Settings Page** | User preferences: break behaviour, strict mode, plan rules, and other enforcement configuration |

---

## Session

**Definition:** A bounded period of intentional focus, created by the user through the Ritual and enforced by the system — or, since the Missed Session amendment, a planned focus *attempt* that may have succeeded or been missed. A Session is now "a focus attempt, which may have succeeded or been missed," not only "a focus period that ran."

**Lifecycle:** `NotStarted → Active → (OnBreak ↔ Active) → Completed`, with a second terminal branch `NotStarted → Missed` reachable only by a Planned session whose originating Focus Block's window closed before the Ritual completed.

**Ubiquitous language rules:**
- A Session is *started*, not *created* or *opened*.
- A Session is *completed* or *auto-ended*, not *closed* or *finished*.
- A Session cannot be paused — only placed *on break*.
- A Session is *missed*, never *failed* or *expired* — "missed" is the only vocabulary for a Planned session whose window closed unfulfilled.

**Break behaviour:** Once a break starts, a break countdown timer replaces the session timer on the Main Page. **Breaks are now fully automatic, not user-initiated** (closed 2026-09-12, `DESIGN_GAP_HISTORY.md` Gap 61, closing the former Open Gap I) — a session picks a `BreakTimerPreset` at Hatsu, and `ActiveSessionMonitor` walks the resulting phase timeline to start and end breaks on its own; `MainPageViewModel.TakeBreak()` and the "Take a Break" button no longer exist. A session may also pick "No timer," running with no breaks at all. Whether enforcement pauses during a break is no longer a fixed "always stays active" rule — see Enforcement, below, and `PolicyEngine`'s `PauseEnforcementDuringBreak` resolution.

**Ending early:** Currently the user can end a session before the timer runs out. A future "Strict Mode" setting will prevent this.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `Session` entity (`Entities/Session.cs`) | Owns all state transitions via `Start()`, `StartBreak()`, `ResumeFromBreak()`, `Complete()`, `Miss()`; raises domain events internally; carries `Origin (SessionOrigin)` and `OriginatingBlockId (Guid?)` |
| Domain | `SessionStatus` enum | Valid states: `NotStarted`, `Active`, `OnBreak`, `Completed`, `Missed` |
| Domain | `SessionOrigin` enum | Valid values: `Spontaneous`, `Planned`; tags every Session with how it was initiated |
| Domain | `SessionGoal`, `SessionDuration`, `BreakSettings` value objects | Immutable configuration attached at creation |
| Application | `ISessionRepository` | Persistence contract; `GetActiveAsync()` is the startup rehydration entry point; `ExistsForOriginatingBlockAsync()` makes missed-session creation idempotent |
| Application | `ISessionStateService` | Singleton in-memory noticeboard; the single source of truth for the UI's view of the active session |
| Application | `IActiveSessionMonitor` | Singleton countdown engine; owns the timer, exposes `SessionTimeRemaining` / `Progress`, auto-ends via `EndSessionCommandHandler`. The auto-end/auto-resume-break calls are fire-and-forget from the timer callback; wrapped in try/catch + `ILogger` since 2026-08-21 (`DESIGN_GAP_HISTORY.md` Gap 31) — an unhandled exception previously vanished as an unobserved task exception, leaving the session stuck at `00:00:00` forever with no error surfaced anywhere. |
| Application | `StartSessionCommandHandler` | Creates and starts the session, calls `_sessionState.SetActive()`, dispatches `session.DomainEvents` via `IDomainEventPublisher`. Rejects starting against an originating block via `PlannedBlock.HasEnded(now)` (fixed 2026-08-21, `DESIGN_GAP_HISTORY.md` Gap 32) rather than an inline `now >= End`, matching the codebase's one canonical "is this past" definition. |
| Application | `EndSessionCommandHandler` | Calls `session.Complete()`, saves, dispatches `session.DomainEvents`; enforcement removal and notification are handled by `SessionEndedEventHandler`. If the session ends before its originating `FocusBlock`'s scheduled end, calls `focusBlock.MarkSessionEnded(endedAt)` — a non-destructive stamp, closed 2026-08-12 (`DESIGN_GAP_HISTORY.md` Gap 22). `block.End` is never overwritten; see `FocusBlockOutcome` below for how `SessionEndedAt` feeds classification. |
| Application | `SessionStartedEventHandler` | Computes policy slice, calls `IEnforcementService.ApplyPolicyAsync`, sends start notification |
| Application | `SessionEndedEventHandler` | Calls `IEnforcementService.RemoveEnforcementAsync`, cancels reminders, sends end notification |
| Application | `BreakStartedEventHandler` | Calls `IEnforcementService.RemoveEnforcementAsync`, sends break notification |
| UI | `MainPageViewModel` | Observer only — reads from `ISessionStateService` and `IActiveSessionMonitor`, dispatches commands. "Use Nen" button is hidden while a session is active. |

---

## Missed Session

**Definition:** A first-class Session, counted, filtered, and timelined in Insights alongside real sessions, produced when a planned Focus Block's scheduled window closes with the Ritual never completed. This reopens the Session aggregate: a Session is now "a focus attempt, which may have succeeded or been missed," not only "a period that ran." A missed session is essentially a planned session **frozen at "never started"** rather than progressing to `Completed` — reached via a direct `NotStarted → Missed` transition, never through `Active`.

**Only a Planned session can be missed.** A Spontaneous session is user-initiated with no scheduled commitment behind it — there is nothing to miss. `Session.Miss()` throws if `Origin != SessionOrigin.Planned`; this is defense-in-depth rather than a reachable case, since the creation path for a missed session (`MarkFocusBlockMissedCommand`) only ever originates from a `FocusBlockId`, never from an existing Spontaneous session.

**What it HAS**, inherited from the originating `FocusBlock`: a `BlockPresetId`, a `SessionDuration` (the block's scheduled length), and a scheduled `Start`/`End` — populated onto `StartedAt`/`EndedAt` at miss-time so it appears in date-range history queries exactly like a real session (`ISessionRepository.GetHistoryAsync` filters on `StartedAt`; a session that never populated it would be silently invisible to history).

**What it LACKS**, because it never ran: break settings, temporary blocked apps/sites (both simply omitted at creation — `Session.Create`'s existing optional parameters already default to `null`/`[]`), and any real elapsed runtime. Its `Goal` is the fixed placeholder `"(missed — no goal set)"` — a normal, valid `SessionGoal` (no user ever reached Zetsu to write one; validation is not loosened for this path).

**Zero completed hours, by construction:** `Session.GetElapsedTime` returns `TimeSpan.Zero` whenever `Status == Missed`, checked *before* the `StartedAt`-null check — because a missed session's `StartedAt`/`EndedAt` hold the *scheduled* window, not real elapsed time, and a naive `EndedAt − StartedAt` would wrongly report the full scheduled duration as "completed." Centralizing this on the entity (rather than in every caller that computes "completed hours") means the contract recap, `GetSessionHistoryQuery`, and any future Insights analytics are all correct for free. This is a single-entity fact ("a session that never ran has zero elapsed time"), distinct from the separate principle that *aggregating* hours across days belongs in the Application layer.

**Reflection still applies:** `SetReflection` (and `SaveReflectionCommandHandler`) accept `Completed` or `Missed` — the missed post-session screen captures a "why did I miss this?" reflection the same way a real post-session screen captures one. There is no rating field on either.

**Blocked attempts are always zero on a missed session — a known, accepted gap.** `BlockedAttempt` rows key off `SessionId`, and tracking only fires once a Session exists; during a Focus Block's active window before (or absent) Ritual completion, enforcement runs but there is no `SessionId` yet to attach an attempt to. Fixing this would mean tracking attempts against `FocusBlockId` during that window — separate future Infrastructure work, out of scope here.

**The originating block is untouched.** `FocusBlock` is not deleted or altered when its session is marked missed — it remains the schedule record (`RitualCompletedAt` stays `null`), while the Session is the attempt record. Both already agree: `FocusBlockOutcomeClassifier` independently derives `Missed` from the same `RitualCompletedAt == null` fact once the scheduled end has passed.

**Idempotent creation:** `MarkFocusBlockMissedCommand` can safely fire more than once for the same block (the expected failure mode for a heartbeat-triggered command, not a hypothetical) — `ISessionRepository.ExistsForOriginatingBlockAsync` guards against creating a second missed session for a block that already has one.

**Not built here:** any Insights UI. This amendment ensures a missed session is modeled correctly and is queryable/representable for when that UI is built.

**Two triggers call `MarkFocusBlockMissedCommand` — cold-start and live release.** A block's window can close unfulfilled while the app is closed (discovered on next launch) or while it's running and the Ritual is on screen for that exact block (discovered by the enforcement heartbeat). Both funnel through the same command; only the discovery path differs.

- **Cold-start sweep:** `SessionStateService.InitializeAsync()` calls `SweepMissedFocusBlocksCommand` after existing session rehydration. The handler queries `IPlannedBlockRepository.GetUnresolvedFocusBlocksAsync(asOf)` — Focus blocks with `End < asOf && RitualCompletedAt == null` — and calls `MarkFocusBlockMissedCommand` for each, ordered by `Start` ascending (oldest first). The resulting Missed sessions are simply persisted with `PostSessionAcknowledgedAt == null`; `ShellViewModel` discovers them itself at its own startup (see below) rather than the sweep announcing them via an event.
- **Live release:** `ScheduledEnforcementService`'s 5-second heartbeat calls `SessionStateService.SetRitualRequired(false)` when a Focus block's window closes without a session starting. `SetRitualRequired` now raises the widened `RitualRequirementChanged` event carrying a `RitualRequirementChange(bool Required, Guid? FocusBlockId)` — on the `false` transition, `FocusBlockId` is the *previously* pending block (captured into a local before `_pendingFocusBlockId` is cleared), since `ScheduledEnforcementService` itself has no memory of what was showing. `ShellViewModel` reacts by hard-closing the open Ritual overlay immediately (it can't wait — the user would be staring at a dead ritual), then calls `MarkFocusBlockMissedCommand` for that block.

**Unified with the Completed-but-undismissed case.** A Completed session whose Post-Session screen was never dismissed (app closed before clicking Done) is the same underlying problem as a Missed session nobody has seen yet — both are "a resolved session the user hasn't acknowledged." `Session.PostSessionAcknowledgedAt (DateTime?)` / `MarkPostSessionAcknowledged(DateTime)` is the one fact that answers "has the user seen this?", stamped only when `PostSessionViewModel.Done()` fires (via the new `AcknowledgePostSessionCommand`), regardless of whether a reflection was written — a blank reflection is a valid, deliberate outcome and must not be confused with "never saw the screen." `GetUnacknowledgedPostSessionsQuery` finds every `Completed` or `Missed` session with `PostSessionAcknowledgedAt == null`, ordered by `EndedAt` ascending, and is what `ShellViewModel` awaits at its own construction to replay anything pending — in the order it actually happened, oldest first, Ritual (if also pending) always enqueued last behind all of them.

**The Shell queues what it can't show immediately.** `ShellViewModel` holds a private `Queue<PendingOverlay>` (`PendingPostSession(PostSessionDto)` | `PendingRitual(Guid FocusBlockId)`) — one unified overlay type for both Missed and Completed-but-undismissed, since `PostSessionDto.Status` already distinguishes them. `TryDrainQueue()` is the single chokepoint — nothing new is shown unless `!IsRitualActive && !IsPostSessionActive && !IsTriageActive`. `ExitRitual`/`ExitRitualAndGoToMain`/`ExitPostSession` all funnel through it, and it lands on the Main page when the queue is empty.

**Triage gates a large backlog — Missed only, deliberately not unified.** If `TryDrainQueue()` is about to pop from the queue and finds more than 3 pending `PendingPostSession` items whose `Status == Missed` (checked fresh each call, not just once when discovered — so items enqueued later still trip the gate), it shows `MissedSessionsTriageViewModel` instead: "Skip All" clears every queued *Missed* post-session (any pending Completed-but-undismissed one is left untouched) and re-drains; "Review Each" sets a `_suppressTriage` flag (so the gate doesn't re-fire mid-review) and re-drains to cycle through them one by one via the normal Post-Session screen. The flag resets once the queue empties. Completed-but-undismissed is structurally excluded from ever needing batching: the overlay itself blocks starting a second session before the first is dismissed, so more than one can never accumulate.

**The Post-Session screen wears different copy for a miss.** `PostSessionDto.Status` (mirrored from the session) drives two computed `PostSessionViewModel` properties: `HeaderLabel` ("SESSION MISSED" vs "SESSION COMPLETE") and `ReflectionPrompt` ("What got in the way?" vs "How did the session go? What did you accomplish?").

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `Session.Miss(DateTime scheduledStart, DateTime scheduledEnd)` | `NotStarted → Missed`; throws if `Origin != Planned` or `Status != NotStarted`; sets `StartedAt`/`EndedAt` to the scheduled window; raises `SessionMissedEvent` |
| Domain | `SessionMissedEvent` (`Events/SessionMissedEvent.cs`) | Carries `SessionId`, `ScheduledStart`, `ScheduledEnd`; `occurredAt = scheduledEnd` |
| Domain | `Session.PostSessionAcknowledgedAt (DateTime?)` / `MarkPostSessionAcknowledged(DateTime)` | Stamped when the user dismisses the post-session screen, on either a `Completed` or `Missed` session; throws otherwise |
| Domain | `Session.HasEnded (bool)` | **Added 2026-08-22, `DESIGN_GAP_HISTORY.md` Gap 34.** `EndedAt.HasValue`, mirroring `PlannedBlock.HasEnded`/`NenContract.IsRestDay`'s naming idiom — a single named question so callers ask whether the session has actually finished (naturally or ended early) rather than null-checking the raw field themselves (`MODULE_STANDARDS.md`'s Application Handler Forbidden Zone). Consumed by `GetNenContractDeficitsQueryHandler` (Redemption Rule 18) to gate whether a block's outcome is trusted for the deficit ledger yet — deliberately **not** `PostSessionAcknowledgedAt`, which exists for an unrelated reason (crash-recovery replay) and would leave an already-final deficit hidden indefinitely if the user never dismisses the Post-Session screen. |
| Application | `MarkFocusBlockMissedCommand` / `CommandHandler` / `Result` / `Validator` (`UseCases/Sessions/MarkFocusBlockMissed/`) | Loads the `FocusBlock`, builds the Session via `Create(...)` + `Miss(block.Start, block.End)`, saves, and builds the `PostSessionDto` (preset name lookup; blocked-attempt dictionaries left empty since a missed session never tracked any). Validator rejects a missing/wrong-type block, an already-completed ritual, or a block that already has a session. No `SessionMissedEventHandler` is registered — `SessionMissedEvent` itself stays unhandled. **Not the same as "no live signal at all" any more** (revised 2026-08-20, `DESIGN_GAP_HISTORY.md` Gap 21's Revisited note): the periodic sweep's caller (`ScheduledEnforcementService`) now pushes its result through `ISessionStateService.NotifyPostSessionsReady`, a live signal built alongside the domain event rather than through it. |
| Application | `IPlannedBlockRepository.GetUnresolvedFocusBlocksAsync(DateTime asOf)` | Focus blocks with `End < asOf && RitualCompletedAt == null`, ordered by `Start` |
| Application | `SweepMissedFocusBlocksCommand` / `Result` / `CommandHandler` (`UseCases/Sessions/SweepMissedFocusBlocks/`) | Optional `AsOf (DateTime?)` — falls back to reading `ISystemClock` fresh when omitted (fixed 2026-08-20, Gap 21's second addendum: a caller that already resolved "now" for a sibling check in the same operation must supply it, or the two checks can disagree about what instant "now" means). Queries unresolved blocks, calls `MarkFocusBlockMissedCommand` per block, collects the resulting `PostSessionDto`s. Invoked from two call sites with different live-visibility: `SessionStateService.InitializeAsync()` (once, at startup, no `AsOf` supplied — the only time-sensitive read in that method, no sibling to disagree with; result relied on purely for `GetUnacknowledgedPostSessionsQuery`'s catch-up, no event) and `ScheduledEnforcementService`'s 5-second heartbeat (built 2026-08-20, Gap 21 — every tick, `AsOf` set to the same `now` already passed to `ComputeActivePolicySliceQuery`, result passed to `ISessionStateService.NotifyPostSessionsReady` for live popup surfacing). |
| Application | `ISessionStateService.PostSessionsReady (EventHandler<IReadOnlyList<PostSessionDto>>)` / `NotifyPostSessionsReady(...)` | **Built 2026-08-20, Gap 21's Revisited note.** Mirrors `RitualRequirementChanged`'s existing Infrastructure→Application-interface→UI bridge shape. No-ops on an empty list. `ShellViewModel` subscribes and enqueues each into the same `_pendingOverlays` queue the live edge-triggered `TryMarkBlockMissed` path already uses — identical triage batching, only the origin of the `PostSessionDto` differs. |
| Application | `ISessionStateService.PostSessionAcknowledged (EventHandler)` / `NotifyPostSessionAcknowledged()` | **Built 2026-08-20, Gap 21's Revisited note.** Raised from `ShellViewModel.ExitPostSession()` (the existing "Done" handler). `PlannerPageViewModel` subscribes and reloads its current week — the moment a sweep-caught Missed block's data is known to have changed, chosen over a poll timer since every other tile refresh in the codebase is already action/event-triggered. |
| Application | `AcknowledgePostSessionCommand` / `CommandHandler` / `Result` (`UseCases/Sessions/AcknowledgePostSession/`) | Loads the session by id, requires `Status` to be `Completed` or `Missed`, calls `MarkPostSessionAcknowledged(now)`, saves. Called unconditionally from `PostSessionViewModel.Done()`, alongside the existing conditional `SaveReflectionCommand` call |
| Application | `ISessionRepository.GetUnacknowledgedPostSessionsAsync` | `Completed`/`Missed` sessions with `PostSessionAcknowledgedAt == null`, ordered by `EndedAt` ascending |
| Application | `GetUnacknowledgedPostSessionsQuery` / `Handler` (`Queries/GetUnacknowledgedPostSessions/`) | Composes the repository call above with preset-name resolution, blocked-attempt grouping, and (for a Completed Planned session) the same grace-period credited-minutes computation `EndSessionCommandHandler` applies — rebuilding an equivalent `PostSessionDto` for a session resolved in a previous app run |
| Application | `PostSessionDto.Status` | Mirrors `Session.Status`; drives Post-Session screen copy |
| Application | `ISessionStateService.RitualRequirementChanged` (widened) / `RitualRequirementChange(bool Required, Guid? FocusBlockId)` (`DTOs/RitualRequirementChange.cs`) | `FocusBlockId` identifies the block on a `false` transition caused by its window closing unfulfilled; `null` for transitions not about a specific block's forced window (session starting/ending) |
| Infrastructure | `SessionStateService.SetRitualRequired` | Captures the outgoing `_pendingFocusBlockId` into a local before clearing it, so the `false` event still carries the right block identity even though the caller (`ScheduledEnforcementService`) never supplies one |
| UI | `ShellViewModel.InitializePendingOverlaysAsync()` | Constructor-fired, fire-and-forget: awaits `GetUnacknowledgedPostSessionsQuery`, enqueues each result as `PendingPostSession` (already chronological), then enqueues any still-pending `PendingRitual` behind them, then calls `TryDrainQueue()` once |
| UI | `ShellViewModel` queue/drain (`Queue<PendingOverlay>`, `TryDrainQueue`, `MissedSessionsTriageViewModel`) | See prose above |

---

## Ritual

**Definition:** The four-step wizard (Ten → Zetsu → Ren → Hatsu) through which a user consciously creates a Session. The Ritual is a design-time guardrail: it forces deliberate intent before focus begins.

**Lifecycle:** Opened by user → steps navigated → `StartSessionCommand` dispatched on Hatsu completion → Ritual closed → user returns to Main Page with active session.

**Two activation paths:**
- **Spontaneous:** User clicks "Use Nen" on the Main Page. All four steps are fully user-controlled.
- **Planned:** A scheduled Focus Block activates and pushes the user to the Ritual. The Ren and Hatsu steps are constrained (see below).

**Step meanings:**

| Step | Theme | Header Text | Purpose in UI |
|------|-------|-------------|---------------|
| **Ten** | Awareness | "Focus the mind, reflect upon the self, and determine the goal" | Greet the user; prompt internal reflection before continuing. Same for both Spontaneous and Planned. |
| **Zetsu** | Silence | "Put it into words" | User writes their session goal into a text box. Same for both Spontaneous and Planned. |
| **Ren** | Persistence | "Intensify your will" | **Spontaneous:** User selects a Focus-type Block Preset from their existing presets. **Planned:** Block Preset is pre-selected and locked (chosen when the Focus Block was scheduled); user cannot change it. User may add additional sites (Blocklist presets only) and apps for this session only — these are not saved to the preset. |
| **Hatsu** | Output | "Put it into action" | **Spontaneous:** User selects session duration (5 min – 8 hours) and optionally configures break reminders. **Planned:** Duration is pre-determined by the Focus Block's scheduled end time (displayed as "active until [time]"); user can only configure break reminders. User clicks "Start Session." |

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `StartSessionCommand` / `StartSessionCommandHandler` | Final output of the Ritual; creates and starts the Session |
| UI | `RitualShellViewModel` | Hosts step navigation; owns `CurrentStep` and navigation commands |
| UI | `TenPageViewModel`, `ZetsuPageViewModel`, `RenPageViewModel`, `HatsuPageViewModel` | One ViewModel per step; collects input, validates locally |
| UI | `RitualShellView.axaml` | Full-screen overlay rendered over the Shell when `IsRitualActive = true` |

---

## Block Preset

**Definition:** A reusable, named configuration of blocked applications and a website filter type. A Block Preset is typed as either **Focus** or **Background** at creation time. The type can be changed later provided the preset is not currently attached to any scheduled block or active session. The type acts as a filter: Focus presets only appear during the Ren step and when scheduling a Focus Block (Planner); Background presets only appear when scheduling a Background Block (Planner).

**Lifecycle:** Created in Block Presets page (type chosen via picker at creation) → (optionally duplicated or edited; type may be changed if no active blocks reference it) → attached to Session at Ritual (Ren step, Focus-type only) or to a Planned Block in the Planner → locked for duration of Session or active Block.

**Website filter type:** Each preset carries a `WebsiteFilterType` of either `Blocklist` (block specific sites, allow everything else) or `Allowlist` (allow specific sites, block everything else). Both Ritual and Background presets can use either mode. Both can also specify blocked apps.

**Time-agnosticism:** A Block Preset is time-agnostic — it carries no schedule, no window, and no knowledge of when it is active. This allows the same preset to be reused across multiple planned blocks at different times. `BlockedSite` and `BlockedApp` are always plain hard blocks — access-time limiting is handled by the separate `UsageCeiling` feature (see `ARCHITECT_GOVERNANCE.md` → Open Gap A), which is deliberately decoupled from `BlockPreset` entirely, not a field on it.

**Ubiquitous language rules:**
- A preset is *attached* to a session or block, not *assigned* or *applied*.
- A preset is **locked** when it's in use by an active session, or referenced by any future `PlannedBlock` — whether that block is an ordinary uncommitted block or one linked to a Nen Contract (`HasFutureBlocksForPresetAsync` doesn't distinguish; both count). A locked preset can never be **deleted**.
- A locked preset can still be **updated**, but only toward being *at least as strict* (2026-07-07 revision — previously a locked preset rejected every edit outright; direction corrected 2026-08-21, `DESIGN_GAP_HISTORY.md` Gap 30 — see `BlockPreset.IsAtLeastAsStrict`): `BlockedApps` must always be a superset of the current list (apps have no allow-mode, so dropping one always weakens the preset). `Websites` depends on `WebsiteFilterType` — under `Blocklist`, the same superset (add-only) rule applies; under `Allowlist` it's the **opposite** — the new list must be a *subset* of the current one (removing an allowed site narrows access, the stricter direction; adding one widens it, the weaker direction, even though it's technically an addition). `Name`, `WebsiteFilterType`, `PresetType`, and `Tags` must all stay unchanged while locked. You can strengthen a live preset; you cannot weaken, rename, or retype it while it's committed.
- A preset is *locked* — fully, not just add-only — for `DeleteBlockPresetCommand`; deletion is never allowed while in use, regardless of what would be deleted.

**UI display:** On the Block Preset page, Focus and Background presets are displayed together in a single list with a type badge (blue = Focus, green = Background). A filter bar lets the user show All, Focus only, or Background only.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BlockPreset` aggregate (`Entities/BlockPreset.cs`) | Owns the list of `BlockedApp` and `BlockedSite` value objects, the `WebsiteFilterType`, and the preset type (`BlockPresetType.Focus` or `BlockPresetType.Background`). `IsAtLeastAsStrict(IReadOnlySet<string> proposedApps, IReadOnlySet<string> proposedSites) -> bool` (added 2026-08-21, `DESIGN_GAP_HISTORY.md` Gap 30) is the entity's own answer to "does this proposed change keep me at least as strict" — apps always add-only, sites add-only under `Blocklist`/remove-only under `Allowlist`. `UpdateBlockPresetValidator` asks the entity rather than re-deriving the direction itself. |
| Domain | `BlockedApp`, `BlockedSite` value objects | Immutable; validated at construction. Always plain hard blocks — no budget/ceiling concept lives on either; see `UsageCeiling` for ambient access limits |
| Domain | `WebsiteFilterType` enum | `Blocklist` or `Allowlist` |
| Application | `IBlockPresetRepository` | CRUD persistence contract |
| Application | `CreateBlockPresetCommandHandler`, `UpdateBlockPresetCommandHandler`, etc. | One handler per mutation |
| Application | `UpdateBlockPresetValidator` | Checks lock state via `ISessionRepository.GetActiveAsync` + `IPlannedBlockRepository.HasFutureBlocksForPresetAsync` (same two checks `DeleteBlockPresetValidator` uses, kept as separate inline checks rather than extracted — no third caller yet to justify a shared abstraction). If locked, fetches the current `BlockPreset` and applies the private `IsAddOnlyChange` comparison: `Name`/`WebsiteFilterType`/`PresetType`/`Tags` must be identical, and `current.IsAtLeastAsStrict(proposedApps, proposedSites)` must hold (direction fixed 2026-08-21 — see `BlockPreset.IsAtLeastAsStrict` above and `DESIGN_GAP_HISTORY.md` Gap 30; previously always required both lists to be supersets, which was backwards for an `Allowlist` preset's `Websites`). Not locked → any change passes. |
| UI | `BlockPresetPageViewModel` | Full CRUD UI; file picker for app selection; type filter |

---

## Daily Usage / Usage Ceiling / Recurring Blocks *(not yet built)*

Three related but independent not-yet-built features — universal per-resource usage tracking, an ambient perpetual access cap, and repeating calendar blocks. `DailyUsage`, `UsageResourceType`, `UsageCeiling`, `CeilingPeriod`, `RecurrenceRule`, `RecurringSeries`, and `SkippedOccurrence` are all defined in the Quick Reference table above. Full domain shape, precedence rules, and interrogation record live in `ARCHITECT_GOVERNANCE.md` → Open Gap A (Daily Usage / Usage Ceiling) and Open Gap E (Recurring Blocks) — not duplicated here, since that file is already read every session.

---

## Enforcement

**Definition:** The active OS-level mechanisms that prevent access to blocked applications and websites during a Session or Planned Block. Enforcement is a *side-effect* of a Session or Block being active — it is not a domain entity.

**Lifecycle:** Applied when a Session starts or a Planned Block activates → remains active during breaks (current behaviour) → removed when session ends or block expires.

**Mechanisms (in order of application):**
1. **Process kill loop** — kills matching process trees every 2 seconds
2. **Hosts file** — writes `0.0.0.0` / `::` entries between `# NEN_FOCUS_START` markers
3. **DNS flush** — `ipconfig /flushdns` after each hosts write
4. **Registry** — disables DoH (`BuiltInDnsClientEnabled = 0`) and sets `DnsOverHttpsMode = "off"` in Chrome, Edge, Brave
5. **Browser extension** — intercepts navigations via `chrome.webNavigation.onBeforeNavigate`, queries the local HTTP API at `localhost:5005/check`, redirects blocked sites to `blocked.html`

**Known gaps:**
- The `/report` endpoint is implemented: when the extension reports a blocked navigation, `WindowsEnforcementService` calls `IBlockedAttemptTracker.RecordWebBlockedAsync` if an active session exists. Background Block attempts (no `ActiveSessionId`) are still not tracked.
- Allowlist mode is handled by the `/check` endpoint: in Allowlist mode the endpoint returns `blocked: true` for any domain NOT in `AllowedSites`. Note that the hosts-file path cannot enforce allowlist mode (you cannot enumerate all domains); the browser extension is the sole allowlist enforcement mechanism.
- Blocked attempt tracking currently only works for app process kills during Ritual sessions (via `IBlockedAttemptTracker`). Website blocked attempts and Background Block tracking are future work.
- Usage Ceiling enforcement is not yet implemented. `PolicyEngine` does not yet read `DailyUsage`. The `/report-time` endpoint does not yet exist. See `ARCHITECT_GOVERNANCE.md` → Open Gap A.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `IEnforcementService` | Contract for applying and removing enforcement; accepts `ActivePolicySlice` (Domain Value Object) |
| Domain | `IPolicyEngine` / `PolicyEngine` (`Services/`) | Computes `ActivePolicySlice` from raw domain entities; sole owner of all merge logic |
| Application | `ComputeActivePolicySliceQueryHandler` | Orchestrates data fetching across three repositories; delegates computation to `IPolicyEngine` |
| Infrastructure | `WindowsEnforcementService` | Consumes `ActivePolicySlice` directly; owns hosts file, process kill loop, registry writes, local HTTP API (`localhost:5005`). Only handles `/check` endpoint currently. |
| Infrastructure | `ScheduledEnforcementService` | `BackgroundService` heartbeat (5s); calls `ComputeActivePolicySliceQuery`, reads `ActiveBlockPresetType`, calls `IEnforcementService` |
| Extension | `background.js` | Intercepts browser navigation; queries `/check`; calls `/report` (not yet handled server-side) |

---

## Policy Slice

**Definition:** The computed, merged enforcement state at a given instant in time. It is the *answer* to the question "what should be blocked right now?" — derived from all currently active Sessions and Planned Blocks. Represented as `ActivePolicySlice`, a **Domain Value Object** in `NenFocus.Domain/ValueObjects/`, not a DTO.

**Computation rules** (all logic lives in `PolicyEngine.ComputeSlice()`):
- **Apps:** union of all blocked apps across all active block presets, always a blocklist. Includes `TemporaryBlockedApps` from the active `Session` entity.
- **Websites:** if *any* active preset uses `Allowlist` mode → default-deny, allowed = intersection of all allowlists minus any blocklist entries. Otherwise → block the union of all blocklisted sites. Includes `TemporaryBlockedSites` from the active `Session` entity.
- **Usage Ceilings *(not yet implemented)*:** In a future slice, `PolicyEngine.ComputeSlice()` will read `DailyUsage` against active `UsageCeiling` entries. Sites and apps whose ceiling has been exhausted for the current period (day or week) will be added to the blocked set — unless a hard-blocking preset already covers them, in which case the hard block wins and no usage accrues. See `ARCHITECT_GOVERNANCE.md` → Open Gap A.
- An `ActivePolicySlice` is considered "empty" only in `Blocklist` mode with no blocked apps or sites. It is *never* empty in `Allowlist` mode.
- **`ActiveBlockPresetType`:** set to `BlockPresetType.Focus` if any Focus Block is currently active, `BlockPresetType.Background` if only Background Blocks are active, `null` if enforcement is driven solely by an active Session with no planned block. Ritual always takes precedence over Background when both overlap.
- **"Active" is re-checked precisely inside `ComputeSlice` itself** (fixed 2026-08-20, `DESIGN_GAP_HISTORY.md` Gap 21's third addendum): `ComputeActivePolicySliceQueryHandler`'s own block query deliberately over-fetches with a ±1-second buffer around `at` (SQLite datetime-comparison slop), so `PolicyEngine` cannot simply trust every block the caller hands it — it filters through `PlannedBlock.IsActive(at)` as the final, precise authority. Before this fix, a block whose `End` fell inside that 1-second buffer could still become `ActiveFocusBlockId` even though `SweepMissedFocusBlocksCommandHandler`'s strict `End < asOf` check (using the same `at`) had already marked it `Missed` in the same heartbeat tick — re-triggering the Ritual for a block that already had a resolved Session. **`IsActive`'s own definition changed 2026-08-21 (Gap 32):** `atTime >= Start && atTime < EffectiveEnd(atTime)`, not raw `End` — a block whose session ended early now correctly stops being "active" the moment it actually ended, not at its original scheduled end (see `PlannedBlock.EffectiveEnd` below).

**Overlap rules:**
- Multiple Background Blocks can overlap freely. The net enforcement is the union of all their presets.
- A Focus Block can overlap with Background Blocks. The net enforcement is the union of all active presets. The Ritual is still triggered.
- Two Focus Blocks cannot overlap. The Planner prevents scheduling conflicts. A Spontaneous session cannot be started if it would overlap with an upcoming Focus Block.

**Why `ActiveBlockPresetType` matters:** `ScheduledEnforcementService` reads this field each heartbeat cycle. If `ActiveBlockPresetType == Ritual` and no Session is active, it calls `_sessionState.SetRitualRequired(true)` to push the user into the Ritual. If `ActiveBlockPresetType == Background`, enforcement is applied silently with no Ritual prompt. This is the correct application of the Ubiquitous Language principle — the difference between a Focus Block and a Background Block is expressed as a domain enum in the result, not as a boolean flag scattered across callers.

**Source of Truth flow:** `Database → PolicyEngine (Domain) → ActivePolicySlice (VO) → WindowsEnforcementService (OS)`

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `ActivePolicySlice` (`ValueObjects/ActivePolicySlice.cs`) | Immutable Domain Value Object; the canonical enforcement contract. Carries `BlockedApps`, `BlockedSites`, `AllowedSites`, `WebsiteFilterTypeMode`, `ActiveBlockPresetType`, `ActiveSessionId`, `IsEmpty`. |
| Domain | `IPolicyEngine` / `PolicyEngine` (`Services/`) | Stateless domain service; accepts raw `BlockPreset`, `Session`, and `PlannedBlock` collections and produces `ActivePolicySlice`. Owns all merge rules — no merge logic exists anywhere else. |
| Application | `ComputeActivePolicySliceQueryHandler` (`Queries/ComputeActivePolicySlice/`) | Pure orchestrator: fetches entities from three repositories, loads `BlockPreset` aggregates, delegates to `IPolicyEngine.ComputeSlice()`. Returns the `ActivePolicySlice` to callers. |
| Infrastructure | `WindowsEnforcementService` | Consumes `ActivePolicySlice` directly (no DTO) to drive OS-level mechanisms |
| Infrastructure | `ScheduledEnforcementService` | Reads `ActiveBlockPresetType` from the `ActivePolicySlice` each heartbeat to decide whether to trigger the Ritual |

---

## PolicyEngine

**Definition:** The sole Domain service responsible for computing the `ActivePolicySlice`. It is a stateless, synchronous function: given a point in time, a list of `BlockPreset` aggregates, a list of active `Session` entities, and a list of active `PlannedBlock` entities, it returns the complete enforcement state for that moment.

**Why it is a Domain Service (not an Application service):** Policy merge logic — the rules for unioning blocked apps, resolving allowlist/blocklist precedence, and assigning `ActiveBlockPresetType` — is domain logic. It expresses invariants of the domain ("an allowlist preset beats a blocklist preset", "a Focus Block takes priority over a Background Block") that must be consistent wherever policy is computed. Placing this logic in a Domain service makes it unit-testable without a database and reachable by any future consumer (e.g., a Planner "preview" query).

**Interface contract:**
```csharp
// NenFocus.Domain/Services/IPolicyEngine.cs
ActivePolicySlice ComputeSlice(
    DateTime at,
    IEnumerable<BlockPreset> presets,
    IEnumerable<Session> sessions,
    IEnumerable<PlannedBlock> blocks);
```

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `IPolicyEngine` (`Services/IPolicyEngine.cs`) | Interface; the contract Application code depends on |
| Domain | `PolicyEngine` (`Services/PolicyEngine.cs`) | Sealed implementation; all merge rules live here. Filters `blocks` through `PlannedBlock.IsActive(at)` before treating any of them as active (fixed 2026-08-20, `DESIGN_GAP_HISTORY.md` Gap 21's third addendum) — the caller's own fetch is allowed to be loose/buffered, but `PolicyEngine` is always the precise final word on "is this block active right now." `IsActive` itself now compares against `EffectiveEnd(at)`, not raw `End` (fixed 2026-08-21, Gap 32) — a block whose session ended early stopped freeing itself from `activeBlocks`, so `activeBlocks.OfType<FocusBlock>().FirstOrDefault()` could still pick a stale, already-resolved block over a genuinely new one scheduled into the freed time, permanently suppressing the new block's Ritual trigger until the stale block's raw window finally closed. **`activePresets` is now derived from `activeBlocks`'/`activeSessions`' own `BlockPresetId`s rather than trusted unfiltered from the caller (fixed 2026-09-10, `DESIGN_GAP_HISTORY.md` Gap 60)** — `activeBlocks` was already correctly narrowed via `IsActive`, but `activePresets` was just `presets.ToList()`, so a block that had genuinely gone inactive (session ended early) still had its preset's `BlockedSites`/`BlockedApps` folded into the computed policy and reapplied every heartbeat tick, for as long as "now" stayed inside the block's *original* `Start`/`End` window. **Also resolves pause-during-break (built 2026-09-12, `DESIGN_GAP_HISTORY.md` Gap 61):** takes an optional `contracts` parameter, used only to look up a Focus Block's `PauseEnforcementDuringBreak` via its `ContractId` when its Session is `OnBreak` — a block with no `ContractId` (including a Bonus block) always falls back to the hardcoded-permissive global default. Only the specific block/session on break loses its own preset's contribution; a co-occurring Background Block is untouched. |
| Application | `ComputeActivePolicySliceQueryHandler` | Only caller; fetches entities, then delegates to `_policyEngine.ComputeSlice()` |
| Infrastructure | `DependencyInjection.cs` | Registers `PolicyEngine` as `IPolicyEngine` (singleton — stateless) |

---

## Planned Block

**Definition:** The abstract base class for all calendar-scheduled enforcement entries. A `PlannedBlock` has a `Start` time, an `End` time, a `BlockPresetId`, and an optional `Title`. It is never instantiated directly — only its two concrete subtypes (`FocusBlock` and `BackgroundBlock`) are created. The C# class type encodes the block preset type; there is no stored `BlockPresetType` column.

**Domain behaviour:** `PlannedBlock` carries two domain methods used by both subtypes and the query layer:
- `IsActive(DateTime atTime)` — returns `true` if `atTime` falls within `[Start, End)`.
- `OverlapsWith(PlannedBlock other)` — returns `true` if the two windows intersect; used by the planner to prevent scheduling conflicts.

**Why it exists:** `IPlannedBlockRepository` returns `IReadOnlyList<PlannedBlock>` so that the enforcement pipeline can work with all scheduled blocks polymorphically without knowing their concrete type. `ComputeActivePolicySliceQueryHandler` then pattern-matches on C# type to derive `ActiveBlockPresetType` — the distinction is made exactly once, in the one place that needs it.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `PlannedBlock` abstract class (`Entities/PlannedBlock.cs`) | Base for `FocusBlock` and `BackgroundBlock`; exposes `IsActive()` and `OverlapsWith()` |
| Application | `IPlannedBlockRepository` | Returns `IReadOnlyList<PlannedBlock>`; callers cast to concrete type when subtype behaviour is needed |
| Application | `ComputeActivePolicySliceQueryHandler` | Pattern-matches on `FocusBlock` / `BackgroundBlock` to set `ActiveBlockPresetType` on the `ActivePolicySlice` value object |

---

## Focus Block

**Definition:** A planned calendar entry that does two things simultaneously at its designated start time: (1) enforcement activates immediately — the block preset's rules are applied before the user has done anything, and (2) the user is pushed to the Nen Ritual page to consciously start the session. The session created via the Ritual is the *Planned Session* associated with this block. Critically, enforcement is already in place from the designated time regardless of whether the user completes the Ritual immediately. If the user never completes the Ritual, the `FocusBlock` row still exists and Insights shows it as "missed" (`RitualCompletedAt` is null). Enforcement still expires at the block's scheduled end time whether the Ritual was completed or not.

**Must use a Focus-type Block Preset.** Only presets created with the Focus type are available when scheduling a Focus Block in the Planner or selecting a preset during the Ren step.

**Overlap rules:** Two Focus Blocks cannot overlap each other. A Focus Block can overlap with one or more Background Blocks — the net enforcement is the union of all active presets.

**Ubiquitous language rules:**
- A Focus Block is *scheduled*, not *started*.
- A Focus Block *activates* and *expires* — it is not started or ended.
- Enforcement from a Focus Block is *pre-emptive* — it does not wait for the Ritual to complete.

**Activation sequence:**
1. System clock reaches the block's `Start` time
2. Enforcement applies immediately (policy slice computed, OS-level blocking begins)
3. App navigates user to the Nen Ritual (Ten → Zetsu → Ren → Hatsu)
4. User completes Ritual → `StartSessionCommand` dispatched with `Origin = Planned` and `OriginatingBlockId = <this block's Id>`
5. `StartSessionCommandHandler` calls `FocusBlock.MarkRitualCompleted(DateTime.UtcNow)` and saves the block
6. The resulting Session (with `Origin = Planned`) is linked to this Focus Block via `Session.OriginatingBlockId`

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `FocusBlock` entity (`Entities/FocusBlock.cs`) | Extends `PlannedBlock`; inherits `Id`, `BlockPresetId`, `Start`, `End`, `Title?`; own field: `RitualCompletedAt?`; block preset type derived from C# class (`FocusBlock` → `BlockPresetType.Focus` in query handler — no stored column) |
| Application | `IPlannedBlockRepository` | Retrieves blocks for overlap validation and active-state queries |
| Application | `ComputeActivePolicySliceQueryHandler` | Queries blocks covering `DateTime.UtcNow`; merges with active Session |
| Application | `StartSessionCommandHandler` | Calls `FocusBlock.MarkRitualCompleted(DateTime.UtcNow)` when `OriginatingBlockId` is provided |
| UI | `PlannerPageViewModel` | Creates, updates, deletes Focus Blocks; drag interactions and form locking implemented |
| UI | `ShellViewModel` | (Not yet built) — will detect Focus Block activation and trigger Ritual overlay |

---

## Background Block

**Definition:** A planned calendar entry that activates enforcement at its designated start time, exactly like a Focus Block, but does **not** trigger the Nen Ritual. The user is not prompted to start a session — the block preset's rules simply become active. There is no associated Session entity. The block expires at its `End` time and enforcement is removed. The `BackgroundBlock` row itself is the complete Insights record — it appears in the Calendar view regardless of whether the machine was on during the scheduled time.

**Must use a Background-type Block Preset.** Only presets created with the Background type are available when scheduling a Background Block in the Planner.

**Overlap rules:** Multiple Background Blocks can run simultaneously. Their presets are merged via the PolicyEngine — the net enforcement is the union of all active presets. Background Blocks can also overlap with Focus Blocks.

**Contrast with Focus Block:**

| | Focus Block | Background Block |
|---|---|---|
| Enforcement at start time | ✅ | ✅ |
| Triggers Nen Ritual | ✅ | ❌ |
| Creates a Session entity | ✅ (via Ritual) | ❌ |
| Appears in Insights Calendar | ✅ | ✅ |
| Has `RitualCompletedAt` field | ✅ | ❌ |
| Can overlap with same type | ❌ | ✅ |
| Shows on Main Page timer | ✅ (after Ritual) | ❌ |
| Has Post-Session screen | ✅ | ❌ |

**Ubiquitous language rules:**
- A Background Block is *scheduled* and *activates* — it is not *started*.
- "Background" refers to the absence of a Ritual and Session, not the absence of enforcement.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BackgroundBlock` entity (`Entities/BackgroundBlock.cs`) | Extends `PlannedBlock`; inherits `Id`, `BlockPresetId`, `Start`, `End`, `Title?`; no additional own fields; block preset type derived from C# class (`BackgroundBlock` → `BlockPresetType.Background` in query handler — no stored column) |
| Application | `ComputeActivePolicySliceQueryHandler` | **Includes** Background Blocks when computing the active policy slice |
| Infrastructure | `ScheduledEnforcementService` | Picks up the Background Block in its 5s heartbeat and applies enforcement |

---

## Break Timer Preset

**Definition:** A saved, reusable work/break cadence — Classic (25/5, 4 cycles, 15m long break), Deep Work (50/10, 3 cycles, 20m), and Flow (90/20, 2 cycles, 30m) ship as ordinary seeded rows, not a special hardcoded case. A future custom preset is just another row in the same table, freely creatable/editable/deletable like any of them — no protected or built-in status, no locking of any kind. Authored on the Settings page (not yet built), not the Presets page.

**Selected at Hatsu**, for both Spontaneous and Planned sessions alike — the same bucket break reminders already occupied, since a preset only governs the Session's own internal pacing and never affects what's enforced or when. Only presets whose phase timeline (see `SessionPhase`/`IBreakScheduleCalculator` in the Quick Reference table) actually produces a real break for the duration in play are offered; the list re-filters live as a Spontaneous session's duration wheel changes. Picking "No timer" means the session runs start-to-finish with no breaks at all.

**Snapshotted, not referenced:** `Session.BreakTimer` is a frozen copy of the chosen preset's cadence, taken at Start — mirrors `ContractPreset`'s "copied by value, not a live reference" rule exactly. Editing or deleting the source preset later never affects a session already running or already in history.

**Cadence loops for the whole session**, bounded only by its total duration (fixed the moment the session starts, same as today — user-picked for Spontaneous, block-derived for Planned): work → short break → ... → long break → work → short break → ..., repeating. A break is only ever included if it can run to completion *and* leaves real Work time after it — never a break that gets cut short, and never one that ends exactly when the session does with nothing left to work on. See `IBreakScheduleCalculator` in the Quick Reference table for the one calculation this and every other consumer reads.

**Manual break-taking is retired app-wide.** There is no "Take a Break" button anywhere — see the Session section's Break behaviour note above.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BreakTimerPreset` (`Entities/BreakTimerPreset.cs`) | Aggregate root: `Id`, `Name`, `BreakTimer`. `Update` is a wholesale replace, mirroring `ContractPreset.Update`. |
| Domain | `BreakTimer` (`ValueObjects/BreakTimer.cs`) | Validated value object: `WorkMinutes`, `ShortBreakMinutes`, `CyclesBeforeLongBreak`, `LongBreakMinutes`, all positive. |
| Domain | `IBreakScheduleCalculator` / `BreakScheduleCalculator` (`Services/`) | Pure Domain service: `ComputeSchedule(BreakTimer, TimeSpan totalDuration) -> IReadOnlyList<SessionPhase>`. The Deep Module every consumer below reads from. |
| Domain | `Session.BreakTimer (BreakTimer?)`, `Session.CurrentBreakKind (SessionPhaseKind?)` | Snapshot taken at Start; `CurrentBreakKind` set from `StartBreak`'s caller-resolved parameter, never re-derived by `Session` itself. |
| Application | `IBreakTimerPresetRepository`, `CreateBreakTimerPreset`/`UpdateBreakTimerPreset`/`DeleteBreakTimerPreset` (`UseCases/BreakTimerPresets/`), `GetAllBreakTimerPresetsQuery` | Full CRUD — built, but **no UI yet** (Settings page doesn't exist). |
| Application | `GetAvailableBreakTimerPresetsQuery` / `Handler` | Given a duration, returns only presets whose computed schedule contains at least one real break. |
| Application | `StartSessionCommand.BreakTimerPresetId (Guid?)` / `StartSessionCommandHandler` | Loads the preset, snapshots its `BreakTimer` onto the new `Session`. |
| Infrastructure | `ActiveSessionMonitor` | Walks the phase timeline every tick; auto-calls `StartBreakCommand`/`ResumeFromBreakCommand` at each transition (no user click); exposes `BreakMarkers` for the Main Page progress bar. |
| UI | `HatsuPageViewModel` (`AvailableBreakTimerPresets`, `BreakTimerPresetChips`, `SelectedBreakTimerPreset`) | Chip-style selector, live-filtered by duration; `BreakTimerPresetChip` pairs each preset with its own `IsSelected` flag so the View never has to compare across an `ItemsControl` boundary. |
| UI | `MainPageView.axaml.cs` (`RenderBreakMarkers`) | Builds the progress bar's tick marks in code-behind — a `ColumnDefinitions` collection can't be data-bound directly in Avalonia. |

---

## Planner Page *(implemented)*

**What it is:** The weekly calendar view where the user schedules Focus Blocks and Background Blocks. Each column is a day; rows are hour slots from a configurable `FirstHour` to `LastHour`. Tiles represent blocks and are colour-coded: gold (`#B8922A`) for Focus, light grey (`#E8E8F0`) for Background.

**Drag/resize mechanics, lock rules, Block Snapshot detail: moved to `UI_UX_STANDARDS.md` → Planner Drag Interactions (2026-09-10)** — pixel-level interaction spec, not domain vocabulary.

---

## Spontaneous Session

**Definition:** A Session created by the user clicking "Use Nen" on the Main Page and completing the full Nen Ritual (Ten → Zetsu → Ren → Hatsu), outside of any scheduled Focus Block. The user has full control: they choose the Block Preset (Focus-type only), set the duration, and configure break reminders. The Session is created with `Origin = Spontaneous` and `OriginatingBlockId = null`. No Block entity of any kind is created.

**Product invariant:** A manual enforcement session cannot exist without a completed Ritual. This is a product invariant enforced by design, not by validation code. If the user abandons the Ritual at any step before clicking "Start Session" on the Hatsu page, no record is saved anywhere — no draft, no partial session, no recovery state.

**Conflict rule:** A Spontaneous Session cannot be started if it would overlap with an upcoming Focus Block. For example, if a Focus Block is scheduled at 18:00, the user cannot start a 30-minute session at 17:45.

**Contrast with Planned Session:** A Spontaneous Session is user-initiated and requires no pre-scheduled block. A Planned Session is triggered by a Focus Block at its scheduled start time. In a Spontaneous Session the user freely chooses the Block Preset and duration; in a Planned Session both are pre-determined.

**Technical mapping:** No Block entity — distinguished at the Application layer by `Session.Origin == Spontaneous`. `Session.OriginatingBlockId` is `null`.

---

## Planned Session

**Definition:** A Session created by the user completing the Nen Ritual after a scheduled Focus Block activated and pushed them to the Ritual overlay. The Block Preset and duration are pre-determined by the Focus Block — the user cannot change the preset (Ren step) or the end time (Hatsu step), but can configure break reminders. The Session is saved with `Origin = Planned` and `OriginatingBlockId = <the triggering FocusBlock's Id>`. `StartSessionCommandHandler` stamps `FocusBlock.RitualCompletedAt` at this moment. If the user never completes the Ritual, no Session row is created; the `FocusBlock` row remains with `RitualCompletedAt = null` and Insights shows it as "missed." Enforcement still runs until the block's scheduled end time regardless.

---

## SessionOrigin

**Definition:** An enum that tags every `Session` with how it was initiated. Values: `Spontaneous` (user completed the Ritual from the Main Page without a pre-scheduled block) and `Planned` (user completed the Ritual after a Focus Block pushed them to it).

**Why this is not a nullable FK check:** `Session.OriginatingBlockId` is `null` for Spontaneous sessions, but reading nullness as "origin type" is a leaky boolean. `SessionOrigin` makes the intent explicit and self-documenting. It remains unambiguous when future origin types are added.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `SessionOrigin` enum (`Enums/SessionOrigin.cs`) | Values: `Spontaneous`, `Planned` |
| Domain | `Session.Origin` | Set at session creation; never changed after the session starts |
| Application | `StartSessionCommandHandler` | Sets `Origin = Planned` when `OriginatingBlockId` is provided; `Origin = Spontaneous` when null |
| Application | `GetInsightsCalendarQuery` | Filters `Origin == Spontaneous` to identify sessions with no associated block for the Calendar view |

---

## Blocked Attempt

**Definition:** An append-only fact recording each time the user attempted to access a blocked app or website during a Session. Each record is keyed by `SessionId` and stored as its own row — not serialized as JSON on the Session entity.

**Current state:** Both app and website blocked attempts are tracked during Ritual sessions, and both deliberately distinguish an active attempt from a resource merely being caught by ambient enforcement when a block first activates (see Gap 46's third correction in `DESIGN_GAP_HISTORY.md` for the full reasoning) — an app/site that was already running/open when the block activated is not "attempted," only a later attempt made while the block is active is. Apps: `WindowsEnforcementService.KillBlockedProcessesImmediate(reportAttempts)` — the one-time sweep `ApplyPolicyAsync` runs when a block first activates passes `false`; the ongoing 2-second process-kill timer, which only ever catches a relaunch attempted during an active block, passes `true`. Websites: `IBlockedAttemptTracker.RecordWebBlockedAsync()` is called via the extension's `/report` endpoint, but only from `background.js`'s `onBeforeNavigate` handler (a real navigation check) — never from `poll.js`'s ambient already-open-tab poll, which only ever redirects. Background Block attempts are not yet tracked.

**Displayed in:** Post-Session screen (e.g. "you tried to access YouTube 5 times during this session") and Insights page (most-attempted sites, per-session history).

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BlockedAttempt` entity (`Entities/BlockedAttempt.cs`) | Append-only fact table; keyed by `SessionId` |
| Domain | `BlockedAttemptType` enum | `App` or `Website` |
| Application | `IBlockedAttemptTracker` | Contract for recording attempts; called by infrastructure |
| Infrastructure | `BlockedAttemptTracker` | Implementation; persists rows |
| Infrastructure | `WindowsEnforcementService` | Calls `RecordAppBlockedAsync` when killing a blocked process |

---

## Post-Session

**Definition:** The mandatory screen shown immediately after a Session ends (timer expires or user ends early). The user must see this screen but is not required to interact with it beyond clicking "Done."

**Displays:**
- Session duration (how long the session lasted)
- Blocked attempts summary (e.g. "you tried to access YouTube 5 times")
- Optional reflection text box (user can write or leave blank)

**Future:** Settings will allow the user to customize which elements appear on the Post-Session screen (e.g. hide the reflection box but keep blocked attempts).

**All Post-Session data is persisted and viewable in the Insights page.**

---

## GetInsightsCalendarQuery

**Definition:** A single query that unifies three data sources — `BackgroundBlock` rows, `FocusBlock` rows, and `Session` rows where `Origin = Spontaneous` — into a single `InsightsEntryDto` list for the Insights Calendar view. The Insights ViewModel never queries these sources directly.

**Why it is a Deep Module:** The three-source complexity (different entities, different table shapes, different "missed" detection logic) is hidden behind a single `IQueryHandler<GetInsightsCalendarQuery, IReadOnlyList<InsightsEntryDto>>` interface. Adding a fourth source requires a change to the handler only — the ViewModel and its bindings are untouched.

**Status:** Stub defined. Implementation deferred until the Insights page is built.

**Insights page will also include:** session history, blocked attempt analytics (most-attempted sites, per-session breakdown), block preset usage stats, total focus time for configurable periods (last week, since January, etc.), calendar view of past Focus Blocks, Background Blocks, and Spontaneous Sessions, and graphs for usage trends.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `GetInsightsCalendarQuery` / `GetInsightsCalendarQueryHandler` (`Queries/GetInsightsCalendar/`) | Merges three sources into `IReadOnlyList<InsightsEntryDto>`; stub only |
| Application | `InsightsEntryDto` | (to be defined) — carries type tag, date, duration, block preset name, completion state |
| UI | `InsightsPageViewModel` | (Not yet built) — binds to the query result; never touches individual source tables directly |

---

## Nen Contract

**Definition, lifecycle summary, and every term** (Seal, Strictness Rule, Daily Floor, Redemption Block, Work Hours, Change Window, Rest Days, Day Met) **are in the Quick Reference table above.** Full technical detail — build history, the 18 Redemption Rules, Strictness Rule mechanics, Work Hours edge cases, technical-mapping rows — lives in `DOMAIN_DICTIONARY_NEN_CONTRACT.md`, split out 2026-09-10 to keep this file's auto-loaded core lean (it was ~220 lines, roughly a quarter of this file, almost entirely build-history narrative rather than current vocabulary). Consult it directly whenever a task actually touches Nen Contract code — same as `DESIGN_GAP_HISTORY.md`, not auto-loaded, read on demand.

---

## Deep Module

**Definition:** A software module (class, service, or interface) that provides *significant* functionality through a *narrow*, simple API. The term comes from John Ousterhout's *A Philosophy of Software Design* and is a core architectural principle for NenFocus.

**The test:** Can a developer understand how to use this module in under 30 seconds by reading only its interface? If yes, it is deep. If the interface requires reading the implementation to use correctly, it is shallow and should be redesigned.

**Examples in NenFocus:**

| Module | Why it is deep |
|--------|---------------|
| `IActiveSessionMonitor` | 4 properties + 2 events; hides a background timer, UTC math, break tracking, and command handler invocation |
| `ISessionStateService` | 3 methods + 2 events; hides DB rehydration, preset name resolution, and broadcast coordination |
| `IEnforcementService` | 2 methods (`ApplyPolicyAsync`, `RemoveEnforcementAsync`); hides process kill loop, hosts file, DNS flush, registry, HTTP API |

---

## Design Gap

**Definition:** The distance between what a developer can see (the current interfaces and existing code) and what they need to understand to implement a new feature correctly. Every new feature starts with a Design Gap; the job of the pre-implementation "Grill Me" session is to close it before any code is written.

**Related principle:** See `ARCHITECT_GOVERNANCE.md → High-Level Development Strategy → 1. Strategic Design & The Design Gap`.
