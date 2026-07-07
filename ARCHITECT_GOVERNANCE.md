# NenFocus — Architect Governance

**This is the Constitution. Read this at the start of every session.**

For detailed vocabulary, see `DOMAIN\\\_DICTIONARY.md`.
For implementation details and build state, see `TECHNICAL\\\_SPEC\\\_INFRA.md`.
For UI rules and ViewModel contracts, see `UI\\\_UX\\\_STANDARDS.md`.
For module and layer guardrails, see `MODULE\\\_STANDARDS.md`.
For TDD workflow and test patterns, see `TESTING\\\_GUIDE.md`.
For historical record of closed design gaps, see `DESIGN\\\_GAP\\\_HISTORY.md`.

\---

## High-Level Development Strategy

### 1\. Strategic Design \& The Design Gap

**Principle:** Bridge the Design Gap before writing any new features.

Before implementing anything, ask as many questions as necessary to reach a shared design concept and understand the full dependency tree. Do not write implementation code until the design is agreed upon. This means: interface first, implementation second.

### 2\. Ubiquitous Language (DDD)

**Principle:** Use a shared vocabulary to eliminate communication overhead.

Always refer to the terms defined in `DOMAIN\\\_DICTIONARY.md` (Ritual, Session, Enforcement, Policy Slice, Focus Block, Background Block, SessionOrigin, Quest, Deep Module, etc.). If a new concept needs to be introduced, define and name it together before writing any code.

**Product invariant:** A manual enforcement session cannot exist without a completed Ritual. This is a product invariant enforced by design, not by validation code. The full Nen Ritual (Ten → Zetsu → Ren → Hatsu) is the only path to creating a Spontaneous Session. Abandoning the Ritual at any point produces no saved record anywhere.

### 3\. TDD — Headlight Management

**Principle:** Never outrun the headlights.

Take small, deliberate steps. When possible, follow a TDD workflow: write a failing test, make it pass, then refactor. Avoid generating large blocks of unverified code. Each increment must be provably correct before the next one begins.

### 4\. Deep Modules — Cognitive Load Management

**Principle:** Complexity must be hidden behind simple interfaces.

Aim for Deep Modules — classes and services that deliver significant functionality through a narrow, obvious API. Concretely:

* **ViewModels** must be kept shallow: no timers, no math, no string formatting. Delegate all tactical logic (countdowns, progress calculation, time formatting) to Infrastructure or Application services.
* **Services** are gray boxes: the human architect should be able to understand and compose them from their interface alone, without reading the implementation.
* When a ViewModel grows beyond "display data + dispatch commands," that is a signal to extract a new service.

\---

## What This App Is

NenFocus is a Windows desktop focus tool built with Avalonia (.NET 9). It combines app/website blocking, intentional session creation via a 4-step ritual wizard, calendar-based focus planning, and session insights.

The name and ritual terminology (Ten, Zetsu, Ren, Hatsu) are inspired by the concept of "Nen" from Hunter × Hunter — each ritual step has a thematic purpose that maps to the session setup flow.

\---

## Architecture

Clean Architecture with 4 code projects + 1 test project:

```
NenFocus.Domain          → Entities, value objects, domain events, enums. No dependencies.
NenFocus.Application     → Use cases (CQRS commands/queries), interfaces, validators, DTOs.
NenFocus.Infrastructure  → EF Core + SQLite, Windows enforcement, toast notifications, DI wiring.
NenFocus                 → Avalonia UI: ViewModels, Views, converters. Entry point.
NenFocus.Tests           → xUnit unit tests for Domain and Application layers.
```

Dependencies only flow inward: UI → Application ← Infrastructure → Domain.

**Key patterns:**

* **CQRS**: `ICommandHandler<TCommand, TResult>` / `IQueryHandler<TQuery, TResult>` — all mutations are commands, all reads are queries. No service classes that do both.
* **Repository pattern**: interfaces in Application, EF Core implementations in Infrastructure.
* **Domain events**: raised inside entities/aggregates, published via `IInProcessDomainEventPublisher`, handled by Application-layer event handlers. Commands do not directly call enforcement or notifications — event handlers do.
* **MVVM**: CommunityToolkit.Mvvm (`ObservableObject`, `RelayCommand`). ViewModels never reference Views.
* **Value objects**: `record` types with validation in the constructor — `SessionGoal`, `SessionDuration`, `BreakSettings`, `BlockedApp`, `BlockedSite`.
* **Vertical Slice Architecture**: Every use case lives in its own feature folder (e.g., `UseCases/Sessions/StartSession/`) containing exactly four files: `Command`, `CommandHandler`, `Result`, `Validator`. Validators are auto-registered via `AddValidatorsFromAssembly`.
* **Enforcement pipeline**: `ScheduledEnforcementService` (heartbeat, 5s interval) → `ComputeActivePolicySliceQueryHandler` (aggregates PlannedBlocks + active Sessions) → `WindowsEnforcementService` (kills processes, rewrites hosts file, exposes local HTTP API).

