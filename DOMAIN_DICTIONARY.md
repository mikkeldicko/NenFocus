# NenFocus — Domain Dictionary

This file is the canonical reference for every domain concept in NenFocus. It exists to eliminate ambiguity between conversations. When a term is used in code, comments, or a design discussion, its meaning here is authoritative.

---

## Quick Reference

| Term | One-line definition |
|------|---------------------|
| **Session** | A bounded focus period; lifecycle `NotStarted → Active → (OnBreak ↔ Active) → Completed` |
| **Ritual** | The four-step wizard (Ten → Zetsu → Ren → Hatsu) that creates a Session |
| **Block Profile** | A reusable template of blocked apps + website policy attached to a Session |
| **Planned Block** | Abstract base for `RitualBlock` and `BackgroundBlock`; carries `Start`, `End`, `BlockProfileId`, `IsActive()`, `OverlapsWith()` |
| **Ritual Block** | A planned calendar entry that activates enforcement AND triggers the Nen Ritual at the designated time |
| **Background Block** | A planned calendar entry that activates enforcement at the designated time without triggering the Ritual |
| **Policy Slice** | The merged, computed enforcement state at a given instant; represented as `ActivePolicySlice` (Domain Value Object), produced by `PolicyEngine` |
| **PolicyEngine** | Stateless Domain service (`NenFocus.Domain/Services/`) that computes `ActivePolicySlice` from raw aggregates; sole owner of all merge rules |
| **Impromptu Session** | A Session created by completing the Ritual manually from the Main Page; no Block entity exists; `Origin = Impromptu` |
| **Planned Session** | A Session created via the Ritual triggered by a scheduled Ritual Block; `Origin = Planned` |
| **SessionOrigin** | Enum (`Impromptu` \| `Planned`) that tags every Session with how it was initiated |
| **Quest** | A named task scoped to Daily / Weekly / Monthly, linked to sessions and tracked in Insights |
| **Enforcement** | OS-level mechanisms (process kill, hosts file, registry, extension) that apply a Policy Slice |
| **Deep Module** | A module with significant functionality hidden behind a narrow, obvious API |
| **Design Gap** | The distance between visible code and what is needed to implement a feature correctly |

---

## Session

**Definition:** A bounded period of intentional focus, created by the user through the Ritual and enforced by the system.

**Lifecycle:** `NotStarted → Active → (OnBreak ↔ Active) → Completed`

**Ubiquitous language rules:**
- A Session is *started*, not *created* or *opened*.
- A Session is *completed* or *auto-ended*, not *closed* or *finished*.
- A Session cannot be paused — only placed *on break*.

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
| UI | `MainPageViewModel` | Observer only — reads from `ISessionStateService` and `IActiveSessionMonitor`, dispatches commands |

---

## Ritual

**Definition:** The four-step wizard (Ten → Zetsu → Ren → Hatsu) through which a user consciously creates a Session. The Ritual is a design-time guardrail: it forces deliberate intent before focus begins.

**Lifecycle:** Opened by user → steps navigated → `StartSessionCommand` dispatched on Hatsu completion → Ritual closed.

**Step meanings:**

| Step | Theme | Purpose in UI |
|------|-------|---------------|
| **Ten** | Awareness | Greet the user; display today's scheduled blocks and active quest list |
| **Zetsu** | Silence | Set the session goal and optionally link to a Quest |
| **Ren** | Persistence | Configure duration and session-level app/site adjustments |
| **Hatsu** | Output | Select (or confirm) the Block Profile; confirm and start |

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `StartSessionCommand` / `StartSessionCommandHandler` | Final output of the Ritual; creates and starts the Session |
| UI | `RitualShellViewModel` | Hosts step navigation; owns `CurrentStep` and navigation commands |
| UI | `TenPageViewModel`, `ZetsuPageViewModel`, `RenPageViewModel`, `HatsuPageViewModel` | One ViewModel per step; collects input, validates locally |
| UI | `RitualShellView.axaml` | Full-screen overlay rendered over the Shell when `IsRitualActive = true` |

