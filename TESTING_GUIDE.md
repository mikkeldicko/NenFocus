# NenFocus — Testing Guide

This guide explains how to use the `NenFocus.Tests` project and how TDD fits into the "Headlight Management" strategy from our High-Level Development Strategy.

---

## Quick Start

```bash
# Run all tests
dotnet test NenFocus.Tests/NenFocus.Tests.csproj

# Run with detailed output
dotnet test NenFocus.Tests/NenFocus.Tests.csproj --logger "console;verbosity=detailed"

# Run tests for a specific class
dotnet test NenFocus.Tests/NenFocus.Tests.csproj --filter "ClassName=SessionTests"

# Run with coverage (requires coverlet, already installed)
dotnet test NenFocus.Tests/NenFocus.Tests.csproj --collect:"XPlat Code Coverage"
```

---

## Project Structure

**Corrected 2026-08-18** — the tree below previously showed a 2-file skeleton and claimed Infrastructure tests didn't exist yet; both are stale. Actual current shape (~90 test files):

```
NenFocus.Tests/
├── Domain/
│   ├── SessionTests.cs, BlockPresetTests.cs, NenContractTests.cs, PolicyEngineTests.cs, ...  ← entity/service state-machine tests (zero mocks)
│   ├── ValueObjects/          ← WorkHoursTests.cs, DailyFloorTests.cs, BreakScheduleTests.cs, etc.
│   └── Services/              ← ContractBlockBatchPreparerTests.cs, PlannedFocusHoursCalculatorTests.cs
├── Application/
│   ├── UseCases/{BlockPresets,ContractPresets,NenContracts,Planner,Sessions}/  ← one *CommandHandlerTests.cs + *ValidatorTests.cs pair per vertical slice
│   ├── Queries/                ← one *QueryHandlerTests.cs per query
│   └── EventHandlers/          ← SessionStartedEventHandlerTests.cs, NenContractActivatedEventHandlerTests.cs, etc.
├── Infrastructure/
│   ├── Persistence/
│   │   ├── SqliteRepositoryTestBase.cs   ← shared base: real SQLite via an in-memory connection, not a fake/InMemory provider
│   │   ├── Repositories/                 ← BlockPresetRepositoryTests.cs, SessionRepositoryTests.cs, NenContractRepositoryTests.cs, PlannedBlockRepositoryTests.cs
│   │   └── EfCoreUnitOfWorkTests.cs
│   ├── Services/               ← BlockedAttemptTrackerTests.cs, SessionStateServiceTests.cs, TransactionalCommandHandlerTests.cs, InProcessDomainEventPublisherTests.cs
│   └── CompositionRootTests.cs ← confirms every handler/service resolves from DI with no missing registrations
├── ArchitectureTests/          ← MODULE_STANDARDS rules that fail on drift (Gap 98) — see "Architecture Tests" below
├── Headless/                   ← real views rendered with no screen (Avalonia.Headless) — see "Headless UI Tests" below
└── NenFocus.Tests.csproj
```

