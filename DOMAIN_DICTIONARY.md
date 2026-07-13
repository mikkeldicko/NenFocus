# NenFocus — Domain Dictionary

This file is the canonical reference for every domain concept in NenFocus. It exists to eliminate ambiguity between conversations. When a term is used in code, comments, or a design discussion, its meaning here is authoritative.

---

## Quick Reference

| Term | One-line definition |
|------|---------------------|
| **Session** | A bounded focus period; lifecycle `NotStarted → Active → (OnBreak ↔ Active) → Completed` |
| **Ritual** | The four-step wizard (Ten → Zetsu → Ren → Hatsu) that creates a Session |
| **Block Preset** | A reusable template of blocked apps + website filter type; typed as Focus or Background at creation |
| **Planned Block** | Abstract base for `FocusBlock` and `BackgroundBlock`; carries `Start`, `End`, `BlockPresetId`, `IsActive()`, `OverlapsWith()` |
| **Focus Block** | A planned calendar entry that activates enforcement AND triggers the Nen Ritual at the designated time |
| **Background Block** | A planned calendar entry that activates enforcement at the designated time without triggering the Ritual |
| **Policy Slice** | The merged, computed enforcement state at a given instant; represented as `ActivePolicySlice` (Domain Value Object), produced by `PolicyEngine` |
| **PolicyEngine** | Stateless Domain service (`NenFocus.Domain/Services/`) that computes `ActivePolicySlice` from raw aggregates; sole owner of all merge rules |
| **Spontaneous Session** | A Session created by completing the Ritual manually from the Main Page; no Block entity exists; `Origin = Spontaneous` |
| **Planned Session** | A Session created via the Ritual triggered by a scheduled Focus Block; `Origin = Planned` |
| **SessionOrigin** | Enum (`Spontaneous` \| `Planned`) that tags every Session with how it was initiated |
| **Blocked Attempt** | An append-only fact recording a user's attempt to access a blocked app or site during a Session |
| **Nen Contract** | A sealed, time-bound commitment over a contiguous date range that governs allowed changes to the Focus/Background blocks within it; own aggregate root, does not own `PlannedBlock`s |
| **Seal** | The act of finalising a Nen Contract; the only way a `NenContract` comes into existence — there is no persisted draft state in the domain |
| **Strictness Rule** | Enum (`Locked` \| `DailyFlexibility` \| `DailyTimeBudget` \| `PeriodTimeBudget`) — one axis of post-seal mutability; interpretation of the rule against an edit's context is an Application-layer concern |
| **Daily Floor** | The minimum completed Focus hours required per day within a Nen Contract; Focus-only, hard-checked against planned hours at Seal, soft-tracked (scored, not blocked) thereafter |
| **Change Window** | An optional daily time-of-day window during which edits to *existing* blocks are permitted, with a configured per-day use limit; the use-count itself is tracked by the Application layer |
| **Day Met** | A scoring outcome: a Nen Contract day is "met" if that day's completed Focus hours ≥ the Daily Floor — full stop. A missed or late Focus block does not fail the day directly; it only costs hours that must be made up |
| **FocusBlockOutcome** | Enum (`CompletedOnTime` \| `StartedLate` \| `Missed`) classifying how a single Focus block resolved; a per-block display concept, separate from the day verdict |
| **Broken** | A Nen Contract the user cancelled before its end date; only reachable if `AllowCancellation` was set true at Seal |
| **Work Hours** | A daily time-of-day window defining where Focus/Background blocks may physically exist on the calendar; blocks cannot be placed, moved, or resized outside it. Distinct from Change Window (which gates *when edits are allowed*, not *where blocks can be*) |
| **Contract Preset** | A saved, reusable template of Nen Contract rules (strictness, work hours, allow-cancellation, etc.) — not a domain aggregate, just a saved configuration row. Created from the Planner wizard; managed on the Presets page under a "Contract Presets" tab |
| **Simple Contract** | A quick-seal path in the Planner wizard: pick a named preset (Lockdown, Daily Flex, Week Study, Light Touch), schedule blocks, seal. No tuning knobs — the floor is derived from planned hours per day |
| **Advanced Contract** | The full-configuration path: every lever exposed (strictness, work hours, custom daily floor, rest days, allow-cancellation). User can save the configuration as a Contract Preset for reuse |
| **Make-Up Block** | A Focus Block explicitly linked to one or more missed/late Focus Blocks of the same preset, covering all or part of their deficit; carries `MakeUpForEntries` |
| **MakeUpForEntry** | A value object tying a make-up block to one missed block: `{ MissedBlockId, MinutesCovered }` |
| **Deficit** | The gap between a missed or late Focus Block's scheduled duration and actual completed minutes; tracked per missed block, not per preset |
| **Enforcement** | OS-level mechanisms (process kill, hosts file, registry, extension) that apply a Policy Slice |
| **Post-Session** | The mandatory screen shown after a Session ends; displays stats, blocked attempts, and an optional reflection |
| **Deep Module** | A module with significant functionality hidden behind a narrow, obvious API |
| **Design Gap** | The distance between visible code and what is needed to implement a feature correctly |
| **DailyUsage** | Append-only fact recording actual seconds spent on any site or app, per calendar day; the universal usage-tracking substrate, independent of ceilings, presets, or contracts |
| **UsageResourceType** | Enum (`App` \| `Website`) tagging a `DailyUsage` or `UsageCeiling` entry |
| **UsageCeiling** | An ambient, perpetual cap on daily or weekly access time for a specific site or app; created on the Presets page; deliberately independent of `BlockPreset` and `NenContract` |
| **CeilingPeriod** | Enum (`Daily` \| `Weekly`) — the aggregation window a `UsageCeiling`'s cap is measured against |
| **IUsageTracker** | Application-layer interface through which infrastructure records usage events; analogous in pattern to `IBlockedAttemptTracker` |

---

## App Structure

NenFocus has five pages accessible via the sidebar:

| Page | Purpose |
|------|---------|
| **Main Page** | Home screen; shows "Use Nen" button when idle, active session timer when a session is running |
| **Block Preset Page** | CRUD for Block Presets and Contract Presets; two top-level tabs (Block Presets, Contract Presets); Block Presets tab has All / Focus / Background filter; Contract Presets tab lists saved contract rule templates (view, edit, delete — creation is from the Planner wizard only) |
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

**Break behaviour:** When a break reminder fires, a break countdown timer replaces the session timer on the Main Page. Enforcement remains active during breaks — blocked sites and apps stay blocked. The break is a reminder only. A future Settings option will allow the user to choose whether enforcement is paused during breaks.

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
| Application | `IActiveSessionMonitor` | Singleton countdown engine; owns the timer, exposes `SessionTimeRemaining` / `Progress`, auto-ends via `EndSessionCommandHandler` |
| Application | `StartSessionCommandHandler` | Creates and starts the session, calls `_sessionState.SetActive()`, dispatches `session.DomainEvents` via `IDomainEventPublisher` |
| Application | `EndSessionCommandHandler` | Calls `session.Complete()`, saves, dispatches `session.DomainEvents`; enforcement removal and notification are handled by `SessionEndedEventHandler` |
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

