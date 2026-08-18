# NenFocus — Module Standards

This file defines the structural rules every module in NenFocus must follow. It operationalises the "Deep Modules" principle from our High-Level Development Strategy.

---

## The Deep Module Test

Before merging any new class or service, ask: *Can a developer understand how to use this module in under 30 seconds by reading only its interface signature?*

- **Yes** → the module is deep. Proceed.
- **No** → the interface is leaking implementation details. Redesign before writing code.

---

## Layer Responsibilities

### Domain (`NenFocus.Domain`)

**Owns:** Business rules, invariants, and state transitions.

**Allowed:**
- Entities, value objects, domain events, enums, constants
- Validation in value object constructors
- State-machine methods on entities (`Start()`, `Complete()`, `StartBreak()`, etc.)

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
| `DurationToStringConverter` | _(inline)_ | `TimeSpan` → `string` | None. Produces human-readable "Xh Ym" (e.g. "2h 30m") or "Xm" (e.g. "45m") for display in the post-session summary. Use instead of `TimeSpanToStringConverter` when the compact human-readable form is needed rather than a clock format. |
| `TimeSpanToHumanReadableConverter` | `Instance` | `TimeSpan` → `string` | None. Spells out a duration in words ("5 minutes", "1 hour", "1h 30m") for prose contexts — e.g. the Ritual's late-to-block notice — where `DurationToStringConverter`'s compact "5m" form reads fine in a stat chip but not mid-sentence. |
| `DateTimeToStringConverter` | `DateTimeConverter` | `DateTime` → `string` | Optional format string, default `h:mm tt`. |
| `DateTimeToUpperStringConverter` | `DateTimeUpperConverter` | `DateTime` → uppercase `string` | Optional format string, default `ddd`. Result is `.ToUpperInvariant()`. Use for day-name headers. |
| `BoolToDotFillConverter` | _(inline)_ | `bool` → brush | — |
| `BoolToOpacityConverter` | _(inline)_ | `bool` → opacity | — |
| `BoolToChevronConverter` | `Instance` | `bool` → `string` | None. `true` → "▾", `false` → "▸" (expand/collapse chevron). |
| `DayFloorStatusToBrushConverter` | `Instance` | `DayFloorPreviewViewModel?` → brush | None. Planner day-header floor badge background, 7 states: rest → gray; today/past met → dark green; today unmet → amber (day isn't over, not a real miss yet); past unmet → dark red (a real miss); future met/unmet (planned, not yet resolved) → blue/light-red tints. |
| `DayFloorStatusToForegroundConverter` | `Instance` | `DayFloorPreviewViewModel?` → brush | None. Pairs with `DayFloorStatusToBrushConverter` — white text on the saturated today/past colors (green/amber/red), dark `#1A1A2E` text on the light rest/future tints. |
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
| Value object | Noun only | `SessionGoal`, `BreakSettings` |
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