\---

## Project Structure

```
📄 ARCHITECT\\\_GOVERNANCE.md    ← Constitution (this file)
📄 DOMAIN\\\_DICTIONARY.md       ← Ubiquitous language + technical mappings
📄 MODULE\\\_STANDARDS.md        ← Layer rules, ViewModel Forbidden Zone, naming conventions
📄 TECHNICAL\\\_SPEC\\\_INFRA.md    ← Build state, enforcement details, dev notes
📄 TESTING\\\_GUIDE.md           ← TDD workflow, NSubstitute patterns, test inventory
📄 UI\\\_UX\\\_STANDARDS.md         ← Colours, layout, ViewLocator, time formatting
📄 DESIGN\\\_GAP\\\_HISTORY.md      ← Record of closed design gaps (reference only)



## The Design Gap Protocol

This protocol is **mandatory** before any implementation code is written for a new feature. Its purpose is to close the Design Gap — the distance between what is currently visible in the codebase and what is needed to implement the feature correctly.

### Step 1 — The Interrogation (Grill the Architect)

Before writing any implementation code, Claude MUST ask at least 5–10 clarifying questions covering:

1. **Trigger:** What event, user action, or system condition initiates this feature?
2. **Dependency tree:** Which existing interfaces, entities, and services will this feature touch or depend on?
3. **New contracts:** Will this feature require a new `ICommandHandler`, `IQueryHandler`, or service interface? What does the signature look like?
4. **State ownership:** Which layer owns the state this feature introduces? Domain entity, Application service, or UI observable?
5. **Edge cases:** What happens if the feature is triggered while a Session is active? While on break? At app startup? At shutdown?
6. **Persistence:** Does this feature require a new DB column, table, or migration?
7. **Enforcement interaction:** Does this feature interact with `ScheduledEnforcementService` or `WindowsEnforcementService`?
8. **UI contract:** What observable properties and commands does the ViewModel need to expose? What XAML bindings does the View need?
9. **Failure modes:** What happens if the underlying operation fails? How does the UI surface the error?
10. **Rollback / undo:** Is there a compensating action? (e.g., if session start fails after enforcement is applied)

This interrogation happens in conversation. Code is not typed until every answer is agreed upon.

### Step 2 — Interface First

The first code artifact for any new feature is **always** the interface, placed in `NenFocus.Application/Interfaces/`. No handler, service, or ViewModel code is written until the interface is:

* Reviewed and approved by the architect
* Consistent with the Deep Module test (understandable in under 30 seconds)
* Free of infrastructure types in its signature

### Step 3 — Thin Vertical Slice

Once the interface is approved, implement the thinnest possible vertical slice:

* Domain entity changes (if any)
* One command or query handler
* One ViewModel binding
* No parallel work on adjacent features

### Step 4 — Verify Before Expanding

Each slice must compile and produce the correct output before the next slice begins. If the project has an active test suite (`NenFocus.Tests`), a passing test is required before moving on. This is the "headlight management" rule: never implement further than you can verify.

\---

## Open Gaps

Gaps that have been identified but not yet closed. Each is here because deferring it is a deliberate choice, not because it has been forgotten.

### Open Gap A — Usage Ceiling & Universal Usage Tracking (interrogated 2026-07-07; not yet built)

**This entirely supersedes the previous "Time Budget" framing of this gap — not a patch, a rewrite.** `BlockedSite` does **not** gain a `TimeBudget` field. `BlockPreset` (Focus and Background alike) remains pure hard-block only, permanently. The budget concept was pulled off `BlockedSite` entirely and rebuilt as an independent feature, for reasons recorded below.

**The feature, in two independent parts:**

1. **Universal usage tracking (foundation).** Every website visited and every application's foreground time is tracked unconditionally, from day one — not just resources with a configured ceiling. This is deliberately general-purpose: the same substrate feeds both Usage Ceiling enforcement and the future Insights page's time-per-site/time-per-app reporting. Scoping collection down to only configured entries would starve Insights of data for everything else.
2. **Usage Ceiling (ambient access cap).** A standing, perpetual cap on a specific site or app's access time — created and managed on the **Presets page**, not Settings, not the Planner, and explicitly not part of Nen Contract (rationale below). Always in effect once created; no calendar involvement whatsoever.

**Precedence rule (the core design decision):** If any currently-active Focus or Background preset hard-blocks a resource, that hard block always wins — the resource is blocked, full stop, and no usage accrues during that window. Outside of any active hard block, the resource is accessible up to its ceiling; once the ceiling is reached for the current period, the resource is blocked for the rest of that period. This carries forward the one rule that was already correct in the old Time Budget spec (hard block wins, budget doesn't tick during it) — unchanged by the rewrite.

**Why decoupled from `BlockPreset`/`BlockedSite`:** The original design tied a site's budget window to the owning `PlannedBlock`'s schedule, which conflated two unrelated things — a schedule-bound hard block (what presets are for) and an ambient, calendar-independent personal limit (what this is for). That conflation was the source of the original "does a time budget make sense on a Focus Block" doubt — it never did, because it was never really a preset-level concept to begin with.

**Why decoupled from Nen Contract:** `NenContract` is sealed and time-bound (`StartDate`–`EndDate`), governing edit permission for a committed block schedule with Daily-Floor scoring. A Usage Ceiling is perpetual, unsealed, and governs passive resource access — coupling them would mean ceilings stop functioning between contracts, or if no contract is ever sealed, which contradicts the point of an *ambient* limit. Sibling concepts, not dependent ones.

**Domain shape (interrogated, not yet built):**

| Concept | Shape |
|---|---|
| `DailyUsage` (entity) | Append-only fact, keyed by `(Date, ResourceName, UsageResourceType)`, accumulates seconds. Mirrors `BlockedAttempt`'s pattern exactly — no `SessionId`, no `PresetId`, no `BlockPresetId`. Belongs to nothing but the day and the resource. |
| `UsageResourceType` (enum) | `App` \| `Website` — mirrors `BlockedAttemptType`. |
| `UsageCeiling` (entity, deliberately not a value object) | `Id`, `ResourceName`, `ResourceType (UsageResourceType)`, `Period (CeilingPeriod)`, cap value(s), `IsPaused`. Entity because it has independent identity, an independent lifecycle (created/edited/paused/deleted via its own CRUD, not nested inside another aggregate), and is looked up by `Id` from `PolicyEngine` on every tick — the same justification that makes `FocusBlock`/`BackgroundBlock` entities rather than value objects. Carries a pure `IsExhausted(...)` comparison method — same justification as `DailyFloor.IsSatisfiedBy` (the rule belongs with its data, reused wherever exhaustion is checked, not duplicated across callers). That's a separate, orthogonal DDD question from the entity/value-object determination — both apply here, for different reasons. No domain events; nothing reacts to a ceiling being created or paused. |
| `CeilingPeriod` (enum) | `Daily` \| `Weekly` — chosen per entry, mirrors the multi-mode shape of `StrictnessRule`. **Daily** mode's cap is a per-weekday shape (e.g. Monday 3h, Tuesday 1h), mirroring `WorkHoursPolicy`'s `{ Default, PerDayExceptions }` resolution pattern. **Weekly** mode's cap is a single aggregate tracked cumulatively across the week, mirroring the idea behind `StrictnessRule.PeriodTimeBudget` applied to resource access instead of block-editing permission. |
| `IUsageTracker` (Application interface) | Narrow write contract for usage events, mirrors `IBlockedAttemptTracker` exactly. |

**Collection mechanisms (asymmetric cost — both required for "track everything"):**

- **Websites** — cheap; the extension already intercepts every navigation. Needs tab-focus-time accumulation added, idle detection via `chrome.idle`, and a media-playing exception (an audible/playing tab doesn't count as idle) so passive video consumption isn't misread as inactivity. Reports via a new `/report-time` endpoint, distinct from the existing `/report` (which only logs blocked *attempts*).
- **Apps** — genuinely new capability. Needs a foreground-process polling loop — cheaper than the existing 2-second kill loop, since it targets one process rather than enumerating all of them — plus idle detection via `GetLastInputInfo`. The native equivalent of the media-playing exception would need Windows Core Audio Session APIs (per-process audio output detection) and is materially harder to build than the web side. **Open question:** whether the native audio exception ships in v1, or apps launch with plain idle-timeout only.

**Enforcement wiring — a fork was considered and resolved:** `PolicyEngine.ComputeSlice()` is **extended, not bypassed**. It gains new inputs (active `UsageCeiling` entries plus resolved `DailyUsage` totals for the relevant period) and folds exhausted ceilings directly into `ActivePolicySlice.BlockedSites`/`BlockedApps`. The alternative — a second, parallel "am I blocked" check inside `WindowsEnforcementService`, OR'd together with `PolicyEngine`'s output at the enforcement layer — was explicitly rejected: it would push a domain rule (exhaustion, precedence) into Infrastructure and break "`PolicyEngine` is the sole owner of all merge rules," the same principle Gap 2 and Gap 5 already established. One authority for "what's blocked and why," not two.

**Consequence for the enforcement pipeline:** `ScheduledEnforcementService`'s heartbeat and `WindowsEnforcementService`'s local HTTP API currently start/stop with enforcement bursts. For ambient ceilings to mean anything, both must run for the app's full lifetime, independent of whether any Session or Block is active. Confirmed acceptable during interrogation — the app-side tracking loop is cheaper than the kill loop already running today, and the extension already intercepts navigation regardless of NenFocus's state, so the performance cost of "always on" is negligible.

**UI placement:** Presets page, new tab (working label "Usage Ceilings"). Not a `BlockPreset`/`ContractPreset`-style reusable template — a ceiling isn't consumed to create another object, it's a standing, directly-enforced rule. Not Settings. Not the Planner.

**Not the same feature as recurring hard blocks — do not conflate:** "Always block Reddit" or "block Facebook 9–5 daily" is not a Usage Ceiling. It's a permanent or repeating hard block — the separately-tracked **Recurring Blocks** gap (see `TECHNICAL_SPEC_INFRA.md` → Not Yet Built) applied to ordinary `BackgroundBlock`s, living on the Planner. No relationship between the two features; Recurring Blocks was not interrogated in this session and remains fully open.

**Open questions, not resolved in interrogation:**
- Whether specific resources (NenFocus's own process, `localhost`, `SystemSafetyConstants.UnkillableProcesses`) are excluded from `DailyUsage`, or truly everything is logged unconditionally.
- Day/week boundary semantics — reset time, week-start day, timezone handling.
- Exact CQRS shapes for `UsageCeiling` CRUD and usage-summation queries.
- Whether the native per-app audio-playing exception ships in v1.

**Must be closed before:** Nothing urgent — deferred until picked up. The domain shape is settled enough to start Slice 1 (entities + interfaces) whenever that happens.

### Open Gap B — Crash Resilience: Session Recovery After Unexpected Shutdown

**The gap:** If the app crashes or is force-killed mid-session, the current recovery path relies entirely on `SessionStateService.InitializeAsync()` querying `ISessionRepository.GetActiveAsync()` at startup. This is sufficient for clean restarts (the session was committed to SQLite before the crash), but it does not cover two failure cases:

1. **Mid-write crash** — the session was started, OS enforcement was applied (`WindowsEnforcementService.ApplyPolicyAsync`), but the DB write had not yet committed when the process died. On restart the DB shows no active session, so enforcement is never re-applied and the user's desktop is unblocked silently.
2. **Enforcement state drift** — the hosts file and process kill timer may be in an enforcing state after a crash, but `WindowsEnforcementService._currentPolicy` is `null` on restart. `ScheduledEnforcementService` will re-apply enforcement on its next heartbeat (≤5 s), so the window is small — but it is a window.

**Why deferred:** Both failure cases require the app to be crashed in a narrow write window. The risk is low for a personal focus tool and the correct fix (a proper recovery protocol) is non-trivial to design correctly. A previous incomplete attempt (`SessionPersistenceService`, a JSON sidecar file) was deleted because it introduced a second source of truth with no active consumer, which creates a divergence risk worse than the problem it was solving.

**Direction (subject to interrogation when closed):** The correct design is to make SQLite the single durable source of truth and close the write window, rather than maintaining a sidecar file. Candidate approaches — to be interrogated before any code is written:

- Begin each session command in a DB transaction that also records an "enforcement intent" row; `ScheduledEnforcementService` reads this on startup before its first heartbeat to re-apply enforcement immediately rather than waiting up to 5 s.
- Alternatively, treat the 5-second heartbeat window as acceptable and document it; add a startup check in `HostExtensions` that calls `WindowsEnforcementService.ApplyPolicyAsync` synchronously if `GetActiveAsync` returns a live session, closing the enforcement drift window to zero.
- Do **not** reintroduce a JSON sidecar — any file-based approach must explain how it stays in sync with the DB on every write. `EfCoreUnitOfWork` already solves exactly this problem for in-process command handling (see Gap 5 in `DESIGN_GAP_HISTORY.md`); a sidecar file would have to solve it independently, with no shared transaction boundary.

**Must be closed before:** The app is used in a high-stakes environment (e.g., exam blocking, supervised study) where a crash silently removing enforcement would be a product safety failure. For personal use, the current behavior is acceptable.

### Open Gap C — Nen Contract Motivation & Scoring Enhancements (three interrogated, unbuilt)

**Why deferred:** All three were fully interrogated in a design session (2026-07-07) and each has a settled interface, but none can be built until the Nen Contract Application/Infrastructure/UI layer exists at all — it is still entirely "not yet built" per `DOMAIN_DICTIONARY.md`. Specifically, all three depend on the **completed-hours-per-day summation query** (planned-block + make-up + matching-spontaneous-session hours, summed per calendar day), which is a prerequisite `IsDayMet` already needs for real use regardless of these additions — so this gap adds no new dependency, it just rides on one already owed to the feature. None of the three affect Planner, Presets, or session-start flows on their own merit; Planner is only touched because C2's streak was chosen to surface live on the already-planned active-contract banner.

**C1 — Weighted Day Met percentage (Insights):**
- New pure domain method `DailyFloor.PercentComplete(double completedHours) -> double?` — returns `null` when `Hours == 0` (rest day), otherwise `completedHours / Hours`, uncapped (133% displays as 133%, never clamped to 100%).
- `NenContract` gains a mirroring wrapper `PercentOfFloorMet(double completedFocusHours)` delegating to `DailyFloor.PercentComplete` — mirrors the existing `IsDayMet` → `IsSatisfiedBy` delegation so the Application layer never reaches into the `DailyFloor` value object directly.
- `IsDayMet` stays the sole authoritative pass/fail for Make-Up deficit logic and lifecycle transitions — this percentage is a purely additive display concern, computed at query time, no new persisted state.
- UI: Insights contract recap gets a day-strip (one tile per day) shaded by percentage; rest days render as "Rest," not "100%."

**C2 — In-contract streaks:**
- Consecutive Day-Met days *within a single contract's date range only*. Deliberately does not span across separate contracts or the gap between them — Day Met has no meaning outside an active contract (no floor to compare against), and extending a streak across that gap would require inventing an ungrounded rule for what "counts" on a non-contract day (rejected: a trivial spontaneous session shouldn't be able to prop up a streak meant to reflect honoring a real commitment).
- Rest days are skipped — they neither extend nor break the streak.
- Surfaces live on the Planner's active-contract banner (e.g. `"Contract: [Name] — Day X/Y — Z days behind — 5-day streak"`) and as a "best streak achieved" stat in the Insights recap after the contract ends.
- Exact query shape (bundled with the progress/deficit query vs. a separate dedicated query) deliberately left open — an implementation-time call, not a design gap.

**C3 — Deficit digest notification:**
- Proactive OS toast (same delivery mechanism as the existing grace-period warning), firing once per calendar day at a **user-configurable time** — a new Settings preference, per-user not per-contract.
- Skips entirely on days with no deficit — no "on track" toast; silence is the intended good-day behavior.
- One-shot per day, same "already fired today" guard pattern as the grace-period warning.
- Content: current deficit + days remaining, e.g. *"You're 1.5h behind on Study Month — 3 days left."*
- Coexists with, does not replace, the already-designed passive Main Page spontaneous make-up prompt.

**Must be closed before:** Nothing — purely additive once the Nen Contract Application layer and completed-hours summation query exist. No urgency driving closure.

**Parked, not interrogated:** Two related ideas came up in the same session and were deliberately shelved by the architect until there's a better view of the finished program — not designed in depth, listed here only so they aren't forgotten:
- **Contract templates from Insights** — a "start a new contract like this one" shortcut on a *completed* contract's Insights recap, pre-filling the Advanced wizard from that specific past instance rather than a saved Contract Preset. Distinct from Contract Presets because it copies a real past result, not a deliberately-saved template.
- **Strictness tightening mid-contract** — already tracked as a parked v2 idea in `DOMAIN_DICTIONARY.md` (Strictness Rule section); re-confirmed parked in this session, no new detail added.

\---

## Minor Cleanups

Trivial style or consistency fixes that don't rise to the level of a Design Gap. Deferred until the relevant file is next edited for other reasons. Items here should be deletable in a single line change.

*(No outstanding items.)*

