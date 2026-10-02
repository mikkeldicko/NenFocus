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

What a ViewModel may and may not hold or do (dispatchers only, no handlers, no timers, math or value formatting, no Domain entities; code-behind does pixel geometry only) is `MODULE_STANDARDS.md` → UI and The ViewModel Forbidden Zone, guarded by the architecture tests. The UI-only conventions:

- Navigation between pages is handled by passing `Action` callbacks into ViewModel constructors — ViewModels never reference the Shell or other ViewModels directly.
- Observable properties use `[ObservableProperty]` from CommunityToolkit.Mvvm.
- Commands use `[RelayCommand]` (a method `FooAsync` becomes `FooCommand`). Can-execute guards use `[NotifyCanExecuteChangedFor]`.

---

## Time Display Formatting

ViewModels expose raw values and Views format them with converters — the rule and the list of approved converters are `MODULE_STANDARDS.md` → No String Formatting and Converter Catalogue. The session screen's formats:

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
- Progress values arrive ready from `IActiveSessionMonitor` (which asks `TimeSlot`/`SessionBreak`); the ViewModel passes them through (`MODULE_STANDARDS.md` → No Math).

---

## Planner Drag Interactions

**Moved from `DOMAIN_DICTIONARY.md` on 2026-09-10** — pixel-level interaction mechanics, not domain vocabulary. See `DOMAIN_DICTIONARY.md`'s Planner Page entry for the one-line domain summary (what the page is, what it schedules).

**Drag interactions (built 2026-07-03):**

| Gesture | Behaviour |
|---------|-----------|
| Click on empty space | Shows a 1-hour gold ghost at the clicked slot, opens the new-block form pre-filled with that range |
| Drag on empty space | Paints a live gold ghost; on release opens the new-block form pre-filled with the dragged range |
| Drag tile body | Moves the tile; ghost overlay follows pixel-smooth, original tile dims to 30% opacity |
| Drag bottom edge (10 px hot zone) | Resizes end time; ghost overlay extends/shrinks |
| Drag top edge (10 px hot zone) | Resizes start time; disabled when start is in the past |
| Click tile | Opens edit form |

**Ghost overlay:** A `Border` added directly to the Canvas (`#B8922A`, 90% opacity, `IsHitTestVisible = false`). Shows a live time label (`HH:mm – HH:mm`) that snaps to 15-minute increments. The create ghost (`#B8922A`, 70% opacity) persists after drag release until the form closes or a rebuild fires.

**Lock rules:**
- `IsTileFullyLocked` — the block's `Allowances` say it can't be rescheduled (`PlannedBlockDto.Allowances.CanReschedule`, from `IBlockChangeChecker` — it has started, or its contract binds it and allows no movement; Gap 93): no move, no resize of either edge. Draft tiles are never locked. A started block runs to its end — it ends early only through End Early or by breaking its contract (Gap 88 phase 0; replaced the old "bottom-resize allowed once started, GCal behaviour").

**Drag error banner:** Red `DragError` banner at row 1 of the planner grid, bound to `PlannerPageViewModel.DragError`. Surfaces overlap rejections and lock violations.

**Event routing:** All pointer movement is handled on the owner `Canvas` (captured via `e.Pointer.Capture(ownerCanvas)` in `OnTilePointerPressed`). The tile button handles `PointerPressed` and hover-cursor feedback only; `PointerMoved` and `PointerReleased` route to the canvas handlers for both create and tile drags.

**Technical mapping:**

| Layer | Artifact | Role |
|-------|----------|------|
| UI | `PlannerPageView.axaml.cs` | All drag state machine, ghost lifecycle, pointer routing |
| UI | `PlannerPageViewModel` | `OpenNewBlockFormWithRangeCommand`, `MoveOrResizeBlockCommand`, `IsTileFullyLocked`, `DragError`, `EditingAllowances` and what reads it (`CanRescheduleBlock`, `CanChangePreset`, `CanSaveBlock`, `CanSplitBlock`, `CanDeleteFromForm`) |
| UI | `PlannerDragMath` (`ViewModels/Components/`) | Pure static pixel-geometry helper: `YToSnappedTime`, `DeltaYToSnappedMinutes`, `ClampRange`, `ClampMoveStart`, `ClampResizeTopStart`, `ClampResizeBottomEnd`. No placement rules — overlap is `BlockPlacementChecker`'s (Gap 88 phase 8) |
| UI | `DragCreatedRange` | Record struct passed from code-behind → ViewModel on drag-create completion |
| UI | `DragMoveResult` | Record struct passed from code-behind → ViewModel on drag-move/resize completion |
| Application | Create/Update validators → `GetBlockPlacementVerdictQuery` → `BlockPlacementChecker` | Decide whether a drop or form save is allowed (past, contract hours, Focus time taken by a block or running session); the refusal text lands in `DragError`/`FormError` (Gap 88) |

