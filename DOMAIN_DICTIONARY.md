# NenFocus — Domain Dictionary

This file is the canonical reference for every domain concept in NenFocus. It exists to eliminate ambiguity between conversations. When a term is used in code, comments, or a design discussion, its meaning here is authoritative.

---

## Quick Reference

| Term | One-line definition |
|------|---------------------|
| **Session** | A bounded focus period; lifecycle `NotStarted → Active → (OnBreak ↔ Active) → Completed` |
| **Ritual** | The four-step wizard (Ten → Zetsu → Ren → Hatsu) that creates a Session |
| **Block Profile** | A reusable template of blocked apps + website policy; typed as Ritual or Background at creation |
| **Planned Block** | Abstract base for `RitualBlock` and `BackgroundBlock`; carries `Start`, `End`, `BlockProfileId`, `IsActive()`, `OverlapsWith()` |
| **Ritual Block** | A planned calendar entry that activates enforcement AND triggers the Nen Ritual at the designated time |
| **Background Block** | A planned calendar entry that activates enforcement at the designated time without triggering the Ritual |
| **Policy Slice** | The merged, computed enforcement state at a given instant; represented as `ActivePolicySlice` (Domain Value Object), produced by `PolicyEngine` |
| **PolicyEngine** | Stateless Domain service (`NenFocus.Domain/Services/`) that computes `ActivePolicySlice` from raw aggregates; sole owner of all merge rules |
| **Impromptu Session** | A Session created by completing the Ritual manually from the Main Page; no Block entity exists; `Origin = Impromptu` |
| **Planned Session** | A Session created via the Ritual triggered by a scheduled Ritual Block; `Origin = Planned` |
| **SessionOrigin** | Enum (`Impromptu` \| `Planned`) that tags every Session with how it was initiated |
| **Blocked Attempt** | An append-only fact recording a user's attempt to access a blocked app or site during a Session |
| **Contract** | *(Future)* A time-bounded commitment (day/week/month) with user-defined strictness rules governing block schedule changes |
| **Enforcement** | OS-level mechanisms (process kill, hosts file, registry, extension) that apply a Policy Slice |
| **Post-Session** | The mandatory screen shown after a Session ends; displays stats, blocked attempts, and an optional reflection |
| **Deep Module** | A module with significant functionality hidden behind a narrow, obvious API |
| **Design Gap** | The distance between visible code and what is needed to implement a feature correctly |
| **TimeBudget** | An optional daily cap on a `BlockedSite` entry; limits cumulative access time per day; enforced by blocking the site once the cap is exhausted |
| **BudgetWindow** | The time range within which a site's Time Budget may tick down; derived from the owning Planned Block's start and end times; not a stored field |
| **DailyBudgetUsage** | Append-only entity recording actual seconds spent on a budgeted site per calendar day and profile entry; written by `IBudgetUsageTracker` |
| **IBudgetUsageTracker** | Application-layer interface through which infrastructure records usage events; analogous in pattern to `IBlockedAttemptTracker` |

---

## App Structure

NenFocus has five pages accessible via the sidebar:

| Page | Purpose |
|------|---------|
| **Main Page** | Home screen; shows "Use Nen" button when idle, active session timer when a session is running |
| **Block Profile Page** | CRUD for Block Profiles; Ritual and Background profiles displayed together with filter option |
| **Planner Page** | Calendar view for scheduling Ritual Blocks, Background Blocks, and (future) Contracts |
| **Insights Page** | Session history, block history, blocked attempt analytics, calendar view of past blocks, usage graphs |
| **Settings Page** | User preferences: break behaviour, strict mode, plan rules, and other enforcement configuration |

---

## Session

**Definition:** A bounded period of intentional focus, created by the user through the Ritual and enforced by the system.

**Lifecycle:** `NotStarted → Active → (OnBreak ↔ Active) → Completed`

**Ubiquitous language rules:**
- A Session is *started*, not *created* or *opened*.
- A Session is *completed* or *auto-ended*, not *closed* or *finished*.
- A Session cannot be paused — only placed *on break*.

