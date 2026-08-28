# PROJECT USAGE AUDIT

Audit date: 2026-08-27 (Asia/Shanghai)

## A. Baseline

- Branch: `feature/dreamscape-settings-hit-test-fix`
- HEAD: `30d48f8e056eb98e85e4965c452668ba5721d8cd`
- Commit: `Polish Dreamscape settings interactions and shared chrome`
- Release build: 0 warnings, 0 errors
- Tests: 915 passed, 0 failed, 0 skipped
- Initial tracked diff: empty
- Initial staging: empty
- Initial protected untracked count: 352

## B. Executive Summary

- P0: 0
- P1: 3
- P2: 6 (4 confirmed, 2 static risks)
- P3: 2 static/design risks
- Overall usability: **Needs fixes**

The normal Dreamscape path is usable and passed build, tests, five scale smokes, 50 navigation rounds, and log-cap verification. The first fixes should address: unauthenticated network input and the remote `task_manager` command; non-atomic Settings apply/save; single-instance restore/activation; non-atomic keyboard binding persistence; and stale Controller pressed state after disconnect/stop.

## C. Confirmed Bugs

### AUD-001 — P1 — Unauthenticated all-interface TCP input permits LAN control and process launch

- User impact: Any host that can reach TCP 8888 can replace the active phone session, inject DS4/keyboard actions, and send the fixed `task_manager` command. This permits denial of the legitimate controller session and OS input/process side effects without pairing or authentication.
- Reproduction: Connect to the Receiver host on port 8888 and send a protocol JSON line. Sending a second connection replaces the current client. Sending `{"command":"task_manager"}` reaches `Process.Start("taskmgr.exe")`.
- Expected: Pairing/authentication or an explicitly trusted transport boundary; bounded messages; no unauthenticated process-launch command.
- Actual: Listener binds `IPAddress.Any`; no handshake/authentication is checked; a new client disposes the previous client; command handling launches Task Manager.
- Evidence: `Ds4Service.cs` lines 277–315, 363–380, and 633–637. `ReadLineAsync` also has no maximum line length, so a client can retain/grow memory before a newline.
- Root cause: The phone protocol is treated as trusted despite binding to every interface.
- Files: `PcDs4Server/Ds4Service.cs`.
- Confidence: Confirmed (deterministic production path; process-launch side effect intentionally not executed).
- Fix scope: Medium.
- Regression risk: High around Android/TCP compatibility; requires a versioned pairing/authentication plan.
- Recommendation: Yes, fix first. Add authentication/session ownership, maximum frame length/timeouts, and remove or locally gate `task_manager`.

### AUD-002 — P1 — Radial Settings save failure leaves Runtime changed while Disk remains old

- User impact: A failed save is reported, but the new settings are already active in memory. The UI remains dirty even though Runtime equals the draft, and restart reverts to the old disk state.
- Reproduction: Use a store path that cannot be replaced, change scale 100→101, then call the real `RadialMenuSettingsPersistence.TryApplyAndSave`.
- Expected: Save succeeds before commit, or Runtime is rolled back when persistence fails.
- Actual: The call returns `false` with access denied while `controller.ActiveSettings.ScalePercent` is 101.
- Evidence: `RadialMenuSettingsStore.cs` lines 118–131 applies first and saves second. Isolated executable result: `persistence-failure.json`, `runtimeChangedDespiteFailure=true`.
- Root cause: Commit ordering is apply-then-save with no rollback/transaction.
- Files: `PcDs4Server/RadialMenuSettingsStore.cs`, `MainForm.DreamscapeSettings.cs`, `RadialMenuSettingsForm.cs`, `DreamscapeSettingsBasicSession.cs`.
- Confidence: Confirmed.
- Fix scope: Medium.
- Regression risk: Medium; preview/live-apply semantics must remain distinct from committed settings.
- Recommendation: Yes. Persist a validated immutable candidate atomically, then apply it, or snapshot and rollback Runtime on failure; make dirty state reflect the actual committed source.

### AUD-003 — P1 — Second instance cannot activate or restore the existing tray-hidden instance