---

## Block Profile

**Definition:** A reusable, named configuration of blocked applications and a website policy. A Block Profile is attached to a Session at start time. It is a *template*, not a live enforcement rule.

**Lifecycle:** Created in Block Profiles page → (optionally duplicated or edited) → attached to Session at Ritual (Hatsu step) → locked for duration of Session.

**Ubiquitous language rules:**
- A profile is *attached* to a session, not *assigned* or *applied*.
- A profile is *locked* when a Ritual Block activates — no edits are permitted from the block's designated start time through the end of the session, including the period before the Ritual is completed.
- A profile is *locked* during a session — no edits are permitted while a session using it is active.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `BlockProfile` aggregate (`Aggregates/BlockProfile.cs`) | Owns the list of `BlockedApp` and `BlockedSite` value objects and the `WebsitePolicy` |
| Domain | `BlockedApp`, `BlockedSite` value objects | Immutable; validated at construction |
| Domain | `WebsitePolicy` enum | `Blocklist` or `Allowlist` |
| Application | `IBlockProfileRepository` | CRUD persistence contract |
| Application | `CreateBlockProfileCommandHandler`, `UpdateBlockProfileCommandHandler`, etc. | One handler per mutation |
| UI | `BlockProfilePageViewModel` | Full CRUD UI; file picker for app selection |

---

## Enforcement

**Definition:** The active OS-level mechanisms that prevent access to blocked applications and websites during a Session or Ritual Block. Enforcement is a *side-effect* of a Session or Ritual Block being active — it is not a domain entity.

**Lifecycle:** Applied when a Session starts or a Ritual Block activates → paused during breaks → removed when session ends or block expires.

**Mechanisms (in order of application):**
1. **Process kill loop** — kills matching process trees every 5 seconds
2. **Hosts file** — writes `0.0.0.0` / `::` entries between `# NEN_FOCUS_START` markers
3. **DNS flush** — `ipconfig /flushdns` after each hosts write
4. **Registry** — disables DoH (`BuiltInDnsClientEnabled = 0`) in Chrome, Edge, Brave
5. **Browser extension** — intercepts navigations, queries `/check`, redirects to `blocked.html`

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `IEnforcementService` | Contract for applying and removing enforcement; accepts `ActivePolicySlice` (Domain Value Object) |
| Domain | `IPolicyEngine` / `PolicyEngine` (`Services/`) | Computes `ActivePolicySlice` from raw domain entities; sole owner of all merge logic |
| Application | `ComputeActivePolicySliceQueryHandler` | Orchestrates data fetching across three repositories; delegates computation to `IPolicyEngine` |
| Infrastructure | `WindowsEnforcementService` | Consumes `ActivePolicySlice` directly; owns hosts file, process kill loop, registry writes, local HTTP API (`localhost:5005`) |
| Infrastructure | `ScheduledEnforcementService` | `BackgroundService` heartbeat (5s); calls `ComputeActivePolicySliceQuery`, reads `ActiveBlockType`, calls `IEnforcementService` |
| Extension | `background.js` | Intercepts browser navigation; queries `/check`; reports to `/report` |

---

## Policy Slice

**Definition:** The computed, merged enforcement state at a given instant in time. It is the *answer* to the question "what should be blocked right now?" — derived from all currently active Sessions and Planned Blocks. Represented as `ActivePolicySlice`, a **Domain Value Object** in `NenFocus.Domain/ValueObjects/`, not a DTO.

**Computation rules** (all logic lives in `PolicyEngine.ComputeSlice()`):
- **Apps:** union of all blocked apps across all active block profiles, always a blocklist. Includes `TemporaryBlockedApps` from the active `Session` entity.
- **Websites:** if *any* active profile uses `Allowlist` mode → default-deny, allowed = intersection of all allowlists minus any blocklist entries. Otherwise → block the union of all blocklisted sites. Includes `TemporaryBlockedSites` from the active `Session` entity.
- An `ActivePolicySlice` is considered "empty" only in `Blocklist` mode with no blocked apps or sites. It is *never* empty in `Allowlist` mode.
- **`ActiveBlockType`:** set to `BlockType.Ritual` if any Ritual Block is currently active, `BlockType.Background` if only Background Blocks are active, `null` if enforcement is driven solely by an active Session with no planned block. Ritual always takes precedence over Background when both overlap.

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
| Application | `ComputeActivePolicySliceQueryHandler` | Pattern-matches on `RitualBlock` / `BackgroundBlock` to set `ActiveBlockType` on the result DTO |

