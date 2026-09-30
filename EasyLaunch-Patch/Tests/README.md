# Push and redirect regression checks

## r8: Do not expose the previous push document (September 21)

- Supersedes r7's presentation rule for initial and push loads only. A separate
  opaque neutral cover blocks old pixels and interaction immediately on accepted
  routing, even while activation, permission UI or a camera modal delays loading.
  The same WKWebView, history and persistent data store are retained. Ordinary
  in-page navigation still has no loading cover.
- `didCommit` does not uncover a push. The active navigation must finish, then
  `takeSnapshot` with `afterScreenUpdates=YES` incorporates pending screen updates.
  Only the still-current route may remove the cover. The tiny snapshot is not
  stored or displayed. Late starts from superseded app loads are ignored too.
- The existing 75-second watchdog remains armed until push presentation completes;
  timeout/rendering failure shows the diagnostic error UI instead of a permanent
  spinner. This can wait longer for slow page resources than r7. No speedup of
  server responses or redirects is claimed, and no HTTP requests are prefetched.
- App/own-scene deactivation covers content before the background snapshot. On
  ordinary return a 350ms grace allows notification-response delivery before
  restoring the document without a reload. A pending push keeps the cover past
  that grace. This bounded window is not an iOS ordering guarantee: unusually
  late response delivery requires device verification. Native camera UI is not
  dismissed, covered with another controller, or navigated away from early.
- Tests cover commit without premature reveal, old callbacks, queued push
  replacement while inactive, foreground restoration without reload, a response
  during the activation grace, and a deadline after commit. WebView test waits
  now include actual push-cover removal rather than only network completion.
- Device acceptance still required: Hello -> 777 -> Hello, either push from lock
  screen/background/cold launch, rapid taps, slow/offline network, redirect tests,
  Skip -> Allow, and camera return with/without a pending push. Native tests must
  run on macOS; portable checks alone do not establish absence of visual flashes.
- Diagnostic/launch markers: `EASYLAUNCH DIAG r8`, `2026-09-21-r8-push-cover`.
  Notification images, payload selection, ATS and request timeouts are unchanged.

## CI scheduling tolerance (September 18)

The r7 simulator run compiled successfully and passed 39/40 native tests,
including both new navigation-display cases. The POST safety test failed on
the test helper's two-second `main queue drained` expectation, not a POST
replay assertion; the log has multi-second gaps around WebKit/UIKit cleanup.
The helper now allows up to 30 seconds (returning as soon as the queued block
runs), matching the existing WebView wait budget. No app timeout or recovery
policy changes. POST checks additionally assert unchanged load count, method
and body. A new macOS run is required to validate this test-harness adjustment.

## r7: Flutter-style navigation display (September 18)

- In the supplied Flutter archive, `isLoading` changes in page callbacks but is
  not rendered by `build`: the same `PlatformWebViewWidget` stays visible. Match
  that behavior instead of covering every navigation with a black loading view
  and uncovering it at `didCommit`, before a new frame is necessarily painted.
- New pushes call `loadRequest` on the existing WKWebView without a separate
  `stopLoading`. Old cancellation callbacks still cannot reload the old route.
- The overlay is errors-only. The 75-second no-commit guard, error diagnostics,
  safe manual retry and stale-navigation/recovery guards remain in place.
  On a first load the WebView may be blank until content arrives, as in Flutter;
  removing the overlay does not accelerate loading or eliminate iOS snapshots.
- Native regression cases cover initial/push/page navigation visibility,
  start/commit callbacks, stale completion/cancellation, clearing an old error,
  preserving the WebView instance and invalidating the watchdog after commit.
  Run them on macOS; Windows portable checks cannot validate UIKit rendering.
- iPhone acceptance: Hello -> tests; Hello -> 777 while loading and after load;
  cold launch from either push; Skip -> Allow -> both pushes; offline failure
  and retry. Check for flicker and correct final destination in each case.
