# NenFocus — UI/UX Standards

Visual conventions, layout rules, and ViewModel binding contracts. Read this when building or reviewing any View or ViewModel.

For the *rules* on what ViewModels are forbidden from doing, see `MODULE_STANDARDS.md → The ViewModel Forbidden Zone`.

---

## Layout & Navigation

- **Window size**: 1100×720 default, minimum 900×580.
- **Sidebar**: fixed 210px left sidebar. Nav items use the `NavItemViewModel` pattern — each item carries its own `PageViewModel` instance.
- **Page content**: `TransitioningContentControl` renders the active `CurrentPage` from `ShellViewModel`.
- **Ritual overlay**: full-screen `ContentControl` rendered over the Shell when `ShellViewModel.IsRitualActive = true`. The Ritual has no sidebar.
- **Post-session overlay**: same pattern as Ritual — full-screen when `ShellViewModel.IsPostSessionActive = true`.

---

## Colour Palette

| Role | Hex | Usage |
|------|-----|-------|
| Accent / gold | `#B8922A` | Active nav dot, Ritual step dots, "USE NEN" button text, break timer, break progress bar, Nen Contract banner/popover "Active" status pill |
| Active nav background | `#F5F0E8` | Selected sidebar item fill |
| Inactive dot | `#DDDDE8` | Non-active Ritual step indicators; Planner day-header badge — any day belonging to a Scheduled (Upcoming) contract, overriding met/unmet/rest (2026-09-06, see `DESIGN_GAP_HISTORY.md` Gap 55) |
| Upcoming-contract tag | `#6C7A96` | Nen Contract banner/popover "Upcoming" status pill (2026-09-06, Gap 55) |
| Active-contract rest day — future | `#C5E4DE` | Planner day-header badge, Active contract, rest day, `Timing == Future` (2026-09-06, Gap 55) |
| Active-contract rest day — today | `#4FA89C` | Planner day-header badge, Active contract, rest day, `Timing == Today` (2026-09-06, Gap 55) |
| Active-contract rest day — past | `#2E7A70` | Planner day-header badge, Active contract, rest day, `Timing == Past` (2026-09-06, Gap 55) |
| Page background | `#F5F5F7` | All page backgrounds |
| Card background | `#FFFFFF` | Session panel, modal cards |
| Card border | `#E8E8F0` | Card outlines |
| Primary text | `#1A1A2E` | Headings, countdown timer |
| Secondary text | `#888899` | Session goal, preset name |
| Muted text | `#AAAABC` | Section labels, date, muted info |
| Disabled / strikethrough | `#BBBBCC` | Completed checklist items |
| Session active green | `#3A9E6A` | "SESSION ACTIVE" status chip; Planner tile — Focus block completed on time (no badge text); Block Snapshot outcome badge ("COMPLETED ON TIME") |
| Missed tile red | `#C0392B` | Planner tile — Focus block missed (ritual never completed, window closed); Block Snapshot outcome badge ("MISSED") |
| Late-or-early tile amber | `#D97706` | Planner tile — Focus block ritual completed after the grace period ("STARTED LATE") or session ended before the block's scheduled end ("ENDED EARLY"); Block Snapshot outcome badge (same two labels) |

---

## Typography

- **Primary font**: system default (Avalonia inter font stack)
- **Monospace / timers**: `Consolas, Courier New, monospace` — used for all countdown displays
- **Countdown size**: `FontSize="52"`, `FontWeight="Thin"`, `LetterSpacing="6"`
- **Section labels**: `FontSize="9"`, `FontWeight="Medium"`, `LetterSpacing="4"` — e.g. "TODAY", "SESSION ACTIVE"
- **Body**: `FontSize="14"`

---

## Ritual Step Indicators

- 4 dot indicators in the top bar of `RitualShellView`.
- Active step dot: gold (`#B8922A`), filled.
- Inactive step dot: `#DDDDE8`, outlined.
- Transition between steps: `CrossFade` animation, 450ms duration.
- Steps in order: **Ten → Zetsu → Ren → Hatsu**. Do not reorder.

---

## ViewLocator Convention

`XyzViewModel` resolves to `NenFocus.Views.XyzView`. The `ViewLocator` strips the `ViewModel` suffix and adds `View`, then looks up the type in `NenFocus.Views`.

Naming rules:
- ViewModel class: `<Page>ViewModel` — e.g. `MainPageViewModel`, `HatsuPageViewModel`
- View class: `<Page>View` — e.g. `MainPageView`, `HatsuPageView`
- XAML file: `<Page>View.axaml`
- Code-behind: `<Page>View.axaml.cs`

---

## ViewModel Binding Contract

- Views bind to ViewModels **only**. No business logic in code-behind.
- ViewModels receive all dependencies (command handlers, query handlers, services) via constructor injection.
- Navigation between pages is handled by passing `Action` callbacks into ViewModel constructors — ViewModels never reference the Shell or other ViewModels directly.
- Observable properties use `[ObservableProperty]` from CommunityToolkit.Mvvm.
- Commands use `[RelayCommand]`. Can-execute guards use `[NotifyCanExecuteChangedFor]`.

