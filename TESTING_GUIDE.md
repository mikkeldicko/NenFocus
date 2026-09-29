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
└── NenFocus.Tests.csproj
```

**Rules:**
- `Domain/` tests never use mocks. Domain is pure logic.
- `Application/` tests mock all interfaces (`ISessionRepository`, `ISessionStateService`, etc.) with NSubstitute.
- `Infrastructure/` tests **are built** — they run against a real SQLite database (via `SqliteRepositoryTestBase`'s in-memory connection, not a fake in-memory EF provider), not mocks. This is the correct place for repository/EF Core behavior and DI composition tests.
- No ViewModel tests — ViewModels are tested via the Application layer they delegate to.

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
| DI composition | `Infrastructure/CompositionRootTests.cs` resolves every registered handler/service to catch missing DI registrations before runtime. |

### Do not test these:

| Target | Why |
|--------|-----|
| ViewModels | They delegate everything; test the service they call instead |
| `WindowsEnforcementService` | Windows API calls; use integration tests in a real process |
| XAML bindings | Visual; use manual testing or a UI test framework |

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