- Diagnostic revision: `EASYLAUNCH DIAG r7`; launch marker:
  `2026-09-18-r7-flutter-navigation-ui`. URL selection, ATS, cookies, notification
  images and Firebase dependencies are unchanged from r6.

## r6: Flutter reference compatibility (September 18)

- Valid `url` fields (root, data, aps) now precede legacy `click_url` fields,
  matching the reference sender contract and superseding r3 below. Reports name
  the selected field, e.g. `field=root.url`, without dumping the payload.
- The main app's Info.plist explicitly enables
  `NSAppTransportSecurity.NSAllowsArbitraryLoadsInWebContent`, like the reference.
  This permits HTTP web content/redirects and requires App Store justification.
  No global ATS/media exception or certificate-authentication bypass is added.
  Existing domain exceptions remain authoritative. On modern iOS the web key
  also overrides an existing global `NSAllowsArbitraryLoads`; check separately
  configured non-web HTTP clients when integrating into another project.
- Initial/push GET requests use Foundation defaults: protocol cache policy and
  60-second timeout. Cookies/storage are preserved. The no-commit UI guard is
  75 seconds, not 45. Increasing this budget alone does not prove the bug fixed.
- `EASYLAUNCH DIAG r6` includes the selected field and the installed bundle's
  web ATS flag/domain override count. Launch marker: `2026-09-18-r6-flutter-parity`.
- Portable tests cover ATS merging, preservation, idempotency and invalid input.
  Export verification rejects a missing/disabled web policy before stamping the
  binary identity; the manifest records `web_ats_exception=true`. Portable tests
  also run in the Mac routing-test gate before XCTest.
  Native tests cover url/click_url conflicts, fallback compatibility, default
  request settings and cancellation without reloading an old page. The simulator
  host uses the same web ATS key. Its local HTTP fixture does NOT verify HTTPS
  downgrade or the production 777 URL: both still require iPhone acceptance.
- NotificationService, its embedding script and Firebase dependencies are unchanged.

## Running tests and earlier revision notes

Run on a Mac with Xcode, an installed iOS Simulator, Python 3 and the `xcodeproj` Ruby gem:

```sh
gem install xcodeproj --no-document
IOS_TEST_DESTINATION='platform=iOS Simulator,name=iPhone 16' bash EasyLaunch-Patch/Tests/run_ios_tests.sh
```

The script creates a separate test project under `build/routing-tests`; it does not modify the Unity export or use production Firebase credentials. The local HTTP fixture uses port 18765. An available iPhone simulator is selected automatically; optionally override it with `IOS_TEST_DESTINATION`. XCTest results are retained as `.xcresult`. The patched GitHub Actions build now runs this suite before TestFlight upload and saves `easylaunch-routing-tests`; test failures block upload.

The suite compiles the actual CustomAppController, PreloadViewController, NotificationPromptViewController, PLLaunchDiagnostics and WebViewController. Unity and the service wrapper are stubbed: it tests the UIKit/WebKit routing but cannot validate Firebase swizzling or real APNs delivery.

## r10: explicit registration ordering (current)

This revision changes behavior, not just diagnostics. Immediate Allow and Skip → Allow both register with APNs, asynchronously retrieve the current FCM token, and await a bounded token-bearing `config.php` request before handing off to WebView. A refresh event is no longer required for an unchanged token. The observer is installed before Firebase initialization; live token/project/platform fields override stored attribution in the update body. A cold push performs the same registration handshake using stored attribution, but never navigates to the config response URL or waits for a new AppsFlyer attribution request. Warm pushes in an already open WebView retain their direct route.

Settings opening no longer counts as consent completion. On returning, the controller reads actual OS permission and uses the same registration handshake only if authorized. If permission is still denied it records a fresh cooldown and continues without falsely registering consent.

