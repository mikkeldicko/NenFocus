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
| **TimeBudget** | An optional daily cap on a `BlockedSite` entry; limits cumulative access time per day; enforced by blocking the site once the cap is exhausted |
| **BudgetWindow** | The time range within which a site's Time Budget may tick down; derived from the owning Planned Block's start and end times; not a stored field |
| **DailyBudgetUsage** | Append-only entity recording actual seconds spent on a budgeted site per calendar day and preset entry; written by `IBudgetUsageTracker` |
| **IBudgetUsageTracker** | Application-layer interface through which infrastructure records usage events; analogous in pattern to `IBlockedAttemptTracker` |

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

**Time-agnosticism:** A Block Preset is time-agnostic — it carries no schedule, no window, and no knowledge of when it is active. The Budget Window within which a `BlockedSite`'s `TimeBudget` may tick down is derived from the owning `PlannedBlock`'s `Start` and `End` times and is not stored on the preset. This allows the same preset to be reused across multiple planned blocks at different times. `BlockedSite` may optionally carry a `TimeBudget`; a site with no budget is a plain hard block.

**Ubiquitous language rules:**
- A preset is *attached* to a session or block, not *assigned* or *applied*.
- A preset is *locked* when a Focus Block activates — no edits are permitted from the block's designated start time through the end of the session, including the period before the Ritual is completed.
- A preset is *locked* during a session — no edits are permitted while a session using it is active.

