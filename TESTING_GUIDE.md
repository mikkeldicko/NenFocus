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
│   └── Services/              ← ContractBlockBatchPreparerTests.cs, BlockPlacementCheckerTests.cs
├── Application/
│   ├── UseCaseTestBase.cs      ← runs a command the way the app does, on real SQLite (Gap 110)
│   ├── UseCases/{BlockPresets,ContractPresets,NenContracts,Planner,Sessions}/  ← one test file per use case, validator cases included
│   ├── Queries/                ← one *QueryHandlerTests.cs per query
│   ├── EventHandlers/          ← SessionStartedEventHandlerTests.cs, NenContractActivatedEventHandlerTests.cs, etc.
│   └── Services/               ← EnforcementSynchronizerTests.cs, NotifierTests.cs, ActiveSessionStateTests.cs, RitualPromptTests.cs, TransactionCommandHandlerDecoratorTests.cs, etc.
├── Infrastructure/
│   ├── Persistence/
│   │   ├── SqliteRepositoryTestBase.cs   ← shared base: real SQLite via an in-memory connection, not a fake/InMemory provider
│   │   ├── Repositories/                 ← BlockPresetRepositoryTests.cs, SessionRepositoryTests.cs, NenContractRepositoryTests.cs, PlannedBlockRepositoryTests.cs
│   │   └── EfCoreUnitOfWorkTests.cs
│   ├── Services/               ← InProcessDomainEventPublisherTests.cs, etc.
│   └── CompositionRootTests.cs ← confirms every handler/service resolves from DI with no missing registrations
├── ArchitectureTests/          ← MODULE_STANDARDS rules that fail on drift (Gap 98) — see "Architecture Tests" below
├── Headless/                   ← real views rendered with no screen (Avalonia.Headless) — see "Headless UI Tests" below
└── NenFocus.Tests.csproj
```

**Rules:**
- `Domain/` tests never use mocks. Domain is pure logic.
- **Tests fake only what lies outside the app (Gap 110).** The database is the app's own, so it is never faked: a command test runs the whole use case — validation, handler, transaction, the event handlers after the commit — on `UseCaseTestBase` (real composition root, fresh in-memory SQLite per test). Faked: the clock (`TestClock`), OS blocking (`IEnforcementService`) and toasts (`INotificationService`). Why: mocked handler tests passed while storage was wrong (Gap 97's membership, Gap 110's never-saved tags) and while a command crashed (Break's recap read unsaved changes); they also tested paths validation never lets through (a 999-minute session, moves the validator refuses). Guarded by `TestDoubleRuleTests.ApplicationTestsDoNotFakeRepositories`; its shrink-only list is the query, event-handler and two session-validator tests still on mocks (→ Gap 115, Gap 114).
- **A use-case test starts from a state the app can produce, and checks only data it stored itself (Gap 110).** Build the starting state through the Domain (`NenContract.Seal`, `Complete`, `FocusBlock.Create`, …) and store it with `SeedAsync`: a block's membership needs its real contract — a made-up contract id passes only because aggregates aren't linked in the database, and so tests a state the app never has. Seeded rows (`SeededFocusPresetId`, …) may stand in for "some valid preset", never for what a test checks: a test that asserts a name or a rhythm stores its own (`FocusPresetAsync`, `BreakTimerPresetAsync`). Assert on what was stored (`FindAsync`, `ReadAsync` — each its own context), not on which repository method was called.
- `Infrastructure/` tests run against a real SQLite database (via `SqliteRepositoryTestBase`'s in-memory connection, not a fake in-memory EF provider). This is the place for repository/EF Core behavior and DI composition tests. **Every persistence test reloads through a fresh context, never the one that saved** — reloading through the saving context hands back the tracked object, which hid the never-saved tags from the first commit until Gap 110. Round-trip tests may use made-up ids (a membership's contract): they test the stored value, nothing acts on it.
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
public class EndSessionTests : UseCaseTestBase
{
    [Fact]
    public async Task ARunningSession_IsStoredCompleted()
    {
        // Arrange — a state the app can produce, stored
        var session = ActiveSession();
        await SeedAsync(session);

        // Act — the whole use case, as the app runs it
        var result = await RunAsync(new EndSessionCommand { SessionId = session.Id });

        // Assert — what was stored
        result.Success.Should().BeTrue(result.ErrorMessage);
        (await FindAsync<Session>(session.Id))!.Status.Should().Be(SessionStatus.Completed);  // ← FAILS until the handler is written
    }
}
```

**2. Run the test — confirm it fails for the right reason**

```bash
dotnet test --filter "ClassName=EndSessionTests"
```

A red test is progress. A test that fails to compile is also progress.

**3. Write the minimum code to make it pass**

Only write what the test requires. No extra logic, no "just in case" branches.

