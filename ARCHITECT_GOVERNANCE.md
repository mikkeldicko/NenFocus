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

Gaps that have been identified but not yet closed. Each is here because deferring it is a deliberate choice, not because it has been forgotten. Once a gap (or a self-contained half of one) is fully closed, its write-up moves to `DESIGN_GAP_HISTORY.md` and is removed from here — this section should only ever contain gaps with genuine remaining work.

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

**Why deferred:** All three were fully interrogated in a design session (2026-07-07) and each has a settled interface, but none can be built until the Nen Contract Application/Infrastructure/UI layer exists at all — it is still entirely "not yet built" per `DOMAIN_DICTIONARY.md`. Specifically, all three depend on the **completed-hours-per-day summation query** (planned-block + redemption + matching-spontaneous-session hours, summed per calendar day), which is a prerequisite `IsDayMet` already needs for real use regardless of these additions — so this gap adds no new dependency, it just rides on one already owed to the feature. None of the three affect Planner, Presets, or session-start flows on their own merit; Planner is only touched because C2's streak was chosen to surface live on the already-planned active-contract banner. **That completed-hours query used to be buggy — fixed 2026-08-12, see `DESIGN_GAP_HISTORY.md` Gap 22 (closed).** `GetNenContractCompletedFocusHoursQueryHandler` used to silently under-credit any block that was ended early (`EndSessionCommandHandler`'s old `TrimEnd` call corrupted the record it read from). Gap 22 closed this before C1/C2/C3 were ever built on top of it, so none of the three inherit the bug — whenever one of them is picked up, the query it depends on is already trustworthy.

**C1 — Weighted Day Met percentage (Insights):**
- New pure domain method `DailyFloor.PercentComplete(double completedHours) -> double?` — returns `null` when `Hours == 0`, otherwise `completedHours / Hours`, uncapped (133% displays as 133%, never clamped to 100%).
- `NenContract` gains a mirroring wrapper `PercentOfFloorMet(DateOnly date, double completedFocusHours) -> double?` — resolves `DailyFloorPolicy.For(date)` and delegates to `DailyFloor.PercentComplete` if a floor exists, otherwise returns `null` (rest day) — mirrors the existing `IsDayMet(DateOnly, double)` → `IsSatisfiedBy` delegation, updated for the per-day `DailyFloorPolicy` redesign (see `DESIGN_GAP_HISTORY.md`), so the Application layer never reaches into `DailyFloorPolicy`/`DailyFloor` directly.
- `IsDayMet` stays the sole authoritative pass/fail for Redemption deficit logic and lifecycle transitions — this percentage is a purely additive display concern, computed at query time, no new persisted state.
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
- Was designed to coexist with the Main Page spontaneous redemption prompt — that prompt has since been dropped (see Open Gap D); C3 does not depend on it and needs no change.

**Must be closed before:** Nothing — purely additive once the Nen Contract Application layer and completed-hours summation query exist. No urgency driving closure.

**Parked, not interrogated:** Two related ideas came up in the same session and were deliberately shelved by the architect until there's a better view of the finished program — not designed in depth, listed here only so they aren't forgotten:
- **Contract templates from Insights** — a "start a new contract like this one" shortcut on a *completed* contract's Insights recap, pre-filling the Advanced wizard from that specific past instance rather than a saved Contract Preset. Distinct from Contract Presets because it copies a real past result, not a deliberately-saved template.
- **Strictness tightening mid-contract** — already tracked as a parked v2 idea in `DOMAIN_DICTIONARY.md` (Strictness Rule section); re-confirmed parked in this session, no new detail added.

### Open Gap D — Spontaneous Redemption Path (fully interrogated 2026-07-08; dropped, not deferred)

**The original design:** When a contract is active with an outstanding deficit, the Main Page shows a prompt ("You're 1.5 hours behind on your Study Month contract. Start a redemption session?"). Clicking through pre-locks the Ritual's Ren step to the preset with the largest deficit and shows the deficit amount at Hatsu; the user picks any duration, including a partial redemption. The resulting Session is ordinary `Origin = Spontaneous`, `OriginatingBlockId = null` — no Block entity involved, matching the Ubiquitous Language definition of a Spontaneous Session.

**Why it turned out harder than the Planner-path redemption (which *is* built — see `DOMAIN_DICTIONARY.md` technical mapping):** the Planner path's deficit ledger (`GetNenContractDeficitsQuery`) is scoped **per missed block**, via `RedemptionEntry` value objects owned by the *covering* `PlannedBlock`. A Spontaneous session has no block to own an equivalent collection — its only anchor is `Session` itself, which has no `RedemptionEntries` field and, per the documented flow, is never given a specific missed block to target (only a preset). Closing that gap turned out to need real new structure, not a small addition.