---

## Ritual Block

**Definition:** A planned calendar entry that does two things simultaneously at its designated start time: (1) enforcement activates immediately — the block profile's rules are applied before the user has done anything, and (2) the user is pushed to the Nen Ritual page to consciously start the session. The session created via the Ritual is the *Planned Session* associated with this block. Critically, enforcement is already in place from the designated time regardless of whether the user completes the Ritual immediately. If the user never completes the Ritual, the `RitualBlock` row still exists and Insights shows it as "missed" (`RitualCompletedAt` is null).

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

**Contrast with Ritual Block:**

| | Ritual Block | Background Block |
|---|---|---|
| Enforcement at start time | ✅ | ✅ |
| Triggers Nen Ritual | ✅ | ❌ |
| Creates a Session entity | ✅ (via Ritual) | ❌ |
| Appears in Insights Calendar | ✅ | ✅ |
| Has `RitualCompletedAt` field | ✅ | ❌ |

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

**Definition:** A Session created by the user completing the full Nen Ritual (Ten → Zetsu → Ren → Hatsu) from the Main Page, outside of any scheduled Ritual Block. Adjustment rules (Ren page) are *permissive* — the user may add or remove apps and sites. The Session is created with `Origin = Impromptu` and `OriginatingBlockId = null`. No Block entity of any kind is created.

**Product invariant:** A manual enforcement session cannot exist without a completed Ritual. This is a product invariant enforced by design, not by validation code. If the user abandons the Ritual at any step before clicking "Start Session" on the Hatsu page, no record is saved anywhere — no draft, no partial session, no recovery state.

**Contrast with Planned Session:** An Impromptu Session is user-initiated and requires no pre-scheduled block. A Planned Session is triggered by a Ritual Block at its scheduled start time. Impromptu Sessions have relaxed adjustment rules; Planned Sessions have strict adjustment rules.

**Technical mapping:** No Block entity — distinguished at the Application layer by `Session.Origin == Impromptu`. `Session.OriginatingBlockId` is `null`.

---

## Planned Session

**Definition:** A Session created by the user completing the Nen Ritual after a scheduled Ritual Block activated and pushed them to the Ritual overlay. Adjustment rules are *strict* — the user may only add items, not remove them. The Session is saved with `Origin = Planned` and `OriginatingBlockId = <the triggering RitualBlock's Id>`. `StartSessionCommandHandler` stamps `RitualBlock.RitualCompletedAt` at this moment. If the user never completes the Ritual, no Session row is created; the `RitualBlock` row remains with `RitualCompletedAt = null` and Insights shows it as "missed."



---

## SessionOrigin

**Definition:** An enum that tags every `Session` with how it was initiated. Values: `Impromptu` (user completed the Ritual from the Main Page without a pre-scheduled block) and `Planned` (user completed the Ritual after a Ritual Block pushed them to it).

**Why this is not a nullable FK check:** `Session.OriginatingBlockId` is `null` for Impromptu sessions, but reading nullness as "origin type" is a leaky boolean. `SessionOrigin` makes the intent explicit and self-documenting. It remains unambiguous when future origin types are added (e.g., a Recurring Block template that generates sessions on a schedule).

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `SessionOrigin` enum (`Enums/SessionOrigin.cs`) | Values: `Impromptu`, `Planned` |
| Domain | `Session.Origin` | Set at session creation; never changed after the session starts |
| Application | `StartSessionCommandHandler` | Sets `Origin = Planned` when `OriginatingBlockId` is provided; `Origin = Impromptu` when null |
| Application | `GetInsightsCalendarQuery` | Filters `Origin == Impromptu` to identify sessions with no associated block for the Calendar view |