**4. Run the test again — confirm it passes**

```bash
dotnet test --filter "ClassName=EndSessionTests"
```

**5. Refactor if needed, then confirm still green**

```bash
dotnet test NenFocus.Tests/NenFocus.Tests.csproj
```

---

## Use-Case Test Patterns (`UseCaseTestBase`, Gap 110)

```csharp
// Arrange: store the starting state (each call its own context)
await SeedAsync(contract, block);
var presetId = await FocusPresetAsync("Deep Work");   // a preset whose name the test checks

// Act: one command through the real dispatcher — validation, handler, transaction, events
var result = await RunAsync(new DeletePlannedBlockCommand { BlockId = block.Id });

// Assert: the answer, and what is stored now
result.Success.Should().BeFalse();
result.ErrorMessage.Should().Be("This block is running and cannot be deleted until it ends."); // the first validation error
(await FindAsync<PlannedBlock>(block.Id)).Should().NotBeNull();                                 // a refusal stores nothing

// An app-wide service as the app has it, e.g. to see what the Shell is told
Resolve<IRecapRelay>();
```

The clock is `Clock` (UTC unless set); `Enforcement` and `Notifications` are NSubstitute fakes to check calls on.

## NSubstitute Patterns (for what lies outside the app)

### Mocking a state service

```csharp
var sessionState = Substitute.For<IActiveSessionState>();

// Verify the event handler refreshed the app's view of the running session
await sessionState.Received(1).RefreshAsync(Arg.Any<CancellationToken>());
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
public async Task AnyDurationInRange_Starts(int durationMinutes)
{
    var result = await RunAsync(new StartSessionCommand
    {
        Goal = "Test", PresetId = SeededFocusPresetId, DurationMinutes = durationMinutes
    });

    result.Success.Should().BeTrue(result.ErrorMessage);
}
```

---

## What to Test and What Not to Test

### Test these:

