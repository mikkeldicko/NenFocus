# NenFocus — Module Standards

This file defines the structural rules every module in NenFocus must follow. It operationalises the "Deep Modules" principle from our High-Level Development Strategy.

---

## The Deep Module Test

Before merging any new class or service, ask: *Can a developer understand how to use this module in under 30 seconds by reading only its interface signature?*

- **Yes** → the module is deep. Proceed.
- **No** → the interface is leaking implementation details. Redesign before writing code.

---

## Where a Rule Lives (added 2026-09-28)

Every rule has exactly one home, chosen by **what data it needs** and **whether it is knowledge or coordination**. Put it in the smallest home that fits.

| Home | When | Examples |
|------|------|----------|
| **Entity method** | A question about one thing with an identity, answered from its own data | `PlannedBlock.IsActive`, `PlannedBlock.EffectiveEnd`, `FocusBlock.IsAwaitingRitual`, `Session.LiftsBlockingAt` |
| **Value object** | Values that belong together and carry their own rules; no identity (equal values are the same thing); usually used in many places | `BreakSchedule`, `WorkHours`, `ContractDay`, `SessionGoal` |
| **Domain service** (`Domain/Services/`) | A business rule that needs several things at once and belongs to none of them; stateless | `PolicyEngine` (blocks + sessions + presets), `FocusBlockOutcomeClassifier` (grace rule shared by outcome and credit) |
| **Application service** (`Application/Services/`) | No business rule — takes a Domain answer and acts on it (database, OS, notifications) | `EnforcementSynchronizer`, `Notifier`, `BlockMarkerNotifier` |

**Decide in this order:**
1. **Rule or coordination?** Only asks the Domain and then acts → application service. Stop.
2. **One thing's data or several?** Several → probably a domain service, but try step 4 first.
3. **Identity or just values?** Has an identity and a lifecycle ("*this* block") → entity method. Just values that travel together → value object. Strong signal: the same primitives keep appearing together (`Start` + `End`) and the same formula keeps being written on them.
4. **Can one of them own it if the others are passed in?** `FocusBlock.CanContributeOwnDeficit(outcome)` — the block owns the rule, the caller supplies the outcome. Only when no single thing is the natural owner does it become a domain service.

**Warning signs:**
- A domain service answering a question about a single entity → the rule belongs on the entity (anemic domain).
- A value object wrapping something used once, with no rule attached → it adds a type without removing any duplication.
- The same expression over raw fields in a second place → the rule has no home yet (see the Application Handler Forbidden Zone below).

**Repositories: narrow in SQL, decide in the entity (added 2026-09-29, Gap 89 Pass 2).** A repository query may *narrow* with a filter that can only include too much — a superset (e.g. `End > now`, since `EffectiveEnd` is never later than `End`); the entity makes the final decision (`IsActive`, `HasEnded`, `IsInEffect`). Precedent: `PolicyEngine` over-fetches by ±1 s and `IsActive` decides (Gap 21). A superset filter can't drift into a wrong answer, only into a few extra rows. A SQL **copy of the rule itself** is allowed only when even the narrowed data is too large to load, and then it is marked as a copy and pinned by a parity test (as `GetUnresolvedFocusBlocksAsync` mirrors `FocusBlock.IsUnresolved`). A comment saying "mirrors X exactly" with no parity test is a promise nothing checks (Gap 94). **Prefer a filter the Domain builds** (`TimeSlot.Filters.Overlaps`, applied through `SlotQuery.OnSlot`): it is the rule itself, translated to SQL, so it needs no parity test at all. Gap 88 replaced the `IsUnresolved` SQL mirror this way.

---

## Design Review Rules (added 2026-09-29)

Learned from the Gap 88 interrogation, where every one of these was a real mistake in a proposed design that had to be corrected afterwards. Apply them when designing, and again when checking a plan against this file.

**Choosing the home**
1. **Walk "Decide in this order" literally; convenience is not a step.** "Build it once and ask it many times" or "it's all about the same topic" does not turn a domain service into a value object or merge two things. *(Proposed a `FocusAvailability` value object for "build once, ask many" when the decision order said domain service.)*
2. **Things that change for different reasons get different homes.** A rule ("may a block go here") and a strategy that only *asks* that rule ("where should we suggest one") are separate, even when both are "about free time". Test: name a plausible change — does it touch one and not the other? Then they're separate. *(Put the Cover Deficit search into the placement rulebook; a UI change — a ghost tile — would have deleted it and edited the rulebook.)*
3. **One caller doesn't earn a new service; one rule doesn't earn two.** Don't create a service for a single caller (leave it where it is, recorded), and don't split one rule's answer across several types. *(Proposed a separate redemption-placement service, then two result types where one sufficed.)*
4. **A query or handler has one job.** If it also decides what the data *becomes* (shortening, rounding, capping), that decision belongs with whichever answer it depends on, not in the query that happens to need it. *(Put redemption capping into the placement query; it belongs with redemption attribution, which already owns the entries it depends on.)*