- User impact: When Receiver is in the tray, launching it again leaves the original window hidden and opens a modal “already running” dialog in the second process. The second process stays alive until the dialog is dismissed.
- Reproduction: Start Receiver, Close→Tray, then launch Receiver again.
- Expected: Second process exits promptly after signalling the first instance to restore/activate.
- Actual: After two seconds the second process was still alive with title `LeftPad DS4 接收器`; the first process remained hidden. Closing the dialog then produced exit code 0.
- Evidence: Runtime PIDs 20560/27112; `Program.cs` lines 24–31 only acquire a mutex and show a MessageBox. There is no IPC/window activation path.
- Root cause: Mutex-only single-instance policy.
- Files: `PcDs4Server/Program.cs`, with restore endpoint in `MainForm.cs`.
- Confidence: Confirmed.
- Fix scope: Medium.
- Regression risk: Medium; global mutex, session boundaries, and foreground activation rules need care.
- Recommendation: Yes. Add a named-pipe/window-message activation channel and call the existing restore path in the first process.

### AUD-004 — P2 — Keyboard binding persistence changes Runtime before a throwing, non-atomic save

- User impact: Disk/access failures can throw through both Web and Native UI event paths. In-memory bindings have already changed, while disk may be old or partially rewritten. Corrupt binding JSON is silently replaced with defaults on load and can later be overwritten without a diagnostic.
- Reproduction: Inject an `IKeyboardBindingStore` whose `Save` throws `IOException`, then update Cross to Enter.
- Expected: Failure is returned/logged; Runtime remains unchanged; disk write is atomic.
- Actual: `IOException` escapes and Runtime Cross is already Enter.
- Evidence: `Ds4Service.cs` lines 210–219; `KeyboardBindings.cs` lines 78–113; `persistence-failure.json` records `runtimeChangedDespiteException=true`.
- Root cause: Assignment precedes save; save uses direct `File.WriteAllText`; load exceptions are swallowed without status.
- Files: `PcDs4Server/Ds4Service.cs`, `KeyboardBindings.cs`, `MainForm.cs`, `MainForm.Dreamscape.cs`.
- Confidence: Confirmed.
- Fix scope: Medium.
- Regression risk: Medium.
- Recommendation: Yes. Use temp+replace, expose structured load/save status, save before swapping Runtime state, and surface failures in both frontends.

### AUD-005 — P2 — Controller pressed indicators remain stuck after Stop/disconnect/session replacement

- User impact: The real output is safely released, but Controller can continue showing Triangle/Square/Cross/Circle as pressed until a later matching event arrives.
- Reproduction: Construct Native MainForm, send `triangle down`, then call `Ds4Service.Stop()`.
- Expected: Display dictionary and Web Controller state reset to all released.
- Actual: `triangle` remains `true` after Stop.
- Evidence: Isolated STA result in `persistence-failure.json`: `controllerDisplayStuckAfterStop=true`. `Ds4Service` resets output at lines 596–620 but publishes no button-state reset; `MainForm.cs` lines 546–565 only mutate `_btnStates` from button events.
- Root cause: Safety reset covers output state but not UI projection state.
- Files: `PcDs4Server/Ds4Service.cs`, `MainForm.cs`, `MainForm.DreamscapeController.cs`.
- Confidence: Confirmed.
- Fix scope: Small.
- Regression risk: Low.
- Recommendation: Yes. Publish an explicit reset snapshot/event on disconnect, replacement, stop, and output failure.

### AUD-006 — P2 — Native Logs clears the whole history instead of retaining the latest 300

- User impact: Under sustained logs, Native fallback loses far more history than Dreamscape. After 1000 messages it retained only lines 898–1000 (103 lines), not 701–1000.
- Reproduction: Append 1000 messages through `MainForm.AppendLog` in Native mode.
- Expected: Last 300 entries remain, matching ReceiverLogBuffer and Dreamscape DOM.
- Actual: 103 non-empty lines remain.
- Evidence: `MainForm.cs` lines 591–601 clear the entire RichTextBox when `Lines.Length > 300`. Isolated STA result in `persistence-failure.json`.
- Root cause: Whole-control clear instead of ring-buffer/snapshot rendering.
- Files: `PcDs4Server/MainForm.cs`.
- Confidence: Confirmed.
- Fix scope: Small/Medium.
- Regression risk: Low.
- Recommendation: Yes. Render the same 300-entry `ReceiverLogBuffer` snapshot or evict only the oldest line.