**Break behaviour:** When a break reminder fires, a break countdown timer replaces the session timer on the Main Page. Enforcement remains active during breaks — blocked sites and apps stay blocked. The break is a reminder only. A future Settings option will allow the user to choose whether enforcement is paused during breaks.

**Ending early:** Currently the user can end a session before the timer runs out. A future "Strict Mode" setting will prevent this.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `Session` entity (`Entities/Session.cs`) | Owns all state transitions via `Start()`, `StartBreak()`, `ResumeFromBreak()`, `Complete()`; raises domain events internally; carries `Origin (SessionOrigin)` and `OriginatingBlockId (Guid?)` |
| Domain | `SessionStatus` enum | Valid states: `NotStarted`, `Active`, `OnBreak`, `Completed` |
| Domain | `SessionOrigin` enum | Valid values: `Impromptu`, `Planned`; tags every Session with how it was initiated |
| Domain | `SessionGoal`, `SessionDuration`, `BreakSettings` value objects | Immutable configuration attached at creation |
| Application | `ISessionRepository` | Persistence contract; `GetActiveAsync()` is the startup rehydration entry point |
| Application | `ISessionStateService` | Singleton in-memory noticeboard; the single source of truth for the UI's view of the active session |
| Application | `IActiveSessionMonitor` | Singleton countdown engine; owns the timer, exposes `SessionTimeRemaining` / `Progress`, auto-ends via `EndSessionCommandHandler` |
| Application | `StartSessionCommandHandler` | Creates and starts the session, calls `_sessionState.SetActive()`, dispatches `session.DomainEvents` via `IDomainEventPublisher` |
| Application | `EndSessionCommandHandler` | Calls `session.Complete()`, saves, dispatches `session.DomainEvents`; enforcement removal and notification are handled by `SessionEndedEventHandler` |
| Application | `SessionStartedEventHandler` | Computes policy slice, calls `IEnforcementService.ApplyPolicyAsync`, sends start notification |
| Application | `SessionEndedEventHandler` | Calls `IEnforcementService.RemoveEnforcementAsync`, cancels reminders, sends end notification |
| Application | `BreakStartedEventHandler` | Calls `IEnforcementService.RemoveEnforcementAsync`, sends break notification |
| UI | `MainPageViewModel` | Observer only — reads from `ISessionStateService` and `IActiveSessionMonitor`, dispatches commands. "Use Nen" button is hidden while a session is active. |

---

## Ritual

**Definition:** The four-step wizard (Ten → Zetsu → Ren → Hatsu) through which a user consciously creates a Session. The Ritual is a design-time guardrail: it forces deliberate intent before focus begins.

**Lifecycle:** Opened by user → steps navigated → `StartSessionCommand` dispatched on Hatsu completion → Ritual closed → user returns to Main Page with active session.

**Two activation paths:**
- **Impromptu:** User clicks "Use Nen" on the Main Page. All four steps are fully user-controlled.
- **Planned:** A scheduled Ritual Block activates and pushes the user to the Ritual. The Ren and Hatsu steps are constrained (see below).

**Step meanings:**

| Step | Theme | Header Text | Purpose in UI |
|------|-------|-------------|---------------|
| **Ten** | Awareness | "Focus the mind, reflect upon the self, and determine the goal" | Greet the user; prompt internal reflection before continuing. Same for both Impromptu and Planned. |
| **Zetsu** | Silence | "Put it into words" | User writes their session goal into a text box. Same for both Impromptu and Planned. |
| **Ren** | Persistence | "Intensify your will" | **Impromptu:** User selects a Ritual-type Block Profile from their existing profiles. **Planned:** Block Profile is pre-selected and locked (chosen when the Ritual Block was scheduled); user cannot change it. |
| **Hatsu** | Output | "Put it into action" | **Impromptu:** User selects session duration (5 min – 8 hours) and optionally configures break reminders. **Planned:** Duration is pre-determined by the Ritual Block's scheduled end time (displayed as "active until [time]"); user can only configure break reminders. User clicks "Start Session." |

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `StartSessionCommand` / `StartSessionCommandHandler` | Final output of the Ritual; creates and starts the Session |
| UI | `RitualShellViewModel` | Hosts step navigation; owns `CurrentStep` and navigation commands |
| UI | `TenPageViewModel`, `ZetsuPageViewModel`, `RenPageViewModel`, `HatsuPageViewModel` | One ViewModel per step; collects input, validates locally |
| UI | `RitualShellView.axaml` | Full-screen overlay rendered over the Shell when `IsRitualActive = true` |