The SDK wait is asynchronous and bounded at 6 seconds. The POST timeout is 8 seconds; an overall 15-second handoff deadline also covers a token rotation/missing SDK callback. On failure, the requested push URL still opens and a persisted pending flag allows a later retry; no old config/page URL is substituted. This means an unavailable registration service cannot create an indefinite new launch blocker. It can add up to 15 seconds before a cold WebView on failure; it does not shorten WebKit's existing request timeout. The journal/report marker is now `EASYLAUNCH DIAG r10` and the manual export gesture still works.

`PushRegistrationTests.m` controls SDK/HTTP boundaries while executing the production coordinator: unchanged-token submission, coalesced callers, request ordering, persisted failure/retry, missing/late callbacks, token rotation during fetch/POST, original deadline ownership, and body-field precedence. `PermissionFlowTests.m` now also asserts that both consent paths wait for the POST, Settings requires an actual return, and a newer cold push replaces the pending target without a second handoff. Routing tests wait for asynchronous registration completion instead of assuming it is synchronous.

The standalone **iOS routing and consent regression tests** Actions workflow runs on relevant pushes/PRs, or manually, without a Unity export, signing credentials or TestFlight upload. The existing full-build test gate remains. Simulator tests still stub Firebase/AppsFlyer; real APNs delivery, backend registration semantics and the reported production `-1001` cannot be proven by those stubs. The identified synchronization defects are addressed, but the link between those defects and the recorded timeout remains an inference, not a reproduced device diagnosis.

## r9: compare immediate Allow with Skip then Allow (historical diagnostic-only revision)

This revision adds observation and permission-flow tests, **not a confirmed fix for the production `-1001` timeout**. Both Allow scenarios already use the same permission handler. The networking sequence, cold-push bypass, request timeout, cookies and r8 old-page cover are unchanged. No automatic diagnostic requests or uploads are added; notification images are untouched.

`PermissionFlowTests.m` executes the production permission handler, substituting only the clock, OS calls and prompt presentation. It covers immediate Allow, an actual Skip followed by Allow exactly 72 hours later, bypass one second before that threshold, already-authorized states, OS denial, opening Settings without claiming authorization, and latest-push precedence during permission completion. The existing routing-only tests that substitute the whole permission method remain separate. Native tests also check the bounded journal, numeric storage, request correlation and read-only manual export. These require the macOS Actions simulator job; passing Python checks on Windows is not an iOS test pass.

Capture **both scenarios on the same r9 build**, using separate fresh test installations/devices for immediate Allow versus Skip if necessary. Do not reinstall between Skip and later Allow or between Allow and the cold-push check. Keep the network and push payload the same, and identify which scenario each report belongs to. Changing the device date can itself affect TLS/server behavior; prefer the real interval on-device (the exact threshold is simulated only in XCTest).

1. Allow immediately, wait for WebView, then copy a baseline report. Terminate the app normally, tap the test push and copy another report.
2. Skip, wait until the three-day cooldown expires, Allow, then repeat those two captures.
3. On a working page or loading cover, **hold three fingers for 1.5 seconds**, then tap **Copy diagnostics** in the `Diagnostics r9` dialog. The gesture does not reload the page or cancel ordinary page touches. If a native picker/modal is open, finish it first. On failure use the existing visible **Copy diagnostics** button before retrying.

The report starts with `EASYLAUNCH DIAG r9`. Alongside the existing per-route timeline it now includes up to 80 persisted startup observations with UTC timestamps, process IDs, numeric statuses and local operation numbers. It records permission decisions/skip age before the date is cleared, stored-token presence, APNs/FCM callbacks, observer readiness, config/token-sync requests and results, and the handoff to WebView. Request result `op` refers to the start event's `#` sequence. HTTP 200 is **not** evidence that the backend accepted/bound the token. The journal is bounded, can contain older runs/builds, and a missing event does not prove that an operation never occurred. It is best-effort preferences storage, not a crash-safe packet trace.