**UI display:** On the Block Preset page, Focus and Background presets are displayed together in a single list with a type badge (blue = Focus, green = Background). A filter bar lets the user show All, Focus only, or Background only.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BlockPreset` aggregate (`Entities/BlockPreset.cs`) | Owns the list of `BlockedApp` and `BlockedSite` value objects, the `WebsiteFilterType`, and the preset type (`BlockPresetType.Focus` or `BlockPresetType.Background`) |
| Domain | `BlockedApp`, `BlockedSite` value objects | Immutable; validated at construction. `BlockedSite` optionally carries a `TimeBudget`; a site with no `TimeBudget` is a plain hard block |
| Domain | `WebsiteFilterType` enum | `Blocklist` or `Allowlist` |
| Application | `IBlockPresetRepository` | CRUD persistence contract |
| Application | `CreateBlockPresetCommandHandler`, `UpdateBlockPresetCommandHandler`, etc. | One handler per mutation |
| UI | `BlockPresetPageViewModel` | Full CRUD UI; file picker for app selection; type filter |

---

## Time Budget

**Definition:** An optional daily cap on a `BlockedSite` entry within a `BlockPreset`. A `TimeBudget` limits how much cumulative time per day the user may access a given site while a Planned Block referencing the preset is active. A site with a `TimeBudget` is accessible until the budget is exhausted, at which point it becomes blocked for the rest of the day. A `BlockedSite` with no `TimeBudget` is a plain hard block — the default and current behaviour.

**What a `TimeBudget` carries:** The daily cap only (e.g., 3 600 seconds). Nothing else.

**What a `TimeBudget` does not carry:**
- The Budget Window (when the budget may tick down) — derived from the owning `PlannedBlock`'s `Start` and `End` times; a Planner concern, not stored on the preset.
- The reset time (when the daily budget resets to full) — a user-wide preference in Settings; default midnight.
- The warning threshold (how many minutes before exhaustion to notify the user) — a user-wide preference in Settings; not per-preset.

**Preset type scope:** Time Budgets can be configured on both Focus-type and Background-type Block Presets. They are expected to be more commonly used on Background presets.

**Hard block interaction:** If a site has a `TimeBudget` but is also covered by a separate active hard-block preset (for example, a Background Block that hard-blocks it with no budget), the hard block wins and the budget is not consumed during that period. The budget is only consumed while the site is genuinely accessible — that is, no overlapping preset hard-blocks it.

**Ubiquitous language rules:**
- A budget is *consumed*, not decremented or used up.
- A site is *exhausted* when its budget reaches zero — it is not *blocked* or *expired*.
- The budget *resets*, not refreshes or clears.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `TimeBudget` value object | Carries the daily cap; lives on `BlockedSite`; optional — absence means plain hard block |
| Domain | `DailyBudgetUsage` entity | Append-only record of actual seconds consumed per site, per preset entry, per calendar day; source of truth for remaining budget |
| Application | `IBudgetUsageTracker` interface | Contract through which infrastructure writes usage events; defined in `NenFocus.Application/Interfaces/`; analogous to `IBlockedAttemptTracker` |
| Infrastructure | `IBudgetUsageTracker` implementation | Persists `DailyBudgetUsage` rows; written by `WindowsEnforcementService`'s `/report` handler when the extension reports tab time; **not yet built** |
| Domain | `PolicyEngine` changes | Will read `DailyBudgetUsage` to determine remaining budget; adds exhausted sites to the blocked set in `ComputeSlice()`; **not yet built** |
| Infrastructure | Enforcement wiring | `ScheduledEnforcementService` re-evaluation trigger on budget exhaustion; warning notification before exhaustion; **not yet built** |

---

## Budget Window *(defined — enforcement not yet built)*

**Definition:** The time range within which a site's `TimeBudget` is permitted to tick down. A Budget Window is derived from the owning `PlannedBlock`'s `Start` and `End` times — it is not a stored field on `BlockPreset`, `BlockedSite`, or any other entity. A site's budget can only be consumed while a Planned Block that references the preset is active via `IsActive()`. Outside that block's scheduled window, the site is blocked regardless of remaining budget.

**Not stored:** The Budget Window has no migration, no database column, and no persistent property. It is a computed concept: when a `PlannedBlock` with known `Start` and `End` is currently active, the Budget Window is that block's `[Start, End)` range.

**Which layer derives it:** The `PolicyEngine` is responsible for applying the Budget Window constraint when computing a slice. When `PolicyEngine.ComputeSlice()` evaluates a site's remaining budget against `DailyBudgetUsage`, it does so only for `PlannedBlock` entries where `IsActive()` returns `true`. Sites whose owning block is inactive are hard-blocked regardless of remaining budget. This logic is not yet implemented.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `PlannedBlock.IsActive(DateTime)` | The active-window gate; the Budget Window is `[Start, End)` while `IsActive()` returns `true` — no additional field needed |
| Domain | `PolicyEngine` (future) | Will apply the Budget Window constraint: budget is only consumed for sites in currently-active blocks; **not yet built** |

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
- Time Budget enforcement is not yet implemented. `PolicyEngine` does not yet read `DailyBudgetUsage`. The `/report` endpoint does not yet handle usage reporting from the extension.

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
- **Time Budgets *(not yet implemented)*:** In a future slice, `PolicyEngine.ComputeSlice()` will read `DailyBudgetUsage` to determine remaining budget per site. Sites whose `TimeBudget` has been fully consumed for the current calendar day will be added to the blocked set regardless of `WebsiteFilterType` mode.
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

**Form locking:** Completed blocks and the active session's originating block open read-only in the form. `IsBlockLocked` (= `IsEditing && (IsEditingCompleted || activeSession.OriginatingBlockId == editingId)`) disables Date, End Time, and Preset fields and hides the Save button. `IsBlockLocked` reacts live to session state changes.

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

## Nen Contract *(Domain layer complete — Application, Infrastructure, and UI not yet built)*

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

**Contract Presets *(designed, not yet built)*:** A saved, reusable template of Nen Contract configuration rules. Not a domain aggregate — just a saved configuration row in the database (entity in Infrastructure, managed via Application-layer CRUD). Contains: name, strictness rule, daily floor value, work hours config, allow-cancellation flag, and (future) change window config. Does **not** include block schedules — those are always scheduled by the user. Creation path: Planner wizard → "Save as preset" at the end. Management: Presets page → "Contract Presets" tab (view, edit, delete). The Planner wizard has a "Start from preset" dropdown that pre-fills the form; the user can tweak (Advanced) or seal immediately (Simple).

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

**Seal:** The act of finalising a contract. There is no persisted draft state in the domain — a `NenContract` comes into existence only via `NenContract.Seal(...)`, its sole factory method. Seal hard-validates the Daily Floor against a caller-supplied per-day map of **Focus hours only** (Background block hours are never counted toward the floor — Focus-only by design). **Only days present in the map are floor-checked** — days not in the map are treated as rest days and skipped entirely. The caller (Application layer) decides which days to include. **In Simple contracts**, the per-day floor values are the planned Focus hours themselves (so the check always passes by construction — the caller passes `plannedHours` as both the "hours" and the "floor" for each day). **In Advanced contracts**, a fixed `DailyFloor` value is checked against each day's planned hours. On seal, `Status` becomes `Active` if the caller-supplied `sealedAt` is on or after `StartDate`, otherwise `Scheduled` — the domain never reads the clock itself; `sealedAt` is passed in, exactly like `Session.Start(DateTime startedAt)`. Raises `NenContractSealedEvent`.

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
- `Active → Broken`: `Break(DateTime)` — user cancels; only valid if `AllowCancellation` was `true` at seal. Raises `NenContractBrokenEvent`. An Application-layer event handler prompts the user: delete all upcoming planned blocks, or just unlink them (`ContractId → null`). Past blocks stay linked for historical accuracy. The domain does not touch blocks — they are a separate aggregate.
- `Active → Active`: `Extend(DateOnly newEndDate, IReadOnlyDictionary<DateOnly, double> plannedFocusHoursByNewDays, DateTime extendedAt)` — pushes `EndDate` forward; re-runs the floor check on the new days only (same rules as Seal: only days present in the map are checked). Raises `NenContractExtendedEvent`. Extending from `Scheduled` or `Completed` is not allowed — only an active contract can be extended. The domain remains in `Active` status after extension.

**Strictness Rule (locked at seal, user-configurable):** Modelled as a single enum, not a value-object hierarchy — the four rules are points on one axis of post-seal mutability, and the logic that *interprets* a rule against an edit's context lives in Application-layer guards, not inside the rule itself. The strictness rule is immutable after seal (no mid-contract changes in v1; a future v2 feature may allow tightening only, never loosening — parked).
- **Locked:** No changes allowed during the contract period. Blocks cannot be moved, removed, or shortened. New blocks can still be added.
- **DailyFlexibility:** Within each day, the user can move blocks around, add or remove them freely, but only for the current or upcoming day.
- **DailyTimeBudget:** Blocks can be rearranged within a day, but the total block time for that day must remain the same.
- **PeriodTimeBudget:** Same as daily, but the budget is tracked across the full contract period.

**Daily Floor:** The minimum completed Focus hours required per day; `0` means disabled. The same pure comparison (`DailyFloor.IsSatisfiedBy(hours)`) serves both the Seal-time check (against *planned* Focus hours) and the later Day Met evaluation (against *completed* Focus hours) — intentionally shared, not duplicated. **In Simple contracts**, the floor is per-day and derived from planned hours: Monday with 2 planned hours has a floor of 2, Tuesday with 3 has a floor of 3. The Application layer passes planned hours as both the "planned" and "floor" values in the map, so the Seal check always passes by construction. **In Advanced contracts**, the user sets a fixed `DailyFloor` value (e.g., "3 hours every day") that is checked against each day's planned hours at Seal — days below the floor are rejected.

**Rest Days:** In Simple contracts, rest days are derived automatically: any day with zero planned Focus hours is a rest day (floor = 0). In Advanced contracts, the user explicitly designates rest days in the Planner wizard. Days omitted from the `plannedFocusHoursByDay` map at seal are rest days — they are not floor-checked and carry no minimum-hour obligation. Rest days are not stored on the contract entity; they are implicit from the map the caller supplies. The user may still schedule blocks on rest days voluntarily — they just aren't required.

**Work Hours *(built)*:** A daily time-of-day window (`TimeOnly` start/end) defining where Focus and Background blocks may physically exist on the calendar. Blocks cannot be placed, moved, or resized to any time outside the work hours window. This is distinct from Change Window — Work Hours constrains *where blocks can be*, Change Window constrains *when edits are allowed*. Advanced contracts only; Simple contracts have no work hours constraint.

Per-day exceptions are supported via an `IReadOnlyDictionary<DateOnly, WorkHours?>` — a `null` value means no constraint for that day (free placement), a non-null value overrides the default work hours for that specific date. This allows patterns like "9–5 weekdays, but 11–8 on Wednesdays" or "no work hours on Saturday." The Planner's drag-math (`PlannerDragMath.WouldCollide`) and create/update command handlers will reject placements outside the window — that guard is Application-layer work, not yet built.

Domain value object `WorkHours { TimeOnly Start, TimeOnly End }` carries one window; `ContainsRange(DateTime start, DateTime end)` checks standard interval containment (`blockStart >= Start && blockEnd <= End`) — a block ending exactly at the window's close (e.g. a 9-to-5 block against 9:00–17:00 work hours) is valid.

The default window and its per-day exceptions are bundled into one value object, `WorkHoursPolicy { WorkHours? Default, IReadOnlyDictionary<DateOnly, WorkHours?> PerDayExceptions }`, rather than stored as two loose fields on `NenContract` — they are always configured and consumed together (one "Work Hours" section in the Advanced wizard, one JSON blob on a Contract Preset). `WorkHoursPolicy.For(DateOnly date)` resolves the effective window for a day: an exception wins (including an explicit `null` meaning "no constraint that day"), otherwise the default applies, otherwise there is no constraint. This is the single place the exception-with-fallback lookup lives — the Planner drag-math and command handler guards will both call `contract.WorkHoursPolicy?.For(date)?.ContainsRange(start, end) ?? true` rather than reimplementing the lookup. `NenContract.WorkHoursPolicy (WorkHoursPolicy?)` is set via an optional trailing parameter on `Seal(...)`, default `null` (Simple contracts, and any Advanced contract without a work-hours constraint).

**Change Window *(parked — domain value object exists, not wired up in v1)*:** An optional daily time-of-day window (`TimeOnly` start/end) during which edits to *existing* blocks are permitted, plus a configured `UsesPerDay` limit. The domain value object (`ChangeWindow`) is built and answers `Contains(TimeOnly)`, but Application-layer use-count tracking, UI configuration, and enforcement guards are all deferred. Distinct from Work Hours (see above). If implemented later, the Change Window configuration would likely live in the Settings page as a user-wide preference rather than per-contract.

**Strictness tightening mid-contract *(parked for v2)*:** The user may want to tighten the strictness rule mid-contract (e.g., switch from `DailyFlexibility` to `Locked` because they're losing discipline). Only tightening would be allowed — never loosening. Not in v1.

**Day Met:** A contract day is "met" if that day's *completed* Focus hours ≥ the Daily Floor — evaluated via `NenContract.IsDayMet(double completedFocusHours)`, a single comparison. A missed or late Focus block does **not** fail the day directly; it only costs hours that must be made up elsewhere. Completed hours are summed across all sources: planned-block sessions, make-up block sessions, and **spontaneous sessions** that match a contract preset (see Spontaneous Make-Up below). If the hours are made up, the day is met. The missed/late information lives in the per-block `FocusBlockOutcome` classification — a separate display concern. Summing a day's actual completed Focus hours is an Application-layer computation.

**FocusBlockOutcome:** How a single Focus block resolved — `CompletedOnTime`, `StartedLate`, or `Missed` — classified purely from its scheduled window and ritual-completion state via `IFocusBlockOutcomeClassifier.Classify(scheduledStart, scheduledEnd, ritualCompletedAt, asOf)`. Returns `null` when no verdict has been reached yet (the block is still open — in progress or future — and could still be started late right up to its scheduled end).

**5-minute grace period** (universal — all Focus Blocks, contracted or not):
- A ritual completed within 5 minutes after the scheduled start (≤ `scheduledStart + 5 min`) is `CompletedOnTime` — the Ritual takes a moment, and penalizing a 13:03 completion for a 13:00 block is noise.
- The Application layer rounds `ritualCompletedAt` back to `scheduledStart` in the completed-hours formula when within the grace period → a 13:00–15:00 block with ritual at 13:03 still credits the full 2 hours toward any Daily Floor.
- **Grace-period warning notification** *(Application/Infrastructure — not yet built)*: 3 minutes past the scheduled start, if the Ritual still hasn't been completed, the app fires a toast: "You have 2 minutes to complete the Ritual or this block will be marked as a late start." At 5 minutes, if still uncompleted, the outcome becomes `StartedLate` (or `Missed` if the end passes). The notification is a one-shot — it does not repeat.
- The missed/late boundary remains the block's **scheduled end**, not its start.

**Make-Up Block *(domain model designed — not yet built)*:** A Focus Block explicitly linked to one or more missed or late Focus Blocks, covering all or part of their deficit. The link is a collection of `MakeUpForEntry` value objects on `PlannedBlock` — null or empty means a regular block. Make-up blocks are always Focus Blocks (Background Blocks have no sessions and cannot contribute toward a Focus-hours deficit).

**Two make-up paths (both designed, neither built):**

1. **Planner — "Make Up For" form section:** When creating/editing a Focus Block with an active contract and outstanding deficits for the selected preset, the block form shows an optional "Make Up For" section. A checkbox list shows missed blocks filtered to the selected preset, with their remaining deficit. Multi-select lets one block cover multiple misses of the same preset. The block's duration can exceed the total selected deficit.

2. **Main Page — spontaneous make-up prompt:** When a contract is active and there is an outstanding deficit, the Main Page shows a prompt: e.g., "You're 1.5 hours behind on your Study Month contract. Start a make-up session?" If the user clicks through and enters the Ritual, the **Ren step pre-locks** the preset with the largest deficit (user cannot change it), and the **Hatsu step** displays the deficit amount. The user chooses their own duration freely — they can do a partial make-up (e.g., only 30 minutes of a 1-hour deficit), shrinking the gap without fully closing it. The resulting session is a Spontaneous session (`Origin = Spontaneous`), not linked to any block, but its completed hours count toward the Daily Floor because it uses the matching preset. The deficit ledger is updated to reflect the partial coverage.

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

**Broken → blocks handling *(Application layer, not yet built)*:** When a contract is broken, an Application-layer event handler on `NenContractBrokenEvent` fires. The user is prompted with a choice: (a) delete all upcoming planned blocks that were under this contract, or (b) unlink them (`ContractId → null`) — the blocks remain on the calendar but are no longer governed by contract rules. Past blocks (those with `End < now`) stay linked to the broken contract for historical accuracy regardless of the user's choice. The domain's `Break()` method does not touch blocks — they are a separate aggregate.

**Block-preset deletion guard *(not yet built)*:** A preset cannot be deleted while referenced by an active or scheduled contract, a future planned block, or an active session. This is an Application-layer concern.

**Settings integration *(future)*:** In the Settings page, the user will be able to pre-save default strictness rules automatically applied when creating a new Nen Contract. The Change Window (if implemented later) would also be configured here as a user-wide preference rather than per-contract. Work Hours configuration is per-contract (set in the Planner wizard), not a Settings-level preference — each contract can have different work hours.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `NenContract` aggregate (`Entities/NenContract.cs`) | Owns `Name`, `Goal`, `StartDate`/`EndDate` (`DateOnly`), `StrictnessRule`, `DailyFloor`, `WorkHoursPolicy?`, `ChangeWindow?`, `AllowCancellation`, `Status`. Factory `Seal(...)`; transitions `Activate(DateTime)`, `Complete(DateTime)`, `Break(DateTime)`, `Extend(DateOnly, IReadOnlyDictionary<DateOnly, double>, DateTime)`; pure calculation `IsDayMet(double completedFocusHours)`. `Seal` and `Extend` floor-check only the days present in the caller-supplied map — absent days are rest days and are skipped. `Extend` requires `Status == Active` and `newEndDate` strictly after the current `EndDate`. |
| Domain | `NenContractGoal` value object (`ValueObjects/NenContractGoal.cs`) | Independent of `SessionGoal` by design — a session goal and a contract goal are distinct domain concepts that only coincidentally share validation rules today |
| Domain | `DailyFloor` value object (`ValueObjects/DailyFloor.cs`) | Wraps required daily Focus hours (`0`–`24`); `IsSatisfiedBy(double)` is the sole pure comparison, reused at Seal, Extend, and Day Met evaluation. In Simple contracts the per-day floor values are the planned hours themselves (Application layer passes planned hours as the floor). In Advanced contracts a fixed value is checked against each day. |
| Domain | `WorkHours` value object (`ValueObjects/WorkHours.cs`) | `{ TimeOnly Start, TimeOnly End }`; `ContainsRange(DateTime start, DateTime end)` checks standard interval containment (`blockStart >= Start && blockEnd <= End`) — a block ending exactly at the window's close is valid. |
| Domain | `WorkHoursPolicy` value object (`ValueObjects/WorkHoursPolicy.cs`) | `{ WorkHours? Default, IReadOnlyDictionary<DateOnly, WorkHours?> PerDayExceptions }`; `For(DateOnly date)` resolves the effective window (exception, including explicit `null`, wins over the default). Stored as `NenContract.WorkHoursPolicy`, set via an optional trailing param on `Seal(...)`. |
| Domain | `ChangeWindow` value object (`ValueObjects/ChangeWindow.cs`) | **Parked for v1.** Wraps `Start`/`End` (`TimeOnly`) and the configured `UsesPerDay` limit; `Contains(TimeOnly)` is the sole pure containment check. Distinct from `WorkHours` — Change Window gates *when edits are allowed*, Work Hours gates *where blocks can exist*. |
| Domain | `MakeUpForEntry` value object (`ValueObjects/MakeUpForEntry.cs`) | `{ MissedBlockId (Guid), MinutesCovered (double) }` — immutable; validated that `MinutesCovered > 0`. The Application layer enforces that `MinutesCovered` does not exceed the referenced block's remaining deficit and that all linked blocks share the same preset as the make-up block. |
| Domain | `FocusBlockOutcome` enum (`Enums/FocusBlockOutcome.cs`) | `CompletedOnTime`, `StartedLate`, `Missed` — per-block display classification, independent of the day verdict |
| Domain | `IFocusBlockOutcomeClassifier` / `FocusBlockOutcomeClassifier` (`Services/`) | Stateless domain service, same pattern as `IPolicyEngine`/`PolicyEngine`. `Classify(scheduledStart, scheduledEnd, ritualCompletedAt, asOf)` returns `FocusBlockOutcome?`; `null` means no verdict yet (still open). **5-minute grace period:** a ritual completed within 5 minutes after the scheduled start (≤ `scheduledStart + 5 min`) is `CompletedOnTime` — the Ritual takes a moment and penalizing a 13:03 completion for a 13:00 block is noise. The missed/late boundary remains the block's scheduled **end** |
| Domain | `StrictnessRule` enum (`Enums/StrictnessRule.cs`) | `Locked`, `DailyFlexibility`, `DailyTimeBudget`, `PeriodTimeBudget`. Immutable after seal in v1; tightening-only change parked for v2. |
| Domain | `NenContractStatus` enum (`Enums/NenContractStatus.cs`) | `Scheduled`, `Active`, `Completed`, `Broken` |
| Domain | `NenContractSealedEvent`, `NenContractActivatedEvent`, `NenContractCompletedEvent`, `NenContractBrokenEvent`, `NenContractExtendedEvent` (`Events/`) | Raised from the corresponding `NenContract` methods, same mechanism as `Session`'s events. `NenContractExtendedEvent` carries `ContractId`, `OldEndDate`, `NewEndDate`; the timestamp is the inherited `DomainEvent.OccurredAt`, same as its siblings. |
| Domain | `PlannedBlock.ContractId (Guid?)` | Additive; `null` = uncommitted block, set = block is under that contract; `AssignContract(Guid?)` mirrors `AssignPreset(Guid)`; `FocusBlock.Create`/`BackgroundBlock.Create` gained a trailing optional `contractId` param, default `null` |
| Domain | `PlannedBlock.MakeUpForEntries` | `IReadOnlyList<MakeUpForEntry>`; empty (never null) = regular block; populated = make-up block covering those specific deficits. Only `FocusBlock.Create` exposes a trailing optional `makeUpForEntries` param — `BackgroundBlock.Create` does not, since make-up blocks are always Focus Blocks. EF Core ignores this property for now (`NenFocusDbContext.OnModelCreating`) — persistence as an owned JSON column is Infrastructure work, not yet built. |
| Infrastructure | `ContractPreset` entity | Saved, reusable template of Nen Contract configuration rules. Not a domain aggregate — a simple persisted row. Contains: `Name`, `StrictnessRule`, `DailyFloor`, `WorkHoursPolicy?`, `AllowCancellation`. Does **not** store block schedules. Created from the Planner wizard ("Save as preset"); managed (view/edit/delete) on the Presets page → Contract Presets tab. **Designed, not yet built.** |
| Application | *(not yet built)* | **Seal/Extend commands:** assemble the per-day planned-hours map (Simple: derived; Advanced: user-configured), call `NenContract.Seal(...)` / `Extend(...)`, save. **Activate/Complete commands:** called by heartbeat; wrap `Activate()` / `Complete()`. **Break command + event handler:** calls `Break()`, then prompts user (delete upcoming blocks vs. unlink them); past blocks stay linked. **Deficit computation query:** given `NenContractId`, returns all missed/late Focus Blocks with remaining deficit. **CreatePlannedBlock/UpdatePlannedBlock guards:** validate same-preset constraint on `MakeUpForEntries`, same-day constraint (if `DailyTimeBudget`), **Work Hours compliance** (reject placements where `WorkHoursPolicy.For(date)?.ContainsRange(start, end) == false`), and that each referenced block has uncovered deficit. **Spontaneous make-up path:** `StartSessionCommandHandler` gains deficit awareness. **Completed-hours summation:** formula summing planned-block, make-up, and matching-preset spontaneous sessions for `IsDayMet`. **Contract Preset CRUD:** create (from wizard save), read, update, delete (from Presets page). **Block-preset deletion guard:** reject if preset referenced by active/scheduled contract or future planned block. |
| Infrastructure | *(not yet built)* | EF Core config/migration for `NenContract` (including `WorkHoursPolicy` as owned JSON, and `MakeUpForEntries` as owned JSON on `PlannedBlock` — currently `Ignore()`d in `NenFocusDbContext`); `ContractPreset` entity + config; heartbeat service (`NenContractHeartbeatService`, mirroring `ScheduledEnforcementService`) to call `Activate()`/`Complete()` when dates arrive |
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
