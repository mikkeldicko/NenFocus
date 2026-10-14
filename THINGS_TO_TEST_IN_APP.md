# Things to test in the app

Manual checks for work in progress — only what the automated tests can't prove (on-screen
wiring, a migration on the real database, timing against the real clock). Tick them off as you
go. When a gap closes, its passed checks are removed from this file; the gap's entry in
`DESIGN_GAP_HISTORY.md` records what was checked.

## Gap 112 — Infrastructure holds no rules (run as administrator)

- [ ] **Hosts file still written.** During a "Social Media Blocked" session, `C:\Windows\System32\drivers\etc\hosts` has the `# NEN_FOCUS_START` section with `0.0.0.0 youtube.com` and `0.0.0.0 www.youtube.com`; after the session it's gone.
- [ ] **A critical app is refused.** In Ren, pick `C:\Windows\explorer.exe` as an extra app and press Start → "'…explorer.exe' is a critical system process and cannot be blocked.", no session starts.
- [ ] **One app launch is one attempt.** In a session blocking Chrome (or Notepad), open it once → Post-Session counts 1, not one per process. Visit a blocked site twice a few seconds apart → it counts 2.