Startup records contain only fixed event IDs and numbers, never tokens, payloads, URLs, headers, bodies or error descriptions. Unlike the older in-memory navigation trace, this small startup journal persists locally across ordinary restarts; it rotates at 80 entries and is removed with app data. The existing URL redaction and explicit-copy-only behavior still apply. Review visible hosts/paths in the navigation section before sharing.

Coverage: a JavaScript button leading through 50 real HTTP 302s, query/cookie preservation, stale redirect and process recovery after a new push, no POST replay, repeated preload appearance, push replacement during permission completion, completion once only, APNs bytes forwarding, background/memory callbacks before Unity startup, and the missing Unity remote-notification superclass method.

Presentation regressions additionally exercise real UIKit modal dismissal with an inactive permission completion, readiness without a second delegate callback, Settings-return activation after the retry budget, a rejected presentation with no completion, ownership when another window is key, and replacement of a pending push without stale replay. OS activation and the permission response are simulated; these tests do not grant actual system permission.

The r3 regressions check explicit `click_url` precedence (root, then data, then aps), fallback to a valid `url` (same container order), whitespace, invalid values and image-only payloads. They also cover a push arriving during a full-screen native modal, selection exceeding the usual retry budget, latest-push replacement, and no document reload on return without a push. The modal is a UIKit stand-in, NOT an actual camera: successful image delivery to an HTML file input still requires an iPhone check. The actual 777 payload has not been supplied, so the parser change is not proof of the cause in the recording.

Device acceptance is still required on the newly built app:

1. Click the redirect button and reach `/final`; verify the skip button and Back.
2. Tap pushes A then B, in both orders and during loading. Check each clicked payload's URL, also when both pushes share the same URL.
3. Repeat from foreground, background and a terminated process.
4. Skip permission, expire the three-day cooldown, allow, then repeat the push checks.
   Also test a fresh install: Allow and Deny must both continue to WebView. With system permission previously denied, enable it in Settings, return to the app, and verify WebView opens. Capture the interval from the permission response to the visible page.
5. Switch to Unity mode and repeat background/foreground and push navigation.
6. On the file test, choose a camera image on the first attempt, cancel and retry, and choose an ordinary file. Repeat with a push received/tapped while a native picker is open. Verify no second WebView covers the picker; after it closes the latest requested destination should open in the original WebView.
7. Cold-start via 777, including offline and a connection dropped before any content appears. Loading must be visible; failure must show an error and safe manual retry. Retry must retain that push's URL, not the previous page/config URL. Then tap a newer push while an error/recovery is pending. Repeat on the actual production redirect URL: the local fixture cannot validate its server or JavaScript.

The r4 regressions cover notification responses before Unity/preload entry, latest early push transfer, interruption of config checks, stale startup callbacks, a push during the Unity fade, a real dropped first-load connection, same-target retry, stale failure/finish events, loading deadline ownership and POST/process-recovery safety. Permission completion ordering and the older redirect/modal regressions remain in the suite. The 45-second no-commit UI deadline is tested by invoking its production handler, not by waiting 45 seconds. This is not evidence that a valid page's own JavaScript cannot render black after content commits.

Identify the installed source by the launch log `routing revision 2026-09-17-r5-diag` and the build number. Presentation logs distinguish `WebView opening deferred`, `WebView still waiting` (URL retained), and `WebView destination delivered in owner window`. If the process still terminates, export the matching `.ips` (including Exception Type and Last Exception Backtrace/Triggered by Thread). A screen recording confirms the symptom but not the native exception or offending thread.

## On-screen diagnostic capture (r5)

This revision adds observation, NOT a confirmed fix for the production 777 timeout. It does not increase timeouts, clear cookies/storage, bypass TLS, request notification permission, or send diagnostic probes. The navigation-response observer preserves WebKit's default allow-if-displayable MIME policy.