---

## Block Profile

**Definition:** A reusable, named configuration of blocked applications and a website policy. A Block Profile is typed as either **Ritual** or **Background** at creation time. This type is immutable — to change it, delete and recreate the profile. The type acts as a filter: Ritual profiles only appear when starting a Ritual session (Ren page) or scheduling a Ritual Block (Planner); Background profiles only appear when scheduling a Background Block (Planner).

**Lifecycle:** Created in Block Profiles page (type chosen at creation) → (optionally duplicated or edited) → attached to Session at Ritual (Ren step) or to a Planned Block in the Planner → locked for duration of Session or active Block.

**Website policy:** Each profile carries a `WebsitePolicy` of either `Blocklist` (block specific sites, allow everything else) or `Allowlist` (allow specific sites, block everything else). Both Ritual and Background profiles can use either mode. Both can also specify blocked apps.

**Time-agnosticism:** A Block Profile is time-agnostic — it carries no schedule, no window, and no knowledge of when it is active. The Budget Window within which a `BlockedSite`'s `TimeBudget` may tick down is derived from the owning `PlannedBlock`'s `Start` and `End` times and is not stored on the profile. This allows the same profile to be reused across multiple planned blocks at different times. `BlockedSite` may optionally carry a `TimeBudget`; a site with no budget is a plain hard block.

**Ubiquitous language rules:**
- A profile is *attached* to a session or block, not *assigned* or *applied*.
- A profile is *locked* when a Ritual Block activates — no edits are permitted from the block's designated start time through the end of the session, including the period before the Ritual is completed.
- A profile is *locked* during a session — no edits are permitted while a session using it is active.