---

## GetInsightsCalendarQuery

**Definition:** A single query that unifies three data sources — `BackgroundBlock` rows, `RitualBlock` rows, and `Session` rows where `Origin = Impromptu` — into a single `InsightsEntryDto` list for the Insights Calendar view. The Insights ViewModel never queries these sources directly.

**Why it is a Deep Module:** The three-source complexity (different entities, different table shapes, different "missed" detection logic) is hidden behind a single `IQueryHandler<GetInsightsCalendarQuery, IReadOnlyList<InsightsEntryDto>>` interface. Adding a fourth source (e.g., Recurring Block instance records) requires a change to the handler only — the ViewModel and its bindings are untouched.

**Status:** Stub defined. Implementation deferred until the Insights page is built.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `GetInsightsCalendarQuery` / `GetInsightsCalendarQueryHandler` (`Queries/GetInsightsCalendar/`) | Merges three sources into `IReadOnlyList<InsightsEntryDto>`; stub only |
| Application | `InsightsEntryDto` | (to be defined) — carries type tag, date, duration, block profile name, completion state |
| UI | `InsightsPageViewModel` | (Not yet built) — binds to the query result; never touches individual source tables directly |

---

## Quest

**Definition:** A named task scoped to a Daily, Weekly, or Monthly period. Quests are created in the Planner, displayed on the Main Page for the current period, and optionally linked to a Session during the Zetsu step.

**Lifecycle:** Created in Planner → displayed on Main page for the relevant period → optionally linked at session start → checked off → reviewed in Insights.

**Scoping rules:**
- A Quest is *daily*, *weekly*, or *monthly* — never a custom range.
- At most one quest list of each scope type per period.

**Behaviour rules:**
- Quests are scoped to **Daily**, **Weekly**, or **Monthly** — never a custom range.
- Created and managed in the Planner page.
- The Main page displays today's active quests (daily quests for today, weekly quests covering this week, monthly quests covering this month).
- In the **Zetsu** ritual step, the user can optionally link the session to a specific quest from today's active list.
- Quest completion is recorded in Insights: completion rate, streaks, session time per quest.
- At most one quest list of each scope type per period.

**Status:** Domain concept defined; UI and persistence are not yet built beyond the Main Page checklist stub.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | (to be defined) | Quest entity, `QuestScope` enum |
| Application | (to be defined) | `CreateQuestCommand`, `GetQuestsForDayQuery` |
| UI | `TodaysChecklist` on `MainPageViewModel` | Currently a stub `ObservableCollection<ChecklistItemViewModel>` |

---

## Deep Module

**Definition:** A software module (class, service, or interface) that provides *significant* functionality through a *narrow*, simple API. The term comes from John Ousterhout's *A Philosophy of Software Design* and is a core architectural principle for NenFocus.

**The test:** Can a developer understand how to use this module in under 30 seconds by reading only its interface? If yes, it is deep. If the interface requires reading the implementation to use correctly, it is shallow and should be redesigned.

**Examples in NenFocus:**

| Module | Why it is deep |
|--------|---------------|
| `IActiveSessionMonitor` | 4 properties + 2 events; hides a background timer, UTC math, break tracking, and command handler invocation |
| `ISessionStateService` | 3 methods + 2 events; hides DB rehydration, profile name resolution, and broadcast coordination |
| `IEnforcementService` | 2 methods (`ApplyEnforcementAsync`, `RemoveEnforcementAsync`); hides process kill loop, hosts file, DNS flush, registry, HTTP API |

---

## Design Gap

**Definition:** The distance between what a developer can see (the current interfaces and existing code) and what they need to understand to implement a new feature correctly. Every new feature starts with a Design Gap; the job of the pre-implementation "Grill Me" session is to close it before any code is written.

**Related principle:** See `ARCHITECT_GOVERNANCE.md → High-Level Development Strategy → 1. Strategic Design & The Design Gap`.
