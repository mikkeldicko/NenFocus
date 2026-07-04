# NenFocus — Design Gap Closure History

A running record of gaps that were identified, interrogated, and closed. Each entry is a concrete demonstration of the Design Gap Protocol working correctly.

**This is reference material, not constitution.** It does not need to be read at the start of every session. Consult it when:
- A new gap looks similar to a previously-closed one — check whether the same principle applies.
- You need to understand *why* the codebase looks the way it does today (e.g. why `BlockedAttempt` is a fact table and not JSON on `Session`).
- You're inducting a new collaborator on how design decisions are made on this project.

For active design tension, see the **Open Gaps** section in `ARCHITECT_GOVERNANCE.md`.

---

## Gap 1 — Enforcement Gap on Session Start *(closed)*

**The gap:** `StartSessionCommandHandler` saved the session and updated UI state but never applied OS-level enforcement. `SessionStartedEventHandler` had been stripped of its enforcement call and only sent a notification. Sessions could start with no blocking in effect. The bug was silent — no test caught it because `IEnforcementService` was never injected into the handler.

**The closure:** Event-driven chain restored. `Session.Start()` raises `SessionStartedEvent` internally → handler dispatches via `PublishAllAsync(session.DomainEvents)` → `SessionStartedEventHandler` computes the current policy slice via `ComputeActivePolicySliceQuery` and calls `IEnforcementService.ApplyPolicyAsync`. `IEnforcementService` remains a simple two-method interface; the orchestration complexity lives in the event handler. Three new tests in `SessionStartedEventHandlerTests` now guard this path permanently.

**Principle demonstrated:** Deep Modules — `IEnforcementService` stayed narrow. The event handler became the "orchestrator" layer, not the command handler and not the infrastructure service.

---

## Gap 2 — BlockType Promotion: From Leaky Boolean to Domain Enum *(closed)*

**The gap:** The system could not distinguish between a Focus Block (which must trigger the Ritual) and a Background Block (which enforces silently) after computing a policy slice. A boolean `RequiresRitual` flag would have been the shallow solution — a boolean with no domain meaning that every caller must interpret differently.

**The closure:** `PolicySliceDto` now carries `ActiveBlockType (BlockType?)` — a domain enum from `NenFocus.Domain.Enums`. `ComputeActivePolicySliceQueryHandler` sets it to the type of the highest-priority active planned block (Ritual > Background). `ScheduledEnforcementService` reads this field and calls `SetRitualRequired(true)` only when `ActiveBlockType == Ritual` with no active session. The decision is expressed once, in the domain's language, in one place.

**Principle demonstrated:** Ubiquitous Language — the difference between a Focus Block and a Background Block was already a domain concept. Promoting it to the `PolicySliceDto` made the contract self-documenting and eliminated a class of future bugs where callers would have needed to re-derive the distinction themselves.

**Side fix included:** Blocklist mode in `ComputeActivePolicySliceQueryHandler` was previously broken — `BlockedSites` was always an empty set (missing `case WebsitePolicy.Blocklist:`). Fixed in the same slice. The browser extension was receiving an empty blocklist for all Blocklist-mode profiles.

---

## Gap 3 — Spontaneous Sessions, Insights Architecture, and the Block Hierarchy *(closed)*

**The gap:** The domain had no way to distinguish a planned Session from a manually-started one, making the planned Insights page impossible to build correctly. Additionally, the block hierarchy was being reconsidered — a proposed `InstantFocusBlock` class and a rename to Focus Block / Background Block were both under evaluation. The `BlockedAttempt` entity was also being considered for conversion to a Value Object.

**The interrogation revealed:**
- Insights requires a unified calendar view showing Focus Blocks (completed and missed), Background Blocks, and Spontaneous Sessions
- Insights requires analytics including session quality comparison between Planned and Spontaneous sessions and block profile follow-through rates
- Background Blocks must appear in Insights using scheduled time as canonical truth regardless of machine state
- A Focus Block where the user missed the Ritual must appear in Insights as "missed" — the `FocusBlock` row is the record
- A Spontaneous Session that did not complete the Ritual must not be saved anywhere
- The ONLY manual path to enforcement is through the full Nen Ritual — this is a product invariant, not a validation rule
- Browsing time tracking on all websites is a desired future feature requiring extension changes
- Planned Period / Strict Mode is a desired future feature

**The closure:**
- `InstantFocusBlock` rejected: a Spontaneous Session is not a Block. It has no calendar identity, no life outside its Session, and no state transitions. Creating an entity to make the schema look symmetric would violate DDD and fail the Deep Module test.
- `BlockedAttempt` kept as append-only fact table: burying it in JSON would make Insights analytics queries require full in-memory deserialisation across hundreds of sessions.
- `FocusBlock` renamed to `FocusBlock`, `PassiveBlock` renamed to `BackgroundBlock`, `BlockType` enum values updated to `Ritual` and `Background`. Rename is complete at every layer: entity, enum, DTO, handler, service, dictionary.
- `Session.Origin (SessionOrigin)` added: explicit `Spontaneous` / `Planned` tag. Does not rely on null-checking `OriginatingBlockId` so it remains unambiguous when future origin types are added.
- `Session.OriginatingBlockId (Guid?)` added: closes the known gap from `TECHNICAL_SPEC_INFRA.md`. Written by `StartSessionCommandHandler` when a Focus Block triggered the session.
- `FocusBlock.RitualCompletedAt (DateTime?)` added: allows Insights to detect missed blocks from the entity alone without a join to Session.
- `GetInsightsCalendarQuery` stubbed: single query handler that unifies three sources behind one interface. The ViewModel never touches the individual tables.

