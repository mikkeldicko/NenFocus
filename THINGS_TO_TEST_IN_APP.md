# Things to test in the app

Manual checks for work in progress — only what the automated tests can't prove (on-screen
wiring, a migration on the real database, timing against the real clock). Tick them off as you
go. When a gap closes, its passed checks are removed from this file; the gap's entry in
`DESIGN_GAP_HISTORY.md` records what was checked.

## Gap 89 Slice A — one heartbeat

- [ ] **The app still opens normally.** Startup now waits for the heartbeat's first tick before the window appears. Open it against your real database: the window appears as quickly as before. (The Post-Session screen at startup is covered by `ShellHeartbeatIntegrationTests`.)
- [ ] **Today's badge counts up during a session.** In a contract day with a Committed block, run its session for a few minutes: today's floor badge goes up within about a minute (it used to refresh every 3 s; now once a minute — up to a minute's lag is expected).

## Gap 89 Slice B — ViewModel audit

- [ ] **The dropdown opens where you are.** Focusing a time field opens its list scrolled to about five rows around the current time, with it highlighted. (Keys, Enter, Escape and the length labels are covered by `TimeOfDayBoxTests`.)
- [ ] **A wrong time's save message.** Type `abc` in Start, Tab away, click Create Block: it says "Invalid time format. Use HH:mm." (The red border appearing and clearing is covered.)
- [ ] **Buttons look the same everywhere (shared styles).** Planner form, presets pages, missed-sessions screen and the contract wizard: dark "Create"/"Save" buttons, grey-edged Cancel, solid red Delete. The wizard's Next/Seal buttons are styled now (they were plain grey before — its views never had the styles).
- [ ] **Presets' error box and field labels.** The presets forms' errors sit in a pale red box; field names ("Start", "Date", "Name") are small grey labels as before. (A refused Planner save's red line is covered.)
- [ ] **Hatsu's hour row still fits.** Hatsu shows hour chips 0–12 on one row at the normal window size. (Which lengths may start is covered by `RitualStepViewTests` and `StartSessionTests`.)
- [ ] **Dragging a draft tile with the mouse.** While drafting a contract, drag a draft tile to a new time: it moves. (Placing, editing, the move itself and Seal are covered by `ContractDraftingViewTests`.)

## Gap 112 — Infrastructure holds no rules (run as administrator)

- [ ] **Hosts file still written.** During a "Social Media Blocked" session, `C:\Windows\System32\drivers\etc\hosts` has the `# NEN_FOCUS_START` section with `0.0.0.0 youtube.com` and `0.0.0.0 www.youtube.com`; after the session it's gone.
- [ ] **A critical app is refused.** In Ren, pick `C:\Windows\explorer.exe` as an extra app and press Start → "'…explorer.exe' is a critical system process and cannot be blocked.", no session starts.
- [ ] **One app launch is one attempt.** In a session blocking Chrome (or Notepad), open it once → Post-Session counts 1, not one per process. Visit a blocked site twice a few seconds apart → it counts 2.
