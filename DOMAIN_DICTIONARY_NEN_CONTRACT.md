# NenFocus — Nen Contract Domain Detail

Current-state reference for the Nen Contract aggregate: its rules, the Redemption Rules, and the technical mapping. Split out of `DOMAIN_DICTIONARY.md` on 2026-09-10; trimmed to current state on 2026-10-06 (the build history it carried lives in `DESIGN_GAP_HISTORY.md` under the Gap numbers cited here).

**Not auto-loaded.** Read it when a task touches Nen Contract code. The core vocabulary (Seal, Strictness Rule, Daily Floor, Contract Membership, Bound block, Bonus Block…) is in `DOMAIN_DICTIONARY.md`'s Quick Reference; this file adds the detail. Same rule as there: one line of current behavior plus a Gap pointer, never the story.

---

## The contract

**Definition:** A sealed, time-bound commitment the user makes to a schedule of Focus and Background blocks over a contiguous date range, with self-imposed rules on how much the schedule may change. Own aggregate root; it does **not** own `PlannedBlock`s — a block points to it through `Membership` (Gap 97).

**Shape:** an unbroken run of **Contract Days** (`ContractCalendar`, one `ContractDay` per date, no gaps), each with a local **window** and a **floor** (none = rest day). `StartsAt` = first day's window start, `EndsAt` = last day's window end; both derived, never stored (Gap 65). `StartDate`/`EndDate` are mirrors kept for SQL; `OriginalEndDate` is frozen at Seal for the recap.

**No shared days:** two in-effect contracts may not share even one date (endpoints inclusive). `SealNenContractValidator` enforces it via `CheckNenContractDateRangeOverlapQuery` (over every contract, so a broken contract's shrunk range still counts); the wizard previews it. A new contract on the day another was broken is Open Gap 103.

**Seal** is the only factory — no draft state in the domain. It takes a `ContractCalendar` (`ContractCalendar.Generate`) and the rules, refuses a start in the past, and hard-checks each day's floor against planned **Focus** hours (rest days skipped). `Status` = `Active` if already started, else `Scheduled`. Raises `NenContractSealedEvent`.

**Lifecycle:** `Scheduled → Active → Completed`; `Scheduled | Active → Broken`.
- `Activate` / `Complete` — by the heartbeat sweep at the exact local `StartsAt` / `EndsAt` (`IsDueForActivation` / `IsDueForCompletion`, Gap 65). Complete early: allowed when `NenContract.CheckComplete` says so — no session running, every committed hour delivered, every block final (Gaps 59, 95).
- `Break` — user cancels while in effect. Drops the days after the break day, keeps the break day whole. `BreakNenContractCommandHandler` asks `NenContract.FateWhenBroken` per member block (cancel / release to plain / leave as history; a block is left once `IsFinal`) (Gaps 23, 60, 97, 95). The Bonus-blocks prompt is Open Gap 108.
- **No contract ends while a session runs** — Complete and Break are both refused (`CheckComplete` / `CheckBreak` → `ContractEndVerdict`, Gap 95 decision 18), by the validators and again by the handlers. Not yet covered: the heartbeat's natural completion while a spontaneous session runs (Gap 67).
- **`AllowCancellation` is stored and shown but not enforced** (interim since 2026-08-18) — any in-effect contract can be broken. Strict Mode (Gap 83) is the intended gate; search for "interim" comments when building it. `CheckBreak` is where its reason goes.
- **No extension.** `Extend` was removed on purpose (2026-09-04): wanting more means sealing a new contract. Extending is re-designed as Open Gap 80.

## Creation: Simple vs Advanced

| Field | Simple (Lockdown / Daily Flex / Period Study) | Advanced (a saved Contract Preset, applied as-is) |
|---|---|---|
| Strictness | Fixed by the option: `Locked` / `DailyFlexibility` / `PeriodTimeBudget` | From the preset |
| Daily Floor | Each day = its planned Focus hours (`FollowsPlan`) | Preset's fixed floor, else `FollowsPlan` |
| Work Hours | None | From the preset (optional) |
| Rest days | Days with no planned Focus | Chosen in the wizard (date-bound, never preset material) |
| Allow Cancellation, keep blocking during breaks, redemption placement, Bonus follows strictness | Defaults | From the preset |
| Change Window | — | Parked |

**Contract Preset** (`ContractPreset`, aggregate root): `Name`, `Rules` (`ContractRules`), optional `WorkHours`. Authored only on the Presets page → Contract Presets tab (Gap 27); the wizard picks one and shows it read-only. The contract keeps its own copy of the rules; `ContractPresetId` exists only so a preset in use by an in-effect contract can't be deleted.

## Rules

**`ContractRules`** (Gap 93): strictness, fixed daily floor (or none), allow cancellation, keep blocking during breaks (Gap 64), redemption placement (Gap 49), Bonus blocks follow strictness. Works out `FloorRule` and `MovementRange`; never stored separately.

**Strictness Rule** — answered only by `ContractRules.MovementRange`: how far a **bound** block may move — `Locked` nowhere, `DailyFlexibility` its own day, `PeriodTimeBudget` anywhere in the contract. Immutable after Seal (tightening mid-contract parked for v2). Nothing outside the Domain compares strictness values (`ChangePermissionRuleTests`).

**What may happen to a member block** — one answer, `IBlockChangeChecker` (Gap 93):
- A started block is locked; a running block can't be deleted; a finished contract's blocks are history.
- While in effect, the contract **binds** its Committed and Redemption blocks, and its Bonus blocks only when the rules say so.
- A bound block moves within its `MovementRange`, keeps its preset, can be split (not under `Locked`), is never deleted.
- A free Bonus block moves anywhere inside the period, may change preset and be deleted until it starts, can't be split.
- No member moves outside the planned period; a Bonus block leaves only when the contract releases it (Break, Delete).
- A floor is a planning minimum of what was signed: only a Committed block's change can lower it, and `NenContract.CheckFloor` counts Committed blocks only — Seal asks the same check (Gap 95, decisions 7, 16).
- Split = atomic delete-and-recreate with the original's membership and preset.
- A Redemption block's length is fixed once placed: it may move and be split, never resized (`NenContract.CheckBlockLength` → `RedemptionLengthFixed`, Gap 95 decision 16).

**Daily Floor:** stored per `ContractDay` (null = no floor), never more than the day's window — a **planning minimum** only: Seal and every Committed-block change are held to it (`CheckFloor`), but it never decides "met" (Gap 95, decision 7). Days get it from `DailyFloorRule` when generated: `Fixed` (a day that can't fit it is refused at Seal, never shrunk), `FollowsPlan`, or `None`. `FloorHoursFor(date)` reads it (the edit form).