**Principle demonstrated:** The asymmetry between Planned and Spontaneous in the schema (Block + Session vs Session alone) is correct and intentional. It reflects a real asymmetry in the domain — a planned block exists before and independently of any Session; a spontaneous one does not. Hiding this asymmetry with a synthetic entity would be lying with code. `GetInsightsCalendarQuery` is the correct place to unify the three sources — complexity is hidden behind a narrow interface, not erased from the domain.

---

## Gap 4 — Domain Events Raised by Handlers, Not Entities; `BlockedAttemptsSerialized` Contradiction *(closed)*

**The gap:** Two DDD violations were found during a doc/code audit. First, domain events (`SessionStartedEvent`, `SessionEndedEvent`, `BreakStartedEvent`, `BlockProfileCreatedEvent`) were being constructed and published directly inside command handlers, not inside the entities and aggregates that own the state transitions. The governance states "domain events: raised inside entities/aggregates" — the code violated this. Second, `EndSessionCommandHandler` and `StartBreakCommandHandler` were calling `IEnforcementService` and `INotificationService` directly, bypassing the event handler chain the governance explicitly requires. Third, `Session.BlockedAttemptsSerialized` persisted blocked attempt data as a JSON blob on the Session row — contradicting the Gap 3 decision to keep `BlockedAttempt` as an append-only fact table.

**The closure:**
- `Session` and `BlockProfile` now own a private `_domainEvents` list. Each state-transition method (`Start()`, `StartBreak()`, `Complete()`) appends the appropriate event internally. Handlers call `entity.DomainEvents` and dispatch via `IDomainEventPublisher.PublishAllAsync`, then clear with `entity.ClearDomainEvents()`. The handler no longer knows which events a domain operation produces.
- `EndSessionCommandHandler` and `StartBreakCommandHandler` had their `IEnforcementService` and `INotificationService` injections removed. All enforcement and notification side-effects now flow exclusively through `SessionEndedEventHandler` and `BreakStartedEventHandler`. `BreakStartedEventHandler` also gained the missing `IEnforcementService.RemoveEnforcementAsync` call it lacked.
- `Session.BlockedAttemptsSerialized` and `Session.SetBlockedAttempts()` removed. `BlockedAttempt` rows (append-only, keyed by `SessionId`) are the sole persistence path for attempt data. Migration `20260512000001_RemoveBlockedAttemptsSerialized` drops the now-redundant `BlockedAttempts` column from the `Sessions` table.

**Principle demonstrated:** Deep Modules and the governance rule "commands do not directly call enforcement or notifications — event handlers do." The command handlers are now thin: validate → mutate domain → save → publish events → return result. All side-effects (enforcement, notifications) are encapsulated in event handlers with narrow, self-documenting interfaces.

---

## Gap 5 — Persistence Boundary: Per-Repository SaveChanges vs. Unit of Work *(closed)*

**The gap:** Repositories used to call `SaveChangesAsync` internally on each `SaveAsync`. `StartSessionCommandHandler` therefore performed two separate database round-trips when a Focus Block triggered a session: one to stamp `FocusBlock.RitualCompletedAt`, one to insert the new `Session` row. If the second save failed, the database would be left with a Focus Block marked completed but no corresponding Session — violating the invariant that a completed Focus Block always has an associated Planned Session. The same fragmentation pushed domain-event dispatching out to every handler, each of which had to remember to call `PublishAllAsync` and `ClearDomainEvents` in the right order relative to `SaveAsync` — a Deep Modules violation.

**The closure:** Repositories (`BlockProfileRepository`, `PlannedBlockRepository`, `SessionRepository`) no longer call `SaveChangesAsync` themselves — `SaveAsync`/`AddAsync`/`UpdateAsync` only register changes with the change tracker. `EfCoreUnitOfWork.CommitAsync()` is the sole place `SaveChangesAsync` is called: it walks the change tracker for `AggregateRoot` entries with pending `DomainEvents`, calls `SaveChangesAsync`, then publishes each aggregate's events via `IDomainEventPublisher.PublishAllAsync` and clears them. `TransactionalCommandHandler<TCommand, TResult>` wraps every command handler in DI and calls `_uow.CommitAsync()` after the inner handler returns — so a Planned Session's `FocusBlock.MarkRitualCompleted()` stamp and the new `Session` row now commit atomically in one `SaveChangesAsync` call, and no handler has to remember the "save, then publish, then clear" sequence itself.

**Confirmed via `NenFocus.Tests`:** `EfCoreUnitOfWorkTests` (persists pending changes, publishes only aggregates with pending events, clears events after publishing) and `TransactionalCommandHandlerTests` (inner handler always runs before `CommitAsync`, and `CommitAsync` still runs even when the inner handler returns a failure result).

**Principle demonstrated:** Deep Modules — the "save, then publish domain events, then clear them" rule now lives in exactly one place (`EfCoreUnitOfWork`) instead of being repeated in every command handler, and the atomicity invariant (all-or-nothing per command) is enforced structurally by the decorator rather than by convention.