### AUD-007 — P2 — Hidden Overview and Settings continue one-second bridge polling

- User impact: Hidden WebViews continue JS timers, bridge traffic, C# state creation, JSON serialization, and DOM application for the full process lifetime. Settings includes mapping/catalog state and is more expensive than a small heartbeat.
- Reproduction: Keep Logs visible and sample hidden Overview/Settings diagnostics over 2250 ms.
- Expected: Hidden pages pause polling; state is pushed on visibility/re-entry.
- Actual: Both hidden pages sent three additional bridge messages; their documents reported `visibilityState=visible` despite hidden WinForms hosts.
- Evidence: `runtime-navigation.json`; Overview `app.js` line 143 and Settings `app.js` line 345. Controller/Logs correctly guard incremental pushes using host visibility.
- Root cause: Browser document visibility does not follow WinForms `UserControl.Visible`, and timers are unconditional.
- Files: `Assets/DreamscapeOverview/app.js`, `Assets/DreamscapeSettings/app.js`, `DreamscapeOverviewHost.cs`, `DreamscapeSettingsHost.cs`.
- Confidence: Confirmed.
- Fix scope: Small/Medium.
- Regression risk: Low.
- Recommendation: Yes, after correctness fixes. Push on activation/state change and suppress hidden requests/posts.

## D. Potential Risks

### RISK-001 — P2 — UI scale is not clamped to monitor working area

- Status: NOT CONFIRMED on this machine; STATIC RISK ONLY.
- Evidence: `ReceiverUiScaling.ApplyCore` scales baseline ClientSize directly and re-centers without clamping to `Screen.WorkingArea`. Current environment is one 3840×2160 display and passed all five presets.
- Impact: 175/200% may exceed a smaller display or become hard to recover after a DPI/monitor move.
- Fix scope: Medium. Clamp size/location and keep window controls reachable.

### RISK-002 — P2 — WebView initialization/disposal callbacks can race form disposal

- Status: NOT CONFIRMED; STATIC RISK ONLY.
- Evidence: Hosts use `async void InitializeAsync` and `async void NavigationCompleted`; failure lambdas call `BeginInvoke` without checking `IsDisposed/IsHandleCreated`; metrics are awaited before later host access.
- Impact: Closing during WebView environment creation/navigation could raise an unhandled UI callback exception or late fallback action.
- Fix scope: Medium. Use cancellable Tasks, disposal guards, and a single owned initialization lifetime.

### RISK-003 — P3 — Bridge validation is inconsistent and payloads are unbounded

- Status: NOT CONFIRMED as exploitable; static hardening gap.
- Evidence: Overview enforces exact properties. Controller and Logs accept any extra properties once `command` is allow-listed. Settings validates value types/ranges but does not enforce exact property sets for all commands. None sets a message-size limit or validates event source origin.
- Impact: Oversized local messages can waste memory/CPU; future command additions may inherit weaker validation assumptions.
- Fix scope: Small/Medium.

### RISK-004 — P3 — Large environment-triggered smoke harnesses ship in production code

- Status: Confirmed design risk, not a normal-path bug.
- Evidence: `MainForm.Dreamscape*.cs` contains report writers, SendKeys/input probes, screenshots, delays, navigation and a Settings smoke that applies/saves test settings then restores exact bytes.
- Impact: A stale environment variable can run invasive automation at normal startup; forced termination during the smoke can strand temporary runtime/disk state. It also expands MainForm lifecycle complexity.
- Fix scope: Large if moved to an external harness; regression risk Medium.

## E. Performance / Resource

50 full rounds were executed on one MainForm: Overview→Controller→Settings→Logs→Overview.