**Form locking:** the form reads the edited block's `Allowances` (`EditingAllowances`; null for a new block or a draft tile, which nothing restricts). `CanRescheduleBlock` enables the date and time fields, `CanChangePreset` the preset picker; Save shows when either is true, Split and Delete from `CanSplit` and `CanDelete`. The ViewModel works out no lock itself and never reads the in-memory session for one (Gap 93).

**Deletion:** Delete shows in the form (`CanDeleteFromForm`) and in the snapshot (`CanDeleteSnapshotBlock`) when the block's `Allowances.CanDelete` says so — a block in no contract that isn't running, or a future Bonus block its contract doesn't bind. The rule is the Domain's (`IBlockChangeChecker`, Gap 93); the validator refuses from the same answer.

**Split dialog:** each piece has a start and an end time. A date field per piece shows only when `Allowances.SplitPiecesMayChangeDay` is set (a bound block under Period Study); otherwise both pieces stay on the block's own day.

**Block Snapshot — read-only view for a done block (Gap 28):** clicking a **done** block opens a separate read-only overlay, not the edit form. "Done" means: a Focus block whose `Outcome` has resolved; a Background block that has ended. The running session's own block opens nothing (checked first, via `ActiveSession?.OriginatingBlockId` — unreliable after a restart, see Gap 89 area 4 F2).

- **Focus block with a session** (`GetSessionSnapshotForBlockQuery` returns one): type/outcome/redemption badges, date, time range, duration, Zetsu goal, reflection (placeholder when blank — a valid outcome, not an error), an expandable list of blocked attempts, and — for a redemption block — which missed blocks it covered and how much.
- **No session** (a missed Focus block, or any Background block): type, preset, date, scheduled time range, duration only. Cancelled blocks never appear as tiles (`GetPlannedBlocksForDayQueryHandler` filters them out).
- **Delete** shows only when the block's `Allowances.CanDelete` says so (see Deletion above) — never for a contract's block that has run.

**Technical mapping (Block Snapshot):**

| Layer | Artifact | Role |
|---|---|---|
| Application | `GetSessionSnapshotForBlockQuery`/`Handler` → `SessionSnapshotDto` (`Queries/GetSessionSnapshotForBlock/`, `DTOs/`) | Given a `PlannedBlockId`, returns `null` when no Session exists, otherwise `{ Goal, Reflection, Attempts, RedemptionCoverage }` — pure composition of `ISessionRepository.GetByOriginatingBlockIdAsync`, `IBlockedAttemptTracker.GetAttemptsForSessionAsync`, and the block's own `RedemptionEntries`. No new persistence. |
| UI | `PlannerPageViewModel.ShowSnapshotAsync`/`IsSnapshotVisible`/`SnapshotBlock`/`SnapshotSession` | Opened from `EditBlockAsync`'s "done" branch. `CanDeleteSnapshotBlock`/`HasSnapshotSession`/`ShowSnapshotNoSessionMessage` are boolean combinations of already-loaded state, same shape as `CanDeleteFromForm`. |
| UI | `PlannerPageView.axaml` snapshot overlay | A separate `Border` overlay, not the reused edit-form modal — badges via `FocusBlockOutcomeToLabelConverter`/`FocusBlockOutcomeToBrushConverter`/`BlockPresetTypeToLabelConverter`, an `Expander` for the blocked-attempts list. |

**`TryParseFormTime`:** Static helper on `PlannerPageViewModel` that special-cases `"24:00"` (rejected by `TimeSpan.TryParse`) to support end-of-day block scheduling.