1. Update to a build containing r5 with `apply_patch=true`; do not reinstall/clear app data just to gather evidence.
2. Close the app and tap 777. On failure, the report must start with `EASYLAUNCH DIAG r5`.
3. Tap **Copy diagnostics** and send the entire text. Alternatively scroll the diagnostic panel and capture all parts. Copy BEFORE pressing Try again; then optionally send the retry's report too.
4. Label whether permission was allowed immediately or after Skip + the three-day cooldown, and whether this was a cold launch. Capture each failing scenario separately.

The report includes the native-vs-UI error source, nested error domain/codes, last stage, elapsed time, observed load/redirect counts, original route URL, current request, last redirect, WebView URL and error URL; app/scene visibility, HTTP response/MIME when observed, permission status, saved skip age (if still stored), build/commit/fingerprint and the last 40 callback events. `not observed` is not proof that the server received no request. The timeline is bounded and is not a packet trace; TLS/DNS timings and HTTP redirect response codes are not available here. URLs/identities can confirm replacement by another address, but do not expose hidden query values for replay.

Privacy: only allowlisted diagnostic fields are collected locally in memory. No automatic upload. Copy writes the report only after an explicit tap. URL credentials, ALL query values, fragments, non-HTTP URL contents and selected sensitive/long path segments are hidden; a short SHA-256 URL identity allows comparison even when query values differ. Hostnames and ordinary path segments remain visible and must be reviewed before sharing. NSError descriptions/userInfo dumps, cookies, headers, bodies, tokens and notification payloads are not included. The frozen report is replaced on the next error, and a new route clears the previous timeline/report.

The added native tests cover URL/description redaction, identity comparison, bounded/reset timelines, UI-vs-WebKit timeout distinction, report copying and response-policy preservation. These are still unexecuted in this Windows workspace; the Mac Actions gate must compile/run them. Check the scroll/copy UI on a small iPhone in portrait and landscape during device acceptance.

These iOS tests have not been executed in the Windows workspace. Local checks must not be reported as a successful device run.

## Export identity checks

`patch.sh` verifies that every native source/header (except the generated credentials config) matches the export before reporting success. It writes `EasyLaunchSourceCommit` and `EasyLaunchPatchSHA256` into the app's Info.plist. Actions also saves `easylaunch-build.json` as the `easylaunch-build-identity` artifact. The launch log prints both values. An old branch without these changes cannot provide this evidence.

Run portable tests with:

```sh
python3 -B -m unittest discover -s EasyLaunch-Patch/Tests -p 'test_*.py' -v
```

## Evidence from the September 14 device log (reported build 1 (8))

- At log times 12:50:13, 12:50:18 and 12:50:35, Cluckstep PID 1100 reports main-frame navigation error `-1007` (log lines 8454, 8469, 8499).
- At 12:50:06, UIKit rejects presentation of WebViewController on a PreloadViewController whose view is not in the window hierarchy (line 8371).
- At 12:55:43 Cluckstep has PID 1125. At 12:55:45 the scene is invalidated and the process no longer exists (lines 24529, 24573). The recording shows the system crash dialog.
- The supplied log contains Error/Fault messages, not a symbolicated crash stack or the informational EasyLaunch version marker. It does not establish which exception terminated the application.
- At the initial investigation, `git ls-remote` found GitHub main at `352ffeeb18c527884cffa45a4507e3fdbf21cda5` (August 31), before the September 14 fixes were committed. The exact Actions run/commit behind build 1 (8) was not retrieved.
- Subsequently, the supplied `easylaunch-build.json` identified commit `b52c75d4f0e73a775dcbed25a586242b1df5cc4c` and native fingerprint `3f4ea68a1ed54a903dbffa8b2d13f1fbc527568a9601d54dfb1ce2798c8706d3`. All 14 listed native files matched the local r1 sources. This confirms export identity, not an on-device pass. The r2 presentation changes require a new export and fingerprint.

Before retesting, publish the prepared changes to the intended build branch, run Actions with `apply_patch=true`, and retain the build identity artifact. Real APNs and the three-day permission flow still require a device acceptance run; use the matching `.ips` if the new binary terminates.