---

## Time Display Formatting

All time values are exposed from ViewModels as `TimeSpan` properties. Views convert to strings using `TimeSpanToStringConverter` registered in each view's resources:

```xml
<UserControl.Resources>
    <vm:TimeSpanToStringConverter x:Key="TimeSpanConverter"/>
</UserControl.Resources>
```

| Display | Binding | ConverterParameter |
|---------|---------|-------------------|
| Session countdown (`hh:mm:ss`) | `SessionTimeRemaining` | _(none, default)_ |
| Break countdown (`mm:ss`) | `BreakTimeRemaining` | `mm\:ss` |
| Session time during break | `SessionTimeRemaining` | _(none)_ + `StringFormat='Session: {0}'` |

---

## Progress Bars

- All progress bars use `Minimum="0" Maximum="1"` with a `double` binding from the ViewModel.
- Session progress: `Foreground="#B8922A"`, `Background="#EEEEF5"`, `Height="3"`, `CornerRadius="2"`.
- Break progress: same style, shown only during `IsOnBreak`.
- Progress values are computed by `IActiveSessionMonitor` — the ViewModel passes them through without modification.

---

## Planner Drag Interactions

**Moved from `DOMAIN_DICTIONARY.md` on 2026-09-10** — pixel-level interaction mechanics, not domain vocabulary. See `DOMAIN_DICTIONARY.md`'s Planner Page entry for the one-line domain summary (what the page is, what it schedules).

**Drag interactions (built 2026-07-03):**

| Gesture | Behaviour |
|---------|-----------|
| Click on empty space | Shows a 1-hour gold ghost at the clicked slot, opens the new-block form pre-filled with that range |
| Drag on empty space | Paints a live gold ghost; on release opens the new-block form pre-filled with the dragged range |
| Drag tile body | Moves the tile; ghost overlay follows pixel-smooth, original tile dims to 30% opacity |
| Drag bottom edge (10 px hot zone) | Resizes end time; ghost overlay extends/shrinks *(commit partially broken — parked)* |
| Drag top edge (10 px hot zone) | Resizes start time; disabled when start is in the past |
| Click tile | Opens edit form |

**Ghost overlay:** A `Border` added directly to the Canvas (`#B8922A`, 90% opacity, `IsHitTestVisible = false`). Shows a live time label (`HH:mm – HH:mm`) that snaps to 15-minute increments. The create ghost (`#B8922A`, 70% opacity) persists after drag release until the form closes or a rebuild fires.

**Lock rules:**
- `IsTileFullyLocked` — the block has started (`PlannedBlockDto.HasStarted`, from `PlannedBlock.HasStarted(now)`): no move, no resize of either edge. Draft tiles are never locked. A started block runs to its end — it ends early only through End Early or by breaking its contract (Gap 88 phase 0; replaced the old "bottom-resize allowed once started, GCal behaviour").

**Drag error banner:** Red `DragError` banner at row 1 of the planner grid, bound to `PlannerPageViewModel.DragError`. Surfaces overlap rejections and lock violations.

**Event routing:** All pointer movement is handled on the owner `Canvas` (captured via `e.Pointer.Capture(ownerCanvas)` in `OnTilePointerPressed`). The tile button handles `PointerPressed` and hover-cursor feedback only; `PointerMoved` and `PointerReleased` route to the canvas handlers for both create and tile drags.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| UI | `PlannerPageView.axaml.cs` | All drag state machine, ghost lifecycle, pointer routing |
| UI | `PlannerPageViewModel` | `OpenNewBlockFormWithRangeCommand`, `MoveOrResizeBlockCommand`, `IsTileFullyLocked`, `DragError`, `IsBlockLocked`, `CanSaveBlock` |
| UI | `PlannerDragMath` (`ViewModels/Components/`) | Pure static helper: `YToSnappedTime`, `DeltaYToSnappedMinutes`, `ClampRange`, `WouldCollide` |
| UI | `DragCreatedRange` | Record struct passed from code-behind → ViewModel on drag-create completion |
| UI | `DragMoveResult` | Record struct passed from code-behind → ViewModel on drag-move/resize completion |
| Application | `CreatePlannedBlockCommandHandler` | Rejects Focus Block if it overlaps a running session (in addition to existing Focus-block overlap check). Compares against `activeSession.EndAt`, not `StartedAt + Duration` (fixed 2026-08-21, `DESIGN_GAP_HISTORY.md` Gap 32) — for a Planned session started within the grace period, `EndAt` is pinned to the originating block's own scheduled end while `Duration` is the block's full nominal length, so the old recomputed window overshot the session's real end by the lateness amount and could wrongly reject a block that legitimately fit right after it. |
| Application | `UpdatePlannedBlockCommandHandler` | Same active-session overlap guard for move/resize, same `EndAt` fix (Gap 32) |