**Options interrogated and rejected:**
- **`Session.RedemptionEntries`, mirroring `PlannedBlock`'s field directly, auto-allocated across missed blocks.** Rejected: the field would only ever be meaningful when `Origin == Spontaneous` — the same "nullness/relevance depends on a discriminator" smell `SessionOrigin` itself was introduced to eliminate (see `DOMAIN_DICTIONARY.md`, `SessionOrigin`).
- **Day-level only — let spontaneous sessions count toward Day Met via the already-built `GetNenContractCompletedFocusHoursQueryHandler`, no per-block attribution at all.** Rejected on closer inspection, not just complexity grounds: completed minutes are always attributed to the day the session/block actually happens on, including for the *existing, shipped* Planner-path redemption mechanism — a Friday redemption block covering a Tuesday miss credits Friday, never retroactively fixes Tuesday's Day Met. Day Met was never the currency "redemption" moves; the block-level deficit ledger is a deliberately separate mechanism that tracks "was this commitment eventually honored," independent of which day. Reusing only Day Met machinery therefore can't move the number the Planner checklist shows — it would just be "extra contract-relevant work helps today," true for any spontaneous session regardless of any deficit, dressed in redemption language without delivering redemption semantics.
- **A new independent `DeficitCoverage`-shaped entity, generalized to reference either a covering block or a covering session (nullable dual-source).** Rejected as over-built for the chosen scope: since the Planner path was never going to be migrated onto it (see next point), the block-source field would be permanently null in practice — reintroducing the same "field only sometimes meaningful" smell in a new location instead of avoiding it.
- **The same entity, but narrowed to spontaneous-only (`{Id, MissedBlockId, CoveringSessionId, MinutesCovered}`, no polymorphism) — full unification with the Planner path deferred.** This was the option the interrogation converged on as correct **if built**: additive-only change to `GetNenContractDeficitsQuery`'s existing formula (one more subtracted term), zero changes to shipped/tested Slice 6/7 code, matches the `DailyUsage`/`UsageCeiling`/`BlockedAttempt` precedent of "an entity with independent identity belonging to nothing but the fact it records." Naming it anything containing "Block" was explicitly rejected — it isn't one (no `Start`/`End`, no calendar placement, no participation in drag-math or enforcement); a name in the `RedemptionEntry` family (e.g. `SpontaneousRedemptionEntry`) was preferred instead.
- **A shared base type / inheritance between `FocusBlock` and a "SpontaneousBlock."** Rejected: `PlannedBlock`'s entire identity is a *pre-scheduled* calendar placement — that's what makes drag-math, `WorkHoursPolicy` guards, and the enforcement pipeline work. A Spontaneous session has no advance schedule; a "SpontaneousBlock" sharing that base would only ever be a retroactive shadow copy of `Session`'s own `StartedAt`/`EndedAt`, forcing an artificial is-a relationship to reuse one field, and widening `PlannedBlock`'s responsibilities to accommodate a case that doesn't share its nature (every consumer — Planner CRUD, enforcement, drag-math — would need a mental exception for it).
- **A shared `IRedemptionCapable` interface instead of a class hierarchy.** Also rejected: `FocusBlock` and `Session` always come from two separate repositories, so nothing ever holds a naturally-mixed polymorphic collection of them to justify the interface — it would have no real call site.
- **Using a shared type to help Insights distinguish Spontaneous vs Planned entries.** Unnecessary: `GetInsightsCalendarQuery` already unifies `BackgroundBlock`, `FocusBlock`, and `Session` (`Origin = Spontaneous`) into one `InsightsEntryDto` via a read-side projection — the correct DDD tool for "show heterogeneous concepts uniformly." A shared write-side base type would risk *blurring* the Spontaneous/Planned distinction Insights actually wants to preserve (e.g. Rule 11's "missed but later redeemed" labeling), not sharpen it.

**Decision:** Dropped, not deferred as a to-do. The Planner's redemption mechanism (domain/Application built Slices 6–7 2026-07-08; the UI to actually drive it — a "Redemption" toggle on the block-creation form — wasn't built until 2026-08-21, see `DESIGN_GAP_HISTORY.md` Gap 25) remains the only redemption mechanism. This also fits the app's own thesis better than the dropped feature did — NenFocus is built around *deliberate*, ritual-based intentionality; requiring the user to consciously link a specific block to a specific miss in the Planner is more consistent with that than a system silently auto-allocating coverage behind a Main Page nudge. That same standard resurfaced and was reaffirmed while building Gap 25's UI: an early design would have auto-linked a matching block silently, and was rejected for reopening exactly this reasoning.

**If ever revisited:** build the narrow, spontaneous-only entity (last rejected-options bullet above), not a shared block hierarchy, not a `Session` field, not the Day-level shortcut. All three were interrogated and found to either leak, over-build, or fail to deliver real redemption semantics.

**Must be closed before:** Nothing — this is a scope decision, not a blocked obligation.

### Open Gap E — Recurring Blocks (interrogated 2026-07-15; not yet built)