| Target | Why |
|--------|-----|
| Domain entity state machines (`Session`, `BlockPreset`) | Zero dependencies; pure logic; high value |
| Use cases (a command, end to end) | What is stored afterwards — never which repository method was called; the commit saves (Gap 110) |
| Use-case refusals | The refusal reaches the caller and nothing is stored (session not found, wrong status, a rule's "no") |
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

`NenFocus.Tests/ArchitectureTests/` checks the structure `MODULE_STANDARDS.md` describes — layers, the ViewModel Forbidden Zone, vertical slices, naming, interface size, one clock, raw time comparisons, side effects through their one door, queries that only read, command handlers that never hold a query handler (Gap 106), ViewModels that neither format values nor do math, use cases run only through the dispatchers outside Application, one builder per DTO, command handlers leaving the session noticeboard to event handlers, repositories as collections (only `Add`, `Remove` and reads; never EF's whole-object `Update`/`Attach` — Gap 110), Application tests that don't fake repositories. Plain xUnit, no library:

**They run automatically (added 2026-10-08):** a Claude Code Stop hook (`.claude/settings.json` → `.claude/hooks/arch-tests.sh`) runs them at the end of any Claude turn in which C# code changed since the last green run, and blocks the hand-back on a failure. Disable or inspect it with `/hooks`.

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
- **A scripted edit fails in two ways:** a parse error runs none of it, a throw midway has already written the files before it. Check what was written before re-running (Gap 93).
- **Manual app checks are for what tests can't prove** (architect, 2026-10-02): on-screen wiring (is the button hidden, the field disabled, the message shown), a migration on the real database, timing against the real clock. A rule case the Domain or validator tests already cover doesn't go on the list — a handful per phase, with a line saying what the tests cover.
- **Tests can reach Domain internals** (`InternalsVisibleTo("NenFocus.Tests")`, Gap 106). Use it only to build a state production makes through a longer path — a block already holding a receipt (`FocusBlock.CreateWithReceipt`), a Redemption membership (`TestContracts.Redemption`) — never to test an internal method instead of the public answer it feeds.
- **Scripted edits on Windows (Gap 106):** a `perl`/`sed` pattern containing a non-ASCII character (—, →) can silently not match; an `awk -v` path with backslashes is mangled, and the script went on to delete lines without inserting. Use the Edit tool for such lines, and check `git diff --stat` after any scripted doc edit.
- **Scripted edits:** match the file's line endings (`\r?\n`; a `\n`-only deletion left an orphan `\r` that shifted every later line number; a whole-file CRLF pass added `\r` to a last line with no newline, and git then saw the file as binary — `git ls-files --eol` shows `w/-text` — and diffed every line, Gap 92); anchor text replacements to a word boundary (replacing `At.X(` also hit `TestAt.X(`); and delete exactly the member a plan names, not its type (read it first).

---

## Mutation Testing (added 2026-10-08, Gap 113)

Coverage says a test *ran* a line; mutation testing says a test would *notice* if the line were wrong. Stryker.NET (`dotnet tool install -g dotnet-stryker`) makes one small change to the Domain at a time — a "mutant": `<` → `<=`, `&&` → `||`, `+=` → `-=`, a statement removed — and runs the tests that cover that line. A test fails → the mutant is **killed** (good). Every test passes → it **survived**: the rule could be wrong and nothing would say so.

**When (`ARCHITECT_GOVERNANCE.md` → Step 5):** at a gap close that changed Domain code, on the changed files only; at the end of each box, over the whole Domain.

```bash
cd NenFocus.Tests
# Gap close: name the gap's changed Domain files, one --mutate each
git diff --name-only <commit-before-the-gap> -- ../NenFocus.Domain   # the list
dotnet stryker --project NenFocus.Domain.csproj --log-to-file \
  --mutate "**/Website.cs" --mutate "**/BlockedApp.cs"            # …one per changed file
# End of a box: the whole Domain (~16 min on 2026-10-08)
dotnet stryker --project NenFocus.Domain.csproj --log-to-file
```

**Not `--since:<commit>` at a gap close (changed 2026-10-13, Gap 112).** `--since` also re-tests every mutant in *unchanged* code whose covering tests changed — in Gap 112 that was 420 extra mutants on top of the 201 that mattered, and a value object under hundreds of real-SQLite use-case tests (`Website`, `BlockedApp`, `Session`) costs 5–10 s a mutant: the run took over 40 minutes. Step 5 asks for the files the gap changed, so name them. Before starting, say how many mutants and roughly how long (`--log-to-file` writes progress under `StrykerOutput/<run>/logs/`); a file under many use-case tests is the slow kind. `--since` is for a broad re-check, run when nothing else needs the machine.

Reports go to `NenFocus.Tests/StrykerOutput/` (git-ignored); open the HTML report to see each survivor inline. **Don't build or run tests in parallel** — Stryker swaps mutated assemblies into the test project's `bin`. In Gap 112 the stop hook's architecture-test run did exactly that mid-mutation and reported failures that weren't there (all 61 passed from a separate output folder); since 2026-10-14 the hook skips while a `dotnet-stryker` process is alive and runs at the next stop after it ends.

**Reading the result** — only one kind needs action:
- *String mutation* survivors (an exception message blanked): ignore; wording isn't a rule (→ Gap 100).
- *Equivalent* mutants (`asOf < End ? asOf : End` → `<=`: equal values give the same answer either way): ignore.
- *Compile error* / *Ignored*: Stryker's own bookkeeping.
- *NoCoverage*: no test runs the line at all — a test, or a reason.
- **Any other survivor** — a behaviour change nothing caught: write the test, and probe it red by applying that exact mutant by hand.

Scores over the whole Domain: 84.5% on 2026-10-08 before Gap 113; 86.4% after it (1,123 tested, 132 survived — wording, equivalent mutants and the untested `EnsureMine` guards).

**Probing by hand: mutate the syntax tree, not the text.** Stryker changes one operator *as C# parsed it*. `a && b && c` is `(a && b) && c`, so its "first `&&` → `||`" mutant is `(a || b) && c`, while a text edit gives `a || (b && c)` — a different change, which other tests may already kill. Add the parentheses when probing (Gap 113, S5). Restoring the file from a copy gives it an older timestamp than the mutated build, so the next `dotnet test` keeps running the mutant — rebuild with `--no-incremental` (or `touch` the file) before trusting a green or red after a probe (2026-10-14).

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

**Against the real app (added 2026-10-08, Gap 92):** when the question is "does the whole chain behave", build the real composition root instead of doubles — `AddInfrastructure` on a temp SQLite file, then register a `TestClock`, a substitute `IEnforcementService` and `INotificationService` *after* it (the last registration wins: no hosts file, no toasts, your time), and `Database.Migrate()`. Run one heartbeat with `ScheduledEnforcementService.TickAsync` (internal; the hosted service is fetched from `GetServices<IHostedService>()`), and give async UI work a few `Settle()` + short delays. Slower (about 1–5 s per test), so keep these few. Examples: `ShellHeartbeatIntegrationTests`, `ContractPresetFormIntegrationTests`; `MainPageBreakFlipTests` uses the real `ActiveSessionMonitor` and waits for its own 1-second tick.

Suites: `RitualStepViewTests`, `TenPageViewTests`, `PostSessionViewTests`, `PresetPageViewTests`, `PlannerEndDropdownViewTests`, `PlannerBlockLockViewTests`, `PlannerContractButtonsViewTests`, `ShellRitualPromptTests`, `ShellHeartbeatIntegrationTests`, `MainPageBreakFlipTests`, `ContractPresetFormIntegrationTests`.

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
