# NenFocus — Domain Dictionary

This file is the canonical reference for every domain concept in NenFocus. It exists to eliminate ambiguity between conversations. When a term is used in code, comments, or a design discussion, its meaning here is authoritative.

---

## Quick Reference

| Term | One-line definition |
|------|---------------------|
| **Session** | A bounded focus period; lifecycle `Active → Completed` — one continuous run, punctuated by the breaks in its `BreakSchedule` (a break is never a status) |
| **Ritual** | The four-step wizard (Ten → Zetsu → Ren → Hatsu) that creates a Session |
| **Block Preset** | A reusable template of blocked apps + website filter type; typed as Focus or Background at creation |
| **Planned Block** | Abstract base for `FocusBlock` and `BackgroundBlock`; carries `Scheduled` (a `TimeSlot`), `BlockPresetId`, `Membership` (its contract and role, or none), `IsActive()`, `HasStarted()`, `ReservedSlot()` |
| **Focus Block** | A planned calendar entry that activates enforcement AND triggers the Nen Ritual at the designated time |
| **Background Block** | A planned calendar entry that activates enforcement at the designated time without triggering the Ritual |
| **Policy Slice** | The merged, computed enforcement state at a given instant; represented as `ActivePolicySlice` (Domain Value Object), produced by `PolicyEngine` |
| **PolicyEngine** | Stateless Domain service (`NenFocus.Domain/Services/`) that computes `ActivePolicySlice` from raw aggregates; sole owner of all merge rules |
| **Enforcement Synchronizer** | `IEnforcementSynchronizer.SyncAsync(at)` — makes the OS's actual blocking match what `PolicyEngine` says right now; the only thing that turns a Policy Slice into an apply/remove call (Gap 62) |
| **Awaiting Ritual** | A Focus Block that is active, not cancelled, and whose Ritual isn't done — `FocusBlock.IsAwaitingRitual(at)`; the Ritual is prompted for it only if no session is running (Gap 62) |
| **Spontaneous Session** | A Session created by completing the Ritual manually from the Main Page, on a walk-in `FocusBlock` (`BookingType = Spontaneous`) |
| **Planned Session** | A Session created via the Ritual triggered by a scheduled Focus Block; `BookingType = Planned` |
| **FocusBlockBookingType** | Enum (`Planned` \| `Spontaneous`) — how a Focus block was booked (FHIR's `appointmentType`); a Spontaneous block is a walk-in, created already arrived, never booked ahead (Gap 114, D2) |
| **BreakTimerPreset** | A saved, named `BreakTimer` (Classic, Deep Work, Flow ship as ordinary seeded rows, freely editable/deletable) — its own aggregate root, authored on the Settings page (not yet built). Just one quick way to produce a session's `BreakSchedule`; the session never references it, only snapshots its name |
| **BreakTimer** | Value object: a work/break rhythm — `WorkMinutes`, `ShortBreakMinutes`, `CyclesBeforeLongBreak`, `LongBreakMinutes`. `ScheduleFor(runLength)` lays it over a session to produce a `BreakSchedule` |
| **BreakSchedule** | Value object owned by `Session`: its break slots as offsets from `StartedAt` — the single source of truth for "is it a break right now." Set at Start; a break that has started never changes; trimmed at Complete, so the same list is the history record (Gap 63) |
| **SessionBreak** | One slot in a `BreakSchedule`: `Kind` (`ShortBreak` \| `LongBreak`), `Start`, `End` (offsets) |
| **BreakTransition** | A break boundary (`Started` \| `Ended`) a running session crossed — reported by `Session.BreakTransitionsBetween(from, to)`, acted on by `IBreakTransitionResponder`; not a domain event, since nothing about the session changes |
| **BlockMarker** | A point on a planned block's timeline (`StartingSoon` \| `Started` \| `Ended`) the clock passed — reported by `Block.MarkersPassedBetween(from, to)`, announced by `IBlockMarkerNotifier` via `INotifier`; not a domain event, and not a "transition" (StartingSoon changes nothing) (Gap 84) |
| **Moment** | A point in time, always UTC (`Domain/ValueObjects/Moment.cs`); no calendar members — which day or hour needs `UserTimeZone`. Only storage and `SystemClock` turn it into a `DateTime` (architecture test). Every stored timestamp, `ISystemClock.Now` and DTO times are `Moment`s (Gap 88) |
| **UserTimeZone** | The user's time zone and its DST rules; the one place a `Moment` becomes a calendar time and back (`ToCalendarTime`, `ToMoment`, `DateOf`, `SplitByDay`, `HoursPerDay(slots)` — hours of the given slots per local day, Gap 92 — and `SlotCovering(days)` — the real time calendar days cover, which repositories are handed instead of dates, Gap 97). Reached as `ISystemClock.TimeZone` (Gap 88) |
| **WholeMinutes** | Value object: an amount of time in whole minutes — the unit focus is counted in (credit, deficits, redemption coverage, a planned block's length). A fraction can't be built; the only ways in from a `TimeSpan` are `RoundDown` (credit — the one rounding in focus accounting) and `TryExact`. Every planned block (Focus and Background) is a whole number of minutes long (`Block.Length`). An amount, not a place on the calendar — that is `TimeSlot` (Gap 92) |
| **SessionTimeline** | Value object: a started session's run, breaks and keep-blocking rule (`Session.Timeline`, worked out, never stored). `ReadingAt(at)` → `TimelineReading` (time left, progress, current break and its time left, breaks taken, blocking lifted) is the one answer to "where is the session now" — `Session`'s break questions, `PolicyEngine` (through them) and the Main page all read it; `Marks` place the breaks on the progress bar. Carried on `SessionDto` (Gap 92) |
| **TimeSlot** | Value object: a span of two `Moment`s, half-open `[Start, End)`, end after start by construction; `Start`/`End` are Domain-only (`internal`). Answers `Overlaps`, `Contains`, `HasStarted`/`HasEnded`, `Length`, `SinceStart`/`RemainingAt`/`ProgressAt`, `Covering(start, WholeMinutes)` (exactly that long); `TimeSlot.Filters` are the same rules as SQL for repositories. Held by blocks (`Scheduled`) and sessions (Gap 88) |
| **BlockPlacementChecker** | Domain service: the one answer to "may a block go here?" — `Check(slot, type, membership, contractsInEffect, blocks, running, now, timeZone)` (`running`: the one running session or none, Gap 92) asks, in order, not in the past → allowed by the governing contract → free for Focus (`CheckFree`: no Focus block's or running session's reserved time). Which contract governs: a member block's own, whatever its role (Gap 106); otherwise the one whose planned period the slot touches (`GoverningContract`). Returns a `PlacementVerdict` (`PlacementOutcome` `Allowed`/`InThePast`/`OutsideContractHours`/`TimeTaken`, the slot in the way, and the governing contract when allowed). Asked by Create/Update (via `GetBlockPlacementVerdictQuery`), Split, Seal and Edit; worded once in `BlockPlacementVerdictDto.From` (Gaps 88, 97) |
| **Contract Membership** | Value object on a block: which Nen Contract it belongs to and its role — Committed (drafted and sealed), Redemption (placed to cover a deficit) or Bonus (placed on a contract in effect for any other reason). Stored, set once, made only by the Domain, and changed only through contract actions — never by a drag (Gap 106); rules ask `CountsTowardFloor` / `IsCommitted` / `IsRedemption` / `IsBonus`, never the role itself; the role is readable only to be shown — a Focus block's Planner tile tags it (COMMITTED / REDEMPTION / BONUS) from `BlockDto.ContractRole` (2026-10-26; `BlockContractBadge`, a copy of the role, deleted). No membership = a plain block (Gap 97) |
| **Contract Rules** | Value object (`ContractRules`): every contract setting that carries no dates — strictness, a fixed daily floor (whole hours 1–24, or none — refused otherwise, Gap 92), allow cancellation, keep blocking during breaks, redemption placement, and whether Bonus blocks follow the strictness. A `ContractPreset` and a sealed `NenContract` each hold their own copy. It works out the daily floor rule and the movement range; what needs the calendar stays on the contract (Gap 93) |
| **BlockChangeChecker** | Domain service: the one answer to "may this block be changed or deleted?" — `CheckReschedule`, `CheckSplit`, `CheckDelete` return a `ChangeVerdict` (allowed, or the first reason not), `AllowancesFor` the ready-made flags (`BlockChangeAllowances`) the block DTO carries. It asks the block's own lock, then its contract, and holds no rule itself; a plain block moved where any contract in effect governs is `WouldJoinContract` (Gap 106). Asked by the Update, Split and Delete validators before placement; worded once in `BlockChangeVerdictDto.From` (Gap 93) |
| **Bound block** | A block its contract holds to what was sealed: Committed and Redemption always; Bonus only when the contract's rules say Bonus blocks follow the strictness (off by default). A bound block moves as far as the strictness allows, keeps its preset, can be split (not under Lockdown) and is never deleted. `NenContract.Binds` (Gap 93) |
| **Started-block lock** | A block that has started (`Block.HasStarted`) can't be moved, resized, split or edited, and can't be deleted until it has ended; it ends early only through End Early or by breaking its contract (Gap 88 phase 0) |
| **Blocked Attempt** | An append-only fact recording a user's attempt to access a blocked app or site during a Session; a repeat of the same thing within 2 s is the same attempt (`BlockedAttempt.IsRepeatOf`, Gap 112) |
| **Website** | A site as a bare domain (`youtube.com`) — the one definition the presets, the slice, the hosts file and the extension share; covers its subdomains. Allowed or blocked is which list it's in. Its app twin is `BlockedApp` (a process name; never a critical Windows process) (Gap 112) |
| **Nen Contract** | A sealed, time-bound commitment over a contiguous date range that governs allowed changes to the Focus/Background blocks within it; own aggregate root, does not own `Block`s |
| **Seal** | The act of finalising a Nen Contract; the only way a `NenContract` comes into existence — there is no persisted draft state in the domain |
| **Strictness Rule** | Enum (`Locked` \| `DailyFlexibility` \| `PeriodTimeBudget`) — how far a block its contract binds may move: nowhere, within its own day, anywhere in the contract. Asked in one place, `ContractRules.MovementRange` (Gap 93) |
| **Daily Floor** | The minimum Focus hours a day must be *planned* with in a Nen Contract — a planning minimum, checked at Seal and on every change to a Committed block, never what decides "met" (Gap 95). Focus-only |
| **Change Window** | *Not built.* The idea of limiting *when* a contract's existing blocks may be changed (a daily window with a use limit, or e.g. "change the coming week on Sunday"). Parked as Open Gap 109; the unused `ChangeWindow` type was deleted in Gap 92 |
| **Day Met** | A Nen Contract day is "met" if its completed Focus hours reach what it owes: its Committed blocks' planned hours plus what the redemptions on it cover (`NenContract.DayStatuses`, Gap 95). Owing nothing is a rest day. A missed or late block does not fail the day directly; it only costs hours that must be made up |
| **FocusBlockOutcome** | Enum (`CompletedOnTime` \| `StartedLate` \| `Missed` \| `EndedEarly` \| `Cancelled`) classifying how a single Focus block resolved; a per-block display concept, separate from the day verdict |
| **Broken** | A Nen Contract the user cancelled before its end date. Only possible when it was sealed with Allow Cancellation on and no session is running (`NenContract.CheckBreak`; the Planner hides Cancel otherwise) |
| **Work Hours** | A daily time-of-day window defining where Focus/Background blocks may physically exist on the calendar; blocks cannot be placed, moved, or resized outside it. Distinct from Change Window (which gates *when edits are allowed*, not *where blocks can be*) |
| **Contract Day** | One day of a Nen Contract: its local window (Work Hours, or the whole day) and its floor (none = rest day). A contract is an unbroken run of them; `StartsAt`/`EndsAt` are read off the first and last day (Gap 65) |
| **Contract Preset** | A saved, reusable template of Nen Contract rules (strictness, daily floor, work hours, allow-cancellation) — a full Domain `AggregateRoot`, not just a saved configuration row. Authored and managed on the Presets page's "Contract Presets" tab; the Contract Wizard only picks one (applied as-is, no editing mid-wizard) — see the App Structure table below |
| **Simple Contract** | A quick-seal path in the Planner wizard: pick a named preset (Lockdown, Daily Flex, Period Study), schedule blocks, seal. No tuning knobs — the floor is derived from planned hours per day |
| **Advanced Contract** | The full-configuration path: every lever exposed (strictness, work hours, custom daily floor, rest days, allow-cancellation). Saving a configuration as a Contract Preset happens on the Presets page, not in the wizard |
| **Redemption Block** | A Focus Block placed (through Cover Deficit, `CoverDeficitCommand` — the only path) inside the contract whose deficit it covers, for missed/late Focus Blocks of the same preset; its role is Redemption for good, and its `Receipt` says what it covers — set once at placement, kept on a move, divided on a split (Gaps 97, 106) |
| **Bonus Block** | A block placed on a contract in effect that was neither drafted nor redeeming. It belongs to the contract (fits its Work Hours, keeps its break rule) but never counts toward the floor; a Focus one earns Bonus hours. Free by default — it can be moved inside the period, re-preset and deleted until it starts — and bound like a Committed block when the contract's rules say so. It never moves out of the contract's period; it leaves only when the contract releases it. A plain block never becomes one by being dragged in — only by being created inside (Gaps 97, 93, 106) |
| **RedemptionEntry** | A value object tying a redemption block to one missed block: `{ MissedBlockId, Covered }`, whole minutes above zero (Gap 92) |
| **RedemptionReceipt** | Value object: a redemption block's promise to pay — its `RedemptionEntry` list, each missed block once; owns `Total`, `CoveredFor(missedBlockId)` and `SplitAt(firstLength)` (time order, for a split). Held by every Focus block (`None` unless redeeming); written only by the Domain. The deficit is the missed block's debt (worked out); the receipt is fixed (Gap 106) |
| **Deficit** | The gap between a missed or late Focus Block's scheduled duration and actual completed minutes; tracked per missed block, not per preset |
| **Enforcement** | OS-level mechanisms (process kill, hosts file, registry, extension) that apply a Policy Slice |
| **Post-Session** | The mandatory screen shown after a Session ends; displays stats, blocked attempts, and an optional reflection |
| **Post-Contract** | The one-time recap screen shown after a Nen Contract finishes (`Completed` or `Broken`); mirrors Post-Session — date range, committed-vs-completed hours, Bonus hours, completion grade, per-block outcome breakdown, day-strip, optional reflection. (Gap 59.) One path to it: `NenContractEndedEventHandler`, after the commit (Gap 110) |
| **Deep Module** | A module with significant functionality hidden behind a narrow, obvious API |
| **Design Gap** | The distance between visible code and what is needed to implement a feature correctly |
| *Not yet built* | **DailyUsage**, **UsageResourceType**, **UsageCeiling** (an ambient daily/weekly cap on a site or app, independent of presets and contracts), **CeilingPeriod**, **IUsageTracker** — `OPEN_GAPS.md` → Gap 69 (A). **RecurrenceRule**, **RecurringSeries**, **SkippedOccurrence** — Gap 73 (E). Defined there, not here |

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

**Definition:** A bounded period of intentional focus — the run (the "visit") inside its Focus block (the "booking", Gap 114). Every Session belongs to a block: a planned one against the scheduled Focus block, a spontaneous one against a walk-in block booked at start.

**Lifecycle:** `Active → Completed` — a Session is born Active (the run has begun; there is no "not started" session).

**Ubiquitous language rules:**
- A Session is *started*, not *created* or *opened*.
- A Session is *completed* or *auto-ended*, not *closed* or *finished*.
- A Session cannot be paused. It is *on break* only while the clock is inside one of its break slots — it is never *put* on break.
- A miss is the *block's* outcome (`FocusBlockOutcome.Missed`), never a session status — a block whose window closed with no Ritual has no session at all.

**Break behaviour:** A session's breaks are decided when it starts and stored as its `BreakSchedule` (Gap 63); during a break slot a break countdown replaces the session timer on the Main Page while the session itself keeps running. There are no break buttons — ending or skipping a break is parked in Open Gap P. "No timer" means an empty schedule. What a break does to blocking is the session's own answer, `Session.LiftsBlockingAt(at)`: inside a break slot, unless the session keeps blocking during breaks (copied at Start from its contract, Gap 64).

**Ending early:** Currently the user can end a session before the timer runs out. A future "Strict Mode" setting will prevent this.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `Session` entity (`Entities/Session.cs`) | The run (the "visit"), created already-Active by the one static factory `Start(id, goal, block, startedAt, temporaryBlockedApps?, temporaryBlockedSites?, breakSchedule?, breakPresetName?, keepBlockingDuringBreaks)` — `block` (a required `FocusBlock`) supplies the run's end and is stamped with the arrival here, the one writer (Gap 114, D6); `Complete(endedAt, originatingBlock)` is the one ending. Carries `OriginatingBlockId (Guid)`, required. Break questions: `IsOnBreakAt(at)`, `LiftsBlockingAt(at)` (in a break slot and not `KeepBlockingDuringBreaks`, Gap 64), `CurrentBreakAt(at)`, `BreakTransitionsBetween(from, to)`, `BreaksTakenBy(at)` / `BreaksTaken`. `CountedTime(originatingBlock)` is the one definition of how long an ended session counted for — its block's credit (Gap 95). `IsDueForAutoEnd(at)`: its time is up and nothing has ended it. `Complete` is the one ending, whether you end it or the clock does: caps `EndedAt` at `EndAt`, trims the schedule (Gap 63) and stamps its block with that same end (`FocusBlock.MarkSessionEnded` is internal — Gaps 86, 89, 114). An ended session always spans time: `CanEndAt(at)` — started, and the capped end falls after the start — is what End early asks and `Complete` enforces. `Recorded` is the span its record covers (its run; null until ended), what `PostSessionDto` carries — not `Timeline`, the planned run (Gap 89 Slice C). The run's `StartedAt`/`EndAt`/`EndedAt` are `internal` (Gap 89 leftover 2). |
| Domain | `SessionStatus` enum | Valid states: `Active`, `Completed` |
| Domain | `SessionGoal`, `BreakSchedule`, `BreakPresetName (string?)` | Immutable configuration. `StartSessionValidator` holds only a spontaneous session's chosen length (1 minute to `FocusBlock.MaxLength`, 12 h) — a planned one is its block's, already capped (Gap 89 Slice B, A1); the schedule and preset-name snapshot are set at Start (see Break Timer Preset & Break Schedule below) |
| Application | `ISessionRepository` | Persistence contract; `GetActiveAsync()` is the one door for "which session is running" (none or one — "is one running" is `is not null`, Gap 92) and the startup rehydration entry point; `GetByOriginatingBlockIdAsync()` (`is not null`) makes missed-session creation idempotent. Unique indexes back both: at most one Active session, one session per Focus block (Gap 92). `Add` only — nothing deletes a session (Gap 110; a mistaken one is Gap 114's question) |
| Application | `IActiveSessionState` | Singleton: the app's view of the running session (`ActiveSession`, `SessionChanged`, `RefreshAsync()`). Read from the database through `GetActiveSessionQuery` — the one `SessionDto` builder — never pushed in; refreshed at startup and first in the session started/ended event handlers (Gap 92, A3) |
| Application | `IRitualPrompt` | Singleton: which Focus block awaits its Ritual (`PendingFocusBlockId`, `Changed` — only when the answer changes — and `RefreshAsync(at)`, asking `GetFocusBlockAwaitingRitualQuery`). Nothing else: why a block stopped awaiting is not its business. The heartbeat calls it last in each tick, with the tick's one `now` (Gap 92, A3) |
| Application | `IRecapRelay` | Interim: the post-session and post-contract relays (`PostSessionsReady`, `PostSessionAcknowledged`, `PostContractsReady` and their `Notify…`), waiting for Gap 107's owner. Post-contract recaps are raised only by `NenContractEndedEventHandler` (Gap 110) |
| Application | `IActiveSessionMonitor` | Singleton countdown engine; owns the 1 s timer; each tick asks the session's `SessionTimeline` and pushes the one `TimelineReading` with `Tick` (also `Current`) — 2 members. Ends nothing: at zero it stops, and the heartbeat, hearing the `HasEnded` reading, auto-ends the session at once (Gap 89). Its fire-and-forget calls are wrapped in try/catch + `ILogger` (Gap 31). |
| Application | `StartSessionCommandHandler` | Creates, starts and saves the session; its `SessionStartedEvent` is published after the commit. Pushes nothing to the UI (Gap 92, A3). Rejects starting against an originating block via `Block.HasEnded(now)`, the one canonical "is this past" definition (Gap 32). |
| Application | `EndSessionCommandHandler` / `AutoEndSessionCommandHandler` | You end it now (End early), or the clock ends it if `IsDueForAutoEnd(AsOf)` (`AutoEndSessionCommand { required AsOf }`, dispatched by the heartbeat every tick; no `Success` flag — nothing due is normal). Both load the originating block and ask `session.Complete(at, block)`; neither builds a screen (Gap 89). A future Strict Mode refuses yours, never the clock's. `block.End` is never overwritten (Gap 22); see `FocusBlockOutcome` below for how the frozen `Result` feeds classification. |
| Application | `SessionStartedEventHandler` | Refreshes `IActiveSessionState`, calls `IEnforcementSynchronizer.SyncAsync(now)`, reports `SessionStarted` to `INotifier` (a planned session is named after its block, Gap 84) |
| Application | `SessionEndedEventHandler` | Refreshes `IActiveSessionState` (now none), then `SyncAsync(now)` (never "remove everything" — a still-running Background Block stays enforced, Gap 62), reports `SessionCompleted` — the one toast for a planned block's end, since the session owns it (Gap 84) — then relays the Post-Session screen, built from what was saved: the one path to it, whoever ended the session (Gap 89) |
| Application | `IBreakTransitionResponder` / `BreakTransitionResponder` (`Services/`) | Given the break boundaries the monitor saw crossed: one `SyncAsync(at)` (`PolicyEngine` decides what the break lifts), then one `BreakStarted`/`BreakEnded` fact for the latest boundary only (Gap 63) |
| Application | `INotifier` / `Notifier` (`Services/`), `NenNotification` (`Notifications/`) | The one door to `INotificationService` (a bare toaster): sources report a typed fact, `Notifier` owns every title and body. A test fails if anything else takes the toaster (Gap 84) |
| UI | `MainPageViewModel` | Observer only — reads from `IActiveSessionState` and `IActiveSessionMonitor`, dispatches commands. "Use Nen" button is hidden while a session is active. |

---

## Missed Block (was Missed Session)

**Definition:** A Focus block whose window closed with the Ritual never completed. A miss is the block's frozen outcome (`FocusBlockOutcome.Missed`), not a session — the block ran nothing, so it has no session at all (Gap 114, D5). The sweep (`SweepMissedFocusBlocksCommand`, every heartbeat tick) stamps each unresolved block `MarkMissed(asOf)`; the block's own `Reflection` holds the miss note and its acknowledgement.

**Key facts:** the block's outcome, work slot and credit are frozen once (`Result`); creation is idempotent — the sweep's `MarkMissed` re-asks `IsUnresolved`, which is false once frozen; the originating block is the record, never a session.

**Full write-up (sweep/triage/Shell behaviour and the technical mapping):** `TECHNICAL_SPEC_INFRA.md` → Missed Session (to be refreshed for the block shape).

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
| **Hatsu** | Output | "Put it into action" | **Spontaneous:** User selects session duration (5 min to 12 hours — a session may run as long as a Focus block, `FocusBlock.MaxLength`, the one bound for every focus period; Gap 89 Slice B) and optionally picks a Break Timer Preset (or "No timer"). **Planned:** Duration is pre-determined by the Focus Block's scheduled end time (displayed as "active until [time]"); user can only pick a Break Timer Preset. User clicks "Start Session." |

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

**Time-agnosticism:** A Block Preset is time-agnostic — it carries no schedule, no window, and no knowledge of when it is active. This allows the same preset to be reused across multiple planned blocks at different times. `Website` and `BlockedApp` are always plain hard blocks — access-time limiting is handled by the separate `UsageCeiling` feature (see `ARCHITECT_GOVERNANCE.md` → Open Gap A), which is deliberately decoupled from `BlockPreset` entirely, not a field on it.

**Ubiquitous language rules:**
- A preset is *attached* to a session or block, not *assigned* or *applied*.
- A preset is **locked** while anything still depends on it: the running session uses it; a block that uses it hasn't really ended (plain or in a contract — by its `ReservedSlot`, so an early end or a cancellation frees it); or a contract in effect still owes time of it (Cover Deficit needs it). `BlockPreset.IsLockedAt` is the one rule (Gap 89 Slice D). A locked preset can never be **deleted**. What a past session or block remembers of it is Gap 120's.
- A locked preset can still be **updated**, but only toward being *at least as strict* (Gap 8; direction fixed in Gap 30 — see `BlockPreset.IsAtLeastAsStrict`): `BlockedApps` add-only. `Websites` add-only under `Blocklist`, but **remove-only** under `Allowlist` (removing an allowed site is the stricter direction). `Name`, `WebsiteFilterType` and `PresetType` must stay unchanged while locked (tags were deleted in Gap 110). You can strengthen a live preset; you cannot weaken, rename, or retype it while it's committed.
- A preset is *locked* — fully, not just add-only — for `DeleteBlockPresetCommand`; deletion is never allowed while in use, regardless of what would be deleted.

**UI display:** On the Block Preset page, Focus and Background presets are displayed together in a single list with a type badge (blue = Focus, green = Background). A filter bar lets the user show All, Focus only, or Background only.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BlockPreset` aggregate (`Entities/BlockPreset.cs`) | Owns the list of `BlockedApp` and `Website` value objects, the `WebsiteFilterType`, and the preset type (`BlockPresetType.Focus` or `BlockPresetType.Background`). `IsAtLeastAsStrict(IReadOnlySet<BlockedApp> proposedApps, IReadOnlySet<Website> proposedSites) -> bool` answers "does this change keep me at least as strict" — apps add-only, sites add-only under `Blocklist`/remove-only under `Allowlist` (Gap 30). `IsLockedAt(now, running, blocksUsingIt, openDeficits)` — locked (see above); `AllowsChangeWhileLocked(name, filter, type, apps, sites)` — name as saved, filter and type unchanged, and at least as strict (Gap 89 Slice D). |
| Domain | `BlockedApp`, `Website` value objects | The one definition of an app and a site (Gap 112): `BlockedApp` is a lowercased process name (a picked path is cut down to it; a critical Windows process is refused — `Check` gives the typed `BlockedAppRefusal`, `NameOf` the name, `Matches` a running process); `Website` is a bare domain (`https://www.YouTube.com/x` → `youtube.com`; `TryCreate` the non-throwing ask) that `Covers` itself and its subdomains. Equal entries are equal values. A preset's, a session's extras and the slice all hold them. Always plain hard blocks — no budget/ceiling concept lives on either; see `UsageCeiling` for ambient access limits |
| Domain | `WebsiteFilterType` enum | `Blocklist` or `Allowlist` |
| Application | `IBlockPresetRepository` | A collection (Gap 110): `Add`, `Remove(preset)` and reads; the command's commit saves changes |
| Application | `CreateBlockPresetCommandHandler`, `UpdateBlockPresetCommandHandler`, etc. | One handler per mutation |
| Application | `CheckBlockPresetLockedQuery` | Gathers what `IsLockedAt` needs — the running session, `IBlockRepository.GetUsingPresetAsync` (a superset; the blocks decide) and every contract in effect's open deficits — at one "now", and asks the preset. Asked by both preset validators: Delete refuses when locked (one reason — Design Review Rule 5); Update, when locked, asks `AllowsChangeWhileLocked` (Gap 89 Slice D). |
| UI | `BlockPresetPageViewModel` | Full CRUD UI; file picker for app selection; type filter |

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
- The `/report` endpoint is implemented: when the extension reports a blocked navigation, `WindowsEnforcementService` reports it with the enforcing policy (`RecordBlockedAttemptCommand`); the use case records it if that policy names a session. Background Block attempts are still not tracked (Gap 117).
- `/check` asks the slice, `ActivePolicySlice.Blocks(website)` (Gap 112) — in Allowlist mode everything is blocked that some active allowlist doesn't cover. Note that the hosts-file path cannot enforce allowlist mode (you cannot enumerate all domains); the browser extension is the sole allowlist enforcement mechanism.
- Usage Ceilings are not enforced yet (no `DailyUsage`, no `/report-time`) — Gap 69 (A).

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `IEnforcementService` | Contract for applying and removing enforcement; accepts `ActivePolicySlice` (Domain Value Object). Called only by `EnforcementSynchronizer` — no exceptions since Gap 101 (guarded by `SideEffectRuleTests`). |
| Application | `IEnforcementSynchronizer` / `EnforcementSynchronizer` (`Interfaces/`, `Services/`) | `SyncAsync(Moment at) -> ActivePolicySlice`: computes the slice via `IQueryDispatcher`, removes enforcement if empty else applies it, as one locked step so a stale slice can never overwrite a fresh one. Singleton. Callers: the heartbeat, the session start/end event handlers (Gap 62), `BreakTransitionResponder` (Gap 63), and the app's shutdown (Gap 101: closing leaves blocking as `PolicyEngine` says for that moment; the close waits up to 2 s for the sync, and on failure leaves blocking as it was). |
| Infrastructure | `WindowsEnforcementService` | Consumes `ActivePolicySlice` directly (no DTO); owns hosts file, process kill loop, registry writes, local HTTP API (`localhost:5005`) — `/check` (is this domain blocked) and `/report` (records a web blocked attempt when a session is active). |
| Infrastructure | `HeartbeatService` | The one `BackgroundService` (architecture test), 5 s, no rules of its own. Each tick, one `now`, in order: `AutoEndSessionCommand`, `SweepMissedFocusBlocksCommand`, `SweepNenContractLifecycleCommand`, `SyncAsync(now)`, block markers passed in (last tick, now] (Gap 84), then `IRitualPrompt.RefreshAsync(now)` (Gap 92). Every use case dispatched; each step fails on its own. The first tick runs in `StartAsync`, before the window opens; a `HasEnded` reading wakes it early (Gap 89) |
| Extension | `background.js` | Intercepts browser navigation; queries `/check`; calls `/report` on a real blocked navigation |

---

## Policy Slice

**Definition:** The computed, merged enforcement state at a given instant in time. It is the *answer* to the question "what should be blocked right now?" — derived from all currently active Sessions and Planned Blocks. Represented as `ActivePolicySlice`, a **Domain Value Object** in `NenFocus.Domain/ValueObjects/`, not a DTO.

**Computation rules** (all logic lives in `PolicyEngine.ComputeSlice()`):
- **Apps:** union of all blocked apps across all active block presets, always a blocklist. Includes `TemporaryBlockedApps` from the active `Session` entity.
- **Websites:** if *any* active preset uses `Allowlist` mode → default-deny, allowed = intersection of all allowlists minus any blocklist entries. Otherwise → block the union of all blocklisted sites. Includes `TemporaryBlockedSites` from the active `Session` entity.
- **Usage Ceilings *(not yet built)*:** an exhausted ceiling will add its site/app to the blocked set; a hard block already covering it wins and no usage accrues — Gap 69 (A).
- An `ActivePolicySlice` is considered "empty" only in `Blocklist` mode with no blocked apps or sites. It is *never* empty in `Allowlist` mode.
- **"Active" is re-checked precisely inside `ComputeSlice` itself:** `ComputeActivePolicySliceQueryHandler` over-fetches blocks with a ±1-second buffer around `at` (SQLite datetime-comparison slop), so `PolicyEngine` filters through `Block.IsActive(at)` as the final authority (Gap 21, third addendum). `IsActive` is `atTime >= Start && atTime < EffectiveEnd(atTime)`, not raw `End` — a block whose session ended early stops being active when it actually ended (see `MODULE_STANDARDS.md` → Application Handler Forbidden Zone).

**Overlap rules:**
- Multiple Background Blocks can overlap freely. The net enforcement is the union of all their presets.
- A Focus Block can overlap with Background Blocks. The net enforcement is the union of all active presets. The Ritual is still triggered.
- Two Focus Blocks cannot overlap. The Planner prevents scheduling conflicts. A Spontaneous session cannot be started if it would overlap with an upcoming Focus Block.

**The slice answers only "what to block."** Whether a Focus Block should push the user into the Ritual is a separate question with its own home: `FocusBlock.IsAwaitingRitual(at)` + `GetFocusBlockAwaitingRitualQuery` (see Focus Block below). The slice used to carry `ActiveBlockPresetType`/`ActiveFocusBlockId`/`FocusSessionCompleted` for that purpose — removed in Gap 62.

**Source of Truth flow:** `Database → PolicyEngine (Domain) → ActivePolicySlice (VO) → EnforcementSynchronizer → WindowsEnforcementService (OS)`

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `ActivePolicySlice` (`ValueObjects/ActivePolicySlice.cs`) | Immutable Domain Value Object; the canonical enforcement contract. Carries `BlockedApps` (`BlockedApp`s), `BlockedSites` (`Website`s, also what the hosts file writes), `AllowLists` (one set per active allowlist preset, kept apart — coverage can't be precomputed as one set, Gap 112), `WebsiteFilterTypeMode` (worked out from `AllowLists`), `ActiveSessionId`, `IsEmpty` — blocking only, no Ritual fields (Gap 62), no time window (Gap 118). Answers `Blocks(Website)` (a block beats an allow; with an allowlist, blocked unless every allowlist covers it) and `BlockedAppFor(processName)`; Infrastructure asks these and never reads the parts (`InfrastructureRuleTests`). |
| Application | `ComputeActivePolicySliceQueryHandler` (`Queries/ComputeActivePolicySlice/`) | `PolicyEngine`'s only caller. Pure orchestrator: fetches entities from three repositories, loads `BlockPreset` aggregates, delegates to `PolicyEngine.ComputeSlice()`. Returns the `ActivePolicySlice` to callers. |

---

## PolicyEngine

**Definition:** The sole Domain service responsible for computing the `ActivePolicySlice`. It is a stateless, synchronous function: given a point in time, `BlockPreset` aggregates, the running `Session` (if any) and `Block`s, it returns the complete enforcement state for that moment.

**Why it is a Domain Service (not an Application service):** Policy merge logic — the rules for unioning blocked apps, resolving allowlist/blocklist precedence, and deciding what a break lifts — is domain logic. It expresses invariants of the domain ("an allowlist preset beats a blocklist preset", "a session whose contract keeps blocking during breaks stays blocked through them") that must be consistent wherever policy is computed. Placing this logic in a Domain service makes it unit-testable without a database and reachable by any future consumer (e.g., a Planner "preview" query).

**Contract** (a sealed class, no interface — Gap 111):
```csharp
// NenFocus.Domain/Services/PolicyEngine.cs
public ActivePolicySlice ComputeSlice(
    Moment at,
    IEnumerable<BlockPreset> presets,
    Session? running,             // at most one runs (Gap 92)
    IEnumerable<Block> blocks);
```

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `PolicyEngine` (`Services/PolicyEngine.cs`) | Sealed class, no interface (Gap 111); all merge rules live here. Filters `blocks` through `Block.IsActive(at)` (`EffectiveEnd`-based) — the caller's fetch may be loose/buffered; `PolicyEngine` is the precise final word on "is this block active right now" (Gap 21). Derives `activePresets` from the active blocks'/sessions' own `BlockPresetId`s rather than trusting the caller's preset list (Gap 60). Resolves what a break lifts by asking each session `LiftsBlockingAt(at)` — the session already carries its contract's keep-blocking rule, so `PolicyEngine` takes no contracts (Gap 64). Only the specific session on break and its originating block lose their preset's contribution; a co-occurring Background Block is untouched. |
| Application | `DependencyInjection.cs` | Registers `PolicyEngine` as itself (`AddScoped`, in `NenFocus.Application`'s `DependencyInjection.cs` — stateless, so singleton would also be safe), like every Domain service |

---

## Planned Block

**Definition:** The abstract base class for all calendar-scheduled enforcement entries. A `Block` has a `Scheduled` time slot (`TimeSlot`, UTC moments), a `BlockPresetId`, and an optional `Title`. It is never instantiated directly — only its two concrete subtypes (`FocusBlock` and `BackgroundBlock`) are created. The C# class type encodes the block preset type; there is no stored `BlockPresetType` column.

**Domain behaviour:** `Block` carries these domain methods, used by both subtypes and the query layer:
- `IsActive(Moment at)` — returns `true` if `at` falls within `[Start, EffectiveEnd(at))`.
- `EffectiveEnd(Moment asOf)` — the one rule for how much of the window the block really occupies; every reason it shrinks lives here. Base: cancellation (ends at the cancel moment; empty if cancelled before `Start`). `FocusBlock` adds session-ended-early and missed; the earliest reason wins. `IsActive`, overlap checks and markers never check `CancelledAt` themselves (Gap 85).
- `HasStarted(Moment asOf)` — asks `Scheduled`; once true the block is locked: no edit or split, no delete until it has ended (Gap 88 phase 0).
- `HasEnded(Moment asOf)` — asks `Scheduled` (half-open: ended *at* its end); the one canonical "is this past" check.
- `ReservedSlot(asOf)` — the Focus time it really takes (up to `EffectiveEnd`), what `BlockPlacementChecker` compares; `OverlapsWith(other)` stays for the Seal batch's own check only — placement never calls it (Gap 88).
- `MarkersPassedBetween(from, to)` — the markers the clock passed in (from, to]: `StartingSoon` (`UpcomingWarningLead`, 5 min, before `Start`), `Started`/`Ended` (`IsActive` flipped — the definition enforcement uses). `Ended` only if the block owns its ending: a `FocusBlock` whose Ritual was completed does not — its session announces it (Gap 84).

**Why it exists:** `IBlockRepository` returns `IReadOnlyList<Block>` so that the enforcement pipeline can work with all scheduled blocks polymorphically without knowing their concrete type. Enforcement treats Focus and Background blocks alike; the only place the subtype matters for triggering is the Ritual, where `GetFocusBlockAwaitingRitualQueryHandler` asks only `FocusBlock`s — the distinction is made exactly once, in the one place that needs it.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `Block` abstract class (`Entities/Block.cs`) | Base for `FocusBlock` and `BackgroundBlock`; exposes `IsActive()`, `OverlapsWith()`, `MarkersPassedBetween()`, `Membership` with `Leave()` (released by its contract — Gaps 97, 93; no move changes membership, `PlaceUnder` deleted in Gap 106), its own lock `CheckOwnChange` / `CheckOwnSplit` / `CheckOwnDelete` (Gap 93; `CheckOwnDelete` refuses any started block, Gap 114 D5 amd 3) and `MarkCancelled` (refuses an ended block; a Focus block also freezes `Cancelled`), `PresetType` (read off the subtype) and `DisplayName(presetName)` (title, else preset name — the one definition of a block's name, Gap 84) |
| Application | `IBlockRepository` | Returns `IReadOnlyList<Block>` (overlap checks, active-state queries); callers cast to concrete type when subtype behaviour is needed |

---

## Focus Block

**Definition:** A planned calendar entry that does two things simultaneously at its designated start time: (1) enforcement activates immediately — the block preset's rules are applied before the user has done anything, and (2) the user is pushed to the Nen Ritual page to consciously start the session. The session created via the Ritual is the *Planned Session* associated with this block. Critically, enforcement is already in place from the designated time regardless of whether the user completes the Ritual immediately. If the user never completes the Ritual, the block is missed (see Missed Block) — there is no session. Enforcement still expires at the block's scheduled end time whether the Ritual was completed or not.

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
5. `StartSessionCommandHandler` calls `FocusBlock.MarkRitualCompleted(now)` (the clock's one `now`); the command's commit saves it
6. The resulting Session (with `Origin = Planned`) is linked to this Focus Block via `Session.OriginatingBlockId`

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `FocusBlock` entity (`Entities/FocusBlock.cs`) | Extends `Block`; inherits `Id`, `BlockPresetId`, `Scheduled`, `Title?`; own fields: `RitualCompletedAt?` (the arrival — internal, written only by `Session.Start`, Gap 114 D6), `BookingType` (`FocusBlockBookingType`), `Result` (the frozen `FocusBlockResult`, null while open), `Reflection` (a missed block's note). The answers (`Outcome`, `StartedLate`, `EndedEarly`, `WorkSlot`, `CreditedSlot`, `IsFinal`) read `Result` once frozen and derive live otherwise; `StartedLate`/`EndedEarly`/`Credit` are derived from `WorkSlot`, never stored. Block preset type derived from C# class (no stored column). `IsAwaitingRitual(at)` — active, not cancelled, Ritual not done — is the one definition of "should this block prompt the Ritual" (Gap 62). `IsCommitted` / `IsRedemption` / `IsBonus` / `CountsTowardFloor` (its stored role in its contract; only a Redemption block may hold a receipt with entries — Gaps 97, 106), `Receipt`, `SplitInto(firstId, first, secondId, second)` (both pieces keep the membership, the receipt divided, nothing shortened — Gap 106) and `IsUnresolved(asOf)` (window closed, Ritual never done, not cancelled, not yet frozen — the missed sweep's rule; the repository's SQL mirrors it, checked by a parity test) name two facts callers used to spell out. `MarkMissed(asOf)`, `MarkSessionEnded(at)` and `MarkCancelled(at)` are the final transitions that freeze `Result`. |
| Application | `GetFocusBlockAwaitingRitualQuery` / `Handler` (`Queries/GetFocusBlockAwaitingRitual/`) | Returns the active Focus Block awaiting its Ritual, or `null` — also `null` if any session is running (checked in the database, not in-memory). What `IRitualPrompt` asks, once per heartbeat tick (Gaps 62, 92) |
| UI | `PlannerPageViewModel` | Creates, updates, deletes Focus Blocks; drag interactions and form locking implemented |
| UI | `ShellViewModel` | Its Ritual follows `IRitualPrompt.Changed`: opens the Ritual overlay for the block the prompt names (queued behind any pending Post-Session screens) and closes it when the prompt stops naming that block — see Missed Block (Gap 92) |

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
| Domain | `BackgroundBlock` entity (`Entities/BackgroundBlock.cs`) | Extends `Block`; no additional own fields; block preset type derived from C# class (no stored column). Enforced through the same slice and heartbeat as every block |

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

## Spontaneous vs Planned Session (FocusBlockBookingType)

Definitions are in the Quick Reference. Rules not stated elsewhere:
- **Spontaneous:** the user chooses preset (Focus-type only), duration and break preset; `OriginatingBlockId = null`, no block of any kind exists (Gap 114 will change this). Abandoning the Ritual before "Start Session" saves nothing — no draft, no partial session, no recovery state. It cannot start if it would overlap an upcoming Focus Block (a block at 18:00 refuses a 30-minute session at 17:45).
- **Planned:** preset (Ren) and end time (Hatsu) are fixed by the block; only the break preset is chosen. `OriginatingBlockId` = the block's Id. A Ritual never completed misses the block (see Missed Block); enforcement runs to the block's end regardless.
- **Why `Origin` and not "is `OriginatingBlockId` null":** nullness read as a type is a leaky boolean; the enum stays unambiguous when more origins are added. Set at creation by `StartSessionCommandHandler`, never changed.

---

## Blocked Attempt

**Definition:** An append-only fact recording each time the user attempted to access a blocked app or website during a Session. Each record is keyed by `SessionId` and stored as its own row — not serialized as JSON on the Session entity.

**Current state:** Both app and website blocked attempts are tracked during Ritual sessions, and both deliberately distinguish an active attempt from a resource merely being caught by ambient enforcement when a block first activates (Gap 46) — an app/site that was already running/open when the block activated is not "attempted," only a later attempt made while the block is active is. Apps: `WindowsEnforcementService.KillBlockedProcessesImmediate(reportAttempts)` — the one-time sweep `ApplyPolicyAsync` runs when a block first activates passes `false`; the ongoing 2-second process-kill timer, which only ever catches a relaunch attempted during an active block, passes `true`. Websites: reported via the extension's `/report` endpoint, but only from `background.js`'s `onBeforeNavigate` handler (a real navigation check) — never from `poll.js`'s ambient already-open-tab poll, which only ever redirects. Background Block attempts are not yet tracked.

**Displayed in:** Post-Session screen (e.g. "you tried to access YouTube 5 times during this session") and Insights page (most-attempted sites, per-session history).

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BlockedAttempt` entity (`Entities/BlockedAttempt.cs`) | Append-only fact table; keyed by `SessionId` |
| Domain | `BlockedAttemptType` enum | `App` or `Website` |
| Domain | `BlockedAttempt.ForApp` / `ForWebsite`, `IsRepeatOf` | Built from the `BlockedApp`/`Website` that was reached; the same thing in the same session within `RepeatWindow` (2 s) is a repeat, not a new attempt (Gap 112). Its own aggregate — append-only, no `Remove` |
| Application | `RecordBlockedAttemptCommand` / `Handler` (`UseCases/Sessions/RecordBlockedAttempt/`) | Enforcement's report: resource, kind, time and the enforcing policy. The handler takes the session from that policy (attempts are queued, so never "whatever runs now"), drops a repeat, adds the attempt. No session → nothing added, not a failure (Gap 112) |
| Application | `IBlockedAttemptRepository` | `Add`, `GetLatestAsync`, `GetForSessionAsync` — read by End Session and the two Post-Session queries |
| Infrastructure | `WindowsEnforcementService` | Reports each killed app once per sweep and each `/report` site, through one private serial queue to the command dispatcher |

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

## Insights *(not yet built)*

The Insights page is a placeholder; no Application code exists. The intended design — one query unifying Focus blocks, Background blocks and spontaneous sessions for the calendar (`GetInsightsCalendarQuery`) — is in `DESIGN_GAP_HISTORY.md` Gap 3; Gap 114 (every session belongs to a block) and Gap 74 reshape it.

---

## Nen Contract

**Definition, lifecycle summary, and every term** (Seal, Strictness Rule, Daily Floor, Redemption Block, Work Hours, Change Window, Rest Days, Day Met) **are in the Quick Reference table above.** Full technical detail — build history, the 18 Redemption Rules, Strictness Rule mechanics, Work Hours edge cases, technical-mapping rows — lives in `DOMAIN_DICTIONARY_NEN_CONTRACT.md` — not auto-loaded; read it whenever a task touches Nen Contract code.

---

## Deep Module

From Ousterhout's *A Philosophy of Software Design*; the test is in `MODULE_STANDARDS.md` → The Deep Module Test. **Examples in NenFocus:**

| Module | Why it is deep |
|--------|---------------|
| `IActiveSessionMonitor` | 2 members (`Current`, `Tick` carrying a `TimelineReading`); hides a background timer and the break-boundary cursor (Gap 92, A4; auto-ending moved to the heartbeat, Gap 89) |
| `IActiveSessionState` | 3 members (`ActiveSession`, `SessionChanged`, `RefreshAsync`); hides the database read, the one `SessionDto` mapping and the broadcast. Split with `IRitualPrompt` (3) from the 15-member `ISessionStateService` (Gap 92, A3) |
| `IEnforcementService` | 2 methods (`ApplyPolicyAsync`, `RemoveEnforcementAsync`); hides process kill loop, hosts file, DNS flush, registry, HTTP API |
| `IEnforcementSynchronizer` | 1 method (`SyncAsync(at)`); hides the policy query, the apply-vs-remove choice, and the lock that stops a stale answer overwriting a fresh one |
| `INotifier` | 1 method (`NotifyAsync(fact)`); hides every notification's wording and the toaster behind it |
