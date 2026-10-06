# Things to test in the app

Manual checks for work in progress — only what the automated tests can't prove (on-screen
wiring, a migration on the real database, timing against the real clock). Tick them off as you
go. When a gap closes, its passed checks are removed from this file; the gap's entry in
`DESIGN_GAP_HISTORY.md` records what was checked.

## Gap 95 — One Source for Focus Hours

The rules behind every item are covered by tests (block outcome and credit, the day rule, the
floor guard, the contract-end refusal); these check that the screen shows them.

- [ ] **Migration:** start the app on your real database. `DropContractFloor` applies without
      error, and an existing Period Study contract's banner still shows its total target.
- [ ] **Tile labels:** a Focus block whose Ritual came past grace and whose session you ended
      early shows both STARTED LATE and ENDED EARLY, and the tile is trimmed at both ends.
- [ ] **Live badge:** during a planned session, today's day badge counts up while it runs.
- [ ] **No ending mid-session:** with any session running, Cancel in the Nen Contract popover
      shows "A session is running — a contract can't be ended until it finishes.", and the
      Complete Contract button is hidden.
- [ ] **Redemption length:** drag the bottom edge of a Redemption tile. The drop is refused with
      "A redemption block keeps the length it was placed with — move or split it instead." and
      the tile snaps back. (The resize handle is still offered — known, see Gap 95 decision 16.)
- [ ] **Day badges:** a day with a redemption on it shows a progress badge, not "Rest"; a
      fixed-floor day planned above its floor shows the planned hours as its target.