**Period target (`PeriodTimeBudget` with no fixed floor):** `FloorRule` is `None` and `HasPeriodTarget` is true; the target is `CommittedFocusHours`, read off the rules, never stored (Gap 95, decision 8 — `ContractFloor` deleted). The total is the planning minimum (`CheckFloor`); each day still owes its own plan like any contract's (Day Met below, Gap 95 decision 5).

**Rest days:** a day that owes nothing (`ContractDayStatus.IsRest`): no Committed block and no redemption on it — trivially met, skipped by days behind and streaks. A day with no stored floor can still owe hours if a redemption lands there (Gap 95; replaced Gap 52's display-only ad-hoc target).

**Day Met (Gap 95, decisions 5 and 6):** one rule for every contract, `NenContract.DayStatuses` → one `ContractDayStatus { Date, TargetHours, CompletedHours, BonusHours, IsRest, IsMet, PercentMet }` per day. **Target** = the day's Committed blocks as planned (missed, ended early or cancelled still count in full) + what its redemptions cover, once per missed block per day and never more than that block's deficit. **Completed** = the credit of its Committed and Redemption blocks + Spontaneous sessions on one of their presets (until Gap 67). **Bonus** = Bonus blocks' credit, apart. Met = completed ≥ target. The Planner badges and banner, the recap and completion all read it (`GetNenContractDayStatusesQuery`); arithmetic in the Domain-internal `ContractDayLedger`.

**Work Hours / day windows:** every day has a window — the Work Hours, or the whole day on a no-Work-Hours day or contract; day 1 starts at the chosen start (Gap 65). `NenContract.AllowsBlockPlacement` checks each day a slot touches and that the slot lies inside the planned period (Gap 97). Only `BlockPlacementChecker` asks it. Drafting still checks the wizard's flat Work Hours with `WorkHours.ContainsRange` (`PlannerPageViewModel.ValidateDraftPlacement`) — moving to the Domain and deleting `ContainsRange` is Gap 68. Per-day hours in drafting: Open Gap 82.

**Change Window:** parked. `ChangeWindow` value object exists (`Contains(TimeOnly)`, `UsesPerDay`), not mapped or enforced.

**Banner:** every contract shows `"Contract: [Name] — Day X/Y — Xh / Yh completed — Zh deficit"` (`NenContractRangeViewDto.TotalCompletedHours` / `TotalFloorHours`; floor total = `CommittedFocusHours`). A week straddling two contracts badges every day; the banner and Cover Deficit name one, switchable (`SelectedContract`, Gap 55). The popover's Active card always names the app-wide Active contract.

## Block outcome and credit

**`FocusBlockOutcome`** — `CompletedOnTime` / `StartedLate` / `Missed` / `EndedEarly`, from `FocusBlock.Outcome(asOf)`, read off the block's own stamps (Gaps 86, 95). `null` = no verdict yet (could still be started late until its scheduled end). `EndedEarly` wins over `StartedLate`; the flags `StartedLate` and `EndedEarly` can both be true; `PlannedBlockDto` carries both, and the tile shows one label per true flag (Gaps 20, 95). The tile draws `WorkSlot() ?? Scheduled`.

**Grace period:** 5 min (`FocusBlock.GracePeriodMinutes`), all Focus blocks. A Ritual completed within it is `CompletedOnTime`. `FocusBlock.IsLateAt(moment)` is the one lateness check (Gaps 20, 95).

**Credit** (Gaps 57, 95): `FocusBlock.WorkSlot()` is the span worked, from the stamps alone — from the scheduled start when the Ritual came within grace (enforcement was already running), else from the Ritual; to the session's end when it ended early, else the scheduled end. `CreditedSlot(asOf)` cuts it at `asOf` and rounds down to whole minutes — rounding is credit's only own rule. An early end credits exactly what was delivered; nothing beyond the scheduled end. `OwnDeficit(asOf)` is what the block failed to deliver once `IsFinal` (Rules 1, 13, 18). `MinimumMeaningfulDeficitMinutes` is only the UI-notice threshold for `WasRedemptionCoverageMeaningfullyCapped`.

**Not built:** the 3-minute grace warning toast.

## Redemption

**Redemption block:** a Focus block with role Redemption (Gap 97), placed through Cover Deficit, whose `RedemptionEntries` (`{ MissedBlockId, MinutesCovered }`, JSON column) name the deficits it covers. It stays Redemption even if its entries drop to none. Tile tag "REDEMPTION". Background blocks never redeem.

**Cover Deficit:** the banner button (`GetSuggestedRedemptionPlacementsQuery`) sizes a tile to a preset's total remaining deficit, at the next quarter-hour, shifted earlier (never before now) to fit before the day's boundary, skipping past Focus tiles; a knowingly-partial tile needs confirmation. More than one preset owing → a picker popover. Only Cover Deficit creations and edits of Redemption blocks ask attribution; every other block is Committed, Bonus or plain and never claims (Gap 97). A spontaneous redemption path was dropped, reopened as Open Gap 72.

| # | Rule |
|---|------|
| 1 | Deficit of a missed / late / ended-early block = scheduled minutes − credited minutes (`FocusBlock.OwnDeficit`), never raw elapsed (Gap 24). Remaining = that − other blocks' `MinutesCovered`, each capped by the covering block's own (finality-gated) outcome: open trusts the claim, `Missed` gives zero, otherwise up to its credited minutes (Gap 33). Only a `1e-6` floating-point guard; no write-off (Gap 57). |
| 2 | Tracked per missed block, not per preset. |
| 3 | A redemption block links to missed blocks via `RedemptionEntries`; only a Redemption-role Focus block may carry them (`AssignRedemptionEntries`). |
| 4 | Same preset only — attribution walks deficits of the block's own preset. |
| 4a | Same contract only — attribution walks one contract's ledger (`ContractId` required, Gap 97). |
| 5 | One redemption block can cover several missed blocks — oldest outstanding first, spilling into the next. |
| 6 | A redemption block may exceed the deficit; the surplus is attributed to nothing. It is capped to the remaining deficit only when created; after that its length is fixed, so a deficit that shrinks leaves surplus (Gap 95, decision 16). |
| 7 | Each entry's `MinutesCovered` = `Min(remaining block minutes, remaining deficit)`. |
| 8 | Where a redemption may go is `RedemptionPlacementPolicy`, not strictness: `AnywhereInContract` (default) or `CurrentDayOnly` — `NenContract.CheckRedemptionEligibility(proposedDate, today)`. A deficit stays claimable however long it has been outstanding (Gap 49). |
| 9 | *Retired — folded into Rule 8 (Gap 49).* |
| 10 | *Retired — folded into Rule 8 (Gap 49).* |
| 11 | A missed block keeps its outcome after being redeemed; Insights shows both. |
| 12 | A redemption block gets its own outcome, classified independently. |
| 13 | A missed redemption block creates no new deficit — no "redeem the redemption" chain (`FocusBlock.OwnDeficit`, Gaps 29, 95). |
| 14 | No cross-preset coverage; with several presets owing, Cover Deficit offers one tile per preset. |
| 15 | Every edit of a Redemption block recomputes its coverage from scratch, in its own contract — never its length (decision 16). |
| 16 | Splitting a redemption block asks attribution once for the whole span and divides the entries across the halves by duration — never copies them to both. Under review in Gap 106. |
| 17 | Cover Deficit sizes a preset's tile to its total remaining deficit and places it as described above. |
| 18 | A block counts in the ledger — as a deficit source or as coverage — only once its session has ended (`Session.HasEnded`); a late session still running counts as open. Not gated on Post-Session acknowledgement (Gap 34). |

## Technical mapping

| Layer | Artifact | Role |
|-------|----------|------|
| Domain | `NenContract` (`Entities/`) | `Name`, `Goal`, private `ContractCalendar`, `StartDate`/`EndDate`/`OriginalEndDate`, `Rules`, `CommittedFocusHours` (frozen at Seal; also the period target), `HasPeriodTarget`, `Status`, `ContractPresetId?`, `ChangeWindow?`, reflection + `PostContractAcknowledgedAt`. `Seal`, `Activate`, `Complete`, `Break`. Questions: `StartsAt`/`EndsAt`, `WindowOn`, `FloorHoursFor`, `DayStatuses`, `IsInEffect`, `IsDueForActivation`/`IsDueForCompletion`, `Governs`, `AllowsBlockPlacement`, `MembershipForPlacedBlock`, `FateWhenBroken`, `CheckComplete`, `CheckBreak`, `CheckRedemptionEligibility`, and the change answers `Binds`, `CheckBlockMove`, `CheckBlockLength`, `CheckBlockMayMove`, `AllowsBlockOnAnotherDay`, `CheckBlockPresetChange`, `CheckBlockSplit`, `CheckBlockDelete`, `CheckFloor` (Gaps 65, 93, 97). |
| Domain | `ContractDay`, `ContractCalendar`, `DailyFloorRule`, `DailyFloor`, `ContractRules`, `MovementRange`, `WorkHours`, `ChangeWindow`, `NenContractGoal`, `RedemptionEntry`, `ContractEndVerdict`, `ContractDayStatus` (`ValueObjects/`) | As described above. `ContractCalendar.Generate` is the one place Work Hours / rest days / planned hours become days; `SuggestedStart` bridges until Open Gap 66's start-time field. |
| Domain | `StrictnessRule`, `NenContractStatus`, `RedemptionPlacementPolicy`, `RedemptionEligibility` (`Allowed`/`WrongDay`), `FocusBlockOutcome`, `ContractRole`, `ContractEndOutcome` (`Allowed`/`SessionIsRunning`/`HoursNotDelivered`/`BlocksStillPending`, worded once in `ContractEndVerdictDto.From`) (`Enums/`) | — |
| Domain | `NenContractSealedEvent` / `ActivatedEvent` / `CompletedEvent` / `BrokenEvent` | Raised by the matching methods. |
| Domain | `ContractPreset` (`Entities/`) | Aggregate root; `Update` replaces every rule (none can be silently reset, Gap 64). |
| Domain | `PlannedBlock.Membership`, `PlaceUnder`, `Leave`; `FocusBlock.CountsTowardFloor` / `IsCommitted` / `IsRedemption` / `IsBonus`, `RedemptionEntries`, `AssignRedemptionEntries` | Membership and redemption facts on the block (Gaps 29, 56, 97). Guarded by `MembershipRuleTests`. |
| Domain | `PlannedBlock.IsFinal(asOf)` (virtual) | "Has this block's story concluded": a Background block once `HasEnded`; a Focus block once its session ended or it was missed — a running session is not final. Asked by completion readiness, `FateWhenBroken` and `OwnDeficit`. Replaced `PlannedBlockResolutionExtensions.IsResolved` (deleted, Gap 95). |
| Domain | `FocusBlock` answers (`Entities/`) | `GracePeriodMinutes`, `IsLateAt`, `StartedLate`, `EndedEarly`, `Outcome`, `WorkSlot`, `CreditedSlot`, `IsFinal`, `OwnDeficit` — outcome and credit from the block's own stamps; `MarkSessionEnded` refuses an end without, or before, the Ritual. Replaced `FocusBlockOutcomeClassifier` (deleted, Gap 95). |
| Domain | `IPlannedFocusHoursCalculator` (`Services/`) | Planned Focus hours per local day; a slot crossing midnight splits. Shared by the wizard preview, Seal, the change checker and the badges. |
| Domain | `IContractBlockBatchPreparer` (`Services/`) → `ContractBlockBatchPreparation` (`ValueObjects/`) | Seal/Edit: builds the batch's blocks (Committed), rejects Focus overlaps within the batch, sums planned hours per day. |
| Domain | `IBlockPlacementChecker`, `IBlockChangeChecker` (`Services/`) | See `DOMAIN_DICTIONARY.md` Quick Reference (Gaps 88, 93). |
| Application | `SealNenContract`, `EditNenContract`, `DeleteNenContract`, `BreakNenContract`, `CompleteNenContract`, `SweepNenContractLifecycle` (`UseCases/NenContracts/`) | Seal/Edit create the contract and its blocks in one commit, each block asked `BlockPlacementChecker.Check`; Edit reseals under the same id (Scheduled only). Delete (Scheduled only) removes drafted blocks and releases Bonus ones. Sweep activates/completes due contracts. |
| Application | `CreateContractPreset` / `UpdateContractPreset` / `DeleteContractPreset`, `GetAllContractPresetsQuery` | Name uniqueness in the handler; Delete refused while an in-effect contract uses the preset (`HasContractsInEffectForPresetAsync`). |
| Application | `INenContractRepository` | `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `SaveAsync`, `DeleteAsync`, `GetAllAsync`, `GetAllInEffectAsync`, `HasContractsInEffectForPresetAsync`, `GetUnacknowledgedPostContractsAsync`. |
| Application | `IPlannedBlockRepository.GetForContractAsync` | Every member, Bonus included; floor-measuring callers keep `CountsTowardFloor`. |
| Application | `ComputePlannedFocusHoursQuery` | Drafts only — the Contract Wizard's live planned hours → `IPlannedFocusHoursCalculator` (Gap 95). |
| Application | `GetNenContractDayStatusesQuery` → `ContractDayStatus` list; `GetNenContractDeficitsQuery` → `FocusBlockDeficitDto` | The days (`NenContract.DayStatuses`, loading its blocks and the sessions on its days), and the deficit ledger (Rules 1, 13, 18). Replaced the completed and Bonus hours queries (Gap 95). |
| Application | `ComputeRedemptionAttributionQuery` → `RedemptionAttributionResult` | What a block covers in one contract (Rules 4–8, 15). A query whose answer commands save — moves into the Domain in Gap 106. |
| Application | `GetSuggestedRedemptionPlacementsQuery` → `SuggestedRedemptionPlacementDto` | Cover Deficit placement; decides in the user's time zone, returns moments (Gap 87). |
| Application | `GetNenContractsForDateRangeQuery` → `NenContractRangeViewDto` | One entry per in-effect contract touching the range (Gap 55): the badges, days behind and banner totals all from `GetNenContractDayStatusesQuery` (a future day shows what it owes as planned), plus deficit and `Status` (Gap 95). |
| Application | `GetInEffectNenContractsQuery` → `NenContractSummaryDto` | The popover's Active card and Upcoming list (Gap 55). |
| Application | `GetNenContractCompletionReadinessQuery` | Loads the members, the sessions on the contract's days and whether a session runs, and returns `NenContract.CheckComplete`'s answer (completed hours from `DayStatuses`; `NenContractCompletionReadinessDto.From`); the Complete validator, the Complete handler and the button all read it (Gaps 59, 95). |
| Application | `GetNenContractRecapQuery` → `NenContractRecapDto` | Post-Contract recap: outcome counts from the blocks, and totals, day strip (`PercentMet`) and best streak from `GetNenContractDayStatusesQuery` (Gaps 59, 95). Insights recap: Open Gap 74. |
| Application | `GetNenContractForEditQuery` → `NenContractEditDto` | A Scheduled contract's rules, days and blocks for the wizard; `ContractEditPrefill` bridges until Open Gap 66. |
| Infrastructure | `NenContractRepository`, `ContractPresetRepository` | Days as one JSON column `Days` (local window strings so 24:00 survives); `Rules` flat columns on both tables (`MapContractRules`); `RedemptionEntries` JSON on `PlannedBlocks`; `ChangeWindow` ignored. |
| Infrastructure | `NenContractHeartbeatService` | 1-minute `BackgroundService` shell: runs the lifecycle sweep, then raises `PostContractsReady` for contracts it finished. Resolves handlers by hand — listed as an exception in `DispatcherRuleTests`. |
| UI | `ContractWizardViewModel`, Nen Contract popover, banner (`PlannerPageViewModel`), `ContractPresetPageViewModel`, `PostContractViewModel` | Wizard picks rules, drafts and seals; popover lists Active/Upcoming with Cancel/Edit/Delete/Make; banner shows totals, Cover Deficit and Complete; Post-Contract mirrors Post-Session (Gap 59). |
