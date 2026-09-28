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
> **Canonical example of doing it right:** `GetSuggestedRedemptionPlacementsQueryHandler.HandleAsync` — `Math.Ceiling(group.Sum(d => d.RemainingDeficitMinutes))` before the value ever reaches `AddMinutes`. A whole number of minutes converts to ticks with zero rounding loss.

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
- Views (XAML + minimal code-behind for purely visual concerns)
- Value converters (XAML `IValueConverter` implementations)
- Navigation callbacks passed as `Action` delegates

**Forbidden (The ViewModel Forbidden Zone):**

#### No Timers
ViewModels must never own or start a timer of any kind (`DispatcherTimer`, `System.Timers.Timer`, `Task.Delay` loops).

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

#### No Domain Entity References
ViewModels must never hold or navigate a Domain entity (`Session`, `BlockPreset`, etc.) directly.

> **Rule:** Data flows from the Application layer through DTOs (`SessionDto`, `BlockPresetDto`, etc.). The ViewModel binds to DTOs and their properties.

---

## Interface-First Rule

Every new service must have its interface defined in `NenFocus.Application/Interfaces/` before any implementation is written. The interface is the design artifact; the implementation is the detail.

**Checklist before creating a new interface:**
- [ ] Can the interface be understood without reading any implementation?
- [ ] Does it have 5 or fewer public members? (If more, consider splitting)
- [ ] Does it avoid leaking infrastructure types in its signature?
- [ ] Is it named for *what it does*, not *how it does it*? (`IActiveSessionMonitor`, not `ITimerWrapper`)

---

## Converter Catalogue

These converters are approved. Reuse before creating new ones. **Registration is per-view, not centralized** (corrected 2026-08-18 — this table previously claimed all converters are "registered in `MainPageView.axaml`"; in practice only `TimeSpanConverter`/`DateTimeConverter` still are, and every converter added since (Planner, Nen Contract wizard) is registered as a resource in whichever `.axaml` view actually uses it, e.g. `ContractWizardView.axaml`, `RitualShellView.axaml`, `PlannerPageView.axaml`).

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

---

## Naming Conventions

| Artifact | Convention | Example |
|----------|------------|---------|
| Command | `<Verb><Noun>Command` | `StartSessionCommand` |
| Command handler | `<Verb><Noun>CommandHandler` | `StartSessionCommandHandler` |
| Command result | `<Verb><Noun>Result` | `StartSessionResult` |
| Query | `Get<Noun>Query` / `Compute<Noun>Query` | `GetActiveSessionQuery` |
| Interface | `I<Noun><Role>` | `ISessionStateService`, `IActiveSessionMonitor` |
| DTO | `<Noun>Dto` | `SessionDto`, `PostSessionDto` |
| Domain event | `<Noun><PastTense>Event` | `SessionStartedEvent`, `SessionEndedEvent` |
| Value object | Noun only | `SessionGoal`, `BreakTimer` |
| ViewModel | `<Page>ViewModel` | `MainPageViewModel`, `HatsuPageViewModel` |
| Converter | `<Input>To<Output>Converter` | `TimeSpanToStringConverter` |

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

**Validator location rule:** Validators are co-located inside the use-case folder — never in a top-level `Validators/` or `Application/Validators/` directory. The `AddValidatorsFromAssembly` scan in `DependencyInjection.cs` finds them automatically anywhere in the assembly.

**Command marker interface:** Every command record must implement `ICommand<TResult>`. This is required for the reflection-based DI scan to match `ICommandHandler<TCommand,TResult>` correctly. The old `ICommandHandler<TCommand>` shorthand (returning `bool`) has been removed — all commands declare an explicit result type.