**Shaping types**
5. **An enum value or type exists only for a distinction that changes behaviour.** A difference that only changes wording is not a domain distinction — it goes in the message. Check it isn't ambiguous either: can the same situation produce both values? *(Split "Focus block in the way" from "session in the way" for wording; a running planned session and its own block are both in the way, so the choice was arbitrary.)*
6. **A value object with no methods is fine if it has a constructor rule or is used in several places;** the warning sign is a wrapper used *once* with no rule. Results a service returns are value objects in `ValueObjects/`, not files in `Services/`. *(Moved result types into `Domain/Services/` by copying one odd precedent, then back.)*
7. **Don't copy a precedent without checking the precedent is itself right.** One existing file doing X is not a convention. *(`ContractBlockBatchPreparation` in `Domain/Services/`.)*
8. **Names say what a thing is or does in the domain.** A service named like a noun ("`BlockPlacement`") reads as a thing; follow the Naming Conventions row for its kind rather than adding a suffix to one class.

**Verifying before claiming**
9. **Verify a suspected bug in the code before calling it one.** Read both sides (validator *and* handler, query *and* caller). *(Flagged an Update-contract mismatch that the handler's own code ruled out.)*
10. **Before claiming "one source", grep every raw use of the fields involved** — Domain, Application, repository SQL, validators, ViewModels, `*Math` helpers. *(Claimed `PlannedBlock` had no raw comparisons left while `FocusBlock.EffectiveEnd`, `Classify` and a Split validator still compared `Start`/`End` directly.)*
11. **Before removing a parameter, flag or method, list every caller.** *(Planned to remove `ritualOnly` without noticing `CheckFocusBlockBatchOverlapQuery` and Edit contract's `ExcludeContractId` depended on it.)*
12. **Moving a check earlier in the pipeline (handler → validator → UI preview) must check the same value that gets saved.** If the handler transforms the value first (capping, rounding), the earlier check needs the transformed value too. *(Moving the overlap check into validators would have checked a redemption block's uncapped length. Again in Gap 97: deleting the Create command's `ContractId` exposed that the validator ran attribution on a capped slot while the handler saved the full slot unlinked — a field can hide such a mismatch until it is removed.)*
13. **Rounding up must never cross a bound the user set.** "Round up to whole minutes" inside a *shortening* function can lengthen past the requested end — clamp to it. And rounding needed in two places is a rule: name it once. *(Proposed a bare `Math.Ceiling` in `CapEndToRedemptionCoverage`.)*

**Checking a plan**
14. **A standards check reads the code, not only the plan.** Each re-check of Gap 88 against the plan text alone passed; each check against the code found something. Surface what the code actually does (including when it contradicts what someone believes — e.g. started blocks *were* editable) and ask, rather than assume either way.
15. **When a decision changes a signature or name, update every mention in the plan in the same edit.** Stale signatures in a plan get implemented. *(The Gap 88 plan kept an old `Check(candidate, …)` signature and "unchanged" wording after both had been decided otherwise.)*
16. **Before approving a signature, run every existing caller's hardest case through it** (added 2026-10-02, Gap 97). A signature can read correctly and still lack an input one caller needs. The mirror of rule 11, for a new or changed signature. *(Approved `Check(contractsInEffect)`; a block already in a contract — Seal's day-1 block, a Committed block moved out — touched no period and came back allowed, until `Check` also took the block's membership.)*

---

## Guard Hierarchy (added 2026-09-29, Gap 98)

Once a rule has its home, something must stop code from going around it. Use the strongest guard that can reach the rule:

| Rank | Guard | When | Examples |
|------|-------|------|----------|
| 1 | **Compiler** — encapsulation, value objects, private fields: the wrong code can't be written | The rule has a natural owner (see Where a Rule Lives) | `TimeSlot` held by blocks and sessions (Gap 90) — no raw `Start`/`End` left to compare |
| 2 | **Test** — architecture tests (`ArchitectureTests/`), parity tests: the wrong code fails `dotnet test` | The compiler can't reach it (SQL mirrors, EF's storage converters, a DTO field with no value object behind it), or the compiler step's gap isn't built yet. The UI boundary is **not** out of reach by itself: a DTO may carry a Domain value object (only entities and services are forbidden in the UI), and then the compiler guards the UI too (corrected 2026-09-29, Gap 99) | Only `SystemClock` reads the machine clock; repository SQL matches `FocusBlock.IsUnresolved` |
| 3 | **Review** — Design Review Rules above | Judgment only: is the module deep, is this the right home | Deep Module test, Where a Rule Lives |

**Every gap's plan answers:** *can the compiler own this rule? If not yet, which test guards it until then?* A test standing in for a later compiler step says so in its header and names the gap that retires it.

**Before settling for rank 2, name what the compiler can't reach and check that claim in the code** (added 2026-09-29, Gap 99). "Can't reach" is a claim like any other (Design Review Rules 9, 14). *(Gap 99 nearly guarded `Moment`'s raw exit with a test listing every DTO builder, repeating this table's old "DTOs carry plain values" example; one grep of `ViewModelRuleTests` showed DTOs could carry `Moment` itself, leaving one storage exit for the test.)*

**Migrating to a stronger type in steps (Gap 88):** keep the old member as an `[Obsolete]` read-only pass-through of the new one, and put conversion bridges in one `[Obsolete]` file (as `MomentBridge` was). Every remaining use is then a build warning, so the compiler keeps the to-do list, each step lands verified, and a guard test lists one file instead of many. Delete the bridge when the warnings reach zero.

**Don't over-apply rank 1:** a value object is earned by values that travel together *and* carry a rule — wrapping a primitive used once only adds a type (see Where a Rule Lives → Warning signs).

---

## Layer Responsibilities

### Domain (`NenFocus.Domain`)

**Owns:** Business rules, invariants, and state transitions.

**Allowed:**
- Entities, value objects, domain events, enums, constants
- Validation in value object constructors
- State-machine methods on entities (`Start()`, `Complete()`, `Miss()`, etc.)

**Forbidden:**
- Any reference to infrastructure, persistence, or UI
- `async` / `await` (domain logic is synchronous)
- `DateTime.Now` / `DateTime.UtcNow` — pass timestamps in as parameters so the domain is testable without mocking the clock

---

### Application (`NenFocus.Application`)

**Owns:** Use cases, orchestration, and contracts.

**Allowed:**
- Command handlers, query handlers, validators
- Interface definitions for all external dependencies (`ISessionRepository`, `IEnforcementService`, etc.)
- DTO definitions
- Event handlers (orchestrating side-effects in response to domain events)

**Forbidden:**
- Direct `new` of infrastructure types
- EF Core, SQLite, Windows API references
- UI framework types (`DispatcherTimer`, Avalonia types)
- Reading the machine clock (`DateTime.UtcNow`/`Now`/`Today`) — here or in any layer. `ISystemClock` is the one source of "now"; only `SystemClock` reads the machine clock, because it carries the debug offset (added 2026-09-29, Gap 98)
- `Math.Clamp`, `TimeSpan.ToString(format)`, progress-percentage arithmetic — these belong here only if they are domain rules; if they are purely for display, they belong in a converter

**Forbidden (added 2026-08-21 — The Application Handler Forbidden Zone):**

#### No Re-Deriving a Business Rule From Raw Entity Fields

A handler must not read an entity's raw fields (`Start`, `End`, `RitualCompletedAt`, `RedemptionEntries`, `BlockedApps`, etc.) and combine them inline to answer a business-meaningful question. If the question is "is this true about the entity," the entity (or a Domain Service, when the answer needs more than one aggregate) answers it through a named method. The handler asks; it does not compute.

> **Rule:** The moment a second call site needs the same fact about an entity, that fact must already be a named method on the entity — never re-derived a second time from primitives, even if the second derivation "looks the same."
>
> **Why this is a hard rule, not a style preference:** in one session (2026-08-21), this exact anti-pattern produced four separate live bugs, each shipped, tested, and working *until a second caller needed the same fact and quietly computed it differently*:
> - `PlannedBlock.IsActive` compared against raw `End`; `EffectiveEnd` (a few lines away, on the same entity) already had the right answer for "is this window still actually reserved." `PolicyEngine` used the wrong one, and a block that ended its session early kept blocking a genuinely new block from ever triggering its Ritual. Fixed by routing `IsActive` through `EffectiveEnd` — one definition, not two.
> - `GetNenContractDeficitsQueryHandler` decided "is this block a deficit source" via two separate inline `continue` guards instead of one question — see `DESIGN_GAP_HISTORY.md` Gap 29, fixed by `FocusBlock.CanContributeOwnDeficit(outcome)`.
> - `UpdateBlockPresetValidator` computed "is this edit at least as strict" via a blanket superset check on raw `Websites`/`BlockedApps` collections, inverted for one preset type nobody had a test for — see Gap 30, fixed by `BlockPreset.IsAtLeastAsStrict(...)`.
> - `GetNenContractDeficitsQueryHandler`'s "how much is already covered" sum, called from three different places (display, create-attribution, edit-attribution) with three subtly different needs, let a block being edited count its own stale, pre-edit contribution as coverage from someone else — collapsing its own new entry toward zero on every edit.
>
> **Smell to watch for while reviewing any new Application handler:** a `.Where(...)`, `if`, or arithmetic expression built from more than one raw entity field, especially one that resembles logic that already exists — even loosely — somewhere else in the codebase. Grep for the fields involved before writing the expression.
>
> **Canonical example of doing it right:** `FocusBlock.CanContributeOwnDeficit(FocusBlockOutcome? outcome)` — takes the already-classified outcome as a parameter (the caller owns *when* to classify, since that needs `now`/session data), but the entity owns the *rule* of what counts as its own deficit.

#### No Baking a Computed Fraction Into a Persisted Timestamp Without Rounding to Its Invariant's Grain

A handler must not convert a fractional `double` (minutes, hours, or any unit derived from subtraction, division, or `Sum(...)` over other doubles) directly into a real, persisted timestamp via `AddMinutes`/`AddSeconds`/`AddTicks` when that timestamp is later read back out and compared against the original value to decide whether some "covers at least X" invariant holds. `DateTime.AddMinutes(double)` rounds to the nearest 100ns tick — the value read back out is not guaranteed to be bit-identical to the value that produced it, and can land fractionally under it.

> **Rule:** Before converting a fractional quantity into a timestamp a later "did this fully satisfy X" check depends on, round it up to whatever grain that timestamp is meaningful at (a whole minute, for anything a person perceives at minute granularity). Never persist the raw fraction and lean on a downstream tolerance to absorb the loss — a tolerance can't tell "tick-rounding noise" apart from "genuinely short by a hair."
>
> **Why this is a hard rule, not a style preference:** (2026-08-22, `DESIGN_GAP_HISTORY.md` Gap 33) `GetSuggestedRedemptionPlacementsQueryHandler` sized a Cover-Deficit redemption tile to a fractional deficit (`31.896581196666666` minutes, from an `EndedEarly` session timestamped to the millisecond) and passed it straight to `AddMinutes`. The round-trip lost ~1.7e-9 minutes, so `GetNenContractDeficitsQueryHandler`'s "is this fully covered" check could never resolve to exactly zero — the Cover Deficit button stayed lit forever even though the tile visibly, functionally closed the gap. This is a **regression** of a guarantee `DESIGN_GAP_HISTORY.md` Gap 25 Addendum 1 already established ("round up so accepting the suggestion can only meet-or-exceed the deficit, never fall short") — it was silently dropped when Gap 26 rewrote the same mechanism the next day, and nobody noticed until live testing surfaced it. A documented rule is not a permanent guarantee against a later rewrite; only a regression test is.
>
> **Smell to watch for:** any `.AddMinutes(x)`/`.AddSeconds(x)`/`.AddTicks(x)` where `x` is a `double` computed from subtraction, division, or `Sum(...)` — not a whole-number literal or an `int`-backed field — especially when the resulting timestamp gets persisted and something else later re-derives a number from it to compare against an "at least this much" threshold.
>
> **Canonical example of doing it right:** `TimeSlot.Covering(start, minutes)` — the `Math.Ceiling` lives inside it, so a caller hands it a raw fractional deficit and gets a whole-minute slot back (Gap 88 phase 7 repointed this from the inline `Math.Ceiling(group.Sum(d => d.RemainingDeficitMinutes))` that `GetSuggestedRedemptionPlacementsQueryHandler` used to do). A whole number of minutes converts to ticks with zero rounding loss.

---

### Infrastructure (`NenFocus.Infrastructure`)

**Owns:** Everything that touches the outside world.

**Allowed:**
- EF Core `DbContext`, repositories, migrations
- Windows API calls (hosts file, registry, process management)
- Background services (`IHostedService`)
- Timer ownership (e.g., `ActiveSessionMonitor` owns `System.Timers.Timer`)
- Notification services

**Forbidden:**
- Business rules that belong in Domain or Application
- Avalonia UI types
- Direct coupling to ViewModels

---

### UI (`NenFocus`)

**Owns:** Visual presentation and user interaction routing only.

**Allowed:**
- ViewModels (`ObservableObject`, `RelayCommand`)
- Views (XAML + minimal code-behind for purely visual concerns). Code-behind may do **pixel geometry** (drag/resize positions — a converter can't, it needs pointer events), never a **business rule** (where a block may go, caps, snapping to allowed times, UTC↔local); those belong in the Domain (added 2026-09-29, Gap 98)
- Value converters (XAML `IValueConverter` implementations)
- Navigation callbacks passed as `Action` delegates

**Forbidden (The ViewModel Forbidden Zone):**

#### No Timers
ViewModels must never own or start a timer of any kind (`DispatcherTimer`, `System.Timers.Timer`, `Task.Delay` loops). Neither may Views' code-behind (added 2026-09-29, Gap 98), so polling can't simply move there — visual effects use Avalonia's animations/transitions declared in XAML, not code timers. Guarded by `ViewModelRuleTests.NoUiCodeOwnsATimer`.

> **Rule:** If a ViewModel needs to update a display value over time, it subscribes to a `Tick` event from a service that owns the timer.
>
> **Canonical example:** `IActiveSessionMonitor.Tick` → `MainPageViewModel.OnMonitorTick` → `Dispatcher.UIThread.Post(...)`.

#### No Math
ViewModels must not compute progress percentages, time deltas, durations, or any arithmetic on domain values.

> **Rule:** If a value requires calculation, it is computed by the Application or Domain layer and exposed as a ready-to-bind property.
>
> **Banned patterns:** `Math.Clamp(...)`, `1.0 - (x / y)`, `remaining.TotalSeconds / total`, manual `TimeSpan` arithmetic.
>
> **Canonical example:** `IActiveSessionMonitor.SessionProgress` is a `double` already clamped to `[0, 1]`. The ViewModel binds it directly.

#### No String Formatting
ViewModels must not call `.ToString(format)` on `TimeSpan`, `DateTime`, numeric values, or any other domain type for display purposes.

> **Rule:** Expose the raw type (`TimeSpan`, `double`, `int`) as an observable property. Use an `IValueConverter` in XAML to produce the display string.
>
> **Banned patterns:** `remaining.ToString(@"hh\:mm\:ss")`, `progress.ToString("P0")`, `string.Format(...)` for display values.
>
> **Canonical example:** `SessionTimeRemaining` is a `TimeSpan` property. `MainPageView.axaml` binds it with `Converter={StaticResource TimeSpanConverter}`.
>
> **What counts (added 2026-09-29, Gap 98):** formatting a *value* — `{x:HH:mm}`, `.ToString("0.#")`, `string.Format`, or calling a converter's static `Format…` helper — is banned. Composing plain UI copy from names (`$"Delete \"{card.Name}\"? This cannot be undone."`) is wording, not value formatting, and may stay in a ViewModel until it has a reason to move: a second front end needs it (Gap 77), translation arrives, or the same sentence appears twice — then it moves to one home (Gap 100). Guarded by `ViewModelRuleTests.NoViewModelFormatsValues`.

#### No Direct Repository Access
ViewModels must never hold a reference to any `IRepository` interface or call one directly.

> **Rule:** All data access must go through an `ICommandHandler<,>` or `IQueryHandler<,>`. If the ViewModel needs a read, there must be a Query. If it needs a write, there must be a Command.
>
> **Why:** Direct repository access in a ViewModel bypasses validation, event publication, and audit trail. It also makes the ViewModel impossible to unit-test without a real database.

#### No Directly-Injected `IQueryHandler<,>`/`ICommandHandler<,>` Either (added 2026-08-30, `DESIGN_GAP_HISTORY.md` Gap 40)
A ViewModel must not hold a constructor-injected `IQueryHandler<,>`/`ICommandHandler<,>` field, even though that's still the correct thing for an Application-layer handler to depend on.

> **Rule:** ViewModels take `IQueryDispatcher`/`ICommandDispatcher` (`Application/Interfaces/`) instead, and call `QueryAsync(query)`/`DispatchAsync(command)`. Never construct a ViewModel with an `IQueryHandler<,>`/`ICommandHandler<,>` parameter.
>
> **Why:** `App.axaml.cs` resolves the whole ViewModel tree from the DI root, with no scope. A directly-injected handler's repository dependencies are Scoped, and since nothing in this app's `Host.CreateDefaultBuilder` call ever enables `ValidateScopes` (only on in the `"Development"` environment), that resolution silently succeeds instead of throwing — meaning every directly-injected handler on a long-lived ViewModel ends up sharing one `DbContext` for the app's entire runtime. Two overlapping operations against it (a poll timer, an event-triggered refresh, a user action) throw an EF Core concurrency exception that vanishes silently, since ViewModel call sites are fire-and-forget via `Dispatcher.UIThread.Post`. `IQueryDispatcher`/`ICommandDispatcher` give every dispatch its own fresh DI scope internally, closing this for good.
>
> **Canonical example:** `PlannerPageViewModel` used to take 18 individually-injected handlers; it now takes exactly `IQueryDispatcher queryDispatcher, ICommandDispatcher commandDispatcher`.
>
> **Regression guard:** `CompositionRootTests.AddInfrastructure_ShellViewModel_ResolvesFromRootProviderLikeAppAxamlDoes` resolves `ShellViewModel` the same way `App.axaml.cs` does, under `ValidateScopes: true` — it fails immediately if this rule is ever violated anywhere in the ViewModel tree.
>
> **Widened to Infrastructure (2026-09-29, Gap 98 / Gap 89 Pass 2):** outside Application, a use case runs *only* through the dispatchers — no UI type or Infrastructure service holds a handler, or fetches one from the container by hand (`GetRequiredService<ICommandHandler<…>>`). The dispatcher is the one door that creates the scope; long-lived services (heartbeats, monitors) are exactly the ones that must not hold one. Exempt by role: the dispatchers themselves, Infrastructure's `DependencyInjection.cs` (it wraps every handler in validation and a transaction), handler wrappers, the composition root. Application code keeps depending on handlers directly. Guarded by `DispatcherRuleTests`.

#### No Domain Entity References
ViewModels must never hold or navigate a Domain entity (`Session`, `BlockPreset`, etc.) directly.

> **Rule:** Data flows from the Application layer through DTOs (`SessionDto`, `BlockPresetDto`, etc.). The ViewModel binds to DTOs and their properties.
>
> The same goes for Domain services and entity constants (added 2026-09-29, Gap 98): no UI code names `NenFocus.Domain.Entities` or `NenFocus.Domain.Services`. A limit like `FocusBlock.MaxDurationHours` reaches the UI through Application, not by reading the entity. Guarded by `ViewModelRuleTests.NoUiCodeUsesDomainEntitiesOrServices`.

---

## Interface-First Rule

Every new service must have its interface defined before any implementation is written — in `NenFocus.Application/Interfaces/`, or `NenFocus.Domain/Services/` for a Domain service (as `IPolicyEngine`; corrected 2026-09-29, the text had drifted from where every Domain service interface actually lives). The interface is the design artifact; the implementation is the detail.

**Checklist before creating a new interface:**
- [ ] Can the interface be understood without reading any implementation?
- [ ] Does it have 5 or fewer public members? (If more, consider splitting.) `InterfaceSizeRuleTests` holds new interfaces to 5; an existing larger one is listed with its count and may not grow — adding a member means editing that count, a visible decision (added 2026-09-29, Gap 98). Repositories count too: an extra query method is usually a business rule moving into SQL.
- [ ] Does it avoid leaking infrastructure types in its signature?
- [ ] Is it named for *what it does*, not *how it does it*? (`IActiveSessionMonitor`, not `ITimerWrapper`)

---

## Converter Catalogue

These converters are approved. Reuse before creating new ones. **This list must match the code** — `ConverterCatalogueTests` fails when a converter is missing here or listed but gone (added 2026-09-29, Gap 98). **Registration is per-view, not centralized** (corrected 2026-08-18 — this table previously claimed all converters are "registered in `MainPageView.axaml`"; in practice only `TimeSpanConverter`/`DateTimeConverter` still are, and every converter added since (Planner, Nen Contract wizard) is registered as a resource in whichever `.axaml` view actually uses it, e.g. `ContractWizardView.axaml`, `RitualShellView.axaml`, `PlannerPageView.axaml`).

| Converter | Key | Input → Output | Format Parameter |
|-----------|-----|----------------|-----------------|
| `TimeSpanToStringConverter` | `TimeSpanConverter` | `TimeSpan` → `string` | Optional format string, default `hh\:mm\:ss`. Automatically escapes unescaped `:` in the format string before falling back to the default — never throws in a binding. |
| `DurationToStringConverter` | _(inline)_ | `TimeSpan` → `string` | None. Produces human-readable "Xh Ym" (e.g. "2h 30m"), "Xm" (e.g. "45m"), or — under a minute — "Ns" (e.g. "45s", never a bare "0m") for display in the post-session summary. Use instead of `TimeSpanToStringConverter` when the compact human-readable form is needed rather than a clock format. |
| `TimeSpanToHumanReadableConverter` | `Instance` | `TimeSpan` → `string` | None. Spells out a duration in words ("5 minutes", "1 hour", "1h 30m") for prose contexts — e.g. the Ritual's late-to-block notice — where `DurationToStringConverter`'s compact "5m" form reads fine in a stat chip but not mid-sentence. |
| `DateTimeToStringConverter` | `DateTimeConverter` | `DateTime` → `string` | Optional format string, default `h:mm tt`. |
| `DateTimeToUpperStringConverter` | `DateTimeUpperConverter` | `DateTime` → uppercase `string` | Optional format string, default `ddd`. Result is `.ToUpperInvariant()`. Use for day-name headers. |
| `BoolToDotFillConverter` | _(inline)_ | `bool` → brush | — |
| `BoolToOpacityConverter` | _(inline)_ | `bool` → opacity | — |
| `BoolToChevronConverter` | `Instance` | `bool` → `string` | None. `true` → "▾", `false` → "▸" (expand/collapse chevron). |
| `DayFloorStatusToBrushConverter` | `Instance` | `DayFloorPreviewViewModel?` → brush | None. Planner day-header floor badge background. Rest → gray; today/past met → dark green; today unmet → amber (day isn't over, not a real miss yet); past unmet → dark red (a real miss). **A sealed contract's future day (any strictness rule, since 2026-08-19) always renders blue via `ShowSimplifiedFuture`** — provably safe by construction (Seal/Edit's own invariant plus post-seal floor-protection guards) — the raw future-met/future-unmet blue/light-red branch is only ever reached during live drafting, where a future day genuinely can be under-floor as actionable feedback. |
| `DayFloorStatusToForegroundConverter` | `Instance` | `DayFloorPreviewViewModel?` → brush | None. Pairs with `DayFloorStatusToBrushConverter` — white text on every saturated background (today/past, and a future-met blue, including the simplified badge above, since 2026-08-19), dark `#1A1A2E` text on the light rest tint and the pale future-unmet pink (drafting only). |
| `CountToBoolConverter` | _(inline)_ | `int` → `bool` | — |
| `CountToVisibilityConverter` | _(inline)_ | `int` → visibility | — |
| `StringEqualConverter` | _(inline)_ | `string` → `bool` | Comparison value |
| `IntEqualConverter` | _(inline)_ | `int` → `bool` | Comparison value |
| `StringToVisibilityConverter` | `IsNonEmpty` | `string` → `bool` | None. `true` when non-null/non-empty. |
| `WeekRangeToStringConverter` | _(inline)_ | `DateTime` (week start) → `string` | None. Formats a week as "MMM d – d, yyyy" (or "MMM d – MMM d, yyyy" across month boundaries) for the Planner's week header. |
| `BlockPresetTypeToColorConverter` | `Instance` | `BlockPresetType` → brush | None. Background color for a Planner tile — distinct Focus/Background fill. |
| `BlockPresetTypeToForegroundConverter` | `Instance` | `BlockPresetType` → brush | None. Companion foreground color to `BlockPresetTypeToColorConverter`. |
| `BlockPresetTypeToLabelConverter` | `Instance` | `BlockPresetType` → `string` | None. "Focus" / "Background". |
| `StrictnessRuleToLabelConverter` | `Instance` | `StrictnessRule` → `string` | None. "Lockdown" / "Daily Flex" / "Period Study". |
| `RedemptionPlacementPolicyToLabelConverter` | `Instance` | `RedemptionPlacementPolicy` → `string` | None. "Anywhere in the contract" / "Current day only". |
| `FocusBlockOutcomeToLabelConverter` | `Instance` | `FocusBlockOutcome` → `string` | None. "MISSED" / "STARTED LATE" / "ENDED EARLY" / "COMPLETED ON TIME". |
| `FocusBlockOutcomeToBrushConverter` | `Instance` | `FocusBlockOutcome` → brush | None. Missed red, late/early amber, on time green, else neutral grey — mirrors the Planner tile colours (`UI_UX_STANDARDS.md`). |
| `EnumEqualConverter` | `Instance` | any enum (nullable) → `bool` | Enum member name. `true` when `value.ToString()` equals it — one converter for every enum-driven visibility. |
| `DayFloorStatusHasBonusConverter` | `Instance` | `DayFloorPreviewViewModel?` → `bool` | None. Whether the Bonus pill shows; bound to the whole status so a null (no contract) is `false`, same null-safety as `DayFloorStatusToBrushConverter`. |
| `DailyFloorToLabelConverter` | `Instance` | `double?` (hours) → `string` | None. "1.5h / day", or "Derived from scheduled blocks" when null. Also `FormatFloor(hours)`. |
| `HoursToStringConverter` | `Instance` | `double` (hours) → `string` | None. "2.5h" — one decimal-hours figure (day badges, bonus pill). Also `FormatHours(hours)`. |
| `MinutesToStringConverter` | `Instance` | `double`/`int` (minutes) → `string` | None. "1h 30m" / "1h" / "30 min" — never a pointless "0m". Also `FormatMinutes(minutes)`. |
| `DurationLabelConverter` | `DurationLabelConverter` | `TimeSpan` → `string` | None. Parenthesized End-dropdown suffix: "(30 mins)", "(1 hr)", "(1.5 hrs)". A sibling of `DurationToStringConverter`, not a change to it (that one is shared with Post-Session). |
| `PercentageToLabelConverter` | `Instance` | `double?` (fraction) → `string` | None. "50%" / "133%" (uncapped), "Rest" when null. Also `FormatPercentage(fraction)`. |

**Static `Format…` entry points** (`FormatFloor`, `FormatHours`, `FormatMinutes`, `FormatPercentage`) let a ViewModel weave the same text into a composed string. Calling them from a ViewModel is still "No String Formatting" — the Gap 89 ViewModel audit decides what replaces it, together with the three static formatters that are named like converters but aren't (`DateRangeToLabelConverter`, `DateTimeRangeToLabelConverter`, `WorkHoursRangeToLabelConverter`).

---

## Naming Conventions

| Artifact | Convention | Example |
|----------|------------|---------|
| Command | `<Verb><Noun>Command` | `StartSessionCommand` |
| Command handler | `<Verb><Noun>CommandHandler` | `StartSessionCommandHandler` |
| Command result | `<Verb><Noun>Result` | `StartSessionResult` |
| Query | `Get<Noun>Query` (fetch) / `Compute<Noun>Query` (derive) / `Check<Noun>Query` (yes/no question; `Check` added 2026-09-29, Gap 98) | `GetActiveSessionQuery`, `CheckNenContractDateRangeOverlapQuery` |
| Interface | `I<Noun><Role>` | `ISessionStateService`, `IActiveSessionMonitor` |
| DTO | `<Noun>Dto` — a read model the Application hands out; every `…Dto` lives in `Application/DTOs/`. That folder is the home for any data shape shared across the Application boundary, so it also holds a few non-DTOs named for what they are (command input `NenContractBlockSpec`, event payload `RitualRequirementChange`). A UI-only item is not a DTO and doesn't take the suffix. **One builder per DTO** (added 2026-09-29): "what the UI sees of this entity" is a rule, so each DTO is built in one production file; a DTO needed by several use cases gets a named factory on itself (`SessionDto.From(…)`, like the existing `SessionDto.MapBreaks`) that they call — guarded by `DtoBuilderRuleTests` | `SessionDto`, `PostSessionDto` |
| Domain event | `<Noun><PastTense>Event` | `SessionStartedEvent`, `SessionEndedEvent` |
| Value object | Noun only | `SessionGoal`, `BreakTimer` |
| Domain service | Agent noun — names the role it plays (`-Engine`, `-Classifier`, `-Calculator`, `-Checker`, …); never `-Service`, which is reserved for Infrastructure and the Application↔UI bridges. Interface `I<Name>` in `Domain/Services/` | `PolicyEngine`, `FocusBlockOutcomeClassifier`, `BlockPlacementChecker` |
| ViewModel | `<Page>ViewModel` | `MainPageViewModel`, `HatsuPageViewModel` |
| Converter | `<Input>To<Output>Converter` for a mapping; `<What>EqualConverter` / `<Subject>Has<X>Converter` for a yes/no test (added 2026-09-29, Gap 98 — the catalogue already listed `IntEqualConverter`). Every converter is listed in the Converter Catalogue above — a test fails otherwise | `TimeSpanToStringConverter`, `StringEqualConverter` |

---

## Vertical Slice Structure

Every use case lives in its own folder with exactly four files:

```
UseCases/Sessions/StartSession/
    StartSessionCommand.cs          ← implements ICommand<StartSessionResult>
    StartSessionCommandHandler.cs   ← implements ICommandHandler<StartSessionCommand, StartSessionResult>
    StartSessionResult.cs
    StartSessionValidator.cs        ← omit if no validation required; MUST live here, not in Validators/
```

No shared base classes for commands or results. No `BaseCommand`. Copy the pattern, don't abstract it.

**Query slices (added 2026-09-29, Gap 98):** every query lives in `Queries/<Name>/` with exactly one `…Query.cs` and one `…QueryHandler.cs`. A result type only this query produces may sit next to them; the moment another slice uses it, it moves to `DTOs/` — one shared definition, never a second copy. The UI reading the answer doesn't count as sharing; it's the reader the type was shaped for. Queries only read: a query handler whose answer commands save (e.g. `ComputeRedemptionAttribution`, used by Create/Update/Split block) is a business rule living in the wrong place — it belongs in the Domain.

**Validators check form and ask, never own a rule (added 2026-09-29, Gap 98):** a validator checks the input's shape (required, in range, well-formed) and asks the Domain or a repository whether the change is allowed. It never holds its own copy of a business rule — two validators with the same formula are two sources of truth (see Gap 92 V9, Gap 93).

**A refusal is a typed reason, not a sentence (added 2026-09-29, Gap 98):** a validator or query that refuses something returns *what* went wrong as data (e.g. below the daily floor, which day, which floor), and the wording is built in one place — the same split as `INotifier`'s typed `NenNotification` facts. Why: the same sentence written in several validators is duplicated wording (the floor message is written three times today), and a future front end (the SwiftUI app, Gap 77) or a translation needs the reason, not an English sentence. Applied as validators are rewritten — Gap 88 (placement) created the mechanism (`PlacementOutcome`, worded once in `BlockPlacementVerdictDto.From`) and the test: `ValidatorWordingRuleTests` lists every sentence-writing validator as "File has N", shrink-only, so no validator gains a sentence; Gap 93 (permissions) and Gap 100 (the rest, first step R1: validator text into `Refusals.resx`) empty it. **Until then, a new refusal's sentence goes in its topic's one wording home** — a named factory or method next to the typed reason (as `BlockPlacementVerdictDto.From`, `Notifier`), never inline in a validator and never in an unnamed helper; each such home becomes one resource file in Gap 100.

**Validator location rule:** Validators are co-located inside the use-case folder — never in a top-level `Validators/` or `Application/Validators/` directory. The `AddValidatorsFromAssembly` scan in `DependencyInjection.cs` finds them automatically anywhere in the assembly.

**A member a command can't do without is `required`, not `= null!` (added 2026-10-01, Gap 88):** `null!` tells the compiler "never null" while the value can be, which pushes the check back into a validator's `NotNull` rule. `required` makes the compiler refuse a command built without it. Guarded by `CqrsRuleTests.CommandsDoNotPromiseNonNullWithNullBang`.

**Command marker interface:** Every command record must implement `ICommand<TResult>`. This is required for the reflection-based DI scan to match `ICommandHandler<TCommand,TResult>` correctly. The old `ICommandHandler<TCommand>` shorthand (returning `bool`) has been removed — all commands declare an explicit result type.