| Round | Host private | Host working set | Handles | GDI | USER | Threads | WebView2 processes |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 65,130,496 | 135,225,344 | 487 | 73 | 126 | 20 | 9 |
| 10 | 68,055,040 | 138,862,592 | 491 | 73 | 128 | 20 | 9 |
| 25 | 71,106,560 | 141,910,016 | 491 | 73 | 128 | 20 | 9 |
| 50 | 78,663,680 | 149,139,456 | 491 | 73 | 128 | 20 | 9 |

- WebView PID set stayed identical for all checkpoints.
- WebView aggregate private memory fluctuated 774 MB→835 MB→793 MB→779 MB, not linearly.
- Host memory grew about 13.5 MB during warm-up/navigation, but stable handles, GDI, USER, threads and process count do not indicate a resource leak in this run.
- Hidden-page CPU was not directly profiled. Background work was confirmed via bridge-message deltas (three requests/page in 2.25 seconds).
- Logs: C# 1000-entry run retained 701–1000; DOM 1000-entry run retained 701–1000 in 317.6 ms; markup remained text and created zero script nodes.

## F. Settings / Persistence

- Draft: Basic/Advanced/Mapping share one C# draft; Web UI does not own a second business state.
- Preview/Hide: covered and functioning; leaving Settings closes preview and discards unsaved draft.
- Apply/Save success: Runtime/disk/reload and both mapping profiles are covered.
- Apply/Save failure: broken transaction ordering (AUD-002).
- Reload/migration: malformed radial JSON returns defaults with status; missing/partial/legacy mappings and modifiers/DS4 actions are covered. Load does not automatically overwrite a bad radial file.
- Keyboard bindings: weaker store with silent load fallback and direct non-atomic save (AUD-004).
- MappingsByProfile: radial-6/radial-8 preservation and legacy migration covered.

## G. WebView2 Lifecycle

- Each Host is constructed once and guarded by `_initializationStarted`.
- Default mode initializes all four WebViews on first `Shown`, including hidden pages.
- Navigation loops do not create additional targets/processes.
- Host Dispose unsubscribes WebMessageReceived/NavigationCompleted; child WebView is disposed by control hierarchy.
- Native master runtime process tree contained one process and zero WebView2 children.
- Controller and Logs suppress hidden incremental pushes. Overview and Settings do not suppress timer-driven hidden requests.
- Failure fallback exists per page. Only Overview missing-assets fallback has a dedicated end-to-end host failure test; all four independent navigation/init failure transitions are not runtime-tested.

## H. Native Fallback

- Overview/Controller/Settings/Logs controls remain constructed in MainForm.
- `LEFTPAD_NATIVE_UI=1` runtime: responding, process-tree count 1, WebView2 count 0.
- Embedded Settings remains available and has no popup owner.
- Native Logs has the 300-cap bug (AUD-006).
- Native close/tray/exit entry points exist; a full automated 10-cycle Native tray run was not performed.

## I. Scaling / DPI

All five isolated Release smokes exited 0, produced reports, had one visible top-level window, passed minimize and close-to-tray, and reported no Settings mapping overflow.

- 100%: PASS on current display; client 1174×649 physical, CSS viewport 587×325.
- 125%: PASS; client 1468×811, viewport 734×406.
- 150%: PASS; client 1761×974, viewport 881×487.
- 175%: PASS; client 2081×1207, viewport 1041×604.
- 200%: PASS; client 2348×1298, viewport 1174×649.
- Per-monitor: NOT TESTED — STATIC RISK ONLY. Environment has one 3840×2160 display. PerMonitorV2 and `DpiChanged` handling exist, but no real monitor transition was available.

## J. Window / Tray / Single Instance

- Minimize: PASS in five scale smokes.
- Close-to-tray/restore: PASS once in each of five scale smokes.
- Ten cycles on one MainForm: NOT COMPLETED; no non-invasive external restore seam was available. This is a test gap, not reported as PASS.
- Top-level windows: 1 in every scale smoke.
- Single instance: FAIL; does not activate/restore the first instance (AUD-003).

## K. Input / DS4 / Radial Protection