**Rules:**
- `Domain/` tests never use mocks. Domain is pure logic.
- `Application/` tests mock all interfaces (`ISessionRepository`, `ISessionStateService`, etc.) with NSubstitute.
- `Infrastructure/` tests **are built** — they run against a real SQLite database (via `SqliteRepositoryTestBase`'s in-memory connection, not a fake in-memory EF provider), not mocks. This is the correct place for repository/EF Core behavior and DI composition tests.
- No stand-alone ViewModel logic tests — ViewModels stay thin and are tested via the Application layer they delegate to. A view and its ViewModel together can be checked end-to-end in `Headless/` (since 2026-09-29).

---

## Test Libraries

| Library | Version | Purpose |
|---------|---------|---------|
| xUnit | 2.9.2 | Test runner and assertions (`[Fact]`, `[Theory]`) |
| NSubstitute | 5.3.0 | Mock/stub generation for interfaces |
| FluentAssertions | 6.12.2 | Readable assertion syntax (`.Should().Be(...)`) |
| coverlet | 6.0.2 | Code coverage collection |

---

## The TDD Workflow (Headlight Management)

The "never outrun the headlights" principle means: **do not write implementation code that you cannot immediately verify with a test**.

### Step-by-step for a new feature:

**1. Write the failing test first**

Before touching any handler or entity, write a test that describes the expected behaviour:

```csharp
[Fact]
public async Task HandleAsync_ActiveSession_CompletesIt()
{
    // Arrange
    var session = CreateActiveSession();
    _sessions.GetByIdAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);

    // Act
    var result = await CreateHandler().HandleAsync(new EndSessionCommand { SessionId = session.Id });

    // Assert
    result.Success.Should().BeTrue();
    session.Status.Should().Be(SessionStatus.Completed);  // ← This FAILS because the handler isn't written yet
}
```

**2. Run the test — confirm it fails for the right reason**

```bash
dotnet test --filter "ClassName=EndSessionCommandHandlerTests"
```

A red test is progress. A test that fails to compile is also progress.

**3. Write the minimum code to make it pass**

Only write what the test requires. No extra logic, no "just in case" branches.

**4. Run the test again — confirm it passes**

```bash
dotnet test --filter "ClassName=EndSessionCommandHandlerTests"
```

**5. Refactor if needed, then confirm still green**

```bash
dotnet test NenFocus.Tests/NenFocus.Tests.csproj
```

---

## NSubstitute Patterns

### Mocking a repository

```csharp
var sessions = Substitute.For<ISessionRepository>();

// Set up a return value
sessions.GetByIdAsync(sessionId, Arg.Any<CancellationToken>()).Returns(mySession);

// Set up a null return (session not found)
sessions.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Session?)null);

// Assert a method was called
await sessions.Received(1).SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());

// Assert a method was NOT called
await sessions.DidNotReceive().SaveAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
```

### Mocking a state service

```csharp
var sessionState = Substitute.For<ISessionStateService>();

// Verify SetActive was called with any SessionDto
sessionState.Received(1).SetActive(Arg.Any<SessionDto>());

// Verify Clear was called
sessionState.Received(1).Clear();
```

### Faking the clock

Always `TestClock.Create()`, never `Substitute.For<ISystemClock>()` (`ClockRuleTests` fails otherwise). Its `Now` and `TimeZone` throw until set, so a forgotten setup fails loudly instead of running at year 0001:

```csharp
private readonly ISystemClock _clock = TestClock.Create();
// in the constructor or test:
_clock.SetNow(new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc)); // several values = a sequence
_clock.SetTimeZoneOffset(TimeSpan.Zero);
```

Set a clock member directly only through `Configure()` (`clock.Configure().Now.Returns(…)`): a plain `.Returns` on a throwing member runs the throw. Why: an unconfigured clock hid an empty Work-Hours check for months, and a Planner view test ran in the wrong week (Gap 88 close-out).

### Theory tests (parameterised)

```csharp
[Theory]
[InlineData(25)]
[InlineData(45)]
[InlineData(60)]
public async Task HandleAsync_ValidDurations_AllSucceed(int durationMinutes)
{
    _plannedBlocks.GetForRangeAsync(...)
        .Returns([]);

    var result = await CreateHandler().HandleAsync(
        new StartSessionCommand { DurationMinutes = durationMinutes, Goal = "Test" },
        CancellationToken.None);

    result.Success.Should().BeTrue();
}
```

---

## What to Test and What Not to Test

### Test these:

| Target | Why |
|--------|-----|
| Domain entity state machines (`Session`, `BlockPreset`) | Zero dependencies; pure logic; high value |
| Command handler happy paths | Verify the correct sequence of repository calls and state updates |
| Command handler failure paths | Verify validation gates work (session not found, wrong status, etc.) |
| Value object construction | Verify invalid inputs throw `ArgumentException` |
| Policy computation logic | The `ComputeActivePolicySliceQuery` merge logic is complex enough to need tests |
| EF Core repositories | **Built** — `Infrastructure/Persistence/Repositories/` tests each repository against a real SQLite database (via `SqliteRepositoryTestBase`'s in-memory connection), not mocks. This row was previously listed under "Do not test these"; corrected 2026-08-18 now that the suite exists. |
| A new stored value object | It gets a round-trip test: save through one database context, load through a second. Handler tests on mocks can't see storage — one membership handed to several blocks was saved on only one of them while every Seal and Split handler test passed (Gap 97). Example: `Infrastructure/Persistence/ContractMembershipPersistenceTests.cs`. |
| DI composition | `Infrastructure/CompositionRootTests.cs` resolves every registered handler/service to catch missing DI registrations before runtime. |

### Do not test these:

| Target | Why |
|--------|-----|
| ViewModels | They delegate everything; test the service they call instead |
| `WindowsEnforcementService` | Windows API calls; use integration tests in a real process |
| XAML bindings | Visual; use manual testing or a UI test framework |

---

## Architecture Tests (Gap 98)

`NenFocus.Tests/ArchitectureTests/` checks the structure `MODULE_STANDARDS.md` describes — layers, the ViewModel Forbidden Zone, vertical slices, naming, interface size, one clock, raw time comparisons, side effects through their one door, queries that only read, ViewModels that neither format values nor do math, use cases run only through the dispatchers outside Application, one builder per DTO, command handlers leaving the session noticeboard to event handlers. Plain xUnit, no library:

| Rule | Mechanism |
|------|-----------|
| Type rules (references, what a class holds, names) | reflection over the compiled assemblies |
| Call bans (`DateTime.UtcNow`, `ToLocalTime()`, raw `.Start <`) and anything reflection can't see (constants are copied into the caller at compile time) | `SourceCode.Files(project)` — the `.cs` files with comments stripped, `obj/`, `bin/` and `Migrations/` skipped |

**Exceptions lists may only shrink.** A rule broken today lists its known violations in an array next to it, each with a comment naming the gap that removes it. `KnownExceptions.Check(violations, exceptions)` fails both ways: a violation not on the list (new drift), and an entry that no longer violates (stale — delete it). Where one file can hold several violations, entries are `"<file or type> has N"`, so growth inside a listed file fails too.

**Working with them:**
- **A rule fails on your change** — fix the code. Add an exception only for a deliberate, reasoned choice, and say why in the comment.
- **A gap removes a violation** — delete its entry; the test stays red until you do.
- **A gap makes a new rule true** (the Architecture box rule) — add its test here, and probe it: add a deliberate violation (a temporary `ZzProbe` file), watch it go red, remove it. A test never seen failing is not trusted.
- **Generated code** (EF migrations, Avalonia's compiled XAML) is out of scope, matched narrowly so a change on their side fails red rather than passing silently.
- **What these can't check** — whether a module is deep, whether a rule is in the right home — stays with the interface-first sign-off (`MODULE_STANDARDS.md` → Guard Hierarchy).
- **Probe with the shapes people actually write.** A probe using a fully qualified name (`Substitute.For<NenFocus.Application.Interfaces.ISystemClock>()`) got past the first version of `TestsMakeFakeClocksOnlyThroughTestClock`; the pattern was widened. Try at least the short and the qualified form.
- **A text scan sees only the shapes it lists.** `MembershipRuleTests` matches comparisons of a block's contract id; `PlannerPageViewModel` filters draft tiles with `b.ContractId is { } id && id == editingId`, which it doesn't match (Gap 97; left for Gap 89 area 2 / Gap 93). Where the compiler can own the rule instead, prefer that (`MODULE_STANDARDS.md` → Guard Hierarchy).
- **Restore a probed file from a backup copy, never `git checkout`.** Checkout also throws away the file's uncommitted work (it nearly did during Gap 88's close-out).

**Lessons from Gap 88 (for any test, not only architecture tests):**
- **A test written after its fix proves nothing until probed:** break the code on purpose, watch the test fail, restore.
- **An assertion can compile on the wrong type.** After a field became `Moment?`, `x.Should().Be(someDateTime)` still compiled (FluentAssertions' `Be(object)`) and failed only at runtime ("expected 12:00:00, found 12:00:00Z"). After a type change, compare with a value of the new type.
- **Scripted edits:** match the file's line endings (`\r?\n`; a `\n`-only deletion left an orphan `\r` that shifted every later line number); anchor text replacements to a word boundary (replacing `At.X(` also hit `TestAt.X(`); and delete exactly the member a plan names, not its type (read it first).

---

## Headless UI Tests (added 2026-09-29)

`NenFocus.Tests/Headless/` renders real views on Avalonia's headless platform: no window on screen, but the real `App` styles, XAML, bindings and ViewModels. `TestAppBuilder` starts it once for the test run; each test is an `[AvaloniaFact]` (runs on Avalonia's UI thread). The package `Avalonia.Headless.XUnit` must stay at the app's Avalonia version — bump both together.

**What they're for:** behaviour a person would check by clicking. What the screen shows, and whether typing, clicking or a key reaches the ViewModel. Useful for any gap that changes a view, and as the before/after check around an Avalonia update: run them green on the old version first. They caught Gap 102's regression (Down arrow in the Planner's time fields) and Gap 104's bug.

**What they're not for:**
- **Binding names.** Views use compiled bindings (`x:DataType`), so a wrong name already fails the build (checked 2026-09-29).
- **Business rules.** Those are tested in Domain/Application, where they live.
- **Anything outside the window:** Windows toasts, the hosts file, real shutdown. Those stay manual.

**Pattern:** create the view, set `DataContext` to a ViewModel built with test doubles, then use `HeadlessTestKit`:
- `FakeQueryDispatcher` answers the queries you list (`.Answer<TQuery, TResult>(…)`); any other query gets an empty list or its default, so a screen's side loads see "nothing". Commands: NSubstitute on `ICommandDispatcher`.
- `Show(view)` puts it in a window; `Settle()` runs queued UI work after anything asynchronous.
- `VisibleTexts`, `ButtonWithText` (innermost match), `Click` (a real mouse click), `Type` (real keyboard text), `PressKey`.
- Gotcha: `IsEffectivelyVisible` can report true under a hidden panel — use `IsShown`, which checks every logical parent.

Suites: `RitualStepViewTests`, `TenPageViewTests`, `PostSessionViewTests`, `PresetPageViewTests`, `PlannerEndDropdownViewTests`.

---

## Adding a New Test File

1. Create the file in the correct folder (`Domain/`, `Application/`, or `Infrastructure/`).
2. Follow the naming convention: `<SubjectName>Tests.cs`.
3. Use `private readonly` substitutes declared at the class level (not inside each test method).
4. Add a `CreateHandler()` or `CreateEntity()` helper to avoid repetition in `[Fact]` methods.
5. Run `dotnet test` before committing. A failing test blocks the feature from being considered done.

---

## Running from Visual Studio / Rider

Both IDEs discover xUnit tests automatically via the test runner UI. Tests are also runnable from the terminal inside the solution root at any time:

```bash
dotnet test
```

This runs all test projects in the solution.
