# Things to test in the app

Manual checks for work in progress — only what the automated tests can't prove (on-screen
wiring, a migration on the real database, timing against the real clock). Tick them off as you
go. When a gap closes, its passed checks are removed from this file; the gap's entry in
`DESIGN_GAP_HISTORY.md` records what was checked.

## Gap 89 Slice A — one heartbeat

- [ ] **The app still opens normally.** Startup now waits for the heartbeat's first tick before the window appears. Open it against your real database: the window appears as quickly as before, and a session whose time ran out while it was closed shows its Post-Session screen.
- [ ] **Today's badge counts up during a session.** In a contract day with a Committed block, run its session for a few minutes: today's floor badge goes up within about a minute (it used to refresh every 3 s; now once a minute — up to a minute's lag is expected).
- [ ] **The debug clock's second-hand still ticks.** Open the debug overlay: the time counts up each second, and a jump (+1h) moves the Planner's now-line at once.

## Gap 89 Slice B — ViewModel audit

- [ ] **Typing a Start doesn't make End jump.** Open a block 13:00–14:00, click Start, type `10:30`: End stays 14:00 while you type; press Tab (or click elsewhere) and End becomes 11:30 at once.
- [ ] **Save works with one click straight after typing.** Type a new Start and click Create Block (or Save Changes) without leaving the field first: it saves the typed time on that one click (the dropdown closing must not swallow it).
- [ ] **The dropdowns look and move as before.** Focusing a time field opens its list scrolled to about five rows around the current time with it highlighted; Up/Down step through it; Enter on `17` gives 17:00; Enter on `10:07` keeps 10:07; Escape closes it; End's rows show "(30 mins)"-style lengths.
- [ ] **A wrong time shows red only after leaving.** Type `abc` in Start, Tab away: red border, Create Block says "Invalid time format. Use HH:mm."; click back in: the red goes.
- [ ] **Split's four fields work the same way.** Open Split on a bound block: the four fields prefill, End rows show lengths, changing a piece's Start keeps its length.
- [ ] **Contract Presets' Work Hours fields.** New preset with Work Hours 18:00–24:00 saves; reopening it shows 24:00 (it used to show 23:59).
- [ ] **Every screen still opens (new folder layout).** Click through Main, Planner, Presets (both tabs), Insights, Settings; run a Ritual to its end (Post-Session shows); open the Nen Contract wizard; open the debug clock. None shows "Not Found: …".
- [ ] **Draft tiles still show while drafting a contract.** Make a contract, place two blocks while drafting (one Focus, one Background): both tiles appear in the draft style with their preset names, can be dragged and edited, and Seal saves them. (Draft tiles are no longer built as fake block DTOs; no automated test reaches drafting.)

## Gap 112 — Infrastructure holds no rules (run as administrator)

- [ ] **Hosts file still written.** During a "Social Media Blocked" session, `C:\Windows\System32\drivers\etc\hosts` has the `# NEN_FOCUS_START` section with `0.0.0.0 youtube.com` and `0.0.0.0 www.youtube.com`; after the session it's gone.
- [ ] **A critical app is refused.** In Ren, pick `C:\Windows\explorer.exe` as an extra app and press Start → "'…explorer.exe' is a critical system process and cannot be blocked.", no session starts.
- [ ] **One app launch is one attempt.** In a session blocking Chrome (or Notepad), open it once → Post-Session counts 1, not one per process. Visit a blocked site twice a few seconds apart → it counts 2.
