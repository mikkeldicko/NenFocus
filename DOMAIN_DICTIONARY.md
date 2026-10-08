# NenFocus — Domain Dictionary

This file is the canonical reference for every domain concept in NenFocus. It exists to eliminate ambiguity between conversations. When a term is used in code, comments, or a design discussion, its meaning here is authoritative.

---

## Quick Reference

| Term | One-line definition |
|------|---------------------|
| **Session** | A bounded focus period; lifecycle `NotStarted → Active → Completed` — one continuous run, punctuated by the breaks in its `BreakSchedule` (a break is never a status) |
| **Ritual** | The four-step wizard (Ten → Zetsu → Ren → Hatsu) that creates a Session |
| **Block Preset** | A reusable template of blocked apps + website filter type; typed as Focus or Background at creation |
| **Planned Block** | Abstract base for `FocusBlock` and `BackgroundBlock`; carries `Scheduled` (a `TimeSlot`), `BlockPresetId`, `Membership` (its contract and role, or none), `IsActive()`, `HasStarted()`, `ReservedSlot()` |
| **Focus Block** | A planned calendar entry that activates enforcement AND triggers the Nen Ritual at the designated time |
| **Background Block** | A planned calendar entry that activates enforcement at the designated time without triggering the Ritual |
| **Policy Slice** | The merged, computed enforcement state at a given instant; represented as `ActivePolicySlice` (Domain Value Object), produced by `PolicyEngine` |
| **PolicyEngine** | Stateless Domain service (`NenFocus.Domain/Services/`) that computes `ActivePolicySlice` from raw aggregates; sole owner of all merge rules |
| **Enforcement Synchronizer** | `IEnforcementSynchronizer.SyncAsync(at)` — makes the OS's actual blocking match what `PolicyEngine` says right now; the only thing that turns a Policy Slice into an apply/remove call (Gap 62) |
| **Awaiting Ritual** | A Focus Block that is active, not cancelled, and whose Ritual isn't done — `FocusBlock.IsAwaitingRitual(at)`; the Ritual is prompted for it only if no session is running (Gap 62) |
| **Spontaneous Session** | A Session created by completing the Ritual manually from the Main Page; no Block entity exists; `Origin = Spontaneous` |
| **Planned Session** | A Session created via the Ritual triggered by a scheduled Focus Block; `Origin = Planned` |
| **SessionOrigin** | Enum (`Spontaneous` \| `Planned`) that tags every Session with how it was initiated |
| **BreakTimerPreset** | A saved, named `BreakTimer` (Classic, Deep Work, Flow ship as ordinary seeded rows, freely editable/deletable) — its own aggregate root, authored on the Settings page (not yet built). Just one quick way to produce a session's `BreakSchedule`; the session never references it, only snapshots its name |
| **BreakTimer** | Value object: a work/break rhythm — `WorkMinutes`, `ShortBreakMinutes`, `CyclesBeforeLongBreak`, `LongBreakMinutes`. `ScheduleFor(runLength)` lays it over a session to produce a `BreakSchedule` |
| **BreakSchedule** | Value object owned by `Session`: its break slots as offsets from `StartedAt` — the single source of truth for "is it a break right now." Set at Start; a break that has started never changes; trimmed at Complete, so the same list is the history record (Gap 63) |
| **SessionBreak** | One slot in a `BreakSchedule`: `Kind` (`ShortBreak` \| `LongBreak`), `Start`, `End` (offsets) |
| **BreakTransition** | A break boundary (`Started` \| `Ended`) a running session crossed — reported by `Session.BreakTransitionsBetween(from, to)`, acted on by `IBreakTransitionResponder`; not a domain event, since nothing about the session changes |
| **BlockMarker** | A point on a planned block's timeline (`StartingSoon` \| `Started` \| `Ended`) the clock passed — reported by `PlannedBlock.MarkersPassedBetween(from, to)`, announced by `IBlockMarkerNotifier` via `INotifier`; not a domain event, and not a "transition" (StartingSoon changes nothing) (Gap 84) || `Started` \| `Ended`) the clock crossed — reported by `PlannedBlock.TransitionsBetween(from, to)`, announced by `IBlockTransitionHandler` via `INotifier`; not a domain event (Gap 84) |
| **Moment** | A point in time, always UTC (`Domain/ValueObjects/Moment.cs`); no calendar members — which day or hour needs `UserTimeZone`. Only storage and `SystemClock` turn it into a `DateTime` (architecture test). Every stored timestamp, `ISystemClock.Now` and DTO times are `Moment`s (Gap 88) |
| **UserTimeZone** | The user's time zone and its DST rules; the one place a `Moment` becomes a calendar time and back (`ToCalendarTime`, `ToMoment`, `DateOf`, `SplitByDay`, `HoursPerDay(slots)` — hours of the given slots per local day, Gap 92 — and `SlotCovering(days)` — the real time calendar days cover, which repositories are handed instead of dates, Gap 97). Reached as `ISystemClock.TimeZone` (Gap 88) |
| **WholeMinutes** | Value object: an amount of time in whole minutes — the unit focus is counted in (credit, deficits, redemption coverage, a planned block's length). A fraction can't be built; the only ways in from a `TimeSpan` are `RoundDown` (credit — the one rounding in focus accounting) and `TryExact`. Every planned block (Focus and Background) is a whole number of minutes long (`PlannedBlock.Length`). An amount, not a place on the calendar — that is `TimeSlot` (Gap 92) |
| **SessionTimeline** | Value object: a started session's run, breaks and keep-blocking rule (`Session.Timeline`, worked out, never stored). `ReadingAt(at)` → `TimelineReading` (time left, progress, current break and its time left, breaks taken, blocking lifted) is the one answer to "where is the session now" — `Session`'s break questions, `PolicyEngine` (through them) and the Main page all read it; `Marks` place the breaks on the progress bar. Carried on `SessionDto` (Gap 92) |
| **TimeSlot** | Value object: a span of two `Moment`s, half-open `[Start, End)`, end after start by construction; `Start`/`End` are Domain-only (`internal`). Answers `Overlaps`, `Contains`, `HasStarted`/`HasEnded`, `Length`, `SinceStart`/`RemainingAt`/`ProgressAt`, `Covering(start, WholeMinutes)` (exactly that long); `TimeSlot.Filters` are the same rules as SQL for repositories. Held by blocks (`Scheduled`) and sessions (Gap 88) |
| **BlockPlacementChecker** | Domain service: the one answer to "may a block go here?" — `Check(slot, type, membership, contractsInEffect, blocks, running, now, timeZone)` (`running`: the one running session or none, Gap 92) asks, in order, not in the past → allowed by the governing contract → free for Focus (`CheckFree`: no Focus block's or running session's reserved time). Which contract governs: a member block's own, whatever its role (Gap 106); otherwise the one whose planned period the slot touches (`GoverningContract`). Returns a `PlacementVerdict` (`PlacementOutcome` `Allowed`/`InThePast`/`OutsideContractHours`/`TimeTaken`, the slot in the way, and the governing contract when allowed). Asked by Create/Update (via `GetBlockPlacementVerdictQuery`), Split, Seal and Edit; worded once in `BlockPlacementVerdictDto.From` (Gaps 88, 97) |
| **Contract Membership** | Value object on a block: which Nen Contract it belongs to and its role — Committed (drafted and sealed), Redemption (placed to cover a deficit) or Bonus (placed on a contract in effect for any other reason). Stored, set once, made only by the Domain, and changed only through contract actions — never by a drag (Gap 106); the role is Domain-only, read through `CountsTowardFloor` / `IsCommitted` / `IsRedemption` / `IsBonus`. A Focus block's Planner tile shows its role as a tag (COMMITTED / REDEMPTION / BONUS). No membership = a plain block (Gap 97) |
| **Contract Rules** | Value object (`ContractRules`): every contract setting that carries no dates — strictness, a fixed daily floor (whole hours 1–24, or none — refused otherwise, Gap 92), allow cancellation, keep blocking during breaks, redemption placement, and whether Bonus blocks follow the strictness. A `ContractPreset` and a sealed `NenContract` each hold their own copy. It works out the daily floor rule and the movement range; what needs the calendar stays on the contract (Gap 93) |
| **BlockChangeChecker** | Domain service: the one answer to "may this block be changed or deleted?" — `CheckReschedule`, `CheckSplit`, `CheckDelete` return a `ChangeVerdict` (allowed, or the first reason not), `AllowancesFor` the ready-made flags (`BlockChangeAllowances`) the block DTO carries. It asks the block's own lock, then its contract, and holds no rule itself; a plain block moved where any contract in effect governs is `WouldJoinContract` (Gap 106). Asked by the Update, Split and Delete validators before placement; worded once in `BlockChangeVerdictDto.From` (Gap 93) |
| **Bound block** | A block its contract holds to what was sealed: Committed and Redemption always; Bonus only when the contract's rules say Bonus blocks follow the strictness (off by default). A bound block moves as far as the strictness allows, keeps its preset, can be split (not under Lockdown) and is never deleted. `NenContract.Binds` (Gap 93) |
| **Started-block lock** | A block that has started (`PlannedBlock.HasStarted`) can't be moved, resized, split or edited, and can't be deleted until it has ended; it ends early only through End Early or by breaking its contract (Gap 88 phase 0) |
| **Blocked Attempt** | An append-only fact recording a user's attempt to access a blocked app or site during a Session |
| **Nen Contract** | A sealed, time-bound commitment over a contiguous date range that governs allowed changes to the Focus/Background blocks within it; own aggregate root, does not own `PlannedBlock`s |
| **Seal** | The act of finalising a Nen Contract; the only way a `NenContract` comes into existence — there is no persisted draft state in the domain |
| **Strictness Rule** | Enum (`Locked` \| `DailyFlexibility` \| `PeriodTimeBudget`) — how far a block its contract binds may move: nowhere, within its own day, anywhere in the contract. Asked in one place, `ContractRules.MovementRange` (Gap 93) |
| **Daily Floor** | The minimum Focus hours a day must be *planned* with in a Nen Contract — a planning minimum, checked at Seal and on every change to a Committed block, never what decides "met" (Gap 95). Focus-only |
| **Change Window** | *Not built.* The idea of limiting *when* a contract's existing blocks may be changed (a daily window with a use limit, or e.g. "change the coming week on Sunday"). Parked as Open Gap 109; the unused `ChangeWindow` type was deleted in Gap 92 |
| **Day Met** | A Nen Contract day is "met" if its completed Focus hours reach what it owes: its Committed blocks' planned hours plus what the redemptions on it cover (`NenContract.DayStatuses`, Gap 95). Owing nothing is a rest day. A missed or late block does not fail the day directly; it only costs hours that must be made up |
| **FocusBlockOutcome** | Enum (`CompletedOnTime` \| `StartedLate` \| `Missed` \| `EndedEarly`) classifying how a single Focus block resolved; a per-block display concept, separate from the day verdict |
| **Broken** | A Nen Contract the user cancelled before its end date. Only possible when it was sealed with Allow Cancellation on and no session is running (`NenContract.CheckBreak`; the Planner hides Cancel otherwise) |
| **Work Hours** | A daily time-of-day window defining where Focus/Background blocks may physically exist on the calendar; blocks cannot be placed, moved, or resized outside it. Distinct from Change Window (which gates *when edits are allowed*, not *where blocks can be*) |
| **Contract Day** | One day of a Nen Contract: its local window (Work Hours, or the whole day) and its floor (none = rest day). A contract is an unbroken run of them; `StartsAt`/`EndsAt` are read off the first and last day (Gap 65) |
| **Contract Preset** | A saved, reusable template of Nen Contract rules (strictness, daily floor, work hours, allow-cancellation) — a full Domain `AggregateRoot`, not just a saved configuration row. Authored and managed on the Presets page's "Contract Presets" tab; the Contract Wizard only picks one (applied as-is, no editing mid-wizard) — see the App Structure table below |
| **Simple Contract** | A quick-seal path in the Planner wizard: pick a named preset (Lockdown, Daily Flex, Period Study), schedule blocks, seal. No tuning knobs — the floor is derived from planned hours per day |
| **Advanced Contract** | The full-configuration path: every lever exposed (strictness, work hours, custom daily floor, rest days, allow-cancellation). User can save the configuration as a Contract Preset for reuse |
| **Redemption Block** | A Focus Block placed (through Cover Deficit, `CoverDeficitCommand` — the only path) inside the contract whose deficit it covers, for missed/late Focus Blocks of the same preset; its role is Redemption for good, and its `Receipt` says what it covers — set once at placement, kept on a move, divided on a split (Gaps 97, 106) |
| **Bonus Block** | A block placed on a contract in effect that was neither drafted nor redeeming. It belongs to the contract (fits its Work Hours, keeps its break rule) but never counts toward the floor; a Focus one earns Bonus hours. Free by default — it can be moved inside the period, re-preset and deleted until it starts — and bound like a Committed block when the contract's rules say so. It never moves out of the contract's period; it leaves only when the contract releases it. A plain block never becomes one by being dragged in — only by being created inside (Gaps 97, 93, 106) |
| **RedemptionEntry** | A value object tying a redemption block to one missed block: `{ MissedBlockId, Covered }`, whole minutes above zero (Gap 92) |
| **RedemptionReceipt** | Value object: a redemption block's promise to pay — its `RedemptionEntry` list, each missed block once; owns `Total`, `CoveredFor(missedBlockId)` and `SplitAt(firstLength)` (time order, for a split). Held by every Focus block (`None` unless redeeming); written only by the Domain. The deficit is the missed block's debt (worked out); the receipt is fixed (Gap 106) |
| **Deficit** | The gap between a missed or late Focus Block's scheduled duration and actual completed minutes; tracked per missed block, not per preset |
| **Enforcement** | OS-level mechanisms (process kill, hosts file, registry, extension) that apply a Policy Slice |
| **Post-Session** | The mandatory screen shown after a Session ends; displays stats, blocked attempts, and an optional reflection |
| **Post-Contract** | The one-time recap screen shown after a Nen Contract finishes (`Completed` or `Broken`); mirrors Post-Session — date range, committed-vs-completed hours, Bonus hours, completion grade, per-block outcome breakdown, day-strip, optional reflection. (Gap 59) |
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

**Lifecycle:** `NotStarted → Active → Completed`, with a second terminal branch `NotStarted → Missed` reachable only by a Planned session whose originating Focus Block's window closed before the Ritual completed.

**Ubiquitous language rules:**
- A Session is *started*, not *created* or *opened*.
- A Session is *completed* or *auto-ended*, not *closed* or *finished*.
- A Session cannot be paused. It is *on break* only while the clock is inside one of its break slots — it is never *put* on break.
- A Session is *missed*, never *failed* or *expired* — "missed" is the only vocabulary for a Planned session whose window closed unfulfilled.

**Break behaviour:** A session's breaks are decided when it starts and stored as its `BreakSchedule` (Gap 63); during a break slot a break countdown replaces the session timer on the Main Page while the session itself keeps running. There are no break buttons — ending or skipping a break is parked in Open Gap P. "No timer" means an empty schedule. What a break does to blocking is the session's own answer, `Session.LiftsBlockingAt(at)`: inside a break slot, unless the session keeps blocking during breaks (copied at Start from its contract, Gap 64).

**Ending early:** Currently the user can end a session before the timer runs out. A future "Strict Mode" setting will prevent this.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `Session` entity (`Entities/Session.cs`) | Owns all state transitions via `Start(startedAt, scheduledEnd?, breakSchedule?, breakPresetName?, keepBlockingDuringBreaks)`, `Complete()`, `Miss()`; carries `Origin (SessionOrigin)` and `OriginatingBlockId (Guid?)`. Break questions: `IsOnBreakAt(at)`, `LiftsBlockingAt(at)` (in a break slot and not `KeepBlockingDuringBreaks`, Gap 64), `CurrentBreakAt(at)`, `BreakTransitionsBetween(from, to)`, `BreaksTakenBy(at)` / `BreaksTaken`. `RunLength(startedAt, scheduledEnd?)` is the one definition of how long it will run; `CountedTime(originatingBlock)` the one definition of how long an ended session counted for — its block's credit when planned, else the whole minutes it ran (Gap 95). `Complete` caps `EndedAt` at `EndAt` and trims the schedule (Gap 63). |
| Domain | `SessionStatus` enum | Valid states: `NotStarted`, `Active`, `Completed`, `Missed` |
| Domain | `SessionOrigin` enum | Valid values: `Spontaneous`, `Planned`; tags every Session with how it was initiated |
| Domain | `SessionGoal`, `SessionDuration` value objects, `BreakSchedule`, `BreakPresetName (string?)` | Immutable configuration; the schedule and preset-name snapshot are set at Start (see Break Timer Preset & Break Schedule below) |
| Application | `ISessionRepository` | Persistence contract; `GetActiveAsync()` is the one door for "which session is running" (none or one — "is one running" is `is not null`, Gap 92) and the startup rehydration entry point; `GetByOriginatingBlockIdAsync()` (`is not null`) makes missed-session creation idempotent. Unique indexes back both: at most one Active session, one session per Focus block (Gap 92) |
| Application | `IActiveSessionState` | Singleton: the app's view of the running session (`ActiveSession`, `SessionChanged`, `RefreshAsync()`). Read from the database through `GetActiveSessionQuery` — the one `SessionDto` builder — never pushed in; refreshed at startup and first in the session started/ended event handlers (Gap 92, A3) |
| Application | `IRitualPrompt` | Singleton: which Focus block awaits its Ritual (`PendingFocusBlockId`, `Changed` — only when the answer changes — and `RefreshAsync(at)`, asking `GetFocusBlockAwaitingRitualQuery`). Nothing else: why a block stopped awaiting is not its business. The heartbeat calls it last in each tick, with the tick's one `now` (Gap 92, A3) |
| Application | `IRecapRelay` | Interim: the post-session and post-contract relays (`PostSessionsReady`, `PostSessionAcknowledged`, `PostContractsReady` and their `Notify…`), waiting for Gap 107's owner |
| Application | `IActiveSessionMonitor` | Singleton countdown engine; owns the timer; each tick asks the session's `SessionTimeline` and pushes the one `TimelineReading` with `Tick` (also `Current`) — 3 members (Gap 92, A4), auto-ends via `EndSessionCommandHandler`. Its fire-and-forget calls are wrapped in try/catch + `ILogger` (Gap 31). |
| Application | `StartSessionCommandHandler` | Creates, starts and saves the session; its `SessionStartedEvent` is published after the commit. Pushes nothing to the UI (Gap 92, A3). Rejects starting against an originating block via `PlannedBlock.HasEnded(now)`, the one canonical "is this past" definition (Gap 32). |
| Application | `EndSessionCommandHandler` | Calls `session.Complete()`, saves, dispatches `session.DomainEvents`; enforcement removal and notification are handled by `SessionEndedEventHandler`. For a planned session it always calls `focusBlock.MarkSessionEnded(endedAt)`, early or on time, so the block's `SessionEndedAt` is a faithful copy of `Session.EndedAt` (Gap 86) — a non-destructive stamp; `block.End` is never overwritten (Gap 22). See `FocusBlockOutcome` below for how `SessionEndedAt` feeds classification. |
| Application | `SessionStartedEventHandler` | Refreshes `IActiveSessionState`, calls `IEnforcementSynchronizer.SyncAsync(now)`, reports `SessionStarted` to `INotifier` (a planned session is named after its block, Gap 84) |
| Application | `SessionEndedEventHandler` | Refreshes `IActiveSessionState` (now none), then `SyncAsync(now)` (never "remove everything" — a still-running Background Block stays enforced, Gap 62), reports `SessionCompleted` — the one toast for a planned block's end, since the session owns it (Gap 84) |
| Application | `IBreakTransitionResponder` / `BreakTransitionResponder` (`Services/`) | Given the break boundaries the monitor saw crossed: one `SyncAsync(at)` (`PolicyEngine` decides what the break lifts), then one `BreakStarted`/`BreakEnded` fact for the latest boundary only (Gap 63) |
| Application | `INotifier` / `Notifier` (`Services/`), `NenNotification` (`Notifications/`) | The one door to `INotificationService` (a bare toaster): sources report a typed fact, `Notifier` owns every title and body. A test fails if anything else takes the toaster (Gap 84) |
| UI | `MainPageViewModel` | Observer only — reads from `IActiveSessionState` and `IActiveSessionMonitor`, dispatches commands. "Use Nen" button is hidden while a session is active. |

---

## Missed Session

**Definition:** A first-class Session, counted, filtered, and timelined in Insights alongside real sessions, produced when a planned Focus Block's scheduled window closes with the Ritual never completed. This reopens the Session aggregate: a Session is now "a focus attempt, which may have succeeded or been missed," not only "a period that ran." A missed session is essentially a planned session **frozen at "never started"** rather than progressing to `Completed` — reached via a direct `NotStarted → Missed` transition, never through `Active`.

**Only a Planned session can be missed.** A Spontaneous session is user-initiated with no scheduled commitment behind it — there is nothing to miss. `Session.Miss()` throws if `Origin != SessionOrigin.Planned`; this is defense-in-depth rather than a reachable case, since the creation path for a missed session (`MarkFocusBlockMissedCommand`) only ever originates from a `FocusBlockId`, never from an existing Spontaneous session.

**What it HAS**, inherited from the originating `FocusBlock`: a `BlockPresetId`, a `SessionDuration` (the block's scheduled length), and a scheduled `Start`/`End` — populated onto `StartedAt`/`EndedAt` at miss-time so it appears in date-range history queries exactly like a real session (`ISessionRepository.GetHistoryAsync` filters on `StartedAt`; a session that never populated it would be silently invisible to history).

**What it LACKS**, because it never ran: break settings, temporary blocked apps/sites (both simply omitted at creation — `Session.Create`'s existing optional parameters already default to `null`/`[]`), and any real elapsed runtime. Its `Goal` is the fixed placeholder `"(missed — no goal set)"` — a normal, valid `SessionGoal` (no user ever reached Zetsu to write one; validation is not loosened for this path).

**Zero completed hours, by construction:** `Session.GetElapsedTime` returns `TimeSpan.Zero` whenever `Status == Missed`, checked *before* the `StartedAt`-null check — because a missed session's `StartedAt`/`EndedAt` hold the *scheduled* window, not real elapsed time, and a naive `EndedAt − StartedAt` would wrongly report the full scheduled duration as "completed." Centralizing this on the entity (rather than in every caller that computes "completed hours") means the contract recap, `GetSessionHistoryQuery`, and any future Insights analytics are all correct for free. This is a single-entity fact ("a session that never ran has zero elapsed time"), distinct from the separate principle that *aggregating* hours across days belongs in the Application layer.

**Reflection still applies:** `SetReflection` (and `SaveReflectionCommandHandler`) accept `Completed` or `Missed` — the missed post-session screen captures a "why did I miss this?" reflection the same way a real post-session screen captures one. There is no rating field on either.

**Blocked attempts are always zero on a missed session — a known, accepted gap.** `BlockedAttempt` rows key off `SessionId`, and tracking only fires once a Session exists; during a Focus Block's active window before (or absent) Ritual completion, enforcement runs but there is no `SessionId` yet to attach an attempt to. Fixing this would mean tracking attempts against `FocusBlockId` during that window — separate future Infrastructure work, out of scope here.

**The originating block is untouched.** `FocusBlock` is not deleted or altered when its session is marked missed — it remains the schedule record (`RitualCompletedAt` stays `null`), while the Session is the attempt record. Both already agree: `FocusBlock.Outcome` independently derives `Missed` from the same `RitualCompletedAt == null` fact once the scheduled end has passed.

**Idempotent creation:** `MarkFocusBlockMissedCommand` can safely fire more than once for the same block (the expected failure mode for a heartbeat-triggered command, not a hypothetical) — `ISessionRepository.GetByOriginatingBlockIdAsync` guards against creating a second missed session for a block that already has one.

**Not built here:** any Insights UI. This amendment ensures a missed session is modeled correctly and is queryable/representable for when that UI is built.

**One home for "missed": the sweep (`SweepMissedFocusBlocksCommand`), at startup and on every heartbeat tick** (Gap 92, A3, D′). A block's window can close unfulfilled while the app is closed or while it runs — with or without its Ritual on screen. The sweep queries `IPlannedBlockRepository.GetUnresolvedFocusBlocksAsync(asOf)` and calls `MarkFocusBlockMissedCommand` for each, oldest first.

- **Startup:** `Program.Main` dispatches it once, after `IActiveSessionState.RefreshAsync()`. Nothing is announced: the missed sessions are persisted with `PostSessionAcknowledgedAt == null`, and `ShellViewModel` finds them at its own startup (see below).
- **While running:** the heartbeat sweeps first in each tick and passes what it found to `IRecapRelay.NotifyPostSessionsReady`; the Shell queues the missed screens. Last in the same tick, with the same `now`, `IRitualPrompt` asks which block awaits its Ritual. The Shell's Ritual follows the prompt: it closes when the prompt stops naming its block — whether the window closed or the session started — and the missed screen already queued shows next. Back-to-back blocks therefore show the previous miss first, then the next Ritual. The Shell never marks a block missed itself.

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
| Application | `MarkFocusBlockMissedCommand` / `CommandHandler` / `Result` / `Validator` (`UseCases/Sessions/MarkFocusBlockMissed/`) | Loads the `FocusBlock`, builds the Session via `Create(...)` + `Miss(block.Start, block.End)`, saves, and builds the `PostSessionDto` (preset name lookup; blocked-attempt dictionaries left empty since a missed session never tracked any). Validator rejects a missing/wrong-type block, an already-completed ritual, or a block that already has a session. No `SessionMissedEventHandler` is registered — `SessionMissedEvent` stays unhandled; live surfacing goes through `IRecapRelay.NotifyPostSessionsReady` instead (Gap 21; the relay's owner is Gap 107's question). Called only by the sweep (Gap 92). |
| Application | `IPlannedBlockRepository.GetUnresolvedFocusBlocksAsync(DateTime asOf)` | Focus blocks with `End < asOf && RitualCompletedAt == null`, ordered by `Start` |
| Application | `SweepMissedFocusBlocksCommand` / `Result` / `CommandHandler` (`UseCases/Sessions/SweepMissedFocusBlocks/`) | Optional `AsOf (DateTime?)` — falls back to `ISystemClock` when omitted; a caller that already resolved "now" for a sibling check must pass it (Gap 21). Queries unresolved blocks, calls `MarkFocusBlockMissedCommand` per block, collects the resulting `PostSessionDto`s. Two call sites: `Program.Main` (startup, no `AsOf`, no event — relies on `GetUnacknowledgedPostSessionsQuery`'s catch-up) and `ScheduledEnforcementService`'s heartbeat (every tick, `AsOf` = the tick's one `now`, result → `NotifyPostSessionsReady`). |
| Application | `IRecapRelay.PostSessionsReady (EventHandler<IReadOnlyList<PostSessionDto>>)` / `NotifyPostSessionsReady(...)` | Interim bridge (Gap 92 → Gap 107). No-ops on an empty list. `ShellViewModel` enqueues each into `_pendingOverlays`, with the usual triage batching. |
| Application | `IRecapRelay.PostSessionAcknowledged (EventHandler)` / `NotifyPostSessionAcknowledged()` | Raised from `ShellViewModel.ExitPostSession()`. `PlannerPageViewModel` subscribes and reloads its current week (Gap 21). |
| Application | `AcknowledgePostSessionCommand` / `CommandHandler` / `Result` (`UseCases/Sessions/AcknowledgePostSession/`) | Loads the session by id, requires `Status` to be `Completed` or `Missed`, calls `MarkPostSessionAcknowledged(now)`, saves. Called unconditionally from `PostSessionViewModel.Done()`, alongside the existing conditional `SaveReflectionCommand` call |
| Application | `ISessionRepository.GetUnacknowledgedPostSessionsAsync` | `Completed`/`Missed` sessions with `PostSessionAcknowledgedAt == null`, ordered by `EndedAt` ascending |
| Application | `GetUnacknowledgedPostSessionsQuery` / `Handler` (`Queries/GetUnacknowledgedPostSessions/`) | Composes the repository call above with preset-name resolution and the originating block, then `PostSessionDto.From` — the one builder, shared with `EndSession` and `MarkFocusBlockMissed` (Gap 95) — rebuilding an equivalent `PostSessionDto` for a session resolved in a previous app run |
| Application | `PostSessionDto.Status` | Mirrors `Session.Status`; drives Post-Session screen copy |
| Application | `IRitualPrompt.Changed (EventHandler<Guid?>)` | The block now awaiting its Ritual, or null; raised only when that changes. Carries no "released" or "handed-off" block — `RitualRequirementChange` and its ids were deleted in Gap 92 (D′) |
| UI | `ShellViewModel.InitializePendingOverlaysAsync()` | Constructor-fired, fire-and-forget: awaits `GetUnacknowledgedPostSessionsQuery`, enqueues each result as `PendingPostSession` (already chronological), then a `PendingRitual` for the block `IRitualPrompt` names (if no session runs), then calls `TryDrainQueue()` once. A queued `PendingRitual` opens only if the prompt still names its block when it comes up |
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
| **Hatsu** | Output | "Put it into action" | **Spontaneous:** User selects session duration (5 min – 8 hours) and optionally picks a Break Timer Preset (or "No timer"). **Planned:** Duration is pre-determined by the Focus Block's scheduled end time (displayed as "active until [time]"); user can only pick a Break Timer Preset. User clicks "Start Session." |

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
- A locked preset can still be **updated**, but only toward being *at least as strict* (Gap 8; direction fixed in Gap 30 — see `BlockPreset.IsAtLeastAsStrict`): `BlockedApps` add-only. `Websites` add-only under `Blocklist`, but **remove-only** under `Allowlist` (removing an allowed site is the stricter direction). `Name`, `WebsiteFilterType`, `PresetType`, and `Tags` must all stay unchanged while locked. You can strengthen a live preset; you cannot weaken, rename, or retype it while it's committed.
- A preset is *locked* — fully, not just add-only — for `DeleteBlockPresetCommand`; deletion is never allowed while in use, regardless of what would be deleted.

**UI display:** On the Block Preset page, Focus and Background presets are displayed together in a single list with a type badge (blue = Focus, green = Background). A filter bar lets the user show All, Focus only, or Background only.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BlockPreset` aggregate (`Entities/BlockPreset.cs`) | Owns the list of `BlockedApp` and `BlockedSite` value objects, the `WebsiteFilterType`, and the preset type (`BlockPresetType.Focus` or `BlockPresetType.Background`). `IsAtLeastAsStrict(IReadOnlySet<string> proposedApps, IReadOnlySet<string> proposedSites) -> bool` answers "does this change keep me at least as strict" — apps add-only, sites add-only under `Blocklist`/remove-only under `Allowlist` (Gap 30). |
| Domain | `BlockedApp`, `BlockedSite` value objects | Immutable; validated at construction. Always plain hard blocks — no budget/ceiling concept lives on either; see `UsageCeiling` for ambient access limits |
| Domain | `WebsiteFilterType` enum | `Blocklist` or `Allowlist` |
| Application | `IBlockPresetRepository` | CRUD persistence contract |
| Application | `CreateBlockPresetCommandHandler`, `UpdateBlockPresetCommandHandler`, etc. | One handler per mutation |
| Application | `UpdateBlockPresetValidator` | Checks lock state via `ISessionRepository.GetActiveAsync` + `IPlannedBlockRepository.HasFutureBlocksForPresetAsync` (same two checks `DeleteBlockPresetValidator` uses, kept as separate inline checks rather than extracted — no third caller yet to justify a shared abstraction). If locked, fetches the current `BlockPreset` and applies the private `IsAddOnlyChange` comparison: `Name`/`WebsiteFilterType`/`PresetType`/`Tags` must be identical, and `current.IsAtLeastAsStrict(proposedApps, proposedSites)` must hold (Gap 30). Not locked → any change passes. |
| UI | `BlockPresetPageViewModel` | Full CRUD UI; file picker for app selection; type filter |

---

## Daily Usage / Usage Ceiling / Recurring Blocks *(not yet built)*

Three related but independent not-yet-built features — universal per-resource usage tracking, an ambient perpetual access cap, and repeating calendar blocks. `DailyUsage`, `UsageResourceType`, `UsageCeiling`, `CeilingPeriod`, `RecurrenceRule`, `RecurringSeries`, and `SkippedOccurrence` are all defined in the Quick Reference table above. Full domain shape, precedence rules, and interrogation record live in `OPEN_GAPS.md` → Open Gap A (Daily Usage / Usage Ceiling) and Open Gap E (Recurring Blocks) — not duplicated here.

---

## Enforcement

**Definition:** The active OS-level mechanisms that prevent access to blocked applications and websites during a Session or Planned Block. Enforcement is a *side-effect* of a Session or Block being active — it is not a domain entity.

**Lifecycle:** Kept in sync with `PolicyEngine` by `IEnforcementSynchronizer` — instantly on session start/end (event handlers) and at each break boundary (`BreakTransitionResponder`, Gap 63) — each calls `SyncAsync`, and every 5s by the heartbeat as a backstop and to catch blocks starting/ending on the clock. On break, only the on-break session's own preset is lifted (with its originating block's), and only if `Session.LiftsBlockingAt(at)` — a session whose contract keeps blocking during breaks stays blocked; every other active block is untouched (Gaps 62, 64).

**Mechanisms (in order of application):**
1. **Process kill loop** — kills matching process trees every 2 seconds
2. **Hosts file** — writes `0.0.0.0` / `::` entries between `# NEN_FOCUS_START` markers
3. **DNS flush** — `ipconfig /flushdns` after each hosts write
4. **Registry** — disables DoH (`BuiltInDnsClientEnabled = 0`) and sets `DnsOverHttpsMode = "off"` in Chrome, Edge, Brave
5. **Browser extension** — intercepts navigations via `chrome.webNavigation.onBeforeNavigate`, queries the local HTTP API at `localhost:5005/check`, redirects blocked sites to `blocked.html`

**Known gaps:**
- The `/report` endpoint is implemented: when the extension reports a blocked navigation, `WindowsEnforcementService` calls `IBlockedAttemptTracker.RecordWebBlockedAsync` if an active session exists. Background Block attempts (no `ActiveSessionId`) are still not tracked.
- Allowlist mode is handled by the `/check` endpoint: in Allowlist mode the endpoint returns `blocked: true` for any domain NOT in `AllowedSites`. Note that the hosts-file path cannot enforce allowlist mode (you cannot enumerate all domains); the browser extension is the sole allowlist enforcement mechanism.
- Blocked attempts (app and website) are only tracked while a Session is active; Background Block attempts are future work — see Blocked Attempt below.
- Usage Ceiling enforcement is not yet implemented. `PolicyEngine` does not yet read `DailyUsage`. The `/report-time` endpoint does not yet exist. See `ARCHITECT_GOVERNANCE.md` → Open Gap A.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `IEnforcementService` | Contract for applying and removing enforcement; accepts `ActivePolicySlice` (Domain Value Object). Called only by `EnforcementSynchronizer` — no exceptions since Gap 101 (guarded by `SideEffectRuleTests`). |
| Application | `IEnforcementSynchronizer` / `EnforcementSynchronizer` (`Interfaces/`, `Services/`) | `SyncAsync(DateTime at) -> ActivePolicySlice`: computes the slice via `IQueryDispatcher`, removes enforcement if empty else applies it, as one locked step so a stale slice can never overwrite a fresh one. Singleton. Callers: the heartbeat, the session start/end event handlers (Gap 62), `BreakTransitionResponder` (Gap 63), and the app's shutdown (Gap 101: closing leaves blocking as `PolicyEngine` says for that moment; the close waits up to 2 s for the sync, and on failure leaves blocking as it was). |
| Domain | `PolicyEngine` (`Services/`) | Computes `ActivePolicySlice` from raw domain entities; sole owner of all merge logic |
| Application | `ComputeActivePolicySliceQueryHandler` | Orchestrates data fetching across three repositories; delegates computation to `PolicyEngine` |
| Infrastructure | `WindowsEnforcementService` | Consumes `ActivePolicySlice` directly; owns hosts file, process kill loop, registry writes, local HTTP API (`localhost:5005`) — `/check` (is this domain blocked) and `/report` (records a web blocked attempt when a session is active). |
| Infrastructure | `ScheduledEnforcementService` | `BackgroundService` heartbeat (5s), no rules of its own. Each tick, one `now` for everything: sweeps missed blocks, calls `SyncAsync(now)`, passes (last tick, now] to `IBlockMarkerNotifier` (block markers passed → `INotifier`, Gap 84), then, last, `IRitualPrompt.RefreshAsync(now)` (Gap 92) |
| Extension | `background.js` | Intercepts browser navigation; queries `/check`; calls `/report` on a real blocked navigation |

---

## Policy Slice

**Definition:** The computed, merged enforcement state at a given instant in time. It is the *answer* to the question "what should be blocked right now?" — derived from all currently active Sessions and Planned Blocks. Represented as `ActivePolicySlice`, a **Domain Value Object** in `NenFocus.Domain/ValueObjects/`, not a DTO.

**Computation rules** (all logic lives in `PolicyEngine.ComputeSlice()`):
- **Apps:** union of all blocked apps across all active block presets, always a blocklist. Includes `TemporaryBlockedApps` from the active `Session` entity.
- **Websites:** if *any* active preset uses `Allowlist` mode → default-deny, allowed = intersection of all allowlists minus any blocklist entries. Otherwise → block the union of all blocklisted sites. Includes `TemporaryBlockedSites` from the active `Session` entity.
- **Usage Ceilings *(not yet implemented)*:** In a future slice, `PolicyEngine.ComputeSlice()` will read `DailyUsage` against active `UsageCeiling` entries. Sites and apps whose ceiling has been exhausted for the current period (day or week) will be added to the blocked set — unless a hard-blocking preset already covers them, in which case the hard block wins and no usage accrues. See `ARCHITECT_GOVERNANCE.md` → Open Gap A.
- An `ActivePolicySlice` is considered "empty" only in `Blocklist` mode with no blocked apps or sites. It is *never* empty in `Allowlist` mode.
- **"Active" is re-checked precisely inside `ComputeSlice` itself:** `ComputeActivePolicySliceQueryHandler` over-fetches blocks with a ±1-second buffer around `at` (SQLite datetime-comparison slop), so `PolicyEngine` filters through `PlannedBlock.IsActive(at)` as the final authority (Gap 21, third addendum). `IsActive` is `atTime >= Start && atTime < EffectiveEnd(atTime)`, not raw `End` — a block whose session ended early stops being active when it actually ended (see `MODULE_STANDARDS.md` → Application Handler Forbidden Zone).

**Overlap rules:**
- Multiple Background Blocks can overlap freely. The net enforcement is the union of all their presets.
- A Focus Block can overlap with Background Blocks. The net enforcement is the union of all active presets. The Ritual is still triggered.
- Two Focus Blocks cannot overlap. The Planner prevents scheduling conflicts. A Spontaneous session cannot be started if it would overlap with an upcoming Focus Block.

**The slice answers only "what to block."** Whether a Focus Block should push the user into the Ritual is a separate question with its own home: `FocusBlock.IsAwaitingRitual(at)` + `GetFocusBlockAwaitingRitualQuery` (see Focus Block below). The slice used to carry `ActiveBlockPresetType`/`ActiveFocusBlockId`/`FocusSessionCompleted` for that purpose — removed in Gap 62.

**Source of Truth flow:** `Database → PolicyEngine (Domain) → ActivePolicySlice (VO) → EnforcementSynchronizer → WindowsEnforcementService (OS)`

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `ActivePolicySlice` (`ValueObjects/ActivePolicySlice.cs`) | Immutable Domain Value Object; the canonical enforcement contract. Carries `BlockedApps`, `BlockedSites`, `AllowedSites`, `WebsiteFilterTypeMode`, `ActiveSessionId`, `SliceStart`/`SliceEnd`, `IsEmpty` — blocking only, no Ritual fields (Gap 62). |
| Domain | `PolicyEngine` (`Services/`) | Stateless domain service; accepts raw `BlockPreset`, `Session`, and `PlannedBlock` collections and produces `ActivePolicySlice`. Owns all merge rules — no merge logic exists anywhere else. |
| Application | `ComputeActivePolicySliceQueryHandler` (`Queries/ComputeActivePolicySlice/`) | Pure orchestrator: fetches entities from three repositories, loads `BlockPreset` aggregates, delegates to `PolicyEngine.ComputeSlice()`. Returns the `ActivePolicySlice` to callers. |
| Application | `EnforcementSynchronizer` | The only consumer that turns a slice into an apply/remove call |
| Infrastructure | `WindowsEnforcementService` | Consumes `ActivePolicySlice` directly (no DTO) to drive OS-level mechanisms |

---

## PolicyEngine

**Definition:** The sole Domain service responsible for computing the `ActivePolicySlice`. It is a stateless, synchronous function: given a point in time, a list of `BlockPreset` aggregates, a list of active `Session` entities, and a list of active `PlannedBlock` entities, it returns the complete enforcement state for that moment.

**Why it is a Domain Service (not an Application service):** Policy merge logic — the rules for unioning blocked apps, resolving allowlist/blocklist precedence, and deciding what a break lifts — is domain logic. It expresses invariants of the domain ("an allowlist preset beats a blocklist preset", "a session whose contract keeps blocking during breaks stays blocked through them") that must be consistent wherever policy is computed. Placing this logic in a Domain service makes it unit-testable without a database and reachable by any future consumer (e.g., a Planner "preview" query).

**Contract** (a sealed class, no interface — Gap 111):
```csharp
// NenFocus.Domain/Services/PolicyEngine.cs
public ActivePolicySlice ComputeSlice(
    Moment at,
    IEnumerable<BlockPreset> presets,
    Session? running,             // at most one runs (Gap 92)
    IEnumerable<PlannedBlock> blocks);
```

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `PolicyEngine` (`Services/PolicyEngine.cs`) | Sealed class, no interface (Gap 111); all merge rules live here. Filters `blocks` through `PlannedBlock.IsActive(at)` (`EffectiveEnd`-based) — the caller's fetch may be loose/buffered; `PolicyEngine` is the precise final word on "is this block active right now" (Gap 21). Derives `activePresets` from the active blocks'/sessions' own `BlockPresetId`s rather than trusting the caller's preset list (Gap 60). Resolves what a break lifts by asking each session `LiftsBlockingAt(at)` — the session already carries its contract's keep-blocking rule, so `PolicyEngine` takes no contracts (Gap 64). Only the specific session on break and its originating block lose their preset's contribution; a co-occurring Background Block is untouched. |
| Application | `ComputeActivePolicySliceQueryHandler` | Only caller; fetches entities, then delegates to `_policyEngine.ComputeSlice()` |
| Application | `DependencyInjection.cs` | Registers `PolicyEngine` as itself (`AddScoped`, in `NenFocus.Application`'s `DependencyInjection.cs` — stateless, so singleton would also be safe), like every Domain service |

---

## Planned Block

**Definition:** The abstract base class for all calendar-scheduled enforcement entries. A `PlannedBlock` has a `Scheduled` time slot (`TimeSlot`, UTC moments), a `BlockPresetId`, and an optional `Title`. It is never instantiated directly — only its two concrete subtypes (`FocusBlock` and `BackgroundBlock`) are created. The C# class type encodes the block preset type; there is no stored `BlockPresetType` column.

**Domain behaviour:** `PlannedBlock` carries these domain methods, used by both subtypes and the query layer:
- `IsActive(Moment at)` — returns `true` if `at` falls within `[Start, EffectiveEnd(at))`.
- `EffectiveEnd(Moment asOf)` — the one rule for how much of the window the block really occupies; every reason it shrinks lives here. Base: cancellation (ends at the cancel moment; empty if cancelled before `Start`). `FocusBlock` adds session-ended-early and missed; the earliest reason wins. `IsActive`, overlap checks and markers never check `CancelledAt` themselves (Gap 85).
- `HasStarted(Moment asOf)` — asks `Scheduled`; once true the block is locked: no edit or split, no delete until it has ended (Gap 88 phase 0).
- `HasEnded(Moment asOf)` — asks `Scheduled` (half-open: ended *at* its end); the one canonical "is this past" check.
- `ReservedSlot(asOf)` — the Focus time it really takes (up to `EffectiveEnd`), what `BlockPlacementChecker` compares; `OverlapsWith(other)` stays for the Seal batch's own check only — placement never calls it (Gap 88).
- `MarkersPassedBetween(from, to)` — the markers the clock passed in (from, to]: `StartingSoon` (`UpcomingWarningLead`, 5 min, before `Start`), `Started`/`Ended` (`IsActive` flipped — the definition enforcement uses). `Ended` only if the block owns its ending: a `FocusBlock` whose Ritual was completed does not — its session announces it (Gap 84).

**Why it exists:** `IPlannedBlockRepository` returns `IReadOnlyList<PlannedBlock>` so that the enforcement pipeline can work with all scheduled blocks polymorphically without knowing their concrete type. Enforcement treats Focus and Background blocks alike; the only place the subtype matters for triggering is the Ritual, where `GetFocusBlockAwaitingRitualQueryHandler` asks only `FocusBlock`s — the distinction is made exactly once, in the one place that needs it.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `PlannedBlock` abstract class (`Entities/PlannedBlock.cs`) | Base for `FocusBlock` and `BackgroundBlock`; exposes `IsActive()`, `OverlapsWith()`, `MarkersPassedBetween()`, `Membership` with `Leave()` (released by its contract — Gaps 97, 93; no move changes membership, `PlaceUnder` deleted in Gap 106), its own lock `CheckOwnChange` / `CheckOwnSplit` / `CheckOwnDelete` (Gap 93), `PresetType` (read off the subtype) and `DisplayName(presetName)` (title, else preset name — the one definition of a block's name, Gap 84) |
| Application | `IPlannedBlockRepository` | Returns `IReadOnlyList<PlannedBlock>`; callers cast to concrete type when subtype behaviour is needed |
| Application | `GetFocusBlockAwaitingRitualQueryHandler` | The one place the Focus/Background distinction drives the Ritual: only `FocusBlock`s are asked `IsAwaitingRitual` |

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
| Domain | `FocusBlock` entity (`Entities/FocusBlock.cs`) | Extends `PlannedBlock`; inherits `Id`, `BlockPresetId`, `Start`, `End`, `Title?`; own fields: `RitualCompletedAt?`, `SessionEndedAt?` — Domain-only stamps (Gap 95): outside the Domain they are read only through the block's answers (`Outcome`, `StartedLate`, `EndedEarly`, `WorkSlot`, `CreditedSlot`, `IsFinal`), and repositories filter on them through `FocusBlock.Filters`; block preset type derived from C# class (no stored column). `IsAwaitingRitual(at)` — active, not cancelled, Ritual not done — is the one definition of "should this block prompt the Ritual" (Gap 62). `IsCommitted` / `IsRedemption` / `IsBonus` / `CountsTowardFloor` (its stored role in its contract; only a Redemption block may hold a receipt with entries — Gaps 97, 106), `Receipt`, `SplitInto(firstId, first, secondId, second)` (both pieces keep the membership, the receipt divided, nothing shortened — Gap 106) and `IsUnresolved(asOf)` (window closed, Ritual never done, not cancelled — the missed sweep's rule; the repository's SQL mirrors it, checked by a parity test) name two facts callers used to spell out |
| Application | `IPlannedBlockRepository` | Retrieves blocks for overlap validation and active-state queries |
| Application | `GetFocusBlockAwaitingRitualQuery` / `Handler` (`Queries/GetFocusBlockAwaitingRitual/`) | Returns the active Focus Block awaiting its Ritual, or `null` — also `null` if any session is running (checked in the database, not in-memory). What `IRitualPrompt` asks, once per heartbeat tick (Gaps 62, 92) |
| Application | `ComputeActivePolicySliceQueryHandler` | Queries blocks covering `DateTime.UtcNow`; merges with active Session |
| Application | `StartSessionCommandHandler` | Calls `FocusBlock.MarkRitualCompleted(DateTime.UtcNow)` when `OriginatingBlockId` is provided |
| UI | `PlannerPageViewModel` | Creates, updates, deletes Focus Blocks; drag interactions and form locking implemented |
| UI | `ShellViewModel` | Its Ritual follows `IRitualPrompt.Changed`: opens the Ritual overlay for the block the prompt names (queued behind any pending Post-Session screens) and closes it when the prompt stops naming that block — see Missed Session (Gap 92) |

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

## Break Timer Preset & Break Schedule

**Break Schedule — the one source of truth.** A session is one continuous run punctuated by breaks. Its breaks are a `BreakSchedule` it stores from Start: a list of slots, each an offset from `StartedAt` (Gap 63). "Is it a break right now," "how long is left of it," "which boundaries were just crossed" and "how many breaks so far" are all read off that list against the clock, never kept as separate state. Offsets rather than clock times, so a schedule exists before Start and a late planned start moves its breaks with it (the first break is always a full work stretch after the actual start). **Past and in-progress breaks never change; only future ones may be revised** (the mechanism for that is parked in Open Gap P). `Complete` trims the schedule to what actually happened, so the stored list is also the permanent history record — never recomputed from rules that could change later.

**Break Timer Preset — a shortcut, not a source.** A saved, named `BreakTimer`: Classic (25/5, 4 cycles, 15m long break), Deep Work (50/10, 3 cycles, 20m), Flow (90/20, 2 cycles, 30m) ship as ordinary seeded rows, freely creatable/editable/deletable, no locking. Authored on the Settings page (not yet built). The session never references it: at Start, `BreakTimer.ScheduleFor(runLength)` turns it into the session's `BreakSchedule`, and only its name is snapshotted (`Session.BreakPresetName`, null when the schedule has no breaks). A future hand-made schedule would be just another way to produce a `BreakSchedule`.

**Selected at Hatsu**, for Spontaneous and Planned sessions alike. Only presets whose `ScheduleFor(duration)` produces at least one break are offered; the list re-filters live as a Spontaneous session's duration wheel changes. "No timer" = empty schedule. If a preset stops fitting between Hatsu and Start (the user waited), the session still starts, with no breaks and no label.

**The cadence rule** (`BreakTimer.ScheduleFor`, the one place it lives): work → short break → ... → long break every `CyclesBeforeLongBreak` cycles → repeat. A break is only included if it runs to completion *and* leaves some work after it.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BreakTimerPreset` (`Entities/BreakTimerPreset.cs`) | Aggregate root: `Id`, `Name`, `BreakTimer`. `Update` is a wholesale replace, mirroring `ContractPreset.Update`. |
| Domain | `BreakTimer` (`ValueObjects/BreakTimer.cs`) | Validated rhythm (all positive); `ScheduleFor(TimeSpan runLength) -> BreakSchedule`. |
| Domain | `BreakSchedule` / `SessionBreak` / `BreakTransition` (`ValueObjects/`) | Schedule invariants in the constructor (ordered, work between breaks); `BreakAt`, `BreaksStartedBy`, `TransitionsBetween` (half-open `(from, to]`, backwards = none), `FitsWithin`, `TrimTo`. |
| Domain | `Session.BreakSchedule`, `Session.BreakPresetName` | Stored at Start (`Start` rejects a schedule that doesn't `FitsWithin` its `RunLength`); persisted as the `SessionBreaks` table (EF `OwnsMany` on `_breaks`). |
| Application | `IBreakTimerPresetRepository`, `CreateBreakTimerPreset`/`UpdateBreakTimerPreset`/`DeleteBreakTimerPreset` (`UseCases/BreakTimerPresets/`), `GetAllBreakTimerPresetsQuery` | Full CRUD — built, but **no UI yet** (Settings page doesn't exist). |
| Application | `GetAvailableBreakTimerPresetsQuery` / `Handler` | Given a duration, returns only presets whose schedule has at least one break. |
| Application | `StartSessionCommand.BreakTimerPresetId (Guid?)` / `StartSessionCommandHandler` | Loads the preset, builds `ScheduleFor(session.RunLength(now, blockEnd))`, starts the session with it and the preset's name. Also resolves `KeepBlockingDuringBreaks` once: the contract's rule for a planned block that belongs to one (Bonus blocks included, Gap 97), `false` otherwise (Spontaneous, plain block). |
| Application | `IBreakTransitionResponder` / `BreakTransitionResponder` | See the Session mapping above. |
| Infrastructure | `ActiveSessionMonitor` | Takes the session's `SessionTimeline` on every `SessionChanged`; each tick reads one `TimelineReading` off it and passes boundaries crossed since its last check (a cursor, its only memory) to `IBreakTransitionResponder`. |
| UI | `HatsuPageViewModel` (`AvailableBreakTimerPresets`, `BreakTimerPresetChips`, `SelectedBreakTimerPreset`) | Chip-style selector, live-filtered by duration; `BreakTimerPresetChip` pairs each preset with its own `IsSelected` flag so the View never has to compare across an `ItemsControl` boundary. |
| UI | `MainPageView.axaml.cs` (`RenderBreakMarkers`) | Builds the progress bar's tick marks in code-behind — a `ColumnDefinitions` collection can't be data-bound directly in Avalonia. |

---

## Planner Page *(implemented)*

**What it is:** The weekly calendar view where the user schedules Focus Blocks and Background Blocks. Each column is a day; rows are hour slots from a configurable `FirstHour` to `LastHour`. Tiles represent blocks and are colour-coded: gold (`#B8922A`) for Focus, light grey (`#E8E8F0`) for Background.

**Drag/resize mechanics, lock rules, Block Snapshot detail: moved to `UI_UX_STANDARDS.md` → Planner Drag Interactions (2026-09-10)** — pixel-level interaction spec, not domain vocabulary.

---

## Spontaneous Session

**Definition:** A Session created by the user clicking "Use Nen" on the Main Page and completing the full Nen Ritual (Ten → Zetsu → Ren → Hatsu), outside of any scheduled Focus Block. The user has full control: they choose the Block Preset (Focus-type only), set the duration, and pick a Break Timer Preset. The Session is created with `Origin = Spontaneous` and `OriginatingBlockId = null`. No Block entity of any kind is created.

**Product invariant:** A manual enforcement session cannot exist without a completed Ritual. This is a product invariant enforced by design, not by validation code. If the user abandons the Ritual at any step before clicking "Start Session" on the Hatsu page, no record is saved anywhere — no draft, no partial session, no recovery state.

**Conflict rule:** A Spontaneous Session cannot be started if it would overlap with an upcoming Focus Block. For example, if a Focus Block is scheduled at 18:00, the user cannot start a 30-minute session at 17:45.

**Contrast with Planned Session:** A Spontaneous Session is user-initiated and requires no pre-scheduled block. A Planned Session is triggered by a Focus Block at its scheduled start time. In a Spontaneous Session the user freely chooses the Block Preset and duration; in a Planned Session both are pre-determined.

**Technical mapping:** No Block entity — distinguished at the Application layer by `Session.Origin == Spontaneous`. `Session.OriginatingBlockId` is `null`.

---

## Planned Session

**Definition:** A Session created by the user completing the Nen Ritual after a scheduled Focus Block activated and pushed them to the Ritual overlay. The Block Preset and duration are pre-determined by the Focus Block — the user cannot change the preset (Ren step) or the end time (Hatsu step), but can pick a Break Timer Preset. The Session is saved with `Origin = Planned` and `OriginatingBlockId = <the triggering FocusBlock's Id>`. `StartSessionCommandHandler` stamps `FocusBlock.RitualCompletedAt` at this moment. If the user never completes the Ritual, no Session row is created; the `FocusBlock` row remains with `RitualCompletedAt = null` and Insights shows it as "missed." Enforcement still runs until the block's scheduled end time regardless.

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

**Current state:** Both app and website blocked attempts are tracked during Ritual sessions, and both deliberately distinguish an active attempt from a resource merely being caught by ambient enforcement when a block first activates (Gap 46) — an app/site that was already running/open when the block activated is not "attempted," only a later attempt made while the block is active is. Apps: `WindowsEnforcementService.KillBlockedProcessesImmediate(reportAttempts)` — the one-time sweep `ApplyPolicyAsync` runs when a block first activates passes `false`; the ongoing 2-second process-kill timer, which only ever catches a relaunch attempted during an active block, passes `true`. Websites: `IBlockedAttemptTracker.RecordWebBlockedAsync()` is called via the extension's `/report` endpoint, but only from `background.js`'s `onBeforeNavigate` handler (a real navigation check) — never from `poll.js`'s ambient already-open-tab poll, which only ever redirects. Background Block attempts are not yet tracked.

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

**Definition, lifecycle summary, and every term** (Seal, Strictness Rule, Daily Floor, Redemption Block, Work Hours, Change Window, Rest Days, Day Met) **are in the Quick Reference table above.** Full technical detail — build history, the 18 Redemption Rules, Strictness Rule mechanics, Work Hours edge cases, technical-mapping rows — lives in `DOMAIN_DICTIONARY_NEN_CONTRACT.md` — not auto-loaded; read it whenever a task touches Nen Contract code.

---

## Deep Module

**Definition:** A software module (class, service, or interface) that provides *significant* functionality through a *narrow*, simple API. The term comes from John Ousterhout's *A Philosophy of Software Design* and is a core architectural principle for NenFocus.

**The test:** Can a developer understand how to use this module in under 30 seconds by reading only its interface? If yes, it is deep. If the interface requires reading the implementation to use correctly, it is shallow and should be redesigned.

**Examples in NenFocus:**

| Module | Why it is deep |
|--------|---------------|
| `IActiveSessionMonitor` | 3 members (`Current`, `Tick` carrying a `TimelineReading`, `SessionAutoEnded`); hides a background timer, the break-boundary cursor and auto-ending (Gap 92, A4) |
| `IActiveSessionState` | 3 members (`ActiveSession`, `SessionChanged`, `RefreshAsync`); hides the database read, the one `SessionDto` mapping and the broadcast. Split with `IRitualPrompt` (3) from the 15-member `ISessionStateService` (Gap 92, A3) |
| `IEnforcementService` | 2 methods (`ApplyPolicyAsync`, `RemoveEnforcementAsync`); hides process kill loop, hosts file, DNS flush, registry, HTTP API |
| `IEnforcementSynchronizer` | 1 method (`SyncAsync(at)`); hides the policy query, the apply-vs-remove choice, and the lock that stops a stale answer overwriting a fresh one |
| `INotifier` | 1 method (`NotifyAsync(fact)`); hides every notification's wording and the toaster behind it |

---

## Design Gap

**Definition:** The distance between what a developer can see (the current interfaces and existing code) and what they need to understand to implement a new feature correctly. Every new feature starts with a Design Gap; the job of the pre-implementation "Grill Me" session is to close it before any code is written.

**Related principle:** See `ARCHITECT_GOVERNANCE.md → High-Level Development Strategy → 1. Strategic Design & The Design Gap`.