- **Cold-start sweep:** `SessionStateService.InitializeAsync()` calls `SweepMissedFocusBlocksCommand` after existing session rehydration. The handler queries `IPlannedBlockRepository.GetUnresolvedFocusBlocksAsync(asOf)` — Focus blocks with `End < asOf && RitualCompletedAt == null` — and calls `MarkFocusBlockMissedCommand` for each, ordered by `Start` ascending (oldest first). The resulting `PostSessionDto`s are raised via the new `MissedSessionsDiscovered` event.
- **Live release:** `ScheduledEnforcementService`'s 5-second heartbeat calls `SessionStateService.SetRitualRequired(false)` when a Focus block's window closes without a session starting. `SetRitualRequired` now raises the widened `RitualRequirementChanged` event carrying a `RitualRequirementChange(bool Required, Guid? FocusBlockId)` — on the `false` transition, `FocusBlockId` is the *previously* pending block (captured into a local before `_pendingFocusBlockId` is cleared), since `ScheduledEnforcementService` itself has no memory of what was showing. `ShellViewModel` reacts by hard-closing the open Ritual overlay immediately (it can't wait — the user would be staring at a dead ritual), then calls `MarkFocusBlockMissedCommand` for that block.

**The Shell queues what it can't show immediately.** `ShellViewModel` holds a private `Queue<PendingOverlay>` (`PendingMissedSession(PostSessionDto)` | `PendingRitual(Guid FocusBlockId)`). `TryDrainQueue()` is the single chokepoint — nothing new is shown unless `!IsRitualActive && !IsPostSessionActive && !IsTriageActive`. `ExitRitual`/`ExitRitualAndGoToMain`/`ExitPostSession` all funnel through it, and it lands on the Main page when the queue is empty.

**Triage gates a large backlog.** If `TryDrainQueue()` is about to pop from the queue and finds more than 3 pending `PendingMissedSession` items (checked fresh each call, not just once when discovered — so items enqueued later still trip the gate), it shows `MissedSessionsTriageViewModel` instead: "Skip All" clears every queued missed session and re-drains; "Review Each" sets a `_suppressTriage` flag (so the gate doesn't re-fire mid-review) and re-drains to cycle through them one by one via the normal Post-Session screen. The flag resets once the queue empties.

**The Post-Session screen wears different copy for a miss.** `PostSessionDto.Status` (mirrored from the session) drives two computed `PostSessionViewModel` properties: `HeaderLabel` ("SESSION MISSED" vs "SESSION COMPLETE") and `ReflectionPrompt` ("What got in the way?" vs "How did the session go? What did you accomplish?").

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `Session.Miss(DateTime scheduledStart, DateTime scheduledEnd)` | `NotStarted → Missed`; throws if `Origin != Planned` or `Status != NotStarted`; sets `StartedAt`/`EndedAt` to the scheduled window; raises `SessionMissedEvent` |
| Domain | `SessionMissedEvent` (`Events/SessionMissedEvent.cs`) | Carries `SessionId`, `ScheduledStart`, `ScheduledEnd`; `occurredAt = scheduledEnd` |
| Application | `MarkFocusBlockMissedCommand` / `CommandHandler` / `Result` / `Validator` (`UseCases/Sessions/MarkFocusBlockMissed/`) | Loads the `FocusBlock`, builds the Session via `Create(...)` + `Miss(block.Start, block.End)`, saves, and builds the `PostSessionDto` (preset name lookup; blocked-attempt dictionaries left empty since a missed session never tracked any). Validator rejects a missing/wrong-type block, an already-completed ritual, or a block that already has a session. No `SessionMissedEventHandler` is registered — this feature is deliberately silent (no toast); the missed post-session screen is the acknowledgment. |
| Application | `IPlannedBlockRepository.GetUnresolvedFocusBlocksAsync(DateTime asOf)` | Focus blocks with `End < asOf && RitualCompletedAt == null`, ordered by `Start` |
| Application | `SweepMissedFocusBlocksCommand` / `Result` / `CommandHandler` (`UseCases/Sessions/SweepMissedFocusBlocks/`) | No input (reads "now" via `ISystemClock`, matching `StartSessionCommandHandler`'s own precedent). Queries unresolved blocks, calls `MarkFocusBlockMissedCommand` per block, collects the resulting `PostSessionDto`s |
| Application | `PostSessionDto.Status` | Mirrors `Session.Status`; drives Post-Session screen copy |
| Application | `ISessionStateService.RitualRequirementChanged` (widened) / `RitualRequirementChange(bool Required, Guid? FocusBlockId)` (`DTOs/RitualRequirementChange.cs`) | `FocusBlockId` identifies the block on a `false` transition caused by its window closing unfulfilled; `null` for transitions not about a specific block's forced window (session starting/ending) |
| Application | `ISessionStateService.MissedSessionsDiscovered` | Raised by `InitializeAsync()` after the cold-start sweep, if non-empty |
| Infrastructure | `SessionStateService.SetRitualRequired` | Captures the outgoing `_pendingFocusBlockId` into a local before clearing it, so the `false` event still carries the right block identity even though the caller (`ScheduledEnforcementService`) never supplies one |
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
- A locked preset can still be **updated**, but only as an *add-only* change (2026-07-07 revision — previously a locked preset rejected every edit outright): the new `BlockedApps`/`Websites` lists must be supersets of the current ones (nothing already blocked may be dropped), and `Name`, `WebsiteFilterType`, `PresetType`, and `Tags` must all stay unchanged. You can strengthen a live preset; you cannot weaken or rename it while it's committed.
- A preset is *locked* — fully, not just add-only — for `DeleteBlockPresetCommand`; deletion is never allowed while in use, regardless of what would be deleted.

**UI display:** On the Block Preset page, Focus and Background presets are displayed together in a single list with a type badge (blue = Focus, green = Background). A filter bar lets the user show All, Focus only, or Background only.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BlockPreset` aggregate (`Entities/BlockPreset.cs`) | Owns the list of `BlockedApp` and `BlockedSite` value objects, the `WebsiteFilterType`, and the preset type (`BlockPresetType.Focus` or `BlockPresetType.Background`) |
| Domain | `BlockedApp`, `BlockedSite` value objects | Immutable; validated at construction. Always plain hard blocks — no budget/ceiling concept lives on either; see `UsageCeiling` for ambient access limits |
| Domain | `WebsiteFilterType` enum | `Blocklist` or `Allowlist` |
| Application | `IBlockPresetRepository` | CRUD persistence contract |
| Application | `CreateBlockPresetCommandHandler`, `UpdateBlockPresetCommandHandler`, etc. | One handler per mutation |
| Application | `UpdateBlockPresetValidator` | Checks lock state via `ISessionRepository.GetActiveAsync` + `IPlannedBlockRepository.HasFutureBlocksForPresetAsync` (same two checks `DeleteBlockPresetValidator` uses, kept as separate inline checks rather than extracted — no third caller yet to justify a shared abstraction). If locked, fetches the current `BlockPreset` and applies the private `IsAddOnlyChange` comparison: `Name`/`WebsiteFilterType`/`PresetType`/`Tags` must be identical, `BlockedApps`/`Websites` must be supersets of their current contents. Not locked → any change passes. |
| UI | `BlockPresetPageViewModel` | Full CRUD UI; file picker for app selection; type filter |

---

## Daily Usage *(Domain shape designed — not yet built)*

**Definition:** An append-only fact recording actual seconds of active use on a specific website or application, per calendar day. `DailyUsage` is the universal usage-tracking substrate — it records every site visited and every app used, unconditionally, not just resources with a configured `UsageCeiling`. It exists independently of `BlockPreset`, `PlannedBlock`, `Session`, and `NenContract` — it belongs to nothing but the day and the resource.

**Why it tracks everything, not just ceiling-configured entries:** The same data feeds two consumers — `UsageCeiling` enforcement (which only cares about resources someone has capped) and the future Insights page (which wants total time-per-site and time-per-app regardless of whether a cap exists). Scoping collection down to only configured entries would starve Insights of data for everything else.

**Idle detection:** Time only accrues while the user is genuinely active, not merely while a tab/app has focus. Web: `chrome.idle`, with an exception so passive media consumption (an audible or playing tab) isn't miscounted as idle. Native apps: `GetLastInputInfo`; the equivalent per-process audio-playing exception is materially harder to build (would need Windows Core Audio Session APIs) and is an open question for whenever this is built — apps may ship with plain idle-timeout and no media exception initially.

**Ubiquitous language rules:**
- Usage *accrues* on a resource; it is *recorded*, matching the existing `IBlockedAttemptTracker` vocabulary.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `DailyUsage` entity | Append-only fact, keyed by `(Date, ResourceName, UsageResourceType)`; accumulates seconds. Mirrors `BlockedAttempt`'s shape. |
| Domain | `UsageResourceType` enum | `App` \| `Website` — mirrors `BlockedAttemptType` |
| Application | `IUsageTracker` interface | Contract for recording usage events; analogous to `IBlockedAttemptTracker` |
| Infrastructure | `IUsageTracker` implementation | Persists `DailyUsage` rows, debounced/batched (same pattern as `BlockedAttemptTracker`'s 2-second debounce) rather than written per tick. **Not yet built.** |
| Infrastructure | Extension tab-focus-time tracking | `background.js` gains focus/idle tracking and calls a new `/report-time` endpoint, distinct from the existing `/report` (which only logs blocked attempts). **Not yet built.** |
| Infrastructure | Native foreground-app tracking | New polling loop in `WindowsEnforcementService` (or a sibling service), same cost profile as the existing 2-second kill loop. **Not yet built.** |

---

## Usage Ceiling *(Domain shape designed — not yet built)*

**Definition:** An ambient, perpetual cap on how much time per day or per week a user may spend on a specific website or application. Unlike `BlockPreset`, it has no calendar involvement, no attachment to any `PlannedBlock` or `Session`, and no relationship to `NenContract` — it is created on the Presets page and stays in effect until edited, paused, or deleted.

**This supersedes the earlier "Time Budget" concept entirely.** The original design put an optional `TimeBudget` on `BlockedSite`, deriving its active window from the owning `PlannedBlock`. That conflated two unrelated things: a schedule-bound hard block (what `BlockPreset` is for) and an ambient, calendar-independent personal limit (what this is for). `BlockPreset` and `BlockedSite` remain pure hard-block only — no budget concept lives on them anymore.

**Precedence rule:** If any currently-active Focus or Background preset hard-blocks a resource, the hard block always wins — the resource is blocked regardless of remaining ceiling, and no usage accrues for it during that window. Outside of any active hard block, the resource is accessible up to its ceiling; once reached for the current period, it is blocked for the rest of that period.

**Two modes, chosen per entry (`CeilingPeriod`):**
- **Daily** — a per-weekday cap shape (e.g. Monday 3h, Tuesday 1h, Wednesday 0h), mirroring `WorkHoursPolicy`'s `{ Default, PerDayExceptions }` resolution pattern.
- **Weekly** — a single aggregate cap tracked cumulatively across the week, mirroring the idea behind `StrictnessRule.PeriodTimeBudget`, applied to resource access instead of block-editing permission.

**Why it is a Domain entity, not a value object:** `UsageCeiling` has independent identity, an independent lifecycle (created, edited, paused, and deleted through its own CRUD, not nested inside another aggregate), and is looked up and referenced by `Id` from `PolicyEngine` on every enforcement tick. This is the same justification that makes `FocusBlock`/`BackgroundBlock` entities rather than value objects — it is a separate, orthogonal question from *why* it carries a domain rule at all (that reasoning mirrors `DailyFloor.IsSatisfiedBy` — the rule belongs with its data, reused wherever exhaustion needs checking, not duplicated across callers).

**Why it stays out of `NenContract`:** `NenContract` is sealed and time-bound, governing edit permission for a committed block schedule with Daily-Floor scoring. `UsageCeiling` is perpetual, unsealed, and governs passive resource access. Coupling them would mean ceilings stop functioning between contracts or without one ever being sealed, contradicting the point of an *ambient* limit. They are sibling concepts.

**Not the same as recurring hard blocks:** "Always block Reddit" or "block Facebook 9–5 daily" is not a `UsageCeiling` — it's a permanent or repeating hard block, which is the separately-tracked **Recurring Blocks** gap (see `TECHNICAL_SPEC_INFRA.md` → Not Yet Built) applied to ordinary `BackgroundBlock`s on the Planner. No relationship between the two features.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `UsageCeiling` entity | `Id`, `ResourceName`, `ResourceType (UsageResourceType)`, `Period (CeilingPeriod)`, cap value(s), `IsPaused`. `IsExhausted(...)` — pure comparison against resolved `DailyUsage` totals for the entry's period, mirrors `DailyFloor.IsSatisfiedBy`. `Pause()` / `Resume()`. No domain events. |
| Domain | `CeilingPeriod` enum | `Daily` \| `Weekly` |
| Domain | `PolicyEngine` (extended) | `ComputeSlice()` gains inputs for active `UsageCeiling` entries and resolved usage totals; folds exhausted ceilings into `ActivePolicySlice.BlockedSites`/`BlockedApps` directly — chosen over a parallel check in `WindowsEnforcementService` specifically to preserve "`PolicyEngine` is the sole owner of all merge rules" (same principle as Gap 2/Gap 5). **Not yet built.** |
| Infrastructure | Enforcement pipeline lifecycle | `ScheduledEnforcementService`'s heartbeat and `WindowsEnforcementService`'s local HTTP API must run for the app's full lifetime rather than starting/stopping with enforcement bursts, since ceilings apply independent of any active Session or Block. **Not yet built.** |
| UI | Presets page, new tab | Working label "Usage Ceilings." Not a `BlockPreset`/`ContractPreset`-style reusable template — a ceiling isn't consumed to create another object, it's a standing, directly-enforced rule. **Not yet built.** |

**Open questions, not resolved in interrogation:**
- Whether specific resources (NenFocus's own process, `localhost`, `SystemSafetyConstants.UnkillableProcesses`) are excluded from `DailyUsage`, or truly everything is logged unconditionally.
- Day/week boundary semantics — reset time, week-start day, timezone handling.
- Exact CQRS shapes for `UsageCeiling` CRUD and usage-summation queries.
- Whether the native per-app audio-playing exception ships in v1.

See `ARCHITECT_GOVERNANCE.md` → Open Gap A for the full interrogation record.

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
| Domain | `PolicyEngine` (`Services/PolicyEngine.cs`) | Sealed implementation; all merge rules live here |
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

## Planner Page *(implemented)*

**What it is:** The weekly calendar view where the user schedules Focus Blocks and Background Blocks. Each column is a day; rows are hour slots from a configurable `FirstHour` to `LastHour`. Tiles represent blocks and are colour-coded: gold (`#B8922A`) for Focus, light grey (`#E8E8F0`) for Background.

**Drag interactions (built 2026-07-03):**

| Gesture | Behaviour |
|---------|-----------|
| Click on empty space | Shows a 1-hour gold ghost at the clicked slot, opens the new-block form pre-filled with that range |
| Drag on empty space | Paints a live gold ghost; on release opens the new-block form pre-filled with the dragged range |
| Drag tile body | Moves the tile; ghost overlay follows pixel-smooth, original tile dims to 30% opacity |
| Drag bottom edge (10 px hot zone) | Resizes end time; ghost overlay extends/shrinks *(commit partially broken — parked)* |
| Drag top edge (10 px hot zone) | Resizes start time; disabled when start is in the past |
| Click tile | Opens edit form |

**Ghost overlay:** A `Border` added directly to the Canvas (`#B8922A`, 90% opacity, `IsHitTestVisible = false`). Shows a live time label (`HH:mm – HH:mm`) that snaps to 15-minute increments. The create ghost (`#B8922A`, 70% opacity) persists after drag release until the form closes or a rebuild fires.

**Lock rules:**
- `IsTileFullyLocked` — tile is completed or is the active session's originating block: no drag at all.
- `IsTileStartLocked` — block start ≤ now: move and top-resize blocked; bottom-resize allowed (GCal behaviour).

**Drag error banner:** Red `DragError` banner at row 1 of the planner grid, bound to `PlannerPageViewModel.DragError`. Surfaces overlap rejections and lock violations.

**Event routing:** All pointer movement is handled on the owner `Canvas` (captured via `e.Pointer.Capture(ownerCanvas)` in `OnTilePointerPressed`). The tile button handles `PointerPressed` and hover-cursor feedback only; `PointerMoved` and `PointerReleased` route to the canvas handlers for both create and tile drags.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| UI | `PlannerPageView.axaml.cs` | All drag state machine, ghost lifecycle, pointer routing |
| UI | `PlannerPageViewModel` | `OpenNewBlockFormWithRangeCommand`, `MoveOrResizeBlockCommand`, `IsTileFullyLocked`, `IsTileStartLocked`, `DragError`, `IsBlockLocked`, `CanSaveBlock` |
| UI | `PlannerDragMath` (`ViewModels/Components/`) | Pure static helper: `YToSnappedTime`, `DeltaYToSnappedMinutes`, `ClampRange`, `WouldCollide` |
| UI | `DragCreatedRange` | Record struct passed from code-behind → ViewModel on drag-create completion |
| UI | `DragMoveResult` | Record struct passed from code-behind → ViewModel on drag-move/resize completion |
| Application | `CreatePlannedBlockCommandHandler` | Rejects Focus Block if it overlaps a running session (in addition to existing Focus-block overlap check) |
| Application | `UpdatePlannedBlockCommandHandler` | Same active-session overlap guard for move/resize |

**Form locking:** Completed blocks and the active session's originating block open read-only in the form. `IsBlockLocked` (= `IsEditing && (IsEditingCompleted || activeSession.OriginatingBlockId == editingId)`) disables Date, End Time, and Preset fields and hides the Save button. `IsBlockLocked` reacts live to session state changes. Note `IsEditingCompleted` is `FocusBlock.RitualCompletedAt.HasValue`-based — it answers "has a Session already been created from this exact block, making rescheduling it dangerous," which is a different, Ritual-specific question from "should this block be deletable" (see `DeletePlannedBlockCommand` below).

**Deletion protection *(built, 2026-07-07)*:** `DeletePlannedBlockCommand` rejects deleting a block if either: (a) `PlannedBlock.HasEnded(now)` is true — the block's window has fully closed, so it's a historical record (Insights depends on a past `BackgroundBlock`'s row being "the complete record," and the Missed Session feature depends on a missed `FocusBlock` never being deleted or altered), or (b) the block is the active session's `OriginatingBlockId`. Deliberately **broader** than the UI's `IsEditingCompleted`/Ritual-completion check: a Focus Block that was *missed* (window closed, Ritual never completed) and any past Background Block (which never has a Ritual at all, so `IsEditingCompleted` is always false for it) were previously deletable with no protection anywhere, UI or backend — `HasEnded` closes both, since it's defined once on the shared `PlannedBlock` base and answers the same question for either block type. Applies uniformly whether the block is an ordinary uncommitted block or one linked to a Nen Contract — `ContractId` plays no part in the check.

**`CanDeleteFromForm` references the real rule instead of deriving its own *(fixed, 2026-07-07)*:** Previously `CanDeleteFromForm => IsEditing` — the Delete button was fully clickable regardless of lock state, a real, reachable gap, not just a theoretical one (see `DESIGN_GAP_HISTORY.md` Gap 9). The fix deliberately did not just gate it on `IsBlockLocked`, since that flag answers the Ritual-completion question, not the deletion question. Instead: `PlannedBlockDto` gained a `HasEnded` field, computed once in `GetPlannedBlocksForDayQueryHandler` via `block.HasEnded(clock.UtcNow)` — the exact same domain method `DeletePlannedBlockValidator` calls. `PlannerPageViewModel.EditBlockAsync` copies it into `IsEditingBlockEnded`, and `CanDeleteFromForm` is now `IsEditing && !IsEditingBlockEnded && activeSession?.OriginatingBlockId != editingBlockId` — composing two values the ViewModel *references* (one from the DTO, one from the live `ISessionStateService` noticeboard), never deriving a business verdict from raw `Start`/`End`/session data itself. This mirrors `MODULE_STANDARDS.md`'s ViewModel Forbidden Zone principle ("a value that requires calculation is computed by the Application layer and exposed as a ready-to-bind property") — the same principle `IsBlockLocked`/`IsEditingCompleted` already followed correctly; `CanDeleteFromForm` just hadn't been brought in line with it until now.

**`TryParseFormTime`:** Static helper on `PlannerPageViewModel` that special-cases `"24:00"` (rejected by `TimeSpan.TryParse`) to support end-of-day block scheduling.

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

**Current state:** App process kills are tracked during Ritual sessions via `IBlockedAttemptTracker.RecordAppBlockedAsync()`, called from `WindowsEnforcementService.KillBlockedProcessesImmediate()`. Website blocked attempts are not yet tracked (the `/report` endpoint is not yet implemented). Background Block attempts are not yet tracked.

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

## Nen Contract *(Domain layer complete; Application layer in progress — Seal, lifecycle-sweep, Break, Extend, and deficit/completed-hours query slices built; Infrastructure and UI not yet built)*

**Definition:** A sealed, time-bound commitment the user makes to a schedule of Focus Blocks and Background Blocks over a contiguous date range, with self-imposed rules governing how much the schedule can be changed during the commitment period. The name is inspired by the concept of binding agreements — the user is making a contract with themselves.

**Two creation paths in the Planner wizard:**

1. **Simple Contract** — Pick from one of four named presets, schedule blocks, seal. No tuning knobs. The floor is automatically set to whatever Focus hours the user planned per day — each day's floor = that day's total scheduled Focus hours. Rest days are automatically derived (days with zero planned hours have a floor of zero). User cannot adjust strictness, work hours, or allow-cancellation individually. The four simple presets are:

| Preset | Strictness Rule | Allow Cancellation | Mental Model |
|---|---|---|---|
| **Lockdown** | `Locked` | No | "This schedule is law. I will not touch it." |
| **Daily Flex** | `DailyFlexibility` | Yes | "I can shuffle blocks around within each day." |
| **Week Study** | `PeriodTimeBudget` | Yes | "Hours can move across days, but the total must balance." |
| **Light Touch** | `DailyFlexibility` | Yes | "Just track my performance. No real restrictions." |

2. **Advanced Contract** — Every lever exposed. User configures strictness rule, daily floor, work hours, rest days, allow-cancellation, and (future) change window. The user can save the configuration as a **Contract Preset** at the end of the wizard for reuse in future contracts. Advanced is the only path that exposes Work Hours.

**Contract Presets *(Application/Domain/Infrastructure built 2026-07-08; wizard UI not yet built)*:** A saved, reusable template of Nen Contract configuration *rules only* — `Name`, `StrictnessRule`, a single `DailyFloor` value, a single optional `WorkHours?` value, `AllowCancellation`. `ContractPreset` is a full Domain `AggregateRoot` (`Entities/ContractPreset.cs`), consistent with every other persisted concept in this codebase — not "an Infrastructure row," an earlier imprecise description now corrected. Rest days, work-hours per-day exceptions, and the date range are deliberately **not** part of a preset: all three are date-bound and can't generalize across the arbitrary future date range a preset gets applied to — they're always supplied fresh on the Planner page when the user seals, whether starting from a preset or not. Copied by value into the wizard's draft fields when selected ("Start from preset") — the resulting `NenContract` stores its own independent copy of every rule, not a live reference back to the preset. `NenContract.ContractPresetId (Guid?)` records which preset (if any) a contract was sealed from, purely so preset deletion can check for a live commitment — see the deletion-guard row below. Creation path: Planner wizard → "Save as preset" at the end (UI not yet built). Management: Presets page → "Contract Presets" tab (view, edit, delete — UI not yet built).

**Simple vs Advanced — which contract fields are user-configured vs. derived:**

| Field | Simple | Advanced |
|---|---|---|
| Strictness Rule | Fixed by preset | User picks |
| Daily Floor | Derived: per-day = planned Focus hours for that day | User sets (0–24, fixed across all days) |
| Work Hours | None (no constraint) | User configures (optional) |
| Rest Days | Derived: days with 0 planned hours | User explicitly designates |
| Allow Cancellation | Fixed by preset | User picks |
| Change Window | None | Parked for v1 |

**Aggregate boundary:** `NenContract` is its own aggregate root. It does **not** own `PlannedBlock`s — blocks remain their own aggregate and legally exist outside any contract (uncommitted weeks). The link is a nullable `ContractId` on `PlannedBlock`: `null` means an ordinary, uncommitted block; set means the block is under that contract. Blocks predate contracts, are mutated directly by the existing drag/create/update paths, and the Daily Floor is a **soft, tracked** invariant after seal (flagged and scored as a miss, not hard-blocked) — there is no need for atomic multi-block invariants enforced inside the contract entity. Enforcement of Strictness Rules and Work Hours constraints is applied later by Application-layer guards that load the contract and inspect blocks separately; the domain only provides the rules and pure calculations.

**Scope:** A Nen Contract covers a single contiguous date range (`StartDate`–`EndDate`, inclusive on both ends, `DateOnly`). Contiguity is structural — a single continuous range has no gaps by construction, so no separate contiguity check exists beyond `StartDate <= EndDate`.

**Seal:** The act of finalising a contract. There is no persisted draft state in the domain — a `NenContract` comes into existence only via `NenContract.Seal(...)`, its sole factory method. Seal hard-validates a caller-supplied `DailyFloorPolicy` against a caller-supplied per-day map of **Focus hours only** (Background block hours are never counted toward the floor — Focus-only by design). For every day in `[StartDate, EndDate]`, the policy resolves that day's floor (`DailyFloorPolicy.For(date)`); a day whose resolved floor is `null` is a rest day — **skipped entirely**, not checked against zero. A day with a resolved floor must have planned hours (from the map, defaulting to `0` if absent) that satisfy it, or Seal throws. **In Simple contracts**, the policy has no `Default` — every planned day is given its own explicit floor equal to that day's planned hours (so the check always passes by construction), and any day the caller omits from `PerDayExceptions` is a true rest day. **In Advanced contracts**, the policy typically has a fixed `Default` (e.g. "3 hours every day") applied to every day, with explicit `PerDayExceptions[date] = null` entries for user-designated rest days. On seal, `Status` becomes `Active` if the caller-supplied `sealedAt` is on or after `StartDate`, otherwise `Scheduled` — the domain never reads the clock itself; `sealedAt` is passed in, exactly like `Session.Start(DateTime startedAt)`. Raises `NenContractSealedEvent`.

**Lifecycle:**

```
                            ┌─ Extend ─┐
                            ↓          │
Scheduled ──► Active ──► Completed     │
                │    │                  │
                │    └──► Broken        │
                │                      │
                └──────────────────────┘
                    (Extend pushes EndDate forward)
```

Transitions:
- `Scheduled → Active`: `Activate(DateTime)` — called by heartbeat when `StartDate` arrives.
- `Active → Completed`: `Complete(DateTime)` — called by heartbeat when `EndDate` passes.
- `Active → Broken`: `Break(DateTime)` — user cancels; only valid if `AllowCancellation` was `true` at seal. Raises `NenContractBrokenEvent`. `BreakNenContractCommandHandler` atomically breaks the contract and deletes every one of its linked blocks that hasn't ended yet (`!PlannedBlock.HasEnded(now)`) — one command, one transaction, no choice prompt (decided 2026-07-07, superseding the earlier delete-or-unlink design; see `DESIGN_GAP_HISTORY.md`). Past blocks stay linked for historical accuracy. The domain does not touch blocks — they are a separate aggregate; the handler mutates them via `IPlannedBlockRepository` directly.
- `Active → Active`: `Extend(DateOnly newEndDate, IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByNewDays, DateTime extendedAt, IReadOnlyDictionary<DateOnly, DailyFloor?>? dailyFloorOverridesForNewDays = null)` — pushes `EndDate` forward; re-runs the floor check on the new days only (same rest-day-skip rule as Seal). A new day without an override falls back to the existing policy's `Default` (or is a rest day if there is no `Default`) — Simple-style contracts, which have no `Default`, must supply an explicit override for every new day or it becomes an unintended rest day. Supplied overrides are merged into the contract's `DailyFloorPolicy.PerDayExceptions` going forward. Raises `NenContractExtendedEvent`. Extending from `Scheduled` or `Completed` is not allowed — only an active contract can be extended. The domain remains in `Active` status after extension.

**Strictness Rule (locked at seal, user-configurable):** Modelled as a single enum, not a value-object hierarchy — the four rules are points on one axis of post-seal mutability, and the logic that *interprets* a rule against an edit's context lives in Application-layer guards, not inside the rule itself. The strictness rule is immutable after seal (no mid-contract changes in v1; a future v2 feature may allow tightening only, never loosening — parked).
- **Locked:** No changes allowed during the contract period. Blocks cannot be moved, removed, or shortened. New blocks can still be added.
- **DailyFlexibility:** Within each day, the user can move blocks around, add or remove them freely, but only for the current or upcoming day.
- **DailyTimeBudget:** Blocks can be rearranged within a day, but the total block time for that day must remain the same.
- **PeriodTimeBudget:** Same as daily, but the budget is tracked across the full contract period.

**Daily Floor:** The minimum completed Focus hours required on a given day. `NenContract.DailyFloorPolicy` (a `DailyFloorPolicy` value object) resolves the floor **per day**, not as one number for the whole contract — this was a deliberate redesign (2026-07-07) from an earlier single-scalar `NenContract.DailyFloor` that could not actually support the per-day-varying behaviour described below; see `DESIGN_GAP_HISTORY.md` for the closure record. `DailyFloorPolicy` mirrors `WorkHoursPolicy`'s shape exactly: a `Default (DailyFloor?)` applied to every day plus `PerDayExceptions (IReadOnlyDictionary<DateOnly, DailyFloor?>)` that override it, resolved via `.For(date)`. The underlying pure comparison (`DailyFloor.IsSatisfiedBy(hours)`) is unchanged and still serves both the Seal-time check (against *planned* Focus hours) and the later Day Met evaluation (against *completed* Focus hours) for whatever floor `.For(date)` resolves to. **In Simple contracts**, the policy has no `Default` — every planned day gets its own explicit `PerDayExceptions` entry equal to that day's planned hours: Monday with 2 planned hours has a floor of 2, Tuesday with 3 has a floor of 3. The Application layer passes planned hours as both the "planned" and "floor" values for each day, so the Seal check always passes by construction. **In Advanced contracts**, the user sets one fixed `Default` (e.g., "3 hours every day") checked against each day's planned hours at Seal — days below the floor are rejected — with `PerDayExceptions` reserved for explicitly designated rest days (see below).

**Rest Days:** A day is a rest day when `DailyFloorPolicy.For(date)` resolves to `null` — no obligation, skipped entirely by Seal/Extend validation and trivially met by `IsDayMet`. **In Simple contracts**, rest days are derived automatically: any day the Application layer omits from `PerDayExceptions` (i.e. any day with zero planned Focus hours) is a rest day, since there is no `Default` to fall back to. **In Advanced contracts**, the user explicitly designates rest days in the Planner wizard, which the Application layer encodes as `PerDayExceptions[date] = null` — this overrides the `Default` for that specific day. Rest days are not stored as their own field on the contract entity; they are implicit from how the policy resolves each date. The user may still schedule blocks on rest days voluntarily — they just aren't required.

**Work Hours *(Application-layer wired 2026-07-08 — closes Task #13; wizard UI still not built)*:** A daily time-of-day window (`TimeOnly` start/end) defining where Focus and Background blocks may physically exist on the calendar. Blocks cannot be placed, moved, or resized to any time outside the work hours window. This is distinct from Change Window — Work Hours constrains *where blocks can be*, Change Window constrains *when edits are allowed*. Advanced contracts only; Simple contracts have no work hours constraint.

Per-day exceptions are supported via an `IReadOnlyDictionary<DateOnly, WorkHours?>` — a `null` value means no constraint for that day (free placement), a non-null value overrides the default work hours for that specific date. `SealNenContractCommand` exposes only the Default-window half of this plus `NoWorkHoursDays` (an explicit-null per-day exception list) — deliberately **not** a full per-day-different-window map, even though `WorkHoursPolicy` itself supports one and the domain/EF-mapping cost is already paid (Task #11): `FixedDailyFloorHours`/`RestDays` (the analogous pair for `DailyFloorPolicy`) exposes the *exact same* asymmetry today — one fixed value plus explicit rest-day exemptions, never a different non-null floor per specific day. Matching that existing precedent was judged more consistent than exposing more for Work Hours than Daily Floor already exposes on this same command. `WorkHoursPolicy.NoConstraintExceptions(IReadOnlyCollection<DateOnly>)` mirrors `DailyFloorPolicy.RestDayExceptions` exactly, same reason (fresh-build and future-merge callers share one conversion). The Planner's drag-math (`PlannerDragMath.WouldCollide`) and CreatePlannedBlock/UpdatePlannedBlock guards (`NenContract.AllowsBlockPlacement`, built Slice 6 Step 3) are now fully live — the guard was correct all along, it just had nothing to check against until now.

Domain value object `WorkHours { TimeOnly Start, TimeOnly End }` carries one window; `ContainsRange(DateTime start, DateTime end)` checks standard interval containment (`blockStart >= Start && blockEnd <= End`) — a block ending exactly at the window's close (e.g. a 9-to-5 block against 9:00–17:00 work hours) is valid.

The default window and its per-day exceptions are bundled into one value object, `WorkHoursPolicy { WorkHours? Default, IReadOnlyDictionary<DateOnly, WorkHours?> PerDayExceptions }`, rather than stored as two loose fields on `NenContract` — they are always configured and consumed together (one "Work Hours" section in the Advanced wizard, one JSON blob on a Contract Preset). `WorkHoursPolicy.For(DateOnly date)` resolves the effective window for a day: an exception wins (including an explicit `null` meaning "no constraint that day"), otherwise the default applies, otherwise there is no constraint. This is the single place the exception-with-fallback lookup lives — the Planner drag-math and command handler guards will both call `contract.WorkHoursPolicy?.For(date)?.ContainsRange(start, end) ?? true` rather than reimplementing the lookup. `NenContract.WorkHoursPolicy (WorkHoursPolicy?)` is set via an optional trailing parameter on `Seal(...)`, default `null` (Simple contracts, and any Advanced contract without a work-hours constraint).

**Change Window *(parked — domain value object exists, not wired up in v1)*:** An optional daily time-of-day window (`TimeOnly` start/end) during which edits to *existing* blocks are permitted, plus a configured `UsesPerDay` limit. The domain value object (`ChangeWindow`) is built and answers `Contains(TimeOnly)`, but Application-layer use-count tracking, UI configuration, and enforcement guards are all deferred. Distinct from Work Hours (see above). If implemented later, the Change Window configuration would likely live in the Settings page as a user-wide preference rather than per-contract.

**Strictness tightening mid-contract *(parked for v2)*:** The user may want to tighten the strictness rule mid-contract (e.g., switch from `DailyFlexibility` to `Locked` because they're losing discipline). Only tightening would be allowed — never loosening. Not in v1.

**Day Met:** A contract day is "met" if that day's *completed* Focus hours ≥ that day's resolved Daily Floor — evaluated via `NenContract.IsDayMet(DateOnly date, double completedFocusHours)`, which resolves `DailyFloorPolicy.For(date)` and compares against it (a day with no resolved floor is trivially met — it's a rest day). A missed or late Focus block does **not** fail the day directly; it only costs hours that must be made up elsewhere. Completed hours are summed across all sources: planned-block sessions, make-up block sessions, and **spontaneous sessions** that match a contract preset (see Spontaneous Make-Up below). If the hours are made up, the day is met. The missed/late information lives in the per-block `FocusBlockOutcome` classification — a separate display concern. Summing a day's actual completed Focus hours is an Application-layer computation.

**FocusBlockOutcome:** How a single Focus block resolved — `CompletedOnTime`, `StartedLate`, or `Missed` — classified purely from its scheduled window and ritual-completion state via `IFocusBlockOutcomeClassifier.Classify(scheduledStart, scheduledEnd, ritualCompletedAt, asOf)`. Returns `null` when no verdict has been reached yet (the block is still open — in progress or future — and could still be started late right up to its scheduled end).

**5-minute grace period** (universal — all Focus Blocks, contracted or not) *(built, 2026-07-08 — see `DESIGN_GAP_HISTORY.md` Gap 11 for the closure record; the window was documented well before it was actually implemented in `Classify`)*:
- A ritual completed within 5 minutes after the scheduled start (≤ `scheduledStart + 5 min`, via `FocusBlockOutcomeClassifier.GracePeriodMinutes`) is `CompletedOnTime` — the Ritual takes a moment, and penalizing a 13:03 completion for a 13:00 block is noise.
- `IFocusBlockOutcomeClassifier.CreditedFocusMinutes(scheduledStart, scheduledEnd, ritualCompletedAt, actualElapsedMinutes)` is the completed-hours formula: within the grace period it returns the full scheduled duration regardless of `actualElapsedMinutes` → a 13:00–15:00 block with ritual at 13:03 still credits the full 2 hours toward any Daily Floor; outside the grace period (or missed) it returns `actualElapsedMinutes` as-is. Shares `GracePeriodMinutes` with `Classify` so the two can never disagree about what "within grace" means.
- **Grace-period warning notification** *(Application/Infrastructure — not yet built)*: 3 minutes past the scheduled start, if the Ritual still hasn't been completed, the app fires a toast: "You have 2 minutes to complete the Ritual or this block will be marked as a late start." At 5 minutes, if still uncompleted, the outcome becomes `StartedLate` (or `Missed` if the end passes). The notification is a one-shot — it does not repeat.
- The missed/late boundary remains the block's **scheduled end**, not its start.

**Make-Up Block:** A Focus Block explicitly linked to one or more missed or late Focus Blocks, covering all or part of their deficit. The link is a collection of `MakeUpForEntry` value objects on `PlannedBlock` — empty (never null) means a regular block. Make-up blocks are always Focus Blocks (Background Blocks have no sessions and cannot contribute toward a Focus-hours deficit). **Built** — see the technical mapping rows for `CheckMakeUpForEntryQuery`, `NenContract.CheckMakeUpEligibility`, and the Create/UpdatePlannedBlock guards below.

**Make-up path — Planner "Make Up For" form section *(built)*:** When creating/editing a Focus Block with an active contract and outstanding deficits for the selected preset, the block form shows an optional "Make Up For" section. A checkbox list shows missed blocks filtered to the selected preset, with their remaining deficit. Multi-select lets one block cover multiple misses of the same preset. The block's duration can exceed the total selected deficit.

**Main Page spontaneous make-up prompt — dropped (2026-07-08), not building it.** The original design: when a contract is active with an outstanding deficit, the Main Page would prompt the user to start a make-up session; the Ritual's Ren step would pre-lock to the preset with the largest deficit, Hatsu would show the deficit amount, and the resulting Spontaneous session's partial coverage would reduce the deficit ledger. Fully interrogated and deliberately dropped, not merely deferred — see `ARCHITECT_GOVERNANCE.md` Open Gap D for the full reasoning (why `Session` has no natural anchor for per-block deficit attribution, why every alternative modeling considered either leaked, over-built, or failed to deliver real make-up semantics, and what to build first if this is ever revisited). The Planner form section above remains the only make-up mechanism.

**Make-Up Rules (14 rules, finalised 2026-07-07):**

| # | Rule |
|---|------|
| 1 | A missed or late Focus Block creates a **deficit** = `scheduledDuration − actualCompletedMinutes` (full scheduled duration for a miss; partial for a late start). |
| 2 | Deficit is tracked **per missed block**, not per preset — each missed block is an independent row in the deficit ledger. |
| 3 | A make-up block links to one or more missed blocks via `IReadOnlyList<MakeUpForEntry> MakeUpForEntries` on `PlannedBlock`. |
| 4 | All linked missed blocks must share the **same `BlockPresetId`** as the make-up block. The form filters the available missed-block checklist to the selected preset. |
| 5 | One make-up block can cover multiple missed blocks of the same preset (e.g., two 30-minute misses → one 60-minute make-up). |
| 6 | A make-up block's duration can **exceed** the total selected deficit — surplus is fine and counts toward the Daily Floor. |
| 7 | Surplus time counts toward the Daily Floor but is **not** allocated to any specific missed block. |
| 8 | Under **`DailyTimeBudget`** strictness: a missed block can only be made up on the **same calendar day**. |
| 9 | Under **`PeriodTimeBudget`** strictness: a missed block can be made up on **any day** within the contract period. |
| 10 | Under **`Locked`** or **`DailyFlexibility`**: the make-up concept does not apply. `Locked` permits no edits; `DailyFlexibility` allows freely moving blocks so "missed" is moot — the user just reschedules. |
| 11 | A missed block's `FocusBlockOutcome` stays `Missed` (or `StartedLate`) regardless of whether it was later made up. Insights shows both: the original miss AND the successful make-up. |
| 12 | A make-up block gets its own `FocusBlockOutcome` (`CompletedOnTime` / `StartedLate` / `Missed`) — classified independently. |
| 13 | A make-up block that itself is missed does **not** create additional deficit. The original missed block's deficit simply remains uncovered. No "make up the make-up" chain. |
| 14 | If the user has missed blocks with **different presets**, they choose which preset to make up for each time they create a block. The form only shows missed blocks matching the currently selected preset. |

**Broken → blocks handling *(built)*:** `BreakNenContractCommandHandler` calls `Break()`, saves, then loads every block linked to the contract and deletes each one for which `HasEnded(now)` is false — no delete-vs-unlink choice. This supersedes the earlier design (an event handler prompting a delete-or-unlink choice) — the architect judged that a contract cancellable at all is very likely being abandoned wholesale, and the "keep my schedule but drop the tracking" case, if it turns out to be real, is cheap to add later as long as nothing else depends on unlink existing today. One atomic command, not an event handler plus a follow-up command: doing both the status flip and the block cleanup in one handler means no "broken but blocks unresolved" state can ever be persisted, mirroring the same atomicity reasoning already applied to Seal. Blocks that have already ended (`End < now`) stay linked to the broken contract for historical accuracy, untouched. The domain's `Break()` method does not touch blocks — they are a separate aggregate; the handler mutates them via `IPlannedBlockRepository` directly.

**Block-preset deletion guard *(not yet built)*:** A preset cannot be deleted while referenced by an active or scheduled contract, a future planned block, or an active session. This is an Application-layer concern.

**Settings integration *(future)*:** In the Settings page, the user will be able to pre-save default strictness rules automatically applied when creating a new Nen Contract. The Change Window (if implemented later) would also be configured here as a user-wide preference rather than per-contract. Work Hours configuration is per-contract (set in the Planner wizard), not a Settings-level preference — each contract can have different work hours.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `NenContract` aggregate (`Entities/NenContract.cs`) | Owns `Name`, `Goal`, `StartDate`/`EndDate` (`DateOnly`), `StrictnessRule`, `DailyFloorPolicy`, `WorkHoursPolicy?`, `ChangeWindow?`, `AllowCancellation`, `Status`. Factory `Seal(...)`; transitions `Activate(DateTime)`, `Complete(DateTime)`, `Break(DateTime)`, `Extend(DateOnly, IReadOnlyDictionary<DateOnly, double>, DateTime, IReadOnlyDictionary<DateOnly, DailyFloor?>?)`; pure calculations `IsDayMet(DateOnly date, double completedFocusHours)`, `IsDueForActivation(DateOnly asOf)`, `IsDueForCompletion(DateOnly asOf)` — the latter two are the canonical definitions the lifecycle-sweep repository queries must match exactly, not reinvent. `Seal` and `Extend` floor-check every day in range via `DailyFloorPolicy.For(date)` — a day resolving to `null` is a rest day and is skipped. `Extend` requires `Status == Active` and `newEndDate` strictly after the current `EndDate`; any supplied floor overrides for the new days are merged into `DailyFloorPolicy.PerDayExceptions`. |
| Domain | `NenContractGoal` value object (`ValueObjects/NenContractGoal.cs`) | Independent of `SessionGoal` by design — a session goal and a contract goal are distinct domain concepts that only coincidentally share validation rules today |
| Domain | `DailyFloor` value object (`ValueObjects/DailyFloor.cs`) | Wraps required daily Focus hours (`0`–`24`) for a *single* day; `IsSatisfiedBy(double)` is the sole pure comparison. No knowledge of dates or per-day variation — that's `DailyFloorPolicy`'s job. |
| Domain | `DailyFloorPolicy` value object (`ValueObjects/DailyFloorPolicy.cs`) | `{ DailyFloor? Default, IReadOnlyDictionary<DateOnly, DailyFloor?> PerDayExceptions }` — mirrors `WorkHoursPolicy`'s shape exactly. `For(DateOnly date)` resolves the effective floor (an exception wins, including an explicit `null` meaning "rest day," otherwise `Default`, otherwise no floor at all — also a rest day). Named factories: `DailyFloorPolicy.None` (no floor anywhere), `DailyFloorPolicy.Fixed(DailyFloor, IReadOnlyCollection<DateOnly>? restDays)` (one floor everywhere except the given rest days — the Advanced shape, built from `RestDayExceptions`), `DailyFloorPolicy.PerDay(IReadOnlyDictionary<DateOnly, double> hoursByDay)` (no `Default`; each day gets its own floor from the map, absent day = rest day — the Simple shape, built from `PerDayExceptionsFrom`). The two conversions are also exposed standalone — `RestDayExceptions(IReadOnlyCollection<DateOnly>)` and `PerDayExceptionsFrom(IReadOnlyDictionary<DateOnly, double>)` — so `ExtendNenContractCommandHandler` (which merges new-day exceptions into an *existing* policy rather than building a fresh one) reuses the identical conversion instead of re-deriving it. `SealNenContractCommandHandler` calls `Fixed`/`PerDay` rather than constructing the dictionary inline, keeping the two contract-mode constructions discoverable from the VO's own interface. Stored as `NenContract.DailyFloorPolicy`, set at `Seal(...)`. |
| Domain | `WorkHours` value object (`ValueObjects/WorkHours.cs`) | `{ TimeOnly Start, TimeOnly End }`; `ContainsRange(DateTime start, DateTime end)` checks standard interval containment (`blockStart >= Start && blockEnd <= End`) — a block ending exactly at the window's close is valid. |
| Domain | `WorkHoursPolicy` value object (`ValueObjects/WorkHoursPolicy.cs`) | `{ WorkHours? Default, IReadOnlyDictionary<DateOnly, WorkHours?> PerDayExceptions }`; `For(DateOnly date)` resolves the effective window (exception, including explicit `null`, wins over the default). Stored as `NenContract.WorkHoursPolicy`, set via an optional trailing param on `Seal(...)`. |
| Domain | `ChangeWindow` value object (`ValueObjects/ChangeWindow.cs`) | **Parked for v1.** Wraps `Start`/`End` (`TimeOnly`) and the configured `UsesPerDay` limit; `Contains(TimeOnly)` is the sole pure containment check. Distinct from `WorkHours` — Change Window gates *when edits are allowed*, Work Hours gates *where blocks can exist*. |
| Domain | `MakeUpForEntry` value object (`ValueObjects/MakeUpForEntry.cs`) | `{ MissedBlockId (Guid), MinutesCovered (double) }` — immutable; validated that `MinutesCovered > 0`. The Application layer enforces that `MinutesCovered` does not exceed the referenced block's remaining deficit and that all linked blocks share the same preset as the make-up block. |
| Domain | `FocusBlockOutcome` enum (`Enums/FocusBlockOutcome.cs`) | `CompletedOnTime`, `StartedLate`, `Missed` — per-block display classification, independent of the day verdict |
| Domain | `IFocusBlockOutcomeClassifier` / `FocusBlockOutcomeClassifier` (`Services/`) | Stateless domain service, same pattern as `IPolicyEngine`/`PolicyEngine`. `GracePeriodMinutes` (`const int = 5`) is the single source of truth both methods below share. `Classify(scheduledStart, scheduledEnd, ritualCompletedAt, asOf)` returns `FocusBlockOutcome?`; `null` means no verdict yet (still open); a ritual completed within `GracePeriodMinutes` after the scheduled start (≤ `scheduledStart + 5 min`) is `CompletedOnTime`. The missed/late boundary remains the block's scheduled **end**. `CreditedFocusMinutes(scheduledStart, scheduledEnd, ritualCompletedAt, actualElapsedMinutes)` returns the full scheduled duration when within grace, otherwise `actualElapsedMinutes` as-is — the completed-hours formula feeding `IsDayMet`. Registered in `Application/DependencyInjection.cs`; consumed by `GetNenContractDeficitsQueryHandler` and `GetNenContractCompletedFocusHoursQueryHandler`. |
| Domain | `StrictnessRule` enum (`Enums/StrictnessRule.cs`) | `Locked`, `DailyFlexibility`, `DailyTimeBudget`, `PeriodTimeBudget`. Immutable after seal in v1; tightening-only change parked for v2. |
| Domain | `NenContractStatus` enum (`Enums/NenContractStatus.cs`) | `Scheduled`, `Active`, `Completed`, `Broken` |
| Domain | `NenContractSealedEvent`, `NenContractActivatedEvent`, `NenContractCompletedEvent`, `NenContractBrokenEvent`, `NenContractExtendedEvent` (`Events/`) | Raised from the corresponding `NenContract` methods, same mechanism as `Session`'s events. `NenContractExtendedEvent` carries `ContractId`, `OldEndDate`, `NewEndDate`; the timestamp is the inherited `DomainEvent.OccurredAt`, same as its siblings. |
| Domain | `PlannedBlock.ContractId (Guid?)` | Additive; `null` = uncommitted block, set = block is under that contract; `AssignContract(Guid?)` mirrors `AssignPreset(Guid)`; `FocusBlock.Create`/`BackgroundBlock.Create` gained a trailing optional `contractId` param, default `null` |
| Domain | `PlannedBlock.HasEnded(DateTime asOf)` | `End < asOf` — canonical definition of "past," mirroring the ubiquitous-language phrase already used for Broken-contract cleanup and Make-Up rules. Added alongside the Break slice rather than inlining `b.End < now` ad hoc in the handler. |
| Domain | `PlannedBlock.MakeUpForEntries` | `IReadOnlyList<MakeUpForEntry>`; empty (never null) = regular block; populated = make-up block covering those specific deficits. Only `FocusBlock.Create` exposes a trailing optional `makeUpForEntries` param — `BackgroundBlock.Create` does not, since make-up blocks are always Focus Blocks. EF Core ignores this property for now (`NenFocusDbContext.OnModelCreating`) — persistence as an owned JSON column is Infrastructure work, not yet built. |
| Domain | `ContractPreset` entity (`Domain/Entities/`) | **Built (2026-07-08).** Saved, reusable template of Nen Contract configuration *rules only* — `Name`, `StrictnessRule`, a single `DailyFloor` value (not `DailyFloorPolicy` — no rest days), a single optional `WorkHours?` value (not `WorkHoursPolicy` — no per-day exceptions), `AllowCancellation`. Rest days, work-hours per-day exceptions, and the date range are deliberately **not** part of a preset — all three are date-bound and can't generalize across the arbitrary future date range a preset gets applied to; they're always supplied fresh on the Planner page when the user seals, whether starting from a preset or not. `AggregateRoot`, matching every other persisted concept in this codebase, even though (like `FocusBlock`/`BackgroundBlock`) it never raises a domain event, since nothing would consume one. Not a `BlockPreset` mirror: no growing collections needing incremental `Add`/`Remove` invariants, so `Update(...)` is a single wholesale replace, not granular mutators. |
| Domain | `NenContract.ContractPresetId (Guid?)`, `NenContract.IsInEffect -> bool` (`Domain/Entities/`) | **Built (2026-07-08).** `ContractPresetId` is additive (new optional trailing param on `Seal(...)`, default `null` — every existing call site keeps compiling unchanged), set only when a contract was sealed via "Start from preset." `IsInEffect` is `Status is Scheduled or Active` — the canonical definition of "a live commitment," same discipline as `IsDueForActivation`/`IsDueForCompletion`: the repository query behind Contract Preset deletion protection (`INenContractRepository.HasContractsInEffectForPresetAsync`) must match it exactly, not reinvent it. Completed and Broken contracts are not in effect and never block preset deletion — mirrors `BlockPreset`'s own future-only guard (past/completed usage never blocks deletion there either). |
| Application | `CreateContractPreset` / `UpdateContractPreset` / `DeleteContractPreset` (`UseCases/ContractPresets/`), `GetAllContractPresetsQuery` (`Queries/GetAllContractPresets/`) | **Built (2026-07-08).** Mirrors `BlockPreset`'s exact CRUD shape and precedent, not the newer NenContract-guard style: name-uniqueness (`ExistsWithNameAsync`) is checked in the **handler**, not the validator, matching `CreateBlockPresetCommandHandler`/`UpdateBlockPresetCommandHandler` precisely. Validators check only structural things (name non-empty, `DailyFloorHours` 0–24, work-hours start/end must both be set or both empty, end after start). `DeleteContractPresetValidator` — unlike the earlier assumption that no guard was needed — checks `!contracts.HasContractsInEffectForPresetAsync(id)`, the one place `NenContract.IsInEffect`'s canonical definition is consumed. No `GetById` query — mirrors `BlockPreset` (only its repository exposes `GetByIdAsync`, used internally by Update/Delete; the UI works off `GetAllContractPresetsQuery`'s DTOs already in memory). **One deliberate departure from the `BlockPreset` precedent**, caught in self-audit: `CreateContractPresetCommandHandler`/`UpdateContractPresetCommandHandler` wrap `DailyFloor`/`WorkHours` construction and the entity `Create`/`Update` call in `try/catch (ArgumentException)`, converting a Domain invariant violation into a clean `Fail(...)` — `CreateBlockPresetCommandHandler` doesn't do this (relies entirely on its validator), which is a latent gap there too, just not one this pass touches. Matches `SealNenContractCommandHandler`'s more recent, more defensive precedent instead: the validator normally prevents this, but nothing in the type system *proves* the validator always ran first (handlers are called directly, unvalidated, in this codebase's own unit tests), so the handler defends its own boundary independently. |
| Infrastructure | `ContractPresetRepository`, `ContractPresetDto`, EF Core config + migration (`AddContractPreset`) | **Built (2026-07-08)** — the first Nen Contract-family slice with real Infrastructure in the same pass (not deferred, unlike `NenContract` itself/Task #11): the entity is small enough (all scalar/single-value fields, no owned per-day dictionaries) that there was no reason to defer it, and — unlike `INenContractRepository` (Gap 12) — nothing outside this slice's own handlers could ever gain a hard, unregistered dependency on `IContractPresetRepository`, so there was no DI-crash risk to weigh against deferring either way. `DailyFloor` maps via `HasConversion` (mirrors `SessionGoal`); `WorkHours?` maps via `OwnsOne` (mirrors `BreakSettings`, EF Core's standard nullable-owned-type handling). `HasContractsInEffectForPresetAsync` currently lives only on `NullNenContractRepository`, returning `false` — correct, not a stub-of-a-stub: no contracts are persisted yet, so nothing can be in effect; the real implementation arrives automatically once Task #11 builds a real `NenContractRepository` against the same interface. |
| Domain | `IPlannedFocusHoursCalculator` / `PlannedFocusHoursCalculator` (`Services/`) | Stateless domain service, same pattern as `IPolicyEngine`. `Calculate(IEnumerable<(DateTime Start, DateTime End)>) -> IReadOnlyDictionary<DateOnly, double>` sums Focus-block ranges into planned hours per calendar day; a range crossing midnight splits proportionally between every day it touches. Caller filters to Focus-only ranges before calling — the calculator itself is agnostic to block type. Reused by both the Seal wizard's live "hours scheduled / left" preview and `SealNenContractCommandHandler`'s real validation, so the two can never disagree. |
| Application | `INenContractRepository` (`Interfaces/`) | `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `SaveAsync` — same shape as sibling repositories. EF Core implementation not yet built. |
| Application | `IPlannedBlockRepository.GetForContractAsync(Guid)` | Added alongside the Seal slice — returns all blocks linked to a given contract; needed to compute planned hours for an already-sealed contract when the user goes back and edits it. |
| Application | `ComputePlannedFocusHoursQuery` / `Handler` (`Queries/ComputePlannedFocusHours/`) | Merges a contract's already-persisted Focus blocks (if `ContractId` is given) with not-yet-saved draft ranges, then delegates to `IPlannedFocusHoursCalculator`. `ContractId: null` is the pre-Seal wizard case (drafts only, no repository call); `ExcludeBlockId` omits a persisted block whose in-progress edit is supplied separately as a draft, avoiding double-counting. One computation, three callers: live wizard feedback, live editing on an existing contract, and Seal itself. |
| Application | `NenContractBlockSpec` (`DTOs/NenContractBlockSpec.cs`) | `{ PresetId, Start, End, IsFocus, Title? }` — one block to create atomically alongside a contract mutation. Lives in the shared `DTOs/` folder, not inside either slice's own folder — same placement convention as `BreakSettingsDto` (used by `StartSessionCommand` from outside `StartSession/`), not the "shared base class for commands/results" pattern `MODULE_STANDARDS.md` forbids. Shared between `SealNenContractCommand` and `ExtendNenContractCommand`; see Gap 10 for the two wrong turns (shared-in-the-wrong-place, then needlessly duplicated) before landing here. |
| Application | `SealNenContractCommand` / `Handler` / `Result` / `Validator` (`UseCases/NenContracts/Seal/`) | **Built.** Atomically creates the `NenContract` and every `NenContractBlockSpec` block together — nothing is persisted until this command commits (Seal atomicity decision, 2026-07-07; see `DESIGN_GAP_HISTORY.md`). Simple vs Advanced is a wizard-only distinction resolved before dispatch: `FixedDailyFloorHours` set = Advanced (`DailyFloorPolicy.Fixed(...)`); left `null` = Simple (`DailyFloorPolicy.PerDay(...)`). Builds the `FocusBlock`/`BackgroundBlock` entities first and reuses `PlannedBlock.OverlapsWith(...)` to reject Focus+Focus overlaps within the batch — not a hand-rolled comparison — before calling the domain; catches `NenContract.Seal(...)`'s `ArgumentException` (a day planned below its floor) and returns it as a `Fail` result rather than duplicating the day-by-day floor loop in the validator. Validator checks structural concerns only (name, date order, floor range, block ordering, preset existence) — business rules live in the handler, matching `CreatePlannedBlockCommandHandler`'s split. `WorkHoursPolicy` is deliberately left out of this first slice (passed as `null` to `Seal`) — Work Hours UI/guard wiring is separate, already-flagged "not yet built" work. |
| Application | `ExtendNenContractCommand` / `Handler` / `Result` / `Validator` (`UseCases/NenContracts/Extend/`) | **Built (2026-07-07).** Same atomicity shape as Seal — creates every new `NenContractBlockSpec` block and extends the contract in one command; reuses `PlannedBlock.OverlapsWith(...)` for the within-batch overlap check. Whether the new days need explicit floors is *derived* from the already-persisted contract, not re-specified by the caller: `contract.DailyFloorPolicy.Default is not null` (Advanced) → new days inherit `Default` automatically, `RestDays` become explicit `null` exceptions via `DailyFloorPolicy.RestDayExceptions(...)`; `Default is null` (Simple) → each new day's floor is derived from its own planned hours via `DailyFloorPolicy.PerDayExceptionsFrom(...)`. Both helpers were extracted from `Fixed(...)`/`PerDay(...)` on the Domain VO itself (not an Application-layer helper) specifically so Extend (which merges into an existing policy's `PerDayExceptions` rather than building a fresh policy) reuses the exact same conversions Seal already established. Validator pre-checks contract exists, is `Active`, and `NewEndDate` is after the current `EndDate` (mirroring `BreakNenContractValidator`'s state-precheck style); handler only catches the domain's `ArgumentException` for a new day planned below its resolved floor — the one thing not cheaply pre-checkable without duplicating the day-by-day loop. |
| Application | `SweepNenContractLifecycleCommand` / `Handler` / `Result` (`UseCases/NenContracts/SweepNenContractLifecycle/`) | **Built.** No input; reads "now" via `ISystemClock`, matching `SweepMissedFocusBlocksCommand`'s precedent. Fetches contracts due for activation/completion via `INenContractRepository`, calls `Activate()`/`Complete()` (which already own their own invariant checks and event-raising), saves, returns the affected contract IDs. No separate per-contract `ActivateNenContractCommand`/`CompleteNenContractCommand` were built — unlike `MarkFocusBlockMissedCommand`, nothing else in the design calls these individually, so a lean single sweep avoids two-command scaffolding nobody uses yet. No manual domain-event publishing in the handler — `TransactionalCommandHandler`/`EfCoreUnitOfWork` (Gap 5) publish `NenContractActivatedEvent`/`NenContractCompletedEvent` automatically once `SaveAsync` registers the change. |
| Application | `BreakNenContractCommand` / `Handler` / `Result` / `Validator` (`UseCases/NenContracts/Break/`) | **Built.** One atomic command — no event handler, no follow-up command. Validator checks the contract exists, is `Active`, and `AllowCancellation`. Handler calls `contract.Break(now)`, saves, then deletes every block linked to the contract for which `HasEnded(now)` is false. Confirmed (2026-07-07) that `DeletePlannedBlockCommand` has no business logic beyond a bare repository call, so the handler calls `IPlannedBlockRepository.DeleteAsync` directly rather than composing through it — composing would have added an indirection hop with nothing behind it, the same "shallow module chain" problem ruled out for the event-handler design this superseded. |
| Infrastructure | `NullNenContractRepository` (`Persistence/Repositories/`) | **Stopgap (2026-07-08) — see `DESIGN_GAP_HISTORY.md` Gap 12.** Registered as the DI implementation of `INenContractRepository` purely so validators/handlers that depend on it can be constructed at all — every lookup returns null/empty, every write is a no-op. Not a real implementation; delete once the real one lands. |
| Infrastructure | `NenContractRepository`, EF Core config + migration (`AddNenContractPersistence`) (`Persistence/Repositories/`, `NenFocusDbContext.cs`) | **Built (2026-07-08) — closes Task #11, replaces `NullNenContractRepository` (Gap 12).** `NenContract.DailyFloorPolicy`/`WorkHoursPolicy` each map to one JSON column via `HasConversion` (Default + PerDayExceptions serialized together, `DateOnly` keys as `"yyyy-MM-dd"` strings) — deliberately **not** split into an `OwnsOne` for `Default` plus a separate JSON column for `PerDayExceptions`: both fields resolve together through `For(date)` as one cohesive VO, and nothing today needs `Default` independently queryable, so splitting them would be an arbitrary division with no payoff. `PlannedBlock.MakeUpForEntries` un-`Ignore()`d, same JSON-column pattern. `ChangeWindow` stays `Ignore()`d — still genuinely parked, no consuming Application logic anywhere (unlike `WorkHoursPolicy`/`DailyFloorPolicy`/`MakeUpForEntries`, which already have real guards built against them without UI). `GetDueForActivationAsync`/`GetDueForCompletionAsync`/`HasContractsInEffectForPresetAsync` each mirror their canonical entity-level definition (`IsDueForActivation`/`IsDueForCompletion`/`IsInEffect`) exactly, per the established discipline. `SaveAsync` mirrors `PlannedBlockRepository.SaveAsync`'s exact shape (DB existence check, not an `Entry().State` check) rather than inventing a new pattern — caught in self-audit, since the more naive approach would mis-handle a detached entity whose `Id` already exists in the store. |
| Infrastructure | `NenContractHeartbeatService` (`Services/`) | **Built (2026-07-08) — closes Task #14.** Thin `BackgroundService` shell mirroring `ScheduledEnforcementService` exactly: per-tick `IServiceScopeFactory.CreateScope()`, resolves `ICommandHandler<SweepNenContractLifecycleCommand, SweepNenContractLifecycleResult>`, dispatches, try/catch-and-log so one bad tick never kills the loop, `Task.Delay` loop. Deliberately carries **no** logic of its own beyond that shell — all real behavior already lives in the tested `SweepNenContractLifecycleCommandHandler`/`NenContract` domain methods, matching this codebase's established pattern of not unit-testing the hosted-service shell directly (`ScheduledEnforcementService` has none either). Interval is 1 minute, not `ScheduledEnforcementService`'s 5 seconds — deliberately coarser, since this only cares about calendar-day boundaries (Scheduled→Active, Active→Completed), not session-level timing; a startup-only sweep (mirroring `SweepMissedFocusBlocksCommand`'s dispatch-once-via-`SessionStateService.InitializeAsync` precedent) was considered and rejected, since a day-boundary crossing can happen at any point during a long-running session, not just at launch. |
| Infrastructure | `CompositionRootTests` (`Tests/Infrastructure/`) | **Built (2026-07-08), added proactively alongside Task #14.** Builds the real `AddInfrastructure(...)` composition root (plus `AddLogging()`, which `Host.CreateDefaultBuilder` provides automatically in the real app but a bare `ServiceCollection` doesn't) with `ValidateOnBuild = true` **and** `ValidateScopes = true`, so both a missing DI registration (Gap 12's failure mode) and a captive-dependency lifetime mismatch (Gap 14 — see `DESIGN_GAP_HISTORY.md`) fail a test immediately, for *any* future service, not just the ones already found. |
| Domain | `IFocusBlockOutcomeClassifier.CreditedFocusMinutes(...)` *(built, 2026-07-08)* | The completed-hours formula: full scheduled duration when within `GracePeriodMinutes` of `scheduledStart`, otherwise the caller-supplied actual elapsed minutes as-is. Fixes a real gap — see `DESIGN_GAP_HISTORY.md` Gap 11: the grace period itself was documented but missing from `Classify` until this same pass. |
| Application | `GetNenContractDeficitsQuery` / `Handler` (`Queries/GetNenContractDeficits/`) → `FocusBlockDeficitDto` (`DTOs/`) | **Built (2026-07-08).** Given a `NenContractId`, returns every Missed/StartedLate Focus Block still carrying uncovered deficit — the "Make Up For" checklist's data source. Per block: raw deficit = scheduled minutes − actual elapsed minutes (via `Session.GetElapsedTime`, found through the new `ISessionRepository.GetByOriginatingBlockIdAsync`); remaining deficit = raw deficit − the sum of `MinutesCovered` across every *other* Focus Block's `MakeUpForEntries` referencing this one. Blocks with fully-covered or zero deficit are omitted. **Scoped deliberately**: only accounts for coverage recorded via `MakeUpForEntries` on `PlannedBlock`. There is no other coverage source to account for — the spontaneous make-up path was interrogated and dropped (see `ARCHITECT_GOVERNANCE.md` Open Gap D), so this scoping is now permanent, not provisional. |
| Application | `GetNenContractCompletedFocusHoursQuery` / `Handler` (`Queries/GetNenContractCompletedFocusHours/`) | **Built (2026-07-08).** Given a `NenContractId`, returns completed Focus hours per calendar day across the contract's range — feeds `NenContract.IsDayMet(date, hours)`. Sums two sources: every resolved (non-open) Focus Block under the contract via `IFocusBlockOutcomeClassifier.CreditedFocusMinutes(...)` (make-up blocks are ordinary Focus Blocks, so they're included automatically, no special-casing needed), plus every Spontaneous session in the contract's date range whose `BlockPresetId` matches a **Focus** preset used by the contract's blocks — deliberately excludes Background presets from the match set even though a Spontaneous session could never carry one in practice (Ren only ever offers Focus-type presets); the exclusion makes that true by construction rather than by an unstated assumption a future reader would have to independently know. A block's credited minutes are attributed entirely to its scheduled `Start` date — deliberately **not** split across midnight the way `IPlannedFocusHoursCalculator` splits *planned* hours; splitting *credited* minutes for a grace-period or late-started block is a materially harder, separate question left open. |
| Application | `ISessionRepository.GetByOriginatingBlockIdAsync(Guid)` | Added alongside the deficit/completed-hours slice — the session tied to a specific resolved Focus Block, needed to read its actual elapsed time. |
| Application | `MakeUpForEntryDto` (`DTOs/`), `FocusBlock.AssignMakeUpForEntries(...)` (`Domain/Entities/`) | **Built (2026-07-08), step 1 of 4 for the CreatePlannedBlock/UpdatePlannedBlock guards slice.** `CreatePlannedBlockCommand` gained `ContractId` and `MakeUpForEntries`; `UpdatePlannedBlockCommand` gained only `MakeUpForEntries` — deliberately **no** mutable `ContractId` on Update, since no designed flow re-parents a block to a different contract through the ordinary edit form (only Seal/Extend set it, only Break's cleanup clears it); adding it would risk silently unlinking a block on an unrelated time/preset edit that forgot to pass the current value through. Both validators reject `MakeUpForEntries` on a Background block (Rule 3: make-up blocks are always Focus Blocks) — a type-safety check, not yet the deeper same-preset/deficit/same-day business rules, which are the next step. `FocusBlock.AssignMakeUpForEntries` is a plain setter mirroring `AssignPreset`/`AssignContract` — no validation inside it, matching the existing "Application layer enforces, Domain just stores" split already true for those two. |
| Domain | `NenContract.CheckMakeUpEligibility(DateOnly missedBlockDate, DateOnly proposedDate) -> MakeUpEligibility` (`Domain/Entities/`), `MakeUpEligibility` enum (`Domain/Enums/`) | **Built (2026-07-08).** Pure instance method, same shape as `IsDayMet`/`IsDueForActivation` — entity's own `StrictnessRule` field + primitive `DateOnly` params → result. Deliberately **not** a domain service: unlike `IFocusBlockOutcomeClassifier` (which exists because outcome classification needs a block's schedule *and* a session's `ritualCompletedAt` — two aggregates, no single owner), this rule's only input besides the two dates is a field `NenContract` already owns outright, so a stateless service would be extraction without a reason. Also deliberately **not** promoted onto a `StrictnessRule` value object: unlike `DailyFloor`/`WorkHoursPolicy`, the enum carries no structure of its own — wrapping it in a record just to hang one method off it would be indirection without hiding complexity, failing the Deep Module test in the other direction (empty box, narrow lid). `Locked` → `NotAllowed`. `DailyFlexibility`/`DailyTimeBudget` → `Allowed` only if `proposedDate == missedBlockDate`, else `WrongDay` (DailyFlexibility gets the same-day rule too, since its whole model is same-day rearrangement anyway). `PeriodTimeBudget` → always `Allowed`. |
| Application | `CheckMakeUpForEntryQuery` / `Handler` / `MakeUpForEntryCheckResult` / `MakeUpForEntryMessages` (`Queries/CheckMakeUpForEntry/`) | **Built (2026-07-08), step 2 of 4.** One query, shared by both `CreatePlannedBlockValidator` and `UpdatePlannedBlockValidator` (composed the same way `SweepMissedFocusBlocksCommandHandler` already composes a sibling `ICommandHandler` — injecting a sibling `IQueryHandler`, not a new abstraction). Checks, in order: the referenced block exists, is a `FocusBlock`, and is linked to a contract (else `MissedBlockNotFound`); same preset (Rule 4, else `PresetMismatch`); delegates the strictness/day check to `NenContract.CheckMakeUpEligibility` (`StrictnessDoesNotAllowMakeUp`/`WrongDay`); finally dispatches `GetNenContractDeficitsQuery` and compares `MinutesCovered` against the missed block's remaining deficit (`ExceedsRemainingDeficit` otherwise). On **update**, `ExcludeBlockId` (the block being edited) has its own prior contribution to the same missed block added back into "remaining" before comparing — otherwise the not-yet-persisted old value would still count as "covered," understating how much room the proposed new value actually has. `MakeUpForEntryMessages.For(...)` maps each result to shared wording, centralized (not duplicated per validator) since two validators giving different text for the same failure would be its own small drift risk. |
| Domain | `NenContract.AllowsBlockPlacement(DateOnly date, DateTime start, DateTime end) -> bool` (`Domain/Entities/`) | **Built (2026-07-08).** Same shape as `IsDayMet`/`CheckMakeUpEligibility` — wraps `WorkHoursPolicy?.For(date)?.ContainsRange(start, end)` as an entity method rather than letting Application callers reach two levels into the owned value-object chain themselves. `true` when no `WorkHoursPolicy` is configured (no constraint) or when the block fits within that day's resolved window; `false` otherwise. Currently always resolves `true` in practice — see the Work Hours row below for why. |
| Application | `CreatePlannedBlockValidator` / `UpdatePlannedBlockValidator`, Work Hours guard | **Built (2026-07-08), step 3 of 4.** Both validators gained an `INenContractRepository` dependency; when the command (Create) or the loaded block (Update) carries a `ContractId`, the contract is loaded and `contract.AllowsBlockPlacement(...)` is checked against the block's proposed `Start`/`End`. A missing contract (`GetByIdAsync` returns null) is treated as no constraint, same as a contract with no `WorkHoursPolicy` — consistent with every other guard in this slice treating "can't resolve the referenced entity" as non-blocking rather than a separate error path. **No longer inert**: `SealNenContractCommand` now sets a real `WorkHoursPolicy` when Advanced contracts configure one (Task #13, closed) — this guard has real effect once a contract carries one. |
| Domain | `IContractBlockBatchPreparer` / `ContractBlockBatchPreparer` / `ContractBlockBatchPreparation` (`Domain/Services/`), `ContractBlockBatchOutcome` enum (`Domain/Enums/`) | **Built (2026-07-08), step 4 of 4 — closes the CreatePlannedBlock/UpdatePlannedBlock contract-guards slice.** Stateless and synchronous, same shape as `IFocusBlockOutcomeClassifier`/`IPlannedFocusHoursCalculator` (composes the latter directly). Given raw block specs as primitive tuples (`(Guid PresetId, DateTime Start, DateTime End, bool IsFocus, string? Title)`, not the Application-layer `NenContractBlockSpec` DTO — Domain cannot depend on Application, so callers project to tuples first) plus a target `contractId`, it: constructs `FocusBlock`/`BackgroundBlock` entities, sorts Focus blocks by `Start` and rejects any adjacent-pair overlap (`OverlapsWith`), and computes `PlannedFocusHoursByDay` via `IPlannedFocusHoursCalculator`. Returns a `ContractBlockBatchPreparation` (`Outcome`, `Success` computed from it, `Blocks`, `FocusBlocks`, `PlannedFocusHoursByDay`) — deliberately carries a pure `ContractBlockBatchOutcome` (`Valid`/`OverlapsWithinBatch`), not a baked English string: mirrors `MakeUpEligibility`/`FocusBlockOutcome` (Domain returns a neutral outcome, the Application-layer caller owns turning it into user-facing text). This is what let Seal and Extend restore their originally-distinct failure wording ("in this schedule" vs "in this extension") after the initial version of this refactor collapsed both into one generic string — each handler now maps `OverlapsWithinBatch` to its own message inline, which is correct specifically *because* the two messages should differ; a shared `...Messages` mapper (the `MakeUpForEntryMessages` pattern) would be the wrong tool here since it exists to keep multiple callers *from* diverging, and divergence is the actual goal in this case. Extracted from `SealNenContractCommandHandler` and `ExtendNenContractCommandHandler`, which had converged on this exact sequence (construction + in-batch overlap + floor calc) for the same reason in both places — a genuine Deep Module opportunity, not just adjacent code. Deliberately **not** an Application-layer service: none of this needs IO, so keeping it in Domain matches the pure/impure split the codebase already draws everywhere else (pure cross-cutting logic → Domain Service, IO-touching cross-cutting checks → Application Query). |
| Application | `CheckFocusBlockBatchOverlapQuery` / `Handler` (`Queries/CheckFocusBlockBatchOverlap/`) | **Built (2026-07-08), step 4 of 4.** The IO-dependent half of the same check: given the already-constructed `FocusBlocks` from `IContractBlockBatchPreparer`, checks each against already-persisted blocks via `IPlannedBlockRepository.GetOverlappingAsync(start, end, ritualOnly: true)`, returning `true` on the first hit — same repository method and `ritualOnly: true` shape `CreatePlannedBlockCommandHandler` already uses for its own single-block check, just looped since Seal/Extend submit a batch. Composed by both `SealNenContractCommandHandler` and `ExtendNenContractCommandHandler` the same way `CheckMakeUpForEntryQuery` is composed by the Planner validators — injecting a sibling `IQueryHandler`. No exclusion by contract: two Focus blocks can never occupy the same clock time regardless of which contract (or no contract) owns them, matching `CreatePlannedBlockCommandHandler`'s existing contract-agnostic check. |
| Application | *(not yet built)* | **Contract Preset CRUD:** create (from wizard save), read, update, delete (from Presets page). |
| Application | Block-preset deletion/update guards (`UpdateBlockPresetValidator`, `DeleteBlockPresetValidator`) | **Already covers the Nen Contract case — verified 2026-07-07, no separate work needed.** `HasFutureBlocksForPresetAsync` queries all `PlannedBlock`s by preset ID with `End > now`, with no filter on `ContractId` — a block under an active or scheduled contract is just a `PlannedBlock` like any other, so it was already caught. Delete stays fully blocked while locked; Update was relaxed the same day to allow add-only changes while locked (see `BlockPreset`'s Ubiquitous Language rules above). |
| UI | *(not yet built)* | **Planner:** contract creation wizard with Simple/Advanced toggle; Simple shows preset picker (Lockdown/Daily Flex/Week Study/Light Touch) + date range + block scheduling; Advanced exposes all levers + "Save as preset" at end + "Start from preset" dropdown at top. Active-contract banner (`"Contract: [Name] — Day X/Y — Z of N days behind — Hh deficit"`). "Make Up For" collapsible section in block form. **Presets page:** "Contract Presets" tab (list, edit, delete). **Main Page:** spontaneous make-up prompt. **Insights:** contract recap with make-up chains. |

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