- MOVE, DS4 release, Keyboard release, Radial execution, DoubleTap and Confirm/Cancel have substantial unit/integration coverage and no production changes were made.
- Safety resets release actual keyboard/DS4 state.
- Display-only Controller pressed projection is stale after reset (AUD-005).
- ViGEm unavailable/create/dispose paths: STATIC/TEST EVIDENCE ONLY; no destructive real failure injection was run.
- Port 8888 occupied: lifecycle unit tests prove cleanup/stopped/retry. The attempted real harness was inconclusive and is not counted as PASS.

## L. Bridge Security

- Overview: strongest parser; exact command properties and exact enums.
- Settings: allow-list and detailed range/profile/action validation; exact-property enforcement is incomplete.
- Controller: command allow-list only; extra properties accepted.
- Logs: command allow-list only; extra properties accepted.
- No JS bridge exposes arbitrary file, shell, or process APIs.
- Separate TCP protocol has the P1 unauthenticated input/process-launch issue (AUD-001).

## M. Test Coverage Gaps

- No test combines real apply callback with real save failure/rollback.
- No throwing keyboard binding store test.
- No MainForm display-state reset test after disconnect/stop/replacement.
- No Native RichTextBox 1000-log cap test.
- No single-instance IPC/hidden-tray activation test.
- No automated 50-round process/handle/thread regression test.
- No real multi-monitor DPI transition or small-working-area 200% test.
- No all-four-host init/navigation-failure fallback matrix.
- Many Dreamscape checks are source-string/static assertions (93 matching assertions in this audit); they do not replace real pointer, focus, WebView lifecycle or message delivery tests.
- Existing strength: 915 tests cover input state machines, safety release, mapping migration/round-trip, Settings draft semantics, layout geometry, command rejection, scaling math and host construction.

## N. Unreasonable Design

- Environment-triggered smoke automation is embedded in production MainForm partials and can mutate/restore user configuration.
- Four WebView Host classes duplicate environment creation, settings, mapping, subscriptions and disposal, increasing drift/race risk.
- MainForm owns service event projection, four frontend coordinators, persistence, tray lifecycle and extensive smoke logic; this concentration caused display-state reset to diverge from service safety reset.
- Four `CoreWebView2Environment` instances are created for one user-data folder. Runtime did share a stable process set, so this is maintenance/startup overhead rather than a demonstrated leak.

## O. Recommended Fix Order

1. Secure TCP 8888: authentication/session ownership, frame cap/timeouts, remove/gate `task_manager`.
2. Make radial Settings commit atomic and define rollback/dirty semantics.
3. Add single-instance activation IPC and restore the tray-hidden window.
4. Make keyboard binding load/save atomic, structured and non-throwing to UI.
5. Publish/reset Controller display state on every safety reset.
6. Make Native Logs consume the same 300-entry ring buffer.
7. Pause hidden Overview/Settings polling and push state on activation.
8. Clamp scaling to working area and add real per-monitor tests.
9. Harden bridge source/size/exact-property validation.
10. Move invasive smoke harnesses out of production MainForm code.

## P. Settings Protection

- Radial SHA before: `1D90E4DE44D9CCE03B365454381BFF25D1984F9E9E9744E88C8ADBF8924E4D27`
- Radial SHA after: `1D90E4DE44D9CCE03B365454381BFF25D1984F9E9E9744E88C8ADBF8924E4D27`
- Radial exact bytes: equal, 818 bytes.
- Keyboard bindings current SHA: `72B0494A52DD2009868D12A3F1946559041A92F6D42F3A30FE49C3ABEDAA1BB8`.
- Keyboard bindings bytes equal the available exact backup from 2026-08-26. Its LastWriteTime changed during Runtime, but content is byte-for-byte identical to that known backup. No binding mutation was sent by this audit.

## Q. Git

- Branch: `feature/dreamscape-settings-hit-test-fix`
- HEAD: `30d48f8e056eb98e85e4965c452668ba5721d8cd`
- Tracked diff: 0
- Staging: 0
- Protected untracked: preserved; final count 367, including 83 files under this new audit directory.
- No source/test/asset modification.
- No stage, commit, push, merge, rebase, reset, restore, stash or clean.