**UI display:** On the Block Profile page, Ritual and Background profiles are displayed together in a single list with a type label. A filter option lets the user show only one type.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BlockProfile` aggregate (`Aggregates/BlockProfile.cs`) | Owns the list of `BlockedApp` and `BlockedSite` value objects, the `WebsitePolicy`, and the profile type (Ritual or Background) |
| Domain | `BlockedApp`, `BlockedSite` value objects | Immutable; validated at construction. `BlockedSite` optionally carries a `TimeBudget`; a site with no `TimeBudget` is a plain hard block |
| Domain | `WebsitePolicy` enum | `Blocklist` or `Allowlist` |
| Application | `IBlockProfileRepository` | CRUD persistence contract |
| Application | `CreateBlockProfileCommandHandler`, `UpdateBlockProfileCommandHandler`, etc. | One handler per mutation |
| UI | `BlockProfilePageViewModel` | Full CRUD UI; file picker for app selection; type filter |

---

## Time Budget

**Definition:** An optional daily cap on a `BlockedSite` entry within a `BlockProfile`. A `TimeBudget` limits how much cumulative time per day the user may access a given site while a Planned Block referencing the profile is active. A site with a `TimeBudget` is accessible until the budget is exhausted, at which point it becomes blocked for the rest of the day. A `BlockedSite` with no `TimeBudget` is a plain hard block — the default and current behaviour.

**What a `TimeBudget` carries:** The daily cap only (e.g., 3 600 seconds). Nothing else.

**What a `TimeBudget` does not carry:**
- The Budget Window (when the budget may tick down) — derived from the owning `PlannedBlock`'s `Start` and `End` times; a Planner concern, not stored on the profile.
- The reset time (when the daily budget resets to full) — a user-wide preference in Settings; default midnight.
- The warning threshold (how many minutes before exhaustion to notify the user) — a user-wide preference in Settings; not per-profile.

**Profile type scope:** Time Budgets can be configured on both Ritual-type and Background-type Block Profiles. They are expected to be more commonly used on Background profiles.

**Hard block interaction:** If a site has a `TimeBudget` but is also covered by a separate active hard-block profile (for example, a Background Block that hard-blocks it with no budget), the hard block wins and the budget is not consumed during that period. The budget is only consumed while the site is genuinely accessible — that is, no overlapping profile hard-blocks it.

**Ubiquitous language rules:**
- A budget is *consumed*, not decremented or used up.
- A site is *exhausted* when its budget reaches zero — it is not *blocked* or *expired*.
- The budget *resets*, not refreshes or clears.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `TimeBudget` value object | Carries the daily cap; lives on `BlockedSite`; optional — absence means plain hard block |
| Domain | `DailyBudgetUsage` entity | Append-only record of actual seconds consumed per site, per profile entry, per calendar day; source of truth for remaining budget |
| Application | `IBudgetUsageTracker` interface | Contract through which infrastructure writes usage events; defined in `NenFocus.Application/Interfaces/`; analogous to `IBlockedAttemptTracker` |
| Infrastructure | `IBudgetUsageTracker` implementation | Persists `DailyBudgetUsage` rows; written by `WindowsEnforcementService`'s `/report` handler when the extension reports tab time; **not yet built** |
| Domain | `PolicyEngine` changes | Will read `DailyBudgetUsage` to determine remaining budget; adds exhausted sites to the blocked set in `ComputeSlice()`; **not yet built** |
| Infrastructure | Enforcement wiring | `ScheduledEnforcementService` re-evaluation trigger on budget exhaustion; warning notification before exhaustion; **not yet built** |

---

## Budget Window *(defined — enforcement not yet built)*

**Definition:** The time range within which a site's `TimeBudget` is permitted to tick down. A Budget Window is derived from the owning `PlannedBlock`'s `Start` and `End` times — it is not a stored field on `BlockProfile`, `BlockedSite`, or any other entity. A site's budget can only be consumed while a Planned Block that references the profile is active via `IsActive()`. Outside that block's scheduled window, the site is blocked regardless of remaining budget.

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
| Infrastructure | `ScheduledEnforcementService` | `BackgroundService` heartbeat (5s); calls `ComputeActivePolicySliceQuery`, reads `ActiveBlockType`, calls `IEnforcementService` |
| Extension | `background.js` | Intercepts browser navigation; queries `/check`; calls `/report` (not yet handled server-side) |

---

## Policy Slice

**Definition:** The computed, merged enforcement state at a given instant in time. It is the *answer* to the question "what should be blocked right now?" — derived from all currently active Sessions and Planned Blocks. Represented as `ActivePolicySlice`, a **Domain Value Object** in `NenFocus.Domain/ValueObjects/`, not a DTO.

**Computation rules** (all logic lives in `PolicyEngine.ComputeSlice()`):
- **Apps:** union of all blocked apps across all active block profiles, always a blocklist. Includes `TemporaryBlockedApps` from the active `Session` entity.
- **Websites:** if *any* active profile uses `Allowlist` mode → default-deny, allowed = intersection of all allowlists minus any blocklist entries. Otherwise → block the union of all blocklisted sites. Includes `TemporaryBlockedSites` from the active `Session` entity.
- **Time Budgets *(not yet implemented)*:** In a future slice, `PolicyEngine.ComputeSlice()` will read `DailyBudgetUsage` to determine remaining budget per site. Sites whose `TimeBudget` has been fully consumed for the current calendar day will be added to the blocked set regardless of `WebsitePolicy` mode.
- An `ActivePolicySlice` is considered "empty" only in `Blocklist` mode with no blocked apps or sites. It is *never* empty in `Allowlist` mode.
- **`ActiveBlockType`:** set to `BlockType.Ritual` if any Ritual Block is currently active, `BlockType.Background` if only Background Blocks are active, `null` if enforcement is driven solely by an active Session with no planned block. Ritual always takes precedence over Background when both overlap.

**Overlap rules:**
- Multiple Background Blocks can overlap freely. The net enforcement is the union of all their profiles.
- A Ritual Block can overlap with Background Blocks. The net enforcement is the union of all active profiles. The Ritual is still triggered.
- Two Ritual Blocks cannot overlap. The Planner prevents scheduling conflicts. An Impromptu session cannot be started if it would overlap with an upcoming Ritual Block.

**Why `ActiveBlockType` matters:** `ScheduledEnforcementService` reads this field each heartbeat cycle. If `ActiveBlockType == Ritual` and no Session is active, it calls `_sessionState.SetRitualRequired(true)` to push the user into the Ritual. If `ActiveBlockType == Background`, enforcement is applied silently with no Ritual prompt. This is the correct application of the Ubiquitous Language principle — the difference between a Ritual Block and a Background Block is expressed as a domain enum in the result, not as a boolean flag scattered across callers.

**Source of Truth flow:** `Database → PolicyEngine (Domain) → ActivePolicySlice (VO) → WindowsEnforcementService (OS)`

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `ActivePolicySlice` (`ValueObjects/ActivePolicySlice.cs`) | Immutable Domain Value Object; the canonical enforcement contract. Carries `BlockedApps`, `BlockedSites`, `AllowedSites`, `WebsitePolicyMode`, `ActiveBlockType`, `ActiveSessionId`, `IsEmpty`. |
| Domain | `IPolicyEngine` / `PolicyEngine` (`Services/`) | Stateless domain service; accepts raw `BlockProfile`, `Session`, and `PlannedBlock` collections and produces `ActivePolicySlice`. Owns all merge rules — no merge logic exists anywhere else. |
| Application | `ComputeActivePolicySliceQueryHandler` (`Queries/ComputeActivePolicySlice/`) | Pure orchestrator: fetches entities from three repositories, loads `BlockProfile` aggregates, delegates to `IPolicyEngine.ComputeSlice()`. Returns the `ActivePolicySlice` to callers. |
| Infrastructure | `WindowsEnforcementService` | Consumes `ActivePolicySlice` directly (no DTO) to drive OS-level mechanisms |
| Infrastructure | `ScheduledEnforcementService` | Reads `ActiveBlockType` from the `ActivePolicySlice` each heartbeat to decide whether to trigger the Ritual |

---

## PolicyEngine

**Definition:** The sole Domain service responsible for computing the `ActivePolicySlice`. It is a stateless, synchronous function: given a point in time, a list of `BlockProfile` aggregates, a list of active `Session` entities, and a list of active `PlannedBlock` entities, it returns the complete enforcement state for that moment.

**Why it is a Domain Service (not an Application service):** Policy merge logic — the rules for unioning blocked apps, resolving allowlist/blocklist precedence, and assigning `ActiveBlockType` — is domain logic. It expresses invariants of the domain ("an allowlist profile beats a blocklist profile", "a Ritual Block takes priority over a Background Block") that must be consistent wherever policy is computed. Placing this logic in a Domain service makes it unit-testable without a database and reachable by any future consumer (e.g., a Planner "preview" query).

**Interface contract:**
```csharp
// NenFocus.Domain/Services/IPolicyEngine.cs
ActivePolicySlice ComputeSlice(
    DateTime at,
    IEnumerable<BlockProfile> profiles,
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

**Definition:** The abstract base class for all calendar-scheduled enforcement entries. A `PlannedBlock` has a `Start` time, an `End` time, a `BlockProfileId`, and an optional `Title`. It is never instantiated directly — only its two concrete subtypes (`RitualBlock` and `BackgroundBlock`) are created. The C# class type encodes the block type; there is no stored `BlockType` column.

**Domain behaviour:** `PlannedBlock` carries two domain methods used by both subtypes and the query layer:
- `IsActive(DateTime atTime)` — returns `true` if `atTime` falls within `[Start, End)`.
- `OverlapsWith(PlannedBlock other)` — returns `true` if the two windows intersect; used by the planner to prevent scheduling conflicts.

**Why it exists:** `IPlannedBlockRepository` returns `IReadOnlyList<PlannedBlock>` so that the enforcement pipeline can work with all scheduled blocks polymorphically without knowing their concrete type. `ComputeActivePolicySliceQueryHandler` then pattern-matches on C# type to derive `ActiveBlockType` — the distinction is made exactly once, in the one place that needs it.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `PlannedBlock` abstract class (`Entities/PlannedBlock.cs`) | Base for `RitualBlock` and `BackgroundBlock`; exposes `IsActive()` and `OverlapsWith()` |
| Application | `IPlannedBlockRepository` | Returns `IReadOnlyList<PlannedBlock>`; callers cast to concrete type when subtype behaviour is needed |
| Application | `ComputeActivePolicySliceQueryHandler` | Pattern-matches on `RitualBlock` / `BackgroundBlock` to set `ActiveBlockType` on the `ActivePolicySlice` value object |

---

## Ritual Block

**Definition:** A planned calendar entry that does two things simultaneously at its designated start time: (1) enforcement activates immediately — the block profile's rules are applied before the user has done anything, and (2) the user is pushed to the Nen Ritual page to consciously start the session. The session created via the Ritual is the *Planned Session* associated with this block. Critically, enforcement is already in place from the designated time regardless of whether the user completes the Ritual immediately. If the user never completes the Ritual, the `RitualBlock` row still exists and Insights shows it as "missed" (`RitualCompletedAt` is null). Enforcement still expires at the block's scheduled end time whether the Ritual was completed or not.

**Must use a Ritual-type Block Profile.** Only profiles created with the Ritual type are available when scheduling a Ritual Block in the Planner or selecting a profile during the Ren step.

**Overlap rules:** Two Ritual Blocks cannot overlap each other. A Ritual Block can overlap with one or more Background Blocks — the net enforcement is the union of all active profiles.

**Ubiquitous language rules:**
- A Ritual Block is *scheduled*, not *started*.
- A Ritual Block *activates* and *expires* — it is not started or ended.
- Enforcement from a Ritual Block is *pre-emptive* — it does not wait for the Ritual to complete.

**Activation sequence:**
1. System clock reaches the block's `Start` time
2. Enforcement applies immediately (policy slice computed, OS-level blocking begins)
3. App navigates user to the Nen Ritual (Ten → Zetsu → Ren → Hatsu)
4. User completes Ritual → `StartSessionCommand` dispatched with `Origin = Planned` and `OriginatingBlockId = <this block's Id>`
5. `StartSessionCommandHandler` calls `RitualBlock.MarkRitualCompleted(DateTime.UtcNow)` and saves the block
6. The resulting Session (with `Origin = Planned`) is linked to this Ritual Block via `Session.OriginatingBlockId`

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `RitualBlock` entity (`Entities/RitualBlock.cs`) | Extends `PlannedBlock`; inherits `Id`, `BlockProfileId`, `Start`, `End`, `Title?`; own field: `RitualCompletedAt?`; block type derived from C# class (`RitualBlock` → `BlockType.Ritual` in query handler — no stored column) |
| Application | `IPlannedBlockRepository` | Retrieves blocks for overlap validation and active-state queries |
| Application | `ComputeActivePolicySliceQueryHandler` | Queries blocks covering `DateTime.UtcNow`; merges with active Session |
| Application | `StartSessionCommandHandler` | Calls `RitualBlock.MarkRitualCompleted(DateTime.UtcNow)` when `OriginatingBlockId` is provided |
| UI | `PlannerPageViewModel` | (Stub) — will create, update, delete Ritual Blocks |
| UI | `ShellViewModel` | (Not yet built) — will detect Ritual Block activation and trigger Ritual overlay |

---

## Background Block

**Definition:** A planned calendar entry that activates enforcement at its designated start time, exactly like a Ritual Block, but does **not** trigger the Nen Ritual. The user is not prompted to start a session — the block profile's rules simply become active. There is no associated Session entity. The block expires at its `End` time and enforcement is removed. The `BackgroundBlock` row itself is the complete Insights record — it appears in the Calendar view regardless of whether the machine was on during the scheduled time.

**Must use a Background-type Block Profile.** Only profiles created with the Background type are available when scheduling a Background Block in the Planner.

**Overlap rules:** Multiple Background Blocks can run simultaneously. Their profiles are merged via the PolicyEngine — the net enforcement is the union of all active profiles. Background Blocks can also overlap with Ritual Blocks.

**Contrast with Ritual Block:**

| | Ritual Block | Background Block |
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
| Domain | `BackgroundBlock` entity (`Entities/BackgroundBlock.cs`) | Extends `PlannedBlock`; inherits `Id`, `BlockProfileId`, `Start`, `End`, `Title?`; no additional own fields; block type derived from C# class (`BackgroundBlock` → `BlockType.Background` in query handler — no stored column) |
| Application | `ComputeActivePolicySliceQueryHandler` | **Includes** Background Blocks when computing the active policy slice |
| Infrastructure | `ScheduledEnforcementService` | Picks up the Background Block in its 5s heartbeat and applies enforcement |

---

## Impromptu Session

**Definition:** A Session created by the user clicking "Use Nen" on the Main Page and completing the full Nen Ritual (Ten → Zetsu → Ren → Hatsu), outside of any scheduled Ritual Block. The user has full control: they choose the Block Profile (Ritual-type only), set the duration, and configure break reminders. The Session is created with `Origin = Impromptu` and `OriginatingBlockId = null`. No Block entity of any kind is created.

**Product invariant:** A manual enforcement session cannot exist without a completed Ritual. This is a product invariant enforced by design, not by validation code. If the user abandons the Ritual at any step before clicking "Start Session" on the Hatsu page, no record is saved anywhere — no draft, no partial session, no recovery state.

**Conflict rule:** An Impromptu Session cannot be started if it would overlap with an upcoming Ritual Block. For example, if a Ritual Block is scheduled at 18:00, the user cannot start a 30-minute session at 17:45.

**Contrast with Planned Session:** An Impromptu Session is user-initiated and requires no pre-scheduled block. A Planned Session is triggered by a Ritual Block at its scheduled start time. In an Impromptu Session the user freely chooses the Block Profile and duration; in a Planned Session both are pre-determined.

**Technical mapping:** No Block entity — distinguished at the Application layer by `Session.Origin == Impromptu`. `Session.OriginatingBlockId` is `null`.

---

## Planned Session

**Definition:** A Session created by the user completing the Nen Ritual after a scheduled Ritual Block activated and pushed them to the Ritual overlay. The Block Profile and duration are pre-determined by the Ritual Block — the user cannot change the profile (Ren step) or the end time (Hatsu step), but can configure break reminders. The Session is saved with `Origin = Planned` and `OriginatingBlockId = <the triggering RitualBlock's Id>`. `StartSessionCommandHandler` stamps `RitualBlock.RitualCompletedAt` at this moment. If the user never completes the Ritual, no Session row is created; the `RitualBlock` row remains with `RitualCompletedAt = null` and Insights shows it as "missed." Enforcement still runs until the block's scheduled end time regardless.

---

## SessionOrigin

**Definition:** An enum that tags every `Session` with how it was initiated. Values: `Impromptu` (user completed the Ritual from the Main Page without a pre-scheduled block) and `Planned` (user completed the Ritual after a Ritual Block pushed them to it).

**Why this is not a nullable FK check:** `Session.OriginatingBlockId` is `null` for Impromptu sessions, but reading nullness as "origin type" is a leaky boolean. `SessionOrigin` makes the intent explicit and self-documenting. It remains unambiguous when future origin types are added.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `SessionOrigin` enum (`Enums/SessionOrigin.cs`) | Values: `Impromptu`, `Planned` |
| Domain | `Session.Origin` | Set at session creation; never changed after the session starts |
| Application | `StartSessionCommandHandler` | Sets `Origin = Planned` when `OriginatingBlockId` is provided; `Origin = Impromptu` when null |
| Application | `GetInsightsCalendarQuery` | Filters `Origin == Impromptu` to identify sessions with no associated block for the Calendar view |

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

**Definition:** A single query that unifies three data sources — `BackgroundBlock` rows, `RitualBlock` rows, and `Session` rows where `Origin = Impromptu` — into a single `InsightsEntryDto` list for the Insights Calendar view. The Insights ViewModel never queries these sources directly.

**Why it is a Deep Module:** The three-source complexity (different entities, different table shapes, different "missed" detection logic) is hidden behind a single `IQueryHandler<GetInsightsCalendarQuery, IReadOnlyList<InsightsEntryDto>>` interface. Adding a fourth source requires a change to the handler only — the ViewModel and its bindings are untouched.

**Status:** Stub defined. Implementation deferred until the Insights page is built.

**Insights page will also include:** session history, blocked attempt analytics (most-attempted sites, per-session breakdown), block profile usage stats, total focus time for configurable periods (last week, since January, etc.), calendar view of past Ritual Blocks, Background Blocks, and Impromptu Sessions, and graphs for usage trends.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `GetInsightsCalendarQuery` / `GetInsightsCalendarQueryHandler` (`Queries/GetInsightsCalendar/`) | Merges three sources into `IReadOnlyList<InsightsEntryDto>`; stub only |
| Application | `InsightsEntryDto` | (to be defined) — carries type tag, date, duration, block profile name, completion state |
| UI | `InsightsPageViewModel` | (Not yet built) — binds to the query result; never touches individual source tables directly |

---

## Contract *(Future — not yet built)*

**Definition:** A time-bounded commitment the user makes to a schedule of Ritual Blocks and Background Blocks, with self-imposed rules governing how much the schedule can be changed during the commitment period. The name is inspired by the concept of binding agreements — the user is making a contract with themselves.

**Scope:** A Contract covers a fixed period: one day, three days, one week, or one month.

**Creation flow:** In the Planner page, the user selects a Contract period → places Ritual Blocks and Background Blocks throughout the period using existing Block Profiles → at the end, specifies the strictness rules for the Contract.

**Strictness rules (user-configurable, examples):**
- **Fully locked:** No changes allowed during the Contract period. Blocks cannot be moved, removed, or shortened. New blocks can still be added.
- **Daily flexibility:** Within each day, the user can move blocks around, add or remove them freely, but only for the current or upcoming day.
- **Time-budget (daily):** Blocks can be rearranged within a day, but the total block time for that day must remain the same (e.g. 4 hours of Ritual Blocks planned = 4 hours must remain).
- **Time-budget (period):** Same as daily, but the budget is tracked across the full Contract period (e.g. user can do 2 fewer hours today if they add 2 hours on another day within the Contract).
- **Custom combinations:** The user picks which constraints apply.

**Settings integration:** In the Settings page, the user can pre-save default strictness rules that are automatically applied when creating a new Contract for a day, week, or month.

**Status:** Domain concept defined. No entities, commands, or UI built yet. To be interrogated via the Design Gap Protocol before implementation begins.

---

## Deep Module

**Definition:** A software module (class, service, or interface) that provides *significant* functionality through a *narrow*, simple API. The term comes from John Ousterhout's *A Philosophy of Software Design* and is a core architectural principle for NenFocus.

**The test:** Can a developer understand how to use this module in under 30 seconds by reading only its interface? If yes, it is deep. If the interface requires reading the implementation to use correctly, it is shallow and should be redesigned.

**Examples in NenFocus:**

| Module | Why it is deep |
|--------|---------------|
| `IActiveSessionMonitor` | 4 properties + 2 events; hides a background timer, UTC math, break tracking, and command handler invocation |
| `ISessionStateService` | 3 methods + 2 events; hides DB rehydration, profile name resolution, and broadcast coordination |
| `IEnforcementService` | 2 methods (`ApplyPolicyAsync`, `RemoveEnforcementAsync`); hides process kill loop, hosts file, DNS flush, registry, HTTP API |

---

## Design Gap

**Definition:** The distance between what a developer can see (the current interfaces and existing code) and what they need to understand to implement a new feature correctly. Every new feature starts with a Design Gap; the job of the pre-implementation "Grill Me" session is to close it before any code is written.

**Related principle:** See `ARCHITECT_GOVERNANCE.md → High-Level Development Strategy → 1. Strategic Design & The Design Gap`.
