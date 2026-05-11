# NenFocus — Domain Dictionary

This file is the canonical reference for every domain concept in NenFocus. It exists to eliminate ambiguity between conversations. When a term is used in code, comments, or a design discussion, its meaning here is authoritative.

---

## Quick Reference

| Term | One-line definition |
|------|---------------------|
| **Session** | A bounded focus period; lifecycle `NotStarted → Active → (OnBreak ↔ Active) → Completed` |
| **Ritual** | The four-step wizard (Ten → Zetsu → Ren → Hatsu) that creates a Session |
| **Block Profile** | A reusable template of blocked apps + website policy attached to a Session |
| **Focus Block** | A calendar entry that schedules enforcement autonomously during a time window |
| **Passive Block** | A calendar entry that logs activity without enforcing anything |
| **Policy Slice** | The merged, computed enforcement state at a given instant in time |
| **Impromptu Session** | A user-initiated session (permissive adjustment rules) |
| **Planned Session** | A block-triggered session (strict adjustment rules) |
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
| Domain | `Session` entity (`Entities/Session.cs`) | Owns all state transitions via `Start()`, `StartBreak()`, `ResumeFromBreak()`, `Complete()` |
| Domain | `SessionStatus` enum | Valid states: `NotStarted`, `Active`, `OnBreak`, `Completed` |
| Domain | `SessionGoal`, `SessionDuration`, `BreakSettings` value objects | Immutable configuration attached at creation |
| Application | `ISessionRepository` | Persistence contract; `GetActiveAsync()` is the startup rehydration entry point |
| Application | `ISessionStateService` | Singleton in-memory noticeboard; the single source of truth for the UI's view of the active session |
| Application | `IActiveSessionMonitor` | Singleton countdown engine; owns the timer, exposes `SessionTimeRemaining` / `Progress`, auto-ends via `EndSessionCommandHandler` |
| Application | `StartSessionCommandHandler` | Entry point that creates and starts the session, calls `_sessionState.SetActive()` |
| Application | `EndSessionCommandHandler` | Calls `session.Complete()`, clears enforcement, calls `_sessionState.Clear()` |
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

**Definition:** The active OS-level mechanisms that prevent access to blocked applications and websites during a Session or Focus Block. Enforcement is a *side-effect* of a Session or Focus Block being active — it is not a domain entity.

**Lifecycle:** Applied when a Session starts or a Focus Block activates → paused during breaks → removed when session ends or block expires.

**Mechanisms (in order of application):**
1. **Process kill loop** — kills matching process trees every 5 seconds
2. **Hosts file** — writes `0.0.0.0` / `::` entries between `# NEN_FOCUS_START` markers
3. **DNS flush** — `ipconfig /flushdns` after each hosts write
4. **Registry** — disables DoH (`BuiltInDnsClientEnabled = 0`) in Chrome, Edge, Brave
5. **Browser extension** — intercepts navigations, queries `/check`, redirects to `blocked.html`

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `IEnforcementService` | Contract for applying and removing enforcement |
| Infrastructure | `WindowsEnforcementService` | Owns hosts file, process kill loop, registry writes, local HTTP API (`localhost:5005`) |
| Infrastructure | `ScheduledEnforcementService` | `BackgroundService` heartbeat (5s); calls `ComputeActivePolicySliceQuery` then `IEnforcementService` |
| Application | `ComputeActivePolicySliceQueryHandler` | Merges all active Sessions and Focus Blocks into a `PolicySliceDto` |
| Extension | `background.js` | Intercepts browser navigation; queries `/check`; reports to `/report` |

---

## Policy Slice

**Definition:** The computed, merged enforcement state at a given instant in time. It is the *answer* to the question "what should be blocked right now?" — derived from all currently active Sessions and Focus Blocks.

**Computation rules:**
- **Apps:** union of all blocked apps across all active blocks (always a blocklist).
- **Websites:** if *any* active block uses `Allowlist` mode → default-deny, allowed = intersection of all allowlists. Otherwise → block the union of all blocklisted sites.
- A `PolicySlice` is considered "empty" only in `Blocklist` mode with no entries. It is *never* empty in `Allowlist` mode (to keep enforcement active with even a small allowlist).

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Application | `PolicySliceDto` | Immutable result DTO from the query; carries `BlockedApps`, `BlockedSites`, `WebsitePolicyMode`, `IsEmpty` |
| Application | `ComputeActivePolicySliceQueryHandler` | Single query that aggregates all active state into one slice |
| Infrastructure | `WindowsEnforcementService` | Consumes the `PolicySliceDto` to drive OS-level mechanisms |

---

## Focus Block

**Definition:** A calendar entry that schedules enforcement during a specific time window. A Focus Block is *planned* — it activates automatically when the system clock enters its window, independently of any user-initiated Session.

**Ubiquitous language rules:**
- A Focus Block is *scheduled*, not *started*.
- A Focus Block *activates* and *expires* — it is not started or ended.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `FocusBlock` entity (`Entities/FocusBlock.cs`) | Holds `Start`, `End`, `BlockProfileId`, `BlockType.Focus` |
| Application | `IPlannedBlockRepository` | Retrieves blocks for overlap validation and active-state queries |
| Application | `ComputeActivePolicySliceQueryHandler` | Queries blocks covering `DateTime.UtcNow`; merges with active Session |
| UI | `PlannerPageViewModel` | (Stub) — will create, update, delete Focus Blocks |

---

## Passive Block

**Definition:** A calendar entry that *logs* focus activity during a time window without enforcing any blocking. Passive Blocks exist to record intent and generate Insights data.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `PlannedBlock` entity with `BlockType.Passive` | Marks the time window; no enforcement action taken |
| Application | `ComputeActivePolicySliceQueryHandler` | Skips Passive Blocks when computing enforcement |

---

## Impromptu Session

**Definition:** A Session started directly from the Main Page, outside of any scheduled Focus Block. Adjustment rules (Ren page) are *permissive* — the user may add or remove apps and sites.

**Contrast with Planned Session:** An Impromptu Session is user-initiated; a Planned Session is block-triggered. Impromptu Sessions have relaxed adjustment rules.

**Technical mapping:** No separate entity — distinguished at the Application layer by the absence of an associated Focus Block ID.

---

## Planned Session

**Definition:** A Session automatically triggered when a scheduled Focus Block activates. Adjustment rules are *strict* — the user may only add items, not remove them.

**Status:** Not yet built. The Focus Block activation trigger and auto-Ritual flow are on the roadmap.

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