**The feature:** A `PlannedBlock` (Focus or Background) that repeats on a schedule — e.g. "9–5 every weekday" — instead of being a single one-off calendar entry. Distinct from Usage Ceiling (a soft ambient cap on access time) and from Nen Contract (a sealed, scored commitment) — this is purely a recurrence mechanism layered onto the existing ordinary block-scheduling flow. Type (Focus vs Background) is decided exactly as it is today: by which `BlockPreset` is picked when the block is created. Recurrence adds no new block type and has no relationship to `NenContract` — it lives entirely on the Planner, the same posture already staked out for this gap before interrogation (see Open Gap A's note distinguishing it from Usage Ceiling).

**Domain shape:**

| Concept | Shape |
|---|---|
| `RecurrenceRule` (value object) | `{ IntervalValue (int), IntervalUnit (Day \| Week \| Month), WeekdayMask (weekday flags, only meaningful when IntervalUnit = Week), EndCondition (Never \| OnDate(DateOnly) \| AfterOccurrences(int)) }`. One shape, not three parallel types — "Every day" and "Specific weekdays" are Planner UI presets that pre-fill this value object (`Every day` = `Unit=Day, Interval=1, Ends=Never`; `Specific weekdays` = `Unit=Week, Interval=1, WeekdayMask=<selection>, Ends=Never`), mirroring the existing Simple/Advanced Contract precedent — one underlying shape, simplified entry points for the common case. Chosen over three separate recurrence types specifically to avoid duplicating "does this fire on date X" logic three times, the same drift risk `DESIGN_GAP_HISTORY.md` Gap 11 and Gap 6 already caught and fixed elsewhere. |
| `RecurringSeries` (entity) | `Id`, `BlockPresetId`, `RecurrenceRule`, time-of-day template (`StartTime`/`EndTime`), `Title?`. Entity, not a value object — independent identity, independent lifecycle (created, edited, truncated, deleted on its own, not nested inside another aggregate) — same justification already used for `FocusBlock`/`BackgroundBlock`/`UsageCeiling`. |
| `PlannedBlock.RecurringSeriesId (Guid?)` | Nullable FK, mirrors `ContractId` exactly. `null` = ordinary one-off block, completely unaffected. Non-null = this row was generated from, and is still linked to, that series. |
| `SkippedOccurrence` (entity, append-only-style fact) | `{ Id, SeriesId, Date, ConflictingBlockId }`. No `Resolved` flag — deliberately avoids the "field only sometimes meaningful" smell already rejected once in Open Gap D. Existence alone means "pending, unresolved"; the row is deleted (not flagged) once the conflict clears and the occurrence is regenerated. No domain events — mirrors `SessionMissedEvent`'s "deliberately silent" precedent; surfaced by a query when the user next opens the app, not by an event handler chain. Belongs to nothing but the fact it records, same family as `BlockedAttempt`/`DailyUsage`/`UsageCeiling`. |

**Materialization — rows are real, not computed lazily:** Unlike Google Calendar (which stores one `RRULE` and expands occurrences lazily, only when a client requests a date range), NenFocus can't defer materialization — `ScheduledEnforcementService`'s heartbeat needs a concrete `PlannedBlock` row in the database to know something should be enforced today, even if nobody has opened the Planner in months. So a `RecurringSeries` materializes real rows ahead of time, on a rolling basis.

**Materialization horizon: 1 year, rolling.** A background heartbeat (mirroring `NenContractHeartbeatService`'s pattern — its own low-frequency `BackgroundService`, not the 5s enforcement loop) keeps each active series materialized 1 year ahead of "now," topping up the window as time passes, plus a cold-start catch-up check at app launch (mirroring the existing dual cold-start-sweep + live-heartbeat pattern already used for Missed Session discovery, since the heartbeat only runs while the app process is alive). The forward-looking row count for any series is constant regardless of whether its `EndCondition` is `Never` or a fixed date years out — only the current rolling window ever exists as real rows.

**Verified performance facts (checked against the live code before setting the horizon, not assumed):**
- `PlannedBlock.Start`/`End` are plain `DateTime`, not `DateTimeOffset` — the SQLite `DateTimeOffset` comparison/`ORDER BY` limitation already documented under `TECHNICAL_SPEC_INFRA.md` → Known Workarounds does **not** apply to this table.
- `PlannedBlocks` currently has **no database index** on `Start`/`End` (confirmed in `NenFocusDbContext.cs`) — every range query is a full table scan today. Not a blocker at any realistic personal-use scale (even a decade of daily-recurrence history is a few thousand rows, trivial for SQLite), but worth indexing `(Start, End)` eventually regardless of this feature — a general debt item, not specific to Recurring Blocks.

**Editing and deletion — three Google-Calendar-style scopes, identical for both operations:**
- **"This event"** — edits or deletes just that one instance. Editing clears its `RecurringSeriesId`, converting it into an ordinary standalone `PlannedBlock`; the rest of the series is untouched.
- **"This and following events"** — splits the series. The original `RecurringSeries` is truncated (its effective end moves to just before the edited/deleted instance's date). A **new** `RecurringSeries` is created starting at that instance, carrying the edited properties forward under the recurrence cadence. Already-materialized future rows from the old series, from that date onward, are replaced with rows generated from the new series.
- **"All events"** — edits the series template directly. Only propagates to instances still linked to that series (`RecurringSeriesId` matches); an instance already detached via "This event" is untouched, same as Google Calendar.

**Conflict handling — two distinct moments, deliberately different mechanics:**
- **At creation time** (user is actively in the Planner): every projected instance across the full 1-year window is validated up front against the existing "no two overlapping Focus Blocks" rule and any other existing block. Any conflicting date is shown to the user immediately, with a per-conflict choice: skip that specific occurrence (create the rest of the series with a gap there) or cancel creation entirely to adjust the pattern. Mirrors the "validate the whole operation before persisting anything" discipline `NenContract.Seal` already follows (`DESIGN_GAP_HISTORY.md` Gap 13).
- **At background top-up time** (a heartbeat running unattended, nobody watching): the conflicting occurrence cannot be created — that would violate the same overlap rule, no matter who's asked — so the skip is forced, not optional. A `SkippedOccurrence` row is written instead of silently discarding the gap. Next time the user opens the app, a query surfaces any pending `SkippedOccurrence` rows as an actionable prompt (not a passive notice): *"[Series] conflicts with [other block] on [date]. [Change it] [Skip]"*. "Change it" navigates to the Planner on that date so the user can resolve the conflicting block themselves — the system never auto-edits or auto-deletes a user's other block on their behalf. "Skip" dismisses and the gap stays.
- **Auto-fill on resolution:** once the conflicting block is cleared (edited, moved, or deleted), the missing occurrence reappears automatically on the next top-up pass — the top-up job must re-scan existing `SkippedOccurrence` rows for conflicts that have since cleared, not just extend the leading edge of the window — and its `SkippedOccurrence` row is deleted once regenerated.

**Enforcement — no new logic needed.** Materialized rows are ordinary `FocusBlock`/`BackgroundBlock` rows; `PolicyEngine`/`ScheduledEnforcementService` consume them completely unchanged. This feature only touches how rows get created and kept topped up, never how they're enforced.

**UI entry point:** a "Repeat" toggle on the existing Planner block-creation form (same tile-based creation flow as Google Calendar) — flipping it on reveals the `RecurrenceRule` picker (Every day / Specific weekdays / Custom, all three pre-filling the one underlying value object). No separate "recurring blocks" screen for v1.

**Must be closed before:** Nothing urgent — deferred until picked up. The domain shape, materialization strategy, editing/deletion scopes, and conflict-handling mechanics are all settled enough to start Slice 1 (domain entities + `RecurrenceRule`) whenever that happens.

\---

### Open Gap G — Nen Contract Recap Data for Insights (interrogated 2026-08-12; not yet built)

**The feature:** Structural facts about a contract — date range/duration, strictness rule, and which Block Presets were scheduled with how many hours each — readable independently of the Insights page itself being built. Widened 2026-08-12 to also cover **broken contracts**: a count of how many contracts the user has broken, and per-block completion breakdown (completed/late/missed/**cancelled**) for whichever blocks are still linked.

**Its two prerequisites are already closed** — the start/end lifecycle notifications and the Break-cleanup redesign (`PlannedBlock.CancelledAt`/`MarkCancelled`, which stops enforcement on a cancelled block and force-ends any active session tied to it) are both built; see `DESIGN_GAP_HISTORY.md` Gap 23 for the full closure record. That means the "Cancelled" category this recap needs already has real data behind it (`CancelledAt.HasValue`) — only the read-side query itself remains unwritten.

**Settled shape (not yet built):**

| Concept | Shape |
|---|---|
| `GetNenContractRecapQuery` / Handler (Application, new) | Given a `ContractId`, returns date range, `StrictnessRule`, a per-preset hours breakdown (Focus vs Background), and — for a `Broken` contract — a per-block breakdown reported as **separate categories**: **Cancelled** (`CancelledAt.HasValue`) versus `CompletedOnTime`/`StartedLate`/`Missed` (`IFocusBlockOutcomeClassifier.Classify(...)`, untouched) for everything else. Composes `INenContractRepository.GetByIdAsync` + `IPlannedBlockRepository.GetForContractAsync` + `IBlockPresetRepository`, the same "inject a sibling query/repository" composition style `GetActiveNenContractQuery` already uses. Deliberately a standalone query, not bundled into the Insights page build — Insights can consume it later exactly as it will consume `GetInsightsCalendarQuery`, but this query has no dependency on the Insights page existing. |
| `INenContractRepository.GetAllAsync()` (or `GetByStatusAsync(NenContractStatus)`) (Application, new) | The only new repository surface needed — feeds a simple "how many contracts have you broken" count for Insights. No new persistence, just a missing read method on an existing repository. |

**Deliberately out of scope, kept separate:** performance/scoring data — Day-Met percentage (Open Gap C1), in-contract streaks (C2), and deficit digest notifications (C3) — stays exactly as already designed in Open Gap C. This gap is structural facts only (what the contract *was*, not how well it went); C1/C2/C3 can layer on top of the eventual Insights UI independently, without this gap needing to change shape.

**Parked, not interrogated — Insights milestone tracking, scoped to Sessions:** raised in the same session as a "what else could an event handler be used for" discussion, deliberately shelved rather than designed. An earlier version of this note anchored the idea to `BlockPresetCreatedEventHandler` — a no-op stub originally commented as a hook for "Insights milestone tracking (e.g. 'Created your first profile')." That handler (and its stripped comment) has since been deleted (2026-08-12): celebrating "you created a preset" was judged not worth tracking — completing a **Session** is the meaningful, celebration-worthy unit in this app, not creating configuration. If revisited, the natural anchor is `SessionEndedEvent` (and possibly `SessionMissedEvent`, currently unhandled), not any `BlockPreset` event: milestones like "first session completed," "N sessions completed," or a weekly recap of total focus hours/session count, surfaced as a small badge list or stat block on the Insights page — not another toast, since session completion already has its own Post-Session screen for in-the-moment feedback. This is **not** a zero-new-state slice: it needs a new append-only fact (same family as `BlockedAttempt`/`DailyUsage` — belongs to nothing but the fact it records) plus a query for Insights to read it back. Open questions for whenever this is picked up: what specifically counts as a milestone, one-shot vs progressive/counted milestones, whether Missed sessions count against a streak or are simply excluded, and whether it retroactively counts sessions completed before the feature existed.

**Must be closed before:** Nothing urgent — deferred until Insights work starts. It remains independently buildable and does not depend on either now-closed prerequisite. The parked milestone idea has no urgency and isn't scheduled.

\---

### Open Gap I — Break Reminder Notifications Are Unreachable Dead Code (surfaced 2026-08-12; not yet built/wired)

**Found while auditing Open Gap B's rehydration path, not while looking for it.** The stated goal was checking whether crash-resilience risk extends beyond the two failure modes Gap B already documents. The session/enforcement rehydration path (`SessionStateService.InitializeAsync()`, `ActiveSessionMonitor`'s timer rebuild from stored `BreakStartedAt`/`BreakSettings`) checked out clean — both already-documented Gap B failure modes are accurate and nothing new was found there. But tracing the specific claim that led here — `TECHNICAL_SPEC_INFRA.md`'s "break reminders... do not survive app restarts" — surfaced something bigger: **the break reminder feature can't fire in the first place, restart or not.**

**Verified against the full solution, not assumed:** `INotificationService.ScheduleBreakReminderAsync` is fully implemented in `WindowsNotificationService` — real `Task.Delay` + `CancellationTokenSource` tracking, exactly as documented. Grepped every file in the solution: **it is never called from anywhere.** Not `SessionStartedEventHandler`, not any command handler, not any ViewModel. `CancelAllRemindersAsync` *is* called (from `SessionEndedEventHandler`, on every session end) — but since nothing ever schedules a reminder, that call is a harmless no-op every single time. `CancelReminderAsync` (the single-token variant) is never called at all.

**Why this is more than unused plumbing:** the Ritual's Hatsu step explicitly lets the user **configure** break reminder settings when starting a session ("user... can configure break reminders") — a user setting an interval reasonably expects to actually get reminded. `DOMAIN_DICTIONARY.md`'s Session section describes it as live, working behavior: *"When a break reminder fires, a break countdown timer replaces the session timer on the Main Page."* `TECHNICAL_SPEC_INFRA.md`'s Done list includes "scheduled break reminders (in-process Task.Delay)" alongside other genuinely-shipped features. Both docs overclaim a feature that cannot currently fire.

**What actually works today:** breaks are 100% user-initiated. `MainPageViewModel.TakeBreak()` → `StartBreakCommand { SessionId = ..., UserInitiated = true }` is the only path into `OnBreak`. The `UserInitiated` flag's very existence is a strong signal a non-user-initiated (reminder-triggered) path was designed and anticipated, then never actually built.

**Two directions, neither decided — this is a "for now" write-up, not a design:**
- **Wire it up for real.** Call `ScheduleBreakReminderAsync` from `SessionStartedEventHandler` (alongside the existing enforcement + notification calls) using `Session.BreakSettings.IntervalMinutes`; on fire, dispatch `StartBreakCommand { UserInitiated = false }` rather than just showing a toast, matching the documented "reminder fires → break starts" framing and giving the `UserInitiated` flag a real reason to exist. **This would reopen Gap B's exact class of problem in a new spot**: a scheduled reminder is just as vulnerable to not surviving a restart as the two failure modes Gap B already tracks — `InitializeAsync()` would need to reschedule the next reminder on rehydration (computing remaining time from `Session.StartedAt`/`BreakSettings.IntervalMinutes`, the same "recompute from stored data" idiom `ActiveSessionMonitor` already uses successfully). Wiring this up without also handling its own rehydration would just relocate the current bug from "unreachable" to "reachable but still silently lost on crash."
- **Or confirm it was deliberately abandoned and correct the docs instead** — remove the "when a break reminder fires" framing from `DOMAIN_DICTIONARY.md`, correct the Done-list entry in `TECHNICAL_SPEC_INFRA.md`, and either delete the dead interface members or leave them as clearly-marked unused scaffolding.

**Must be closed before:** Nothing urgent — low severity (a missing UX nicety, not a data-integrity or enforcement-safety issue like Gap B's tracked failure modes). Flagged so the doc overclaim and the dead-code/half-built-feature status aren't silently carried forward.

\---

### Open Gap J — System Clock Tampering: Detect and Nudge, Not Block (raised 2026-08-20; parked, not interrogated)

**The gap:** `ScheduledEnforcementService`'s heartbeat, and every consumer of `ISystemClock.UtcNow`, currently trusts the OS wall clock unconditionally. A user could manually set their system clock backward (before a block's `Start`, dropping enforcement entirely) or forward (skipping through a block's remaining time) to bypass a block they set for themselves. No detection or response exists today.

**Explicitly not a timezone concern.** The app is internally UTC-consistent throughout (`SystemClock.UtcNow`, `PlannedBlock.Start`/`End`, etc. — confirmed repeatedly during today's session). A timezone change alone never moves `DateTime.UtcNow`; only a manual date/time change does.

**Threat model, framed deliberately narrow:** NenFocus is a self-discipline tool — the person who might tamper with the clock and the person the app is protecting are the same user. Bulletproof anti-tamper (kernel-level clock hooks, etc.) would be disproportionate for a personal focus tool and was explicitly rejected as the goal. **The architect's own framing, which settles the intended response:** don't block further or otherwise punish — just remind the user not to cheat. A nudge, not an escalation.

**Candidate detection mechanism (sketched, not designed):** compare elapsed wall-clock time (`DateTime.UtcNow` deltas between heartbeat ticks) against elapsed monotonic time (`Environment.TickCount64`/`Stopwatch`, driven by system uptime and unaffected by manual clock changes). A large mismatch — wall clock jumped backward, or forward by far more than the ~5s tick interval could explain — is a strong signal of manual tampering. The open design problem is distinguishing that from two legitimate causes of the same kind of discontinuity: sleep/hibernate (large forward jump, expected — adjacent to Open Gap B's already-tracked territory) and ordinary NTP drift correction (small, should be tolerated, not flagged).

**Must be closed before:** Nothing — parked for later. Not yet interrogated: no decision on where the check lives (`ScheduledEnforcementService` heartbeat is the obvious candidate, already the sole per-tick observer of both `now` and elapsed time), what the nudge actually says, whether it's rate-limited (once per detected event? once per day?), or how sleep/wake and NTP-drift get reliably excluded from triggering a false nudge.

\---

### Open Gap K — Planner Time Range Editor, built on Cross-Midnight Block Support (interrogated 2026-08-21; not yet built)

**Two features in a mandatory build order — do not start Part 2 before Part 1 is closed.**

1. **Cross-Midnight Block Support** (preliminary read only — needs its own full Design Gap interrogation before implementation).
2. **Planner Time Range Editor** — Google Calendar-style start/end editing on the Planner block form. Fully interrogated, settled, ready to build once Part 1 lands.

Part 2 depends on Part 1 because the duration-preserving shift math and the End-time dropdown both need to reason about a block whose End can land on a different calendar day than its Start — impossible to represent in the form today.

---

#### Part 1 — Cross-Midnight Block Support (preliminary read; not fully interrogated)

**The rule:** a block spanning midnight credits hours to each day it actually occupies. A 22:00–01:00 block (3h) credits 2h to the day it starts and 1h to the day it ends — never all 3h to one day.

**Already correct, verified against the live code — nothing to build here:**
- `PlannedFocusHoursCalculator.Calculate` (`NenFocus.Domain/Services/PlannedFocusHoursCalculator.cs:9-25`) already walks a block's range day-by-day and splits its hours exactly per this rule. Feeds the planned-hours/Day-Met-preview machinery (`ComputePlannedFocusHoursQuery`, `RefreshDayFloorPreviewsAsync`, `UpdatePlannedBlockValidator`'s floor checks). Untested for this case only because no UI path can create a cross-midnight block yet.
- `PlannedBlock.Reschedule`/`FocusBlock.Create` (`NenFocus.Domain/Entities/PlannedBlock.cs:67-73`, `FocusBlock.cs:16-38`) have no same-day constraint at all — only `end > start`.
- `PlannedBlockRepository.GetForDayAsync` (`NenFocus.Infrastructure/Persistence/Repositories/PlannedBlockRepository.cs:14-25`) already queries by overlap (`b.Start < dayEnd && b.End > dayStart`), not "starts on this day" — a cross-midnight block would already surface correctly on both days' Planner queries, and existing per-day tile clipping (`PlannerLayout.GetTilePosition`) would likely already render it as two separate tiles without changes.

**Real gaps, confirmed by reading the code — these need work:**
- `GetNenContractCompletedFocusHoursQueryHandler.cs:62` credits a session's entire completed minutes to a single day (`block.Start`'s date) — no split. This is the *actual*-hours side (vs. the planned-hours side above, which already works), and it's the harder fix: it needs to derive a per-day split from a real session's actual start/end timestamps, not just a scalar credited-minutes value.
- `WorkHours.ContainsRange` (`NenFocus.Domain/ValueObjects/WorkHours.cs:26-31`) anchors its window to `start.Date` — a cross-midnight Focus block under an active Work-Hours-enforcing contract would be rejected outright. Needs a decision on what "Work Hours" even means for a block spanning two days.
- `SplitPlannedBlockValidator` explicitly requires both halves of a split stay on the original block's day — needs an explicit decision on whether Split allows crossing at all.
- `PlannerDragMath.ClampRange` clamps at 24:00 — dragging a tile past midnight is currently impossible.
- The Planner form has one shared `DatePicker` for both Start and End (`PlannerPageView.axaml:414`) — no "which calendar day does End belong to" concept exists yet.

**UI behavior settled during this session (informal — revisit and confirm at the full interrogation):**
- Typed or picked end-before-start auto-rolls to the next day (Google-style); no explicit "+1 day" toggle.
- When it rolls, a second date box appears next to End showing the resolved date (e.g. `3:30pm to 11:30am · Aug 22, 2026`); hidden otherwise.
- **Focus blocks are capped at 12 hours.** Background blocks are not — no existing max-duration rule exists for either type today; this cap is entirely new, and Focus-only by design, mirroring the already-established Focus-only Work Hours exception (`ValidateDraftPlacement`'s own comment: *"Background blocks are never Work-Hours constrained"*, `PlannerPageViewModel.cs:1409`). Exceeding it is rejected at Save, never silently clamped — matches this codebase's existing reject-after-the-fact philosophy for every other Planner placement rule.
- **Cap enforcement lives in Domain, not Application** — the more-correct DDD placement, confirmed by precedent already in the entity: `FocusBlock.Create` already enforces `end <= start` inline, and `AssignRedemptionEntries`'s own doc comment draws the exact line ("same-preset, uncovered-deficit, and same-day constraints are Application-layer concerns, not enforced here" — i.e., anything needing a repository lookup stays in Application; pure Start/End facts belong in the entity). The 12h cap needs nothing but the two `DateTime`s already in hand, so it belongs beside the existing `end <= start` guard. `FocusBlock` will also need its own override of `Reschedule` (currently only defined on the base `PlannedBlock`), since `UpdatePlannedBlockCommandHandler` mutates existing blocks through `Reschedule`, not `Create`. Mirrored by a thin duplicate check in `CreatePlannedBlockValidator`/`UpdatePlannedBlockValidator` purely to produce a friendly `FormError` instead of an unhandled exception — the exact same two-layer pattern already used for `end <= start` today (Domain is the real, unbypassable invariant; the validator exists only for messaging, and protects nothing the entity doesn't already guarantee).

**Not yet interrogated:** day/week boundary semantics for the completed-hours split, exact query signature changes to `GetNenContractCompletedFocusHoursQueryHandler`, whether Split allows crossing, and full Work-Hours semantics for a crossing block. This needs its own dedicated interrogation before implementation — what's written here is a preliminary read done while scoping Part 2, not a settled design.

---

#### Part 2 — Planner Time Range Editor (fully interrogated, settled)

**The feature:** the Planner block-creation form's `FormStartTime`/`FormEndTime` fields (`AutoCompleteBox`, `PlannerPageView.axaml:420-432`) gain Google Calendar-style behavior: moving Start preserves duration by shifting End; moving End re-derives duration without moving Start; both get dropdowns of preset times with the End dropdown labeled by resulting duration.

**Scope:**
- **Block create/edit form** — gets the full treatment below.
- **Split dialog** (`SplitFirstStartTime`/`SplitFirstEndTime`/`SplitSecondStartTime`/`SplitSecondEndTime`) — explicitly **no** linkage between any of the four fields, confirmed. They stay on the plain flat 15-minute grid, unchanged from today — no anchored/duration-labeled dropdown either, since there's no live relationship to anchor one to.
- **Work Hours fields** (Presets page, `ContractPresetPageView.axaml:245-257`, `WorkHoursStartText`/`WorkHoursEndText`) — plain widget only. Confirmed: moving Start does **not** move End here — it's a boundary-window rule ("work may happen between these two clock times"), not a scheduled appointment with a duration worth preserving.
- The Contract Wizard has no time input of its own — non-issue.

**Linkage rule (form only — does not touch drag/resize):**
- Editing **Start** as typed/picked text → **End** shifts to preserve the current duration. Permanent, re-derived every time, never reverts to an original value.
- Editing **End** as typed/picked text → Start stays put; the new duration becomes what's preserved on the next Start edit.
- **Dragging/resizing a tile on the calendar grid is completely unaffected** — `PlannerDragMath`/`MoveOrResizeBlockAsync` keep today's plain resize semantics (opposite edge stays fixed). The linkage is a form-text-field-only behavior; drag math is a separate code path and is out of scope for this feature.
- Needs a "populating" guard (bool flag) around `EditBlockAsync`/`NewBlockAtAsync`/`OpenNewBlockFormWithRangeAsync`'s `FormStartTime =`/`FormEndTime =` assignments, so opening the form for an existing block doesn't fire the shift handler against stale leftover values from a previous form session.
- `IsStartLocked` (block's start already passed) freezes Start; End stays editable in that state — already true today (`PlannerPageView.axaml:424,432`), no change needed, just confirmed as compatible.

**Dropdowns:**
- **Start:** unchanged — the existing flat `:00/:15/:30/:45`, 24-hour, context-free grid (`GenerateTimeOptions`, `PlannerPageViewModel.cs:589-600`). Always shows the same absolute grid regardless of what's currently typed, and is never filtered by Work Hours or contract windows — matches this codebase's existing reject-after-the-fact philosophy (nothing else pre-filters a dropdown to avoid an eventually-invalid choice; every placement rule is enforced once, at Save).
- **End:** new, generated live from the current Start value — Start+15, +30, +45, +60 (first hour, 15-minute steps), then +90, +120, +150… (30-minute steps from there on), wrapping past midnight back to just before Start, truncated at the earlier of that wraparound or the 12-hour Focus cap (Focus blocks only — Background blocks get no cap truncation, per Part 1). Regenerated on every Start change. Confirmed against two independent worked examples (10:15 start, 10:35 start) — both fit this exact step pattern and no other.
- Each End entry labeled with its resulting duration: `(30 mins)`, `(1 hr)`, `(1.5 hrs)`, `(3 hrs)` — decimal-hours-past-the-hour-mark family, Google's own wording style. **New** converter/formatter — not a change to `DurationToStringConverter` (`NenFocus/Converters/DurationToStringConverter.cs`), which stays exactly as-is; it's shared with the post-session completion screen and the block snapshot modal, and changing its output would silently change both.
- No persistent duration readout under the fields — dropdown-suffix only.

**Parsing:** one-line patch to the existing `TryParseFormTime` (`PlannerPageViewModel.cs:1170-1174`, already shared by the form and Split dialog):
```
if (!TimeSpan.TryParse(s, out result)) return false;
if (result.Days != 0) { result = default; return false; }
```
Closes a real latent bug found while investigating this: today, typing `93` parses "successfully" as a 93-day `TimeSpan` (`TimeSpan.Parse` rolls anything over 23 hours into extra days rather than rejecting it) and would silently place a block 93 days out with nothing to catch it. Confirmed via direct testing, not assumed. `9:3` deliberately keeps its current behavior (`09:03`, not `09:30`) — a full custom positional parser was considered and explicitly rejected as overkill for a rare edge case. The Preset page's separate `TimeOnly`-based parser (`ContractPresetPageViewModel.cs:262-265`) doesn't have this bug and isn't touched by this patch; its own pre-existing `"24:00"`-rejection gap is unrelated and out of scope here.

**Validation timing:**
- **Live** (every keystroke): syntax only — can this text parse as a valid time-of-day at all. Field turns red; on blur (`LostFocus`), snaps back to the last successfully-parsed value. Needs new plumbing: a `_lastValidFormStartTime`/`_lastValidFormEndTime` backing field pair, and a `LostFocus` hook wired from the View's code-behind (`PlannerPageView.axaml.cs`) into the ViewModel — nothing in this form has focus-driven behavior today.
- **Save-time** (unchanged): 12h cap, End>Start, Work Hours, floor, overlap — exactly as today, surfaced via `FormError`. Explicitly confirmed: no live business-rule validation (floor/overlap/contract) is being added by this feature — only the syntax-level red/snapback above is new.
- 24-hour clock throughout (`HH:mm`) — matches every other time field in the app; the Google Calendar screenshots used as reference are 12-hour only because that's Google's locale, not a spec requirement.

**Where new code lives:**
- Duration-preserving shift math → a new static helper in `ViewModels/Components/`, sibling to `PlannerLayout`/`PlannerDragMath` — pure presentation math with zero business-rule content, same category as those two, not a Domain value object (an earlier draft of this gap considered a Domain value object, reasoning from `WorkHours`/`SessionDuration` as precedent, but those carry real enforced business rules this math doesn't; `PlannerDragMath` turned out to be the closer, better-fitting precedent once compared directly).
- End-list generation → new static helper, same neighborhood.
- Duration-label formatting → new converter, sibling to `DurationToStringConverter`, not a modification of it.
- No `DOMAIN_DICTIONARY.md` entry for the shift-math concept — confirmed it's plumbing, not ubiquitous language; it enforces nothing anything else in the app reasons about.

**Proposed build order once Part 1 lands (thinnest-slice-first):**
1. Domain: 12h cap in `FocusBlock.Create` + new `Reschedule` override, with tests.
2. Application: mirroring validator messages in `CreatePlannedBlockValidator`/`UpdatePlannedBlockValidator`, with tests.
3. UI-layer shift-math helper, unit-tested in isolation, not yet wired to the form.
4. Wire shift math into `OnFormStartTimeChanged`/`OnFormEndTimeChanged` + the populating-guard.
5. Parser patch (the `Days != 0` guard above).
6. Snapback: last-valid-value tracking + `LostFocus` wiring + red-field styling.
7. End-dropdown generation + duration-label converter, regenerated on Start change.
8. Manual verification in the running app.

**Must be closed before:** Nothing urgent — deferred until picked up. Part 1 must be interrogated and closed before Part 2's implementation begins; Part 2's own design is settled enough to start Slice 1 (Domain 12h cap) the moment Part 1 lands.

\---

## Minor Cleanups

Trivial style or consistency fixes that don't rise to the level of a Design Gap. Deferred until the relevant file is next edited for other reasons. Items here should be deletable in a single line change.

*(No outstanding items.)*