**Form locking:** `IsBlockLocked` (= `IsEditing && IsEditingBlockStarted`, copied from `PlannedBlockDto.HasStarted`) disables every field and hides Save. It covers a completed Ritual and the running session's block too — both only exist once the block has started. The ViewModel never compares times itself (Gap 88 phase 0).

**Deletion protection:** `DeletePlannedBlockValidator` refuses (a) any block linked to a Nen Contract, forever, and (b) a running block (`HasStarted(now) && !HasEnded(now)`) — including a Focus block still awaiting its Ritual and the running session's own block. An ordinary block may be deleted before it starts, or after it ends (from the snapshot view). `CanDeleteFromForm` (`IsEditing && !IsEditingBlockStarted && !IsEditingBlockContractLocked`) and `CanDeleteSnapshotBlock` (`ContractId is null`) mirror it from DTO fields — the form only opens for blocks that haven't ended, so "started" is the running check there. History: Gaps 9, 28, 88.

**Block Snapshot — read-only view for a done block *(built, 2026-08-21 — see `DESIGN_GAP_HISTORY.md` Gap 28)*:** clicking a **done** block no longer opens the edit form with fields disabled — it opens a dedicated, genuinely separate read-only overlay. "Done" means: for a Focus Block, `Outcome` has resolved (`Missed`/`StartedLate`/`EndedEarly`/`CompletedOnTime` — the block is no longer open); for a Background Block, which has no `Outcome` concept at all, `HasEnded` is the only signal. The pre-existing active-session-originating-block guard (`_sessionState.ActiveSession?.OriginatingBlockId == tile.Id`) is checked first and unchanged — a block whose session is still running keeps opening nothing (matching the existing behavior the locked edit-form fields were already built for), never the snapshot.

- **Content, Focus block with a Session** (`GetSessionSnapshotForBlockQuery` returns non-null): type/outcome/redemption badges, date, time range, duration, Zetsu goal, reflection (placeholder text when blank — a session ending without a reflection is a valid, deliberate outcome per `Session.SetReflection`'s own precedent, not an error state), a full expandable list of blocked attempts (`BlockedAttemptSnapshotDto` — resource name, type, timestamp), and — when `IsRedemption` — which missed block(s) it covered and how much. The sub-minute "within grace, full credit given" note this section used to show (Gap 45) is gone — see `DESIGN_GAP_HISTORY.md` for the whole-minutes crediting redesign that removed Gap 45's forgiveness outright; an `EndedEarly` badge now always has a real, honest deficit behind it, however small.
- **Content, no Session** (Missed Focus block, or any Background block — the query returns `null`): minimal snapshot only — type, preset, date, scheduled time range, duration. No session section at all, since there is structurally nothing to show. (A Cancelled block never reaches this view, or any Planner tile — `GetPlannedBlocksForDayQueryHandler` already filters `CancelledAt.HasValue` blocks out of the tile list entirely.)
- **Delete, from the snapshot only:** enabled iff the block has never been linked to a Nen Contract (`ContractId is null`) — permanently, even after that contract concludes. This **reverses** the historical-record protection `DeletePlannedBlockValidator` has had since Gap 9 (ended blocks were previously never deletable, full stop) for the non-contract-linked case specifically, while making contract-linkage protection **stronger** than before (previously scoped to `IsInEffect`, i.e. only while the contract was still live).

**Technical mapping (Block Snapshot):**

| Layer | Artifact | Role |
|---|---|---|
| Application | `GetSessionSnapshotForBlockQuery`/`Handler` → `SessionSnapshotDto` (`Queries/GetSessionSnapshotForBlock/`, `DTOs/`) | Given a `PlannedBlockId`, returns `null` when no Session exists, otherwise `{ Goal, Reflection, Attempts, RedemptionCoverage }` — pure composition of `ISessionRepository.GetByOriginatingBlockIdAsync`, `IBlockedAttemptTracker.GetAttemptsForSessionAsync`, and the block's own `RedemptionEntries`. No new persistence. |
| UI | `PlannerPageViewModel.ShowSnapshotAsync`/`IsSnapshotVisible`/`SnapshotBlock`/`SnapshotSession` | Opened from `EditBlockAsync`'s "done" branch. `CanDeleteSnapshotBlock`/`HasSnapshotSession`/`ShowSnapshotNoSessionMessage` are boolean combinations of already-loaded state, same shape as the pre-existing `CanDeleteFromForm`/`IsBlockLocked`. |
| UI | `PlannerPageView.axaml` snapshot overlay | A separate `Border` overlay, not the reused edit-form modal — badges via `FocusBlockOutcomeToLabelConverter`/`FocusBlockOutcomeToBrushConverter`/`BlockPresetTypeToLabelConverter`, an `Expander` for the blocked-attempts list. |

**`TryParseFormTime`:** Static helper on `PlannerPageViewModel` that special-cases `"24:00"` (rejected by `TimeSpan.TryParse`) to support end-of-day block scheduling.
