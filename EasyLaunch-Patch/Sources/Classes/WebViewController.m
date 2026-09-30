#import "WebViewController.h"
#import "WebViewConfig.h"
#import "PLLaunchDiagnostics.h"
#import "ScreenCaptureBlocker.h"
#import <WebKit/WebKit.h>
#import <UserNotifications/UserNotifications.h>
#import <CommonCrypto/CommonDigest.h>

// Leave time for the standard 60-second request; also bound push presentation.
static NSTimeInterval const PLWebLoadingDeadline = 75.0;

// Diagnostic text is deliberately built from allowlisted fields, never an
// NSError description/userInfo dump, request headers, cookies or push payload.
static NSString *PLDiagnosticAtom(NSString *text)
{
    if (![text isKindOfClass:NSString.class] || !text.length) return @"-";
    NSCharacterSet *allowed = [NSCharacterSet characterSetWithCharactersInString:
        @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-/: "];
    NSString *clean = [[text componentsSeparatedByCharactersInSet:allowed.invertedSet] componentsJoinedByString:@"_"];
    return clean.length > 160 ? [[clean substringToIndex:160] stringByAppendingString:@"…"] : clean;
}

static NSString *PLDiagnosticURL(NSURL *url)
{
    if (![url isKindOfClass:NSURL.class]) return @"-";
    NSData *bytes = [url.absoluteString dataUsingEncoding:NSUTF8StringEncoding];
    unsigned char digest[CC_SHA256_DIGEST_LENGTH];
    CC_SHA256(bytes.bytes, (CC_LONG)bytes.length, digest);
    NSMutableString *identity = [NSMutableString string];
    for (NSUInteger i = 0; i < 6; i++) [identity appendFormat:@"%02x", digest[i]];
    NSURLComponents *parts = [NSURLComponents componentsWithURL:url resolvingAgainstBaseURL:NO];
    NSString *scheme = parts.scheme.lowercaseString;
    if (!([scheme isEqualToString:@"http"] || [scheme isEqualToString:@"https"]) || !parts.host.length)
        return [NSString stringWithFormat:@"%@:<hidden> [id=%@]", PLDiagnosticAtom(scheme), identity];
    // User/password and fragment are NEVER emitted. Query values are ALL hidden.
    NSMutableArray *segments = [NSMutableArray array];
    NSCharacterSet *safePath = [NSCharacterSet characterSetWithCharactersInString:
        @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-"];
    NSSet *sensitive = [NSSet setWithArray:@[@"token", @"key", @"auth", @"session", @"password", @"code", @"user", @"email"]];
    BOOL hideNext = NO;
    for (NSString *segment in [parts.path componentsSeparatedByString:@"/"]) {
        BOOL hide = hideNext || segment.length > 32 || [segment rangeOfCharacterFromSet:safePath.invertedSet].location != NSNotFound;
        if (segment.length >= 8 && [segment rangeOfCharacterFromSet:NSCharacterSet.decimalDigitCharacterSet.invertedSet].location == NSNotFound) hide = YES;
        [segments addObject:hide ? @"<hidden>" : segment];
        hideNext = [sensitive containsObject:segment.lowercaseString];
    }
    NSString *path = [segments componentsJoinedByString:@"/"];
    if (path.length > 240) path = [[path substringToIndex:240] stringByAppendingString:@"…"];
    NSMutableArray *query = [NSMutableArray array];
    for (NSURLQueryItem *item in parts.queryItems) {
        if (query.count == 12) { [query addObject:@"…"]; break; }
        NSString *key = item.name.length <= 40 && [item.name rangeOfCharacterFromSet:safePath.invertedSet].location == NSNotFound ? item.name : @"<key>";
        [query addObject:[NSString stringWithFormat:@"%@=<hidden>", key]];
    }
    return [NSString stringWithFormat:@"%@://%@%@%@%@ [id=%@]", scheme, PLDiagnosticAtom(parts.host),
        parts.port ? [NSString stringWithFormat:@":%@", parts.port] : @"", path,
        query.count ? [@"?" stringByAppendingString:[query componentsJoinedByString:@"&"]] : @"", identity];
}

@interface WebViewController () <WKNavigationDelegate, WKUIDelegate, UIGestureRecognizerDelegate>
@property (nonatomic, strong) WKWebView *webView;
@property (nonatomic, strong) NSURL *url;

@property (nonatomic, assign) NSUInteger navigationGeneration;
@property (nonatomic, strong) WKNavigation *activeNavigation;
@property (nonatomic, copy) NSURLRequest *mainFrameRequest;
@property (nonatomic, strong) NSURL *lastServerRedirectURL;
@property (nonatomic, strong) NSMutableSet<NSString *> *resumedRedirectURLs;
@property (nonatomic, assign) NSUInteger processRecoveryCount;
@property (nonatomic, copy) NSURLRequest *retryRequest;
@property (nonatomic, strong) UIView *loadStatusView;
@property (nonatomic, strong) UILabel *loadStatusLabel;
@property (nonatomic, strong) UIButton *retryButton;
@property (nonatomic, assign) BOOL displayingLoadError;
@property (nonatomic, assign) NSUInteger loadStatusGeneration;
@property (nonatomic, strong) UITextView *diagnosticTextView;
@property (nonatomic, strong) UIButton *diagnosticCopyButton;
@property (nonatomic, copy) NSString *diagnosticReport;
@property (nonatomic, strong) NSMutableArray<NSString *> *diagnosticEvents;
@property (nonatomic, strong) NSURL *diagnosticRouteURL;
@property (nonatomic, copy) NSString *diagnosticStage;
@property (nonatomic, copy) NSString *diagnosticPermission;
@property (nonatomic, copy) NSString *diagnosticLastResponse;
@property (nonatomic, assign) NSTimeInterval diagnosticStartedAt;
@property (nonatomic, strong) NSDate *diagnosticStartedDate;
@property (nonatomic, assign) NSUInteger diagnosticLoadCount;
@property (nonatomic, assign) NSUInteger diagnosticRedirectCount;
@property (nonatomic, assign) BOOL diagnosticDidStart;
@property (nonatomic, assign) BOOL diagnosticDidCommit;
@property (nonatomic, strong) UIView *pushCoverView;
@property (nonatomic, assign) BOOL pushCoverPending;
@property (nonatomic, assign) BOOL awaitingPushNavigation;
@property (nonatomic, assign) BOOL backgroundCovered;
@property (nonatomic, assign) NSUInteger coverGeneration;
@property (nonatomic, assign) NSUInteger foregroundGeneration;
@property (nonatomic, strong) WKNavigation *finishedNavigation;
@property (nonatomic, strong) NSHashTable<WKNavigation *> *supersededNavigations;

@end

@implementation WebViewController

- (instancetype)initWithURL:(NSURL *)url
{
    self = [super initWithNibName:nil bundle:nil];
    if (self) {
        _url = url;
        _navigationGeneration = 1;
        _pushCoverPending = YES;
        _supersededNavigations = [NSHashTable weakObjectsHashTable];
        _resumedRedirectURLs = [NSMutableSet set];
        [self pl_beginDiagnosticRoute:url];
        self.modalPresentationStyle = UIModalPresentationFullScreen;
    }
    return self;
}

- (void)navigateToURL:(NSURL *)url
{
    if (!url) return;

    void (^navigate)(void) = ^{
        [self prepareForPushNavigation];
        self.navigationGeneration++;
        self.url = url;
        [self pl_beginDiagnosticRoute:url];
        if (!self.isViewLoaded || !self.webView) return;

        // Match Flutter's direct loadRequest on the existing WebView. The new
        // navigation replaces the old one; cancellation callbacks stay ignored.

        NSURLRequest *request = [NSURLRequest requestWithURL:url
                                                cachePolicy:NSURLRequestUseProtocolCachePolicy
                                            timeoutInterval:WebViewConfigNavigationTimeout];
        [self pl_loadRequest:request resetRedirects:YES];
    };

    if ([NSThread isMainThread]) navigate();
    else dispatch_async(dispatch_get_main_queue(), navigate);
}

- (void)viewDidLoad
{
    [super viewDidLoad];
    // Keep UI outside the web content black
    self.view.backgroundColor = [UIColor blackColor];

    WKWebViewConfiguration *cfg = [WKWebViewConfiguration new];
    // Allow inline media playback and enable autoplay where possible
    cfg.allowsInlineMediaPlayback = YES;
    if (@available(iOS 10.0, *)) {
        cfg.mediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypeNone;
    } else {
        cfg.requiresUserActionForMediaPlayback = NO;
    }

    self.webView = [[WKWebView alloc] initWithFrame:CGRectZero configuration:cfg];
    // Ensure any transparent parts show black background
    self.webView.backgroundColor = [UIColor clearColor];
    self.webView.opaque = NO;
    self.webView.scrollView.backgroundColor = [UIColor blackColor];
    self.webView.navigationDelegate = self;
    // Handle JS-initiated new windows (window.open / target="_blank")
    self.webView.UIDelegate = self;
    self.webView.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:self.webView];

    // Constrain webView to the view's safe area so content doesn't go under notch/status bar
    UILayoutGuide *safe = self.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [self.webView.topAnchor constraintEqualToAnchor:safe.topAnchor],
        [self.webView.bottomAnchor constraintEqualToAnchor:safe.bottomAnchor],
        [self.webView.leadingAnchor constraintEqualToAnchor:safe.leadingAnchor],
        [self.webView.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor]
    ]];
    [self pl_setupLoadStatus];
    [self pl_setupPushCover];
    // Hidden export for comparing a successful run with a failed run. It does
    // not cancel page touches, navigate, request permission or upload anything.
    UILongPressGestureRecognizer *diagnostics = [[UILongPressGestureRecognizer alloc] initWithTarget:self action:@selector(pl_showComparisonDiagnostics:)];
    diagnostics.numberOfTouchesRequired = 3;
    diagnostics.minimumPressDuration = 1.5;
    diagnostics.cancelsTouchesInView = NO;
    diagnostics.delaysTouchesBegan = NO;
    diagnostics.delaysTouchesEnded = NO;
    diagnostics.delegate = self;
    [self.view addGestureRecognizer:diagnostics];
    for (NSNotificationName name in @[UIApplicationDidBecomeActiveNotification,
                                      UIApplicationWillResignActiveNotification,
                                      UIApplicationDidEnterBackgroundNotification,
                                      UISceneWillDeactivateNotification,
                                      UISceneDidEnterBackgroundNotification,
                                      UISceneDidActivateNotification]) {
        [NSNotificationCenter.defaultCenter addObserver:self selector:@selector(pl_diagnosticLifecycle:) name:name object:nil];
    }

    // Hard-lock scroll view zoom scale so pinch-to-zoom is impossible
    self.webView.scrollView.minimumZoomScale = 1.0;
    self.webView.scrollView.maximumZoomScale = 1.0;
    // The page's viewport controls zoom; disabling the native pinch recognizer
    // is enough and does not interfere with taps or JavaScript navigation.
    if (self.webView.scrollView.pinchGestureRecognizer) {
        self.webView.scrollView.pinchGestureRecognizer.enabled = NO;
    }

    // Add left-edge pan gesture to navigate back in web view history
    UIScreenEdgePanGestureRecognizer *edgePan = [[UIScreenEdgePanGestureRecognizer alloc] initWithTarget:self action:@selector(handleEdgePan:)];
    edgePan.edges = UIRectEdgeLeft;
    edgePan.delegate = self;
    [self.view addGestureRecognizer:edgePan];

    // Force fullscreen modal presentation and prevent user dismissal (swipe down)
    if (@available(iOS 13.0, *)) {
        self.modalInPresentation = YES;
        if (self.navigationController) {
            self.navigationController.modalInPresentation = YES;
        }
    }

    if (self.url) {
        NSURLRequest *req = [NSURLRequest requestWithURL:self.url cachePolicy:NSURLRequestUseProtocolCachePolicy timeoutInterval:WebViewConfigNavigationTimeout];
        [self pl_loadRequest:req resetRedirects:YES];
    }
}

- (void)viewDidAppear:(BOOL)animated
{
    [super viewDidAppear:animated];
    [self pl_recordDiagnostic:@"viewDidAppear" URL:nil];
    [self pl_revealFinishedPush];
    // Применяем защиту от захвата экрана после того, как view добавлена в окно.
    // Метод CALayer-swap требует, чтобы view уже была в иерархии.
    // [ScreenCaptureBlocker applyProtectionToLayer:self.webView.layer];
}

- (void)onCloseTapped
{
    // Close action intentionally left empty — controller is non-dismissible.
}

#pragma mark - WKNavigationDelegate
- (void)pl_beginDiagnosticRoute:(NSURL *)url
{
    self.diagnosticRouteURL = url;
    self.diagnosticStartedAt = NSProcessInfo.processInfo.systemUptime;
    self.diagnosticStartedDate = NSDate.date;
    self.diagnosticEvents = [NSMutableArray array];
    self.diagnosticLoadCount = 0;
    self.diagnosticRedirectCount = 0;
    self.diagnosticReport = nil;
    self.diagnosticStage = @"route accepted; loadRequest not called yet";
    self.diagnosticLastResponse = @"not observed";
    self.diagnosticDidStart = NO;
    self.diagnosticDidCommit = NO;
    self.diagnosticPermission = @"pending";
    [self pl_recordDiagnostic:@"route accepted" URL:url];
    [PLLaunchDiagnostics record:PLLaunchEventWebRoute value:1];
    __weak typeof(self) weakSelf = self;
    NSDate *routeDate = self.diagnosticStartedDate;
    [UNUserNotificationCenter.currentNotificationCenter getNotificationSettingsWithCompletionHandler:^(UNNotificationSettings *settings) {
        dispatch_async(dispatch_get_main_queue(), ^{
            // Informational only: never asks for permission or influences routing.
            if (weakSelf.diagnosticStartedDate != routeDate) return;
            weakSelf.diagnosticPermission = [NSString stringWithFormat:@"%ld (0=notDetermined,1=denied,2=authorized,3=provisional,4=ephemeral)", (long)settings.authorizationStatus];
        });
    }];
}

- (void)pl_recordDiagnostic:(NSString *)event URL:(NSURL *)url
{
    NSString *entry = [NSString stringWithFormat:@"+%.2fs %@%@", NSProcessInfo.processInfo.systemUptime - self.diagnosticStartedAt,
        event, url ? [@" | " stringByAppendingString:PLDiagnosticURL(url)] : @""];
    [self.diagnosticEvents addObject:entry];
    if (self.diagnosticEvents.count > 40) [self.diagnosticEvents removeObjectAtIndex:0];
}

- (void)pl_diagnosticLifecycle:(NSNotification *)notification
{
    BOOL sceneEvent = [notification.name isEqualToString:UISceneWillDeactivateNotification] ||
        [notification.name isEqualToString:UISceneDidEnterBackgroundNotification] ||
        [notification.name isEqualToString:UISceneDidActivateNotification];
    if (sceneEvent && notification.object != self.viewIfLoaded.window.windowScene) return;
    [self pl_recordDiagnostic:notification.name URL:nil];
    if ([notification.name isEqualToString:UIApplicationWillResignActiveNotification] ||
        [notification.name isEqualToString:UIApplicationDidEnterBackgroundNotification] ||
        [notification.name isEqualToString:UISceneWillDeactivateNotification] ||
        [notification.name isEqualToString:UISceneDidEnterBackgroundNotification]) {
        self.foregroundGeneration++;
        self.backgroundCovered = YES;
        [self pl_updatePushCover];
    } else if ([notification.name isEqualToString:UIApplicationDidBecomeActiveNotification] ||
               [notification.name isEqualToString:UISceneDidActivateNotification]) {
        NSUInteger generation = ++self.foregroundGeneration;
        // iOS can deliver the notification response just after activation.
        // Keep the neutral background snapshot through that handoff. A normal
        // return restores the same document, never reloads a form or camera page.
        __weak typeof(self) weakSelf = self;
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.35 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
            if (!weakSelf || generation != weakSelf.foregroundGeneration) return;
            weakSelf.backgroundCovered = NO;
            [weakSelf pl_updatePushCover];
            [weakSelf pl_revealFinishedPush];
        });
    }
}

- (void)prepareForPushNavigation
{
    NSAssert(NSThread.isMainThread, @"Push presentation must run on the main thread");
    self.coverGeneration++;
    self.pushCoverPending = YES;
    self.awaitingPushNavigation = YES;
    self.finishedNavigation = nil;
    [self pl_updatePushCover]; // Do not force view loading while a modal owns it.
}

- (void)pl_setupPushCover
{
    self.pushCoverView = [UIView new];
    self.pushCoverView.backgroundColor = [UIColor colorWithRed:0.055 green:0.075 blue:0.11 alpha:1];
    self.pushCoverView.accessibilityIdentifier = @"web-push-transition";
    self.pushCoverView.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:self.pushCoverView];
    UIActivityIndicatorView *spinner = [[UIActivityIndicatorView alloc] initWithActivityIndicatorStyle:UIActivityIndicatorViewStyleLarge];
    spinner.color = UIColor.whiteColor;
    [spinner startAnimating];
    UILabel *label = [UILabel new];
    label.text = @"Loading…";
    label.textColor = UIColor.whiteColor;
    UIStackView *stack = [[UIStackView alloc] initWithArrangedSubviews:@[spinner, label]];
    stack.axis = UILayoutConstraintAxisVertical;
    stack.alignment = UIStackViewAlignmentCenter;
    stack.spacing = 16;
    stack.translatesAutoresizingMaskIntoConstraints = NO;
    [self.pushCoverView addSubview:stack];
    [NSLayoutConstraint activateConstraints:@[
        [self.pushCoverView.topAnchor constraintEqualToAnchor:self.view.topAnchor],
        [self.pushCoverView.bottomAnchor constraintEqualToAnchor:self.view.bottomAnchor],
        [self.pushCoverView.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [self.pushCoverView.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [stack.centerXAnchor constraintEqualToAnchor:self.pushCoverView.centerXAnchor],
        [stack.centerYAnchor constraintEqualToAnchor:self.pushCoverView.centerYAnchor]
    ]];
    [self pl_updatePushCover];
}

- (void)pl_updatePushCover
{
    BOOL covered = self.backgroundCovered || self.awaitingPushNavigation ||
        (self.pushCoverPending && !self.displayingLoadError);
    self.pushCoverView.hidden = !covered;
    self.webView.userInteractionEnabled = !covered;
    self.webView.accessibilityElementsHidden = covered;
}

- (void)pl_revealFinishedPush
{
    if (!self.pushCoverPending || self.awaitingPushNavigation || self.backgroundCovered ||
        self.displayingLoadError || !self.finishedNavigation ||
        self.finishedNavigation != self.activeNavigation || self.webView.loading ||
        !self.webView.window || CGRectIsEmpty(self.webView.bounds)) return;
    NSUInteger generation = self.coverGeneration;
    NSUInteger navigationGeneration = self.navigationGeneration;
    WKNavigation *navigation = self.activeNavigation;
    WKSnapshotConfiguration *configuration = [WKSnapshotConfiguration new];
    configuration.afterScreenUpdates = YES;
    configuration.snapshotWidth = @32; // Rendering barrier only; never store/share the image.
    __weak typeof(self) weakSelf = self;
    [self.webView takeSnapshotWithConfiguration:configuration completionHandler:^(UIImage *image, NSError *error) {
        [weakSelf pl_finishPushReveal:navigation coverGeneration:generation navigationGeneration:navigationGeneration
                              error:error ?: (image ? nil : [NSError errorWithDomain:WKErrorDomain code:WKErrorUnknown userInfo:nil])];
    }];
}

- (void)pl_finishPushReveal:(WKNavigation *)navigation coverGeneration:(NSUInteger)generation
      navigationGeneration:(NSUInteger)navigationGeneration error:(NSError *)error
{
    if (generation != self.coverGeneration || navigationGeneration != self.navigationGeneration ||
        navigation != self.activeNavigation || navigation != self.finishedNavigation ||
        !self.pushCoverPending || self.awaitingPushNavigation || self.backgroundCovered ||
        self.displayingLoadError || self.webView.loading) return;
    if (error) {
        // Fail closed: never expose the old frame on a rendering failure.
        [self pl_showLoadError:error source:@"push display update failed"];
        return;
    }
    self.pushCoverPending = NO;
    [self pl_hideLoadStatus];
    [self pl_updatePushCover];
    [self pl_recordDiagnostic:@"push document revealed after screen update" URL:nil];
}

- (void)dealloc
{
    [NSNotificationCenter.defaultCenter removeObserver:self];
}

- (NSString *)pl_diagnosticReportForError:(NSError *)error source:(NSString *)source
{
    NSURL *failedURL = error.userInfo[NSURLErrorFailingURLErrorKey];
    if (![failedURL isKindOfClass:NSURL.class]) {
        id text = error.userInfo[NSURLErrorFailingURLStringErrorKey];
        failedURL = [text isKindOfClass:NSString.class] ? [NSURL URLWithString:text] : nil;
    }
    NSMutableArray *causes = [NSMutableArray array];
    NSError *cause = error;
    for (NSUInteger i = 0; i < 4 && [cause isKindOfClass:NSError.class]; i++) {
        [causes addObject:[NSString stringWithFormat:@"%@ %ld", PLDiagnosticAtom(cause.domain), (long)cause.code]];
        cause = cause.userInfo[NSUnderlyingErrorKey];
    }
    NSDate *denied = [NSUserDefaults.standardUserDefaults objectForKey:@"PLLastNotificationDeniedAt"];
    NSString *skipAge = [denied isKindOfClass:NSDate.class] ? [NSString stringWithFormat:@"%.1f hours", -denied.timeIntervalSinceNow / 3600.0] : @"not stored (may have been cleared on Allow)";
    UIWindow *window = self.viewIfLoaded.window;
    NSBundle *bundle = NSBundle.mainBundle;
    NSDictionary *ats = [bundle objectForInfoDictionaryKey:@"NSAppTransportSecurity"];
    id webException = [ats isKindOfClass:NSDictionary.class] ? ats[@"NSAllowsArbitraryLoadsInWebContent"] : nil;
    id domainExceptions = [ats isKindOfClass:NSDictionary.class] ? ats[@"NSExceptionDomains"] : nil;
    NSString *atsSummary = [NSString stringWithFormat:@"Web ATS exception=%@; domain overrides=%lu",
        [webException respondsToSelector:@selector(boolValue)] && [webException boolValue] ? @"yes" : @"no",
        (unsigned long)([domainExceptions isKindOfClass:NSDictionary.class] ? [domainExceptions count] : 0)];
    return [NSString stringWithFormat:
        @"EASYLAUNCH DIAG r10\nSource: %@\nError: %@\nStage: %@\nElapsed: %.2fs; loads=%lu; redirects=%lu\n"
        @"Started=%@; committed=%@\nLast response: %@\n\nRoute URL (initial/push): %@\nCurrent request: %@\nLast redirect: %@\nWebView URL: %@\nFailing URL: %@\n\n"
        @"Routing: %@\nMethod: %@; request timeout=%.0fs; UI deadline=%.0fs\nApp state=%ld (0=active,1=inactive,2=background); scene=%ld\nAttached=%@; visible=%@; loading=%@; progress=%.2f\n"
        @"Notifications: %@\nLast skip: %@\nSaved launch mode: %@\nData store: %@; custom UA: %@\n%@\n"
        @"App %@ (%@); iOS %@\nCommit: %@\nPatch: %@\nStarted UTC: %@\n\nLast 40 events (observed callbacks):\n%@\n\n"
        @"%@\n"
        @"Privacy: credentials, query values and fragments hidden; selected path segments masked. Review host/path before sharing. No cookies, headers, request bodies or push payload included. URL id compares exact URLs, including hidden values.\n",
        source, causes.count ? [causes componentsJoinedByString:@" <- "] : @"none (manual snapshot)", self.diagnosticStage,
        NSProcessInfo.processInfo.systemUptime - self.diagnosticStartedAt,
        (unsigned long)self.diagnosticLoadCount, (unsigned long)self.diagnosticRedirectCount,
        self.diagnosticDidStart ? @"yes" : @"no", self.diagnosticDidCommit ? @"yes" : @"no", self.diagnosticLastResponse,
        PLDiagnosticURL(self.diagnosticRouteURL), PLDiagnosticURL(self.mainFrameRequest.URL), PLDiagnosticURL(self.lastServerRedirectURL),
        PLDiagnosticURL(self.webView.URL), PLDiagnosticURL(failedURL), self.diagnosticContext ?: @"not supplied",
        PLDiagnosticAtom(self.mainFrameRequest.HTTPMethod ?: @"GET"), self.mainFrameRequest.timeoutInterval, PLWebLoadingDeadline,
        (long)UIApplication.sharedApplication.applicationState, window.windowScene ? (long)window.windowScene.activationState : -99L,
        window ? @"yes" : @"no", window && !window.hidden && window.alpha > 0 ? @"yes" : @"no",
        self.webView.loading ? @"yes" : @"no", self.webView.estimatedProgress,
        self.diagnosticPermission, skipAge, PLDiagnosticAtom([NSUserDefaults.standardUserDefaults stringForKey:@"PLLaunchMode"]),
        self.webView.configuration.websiteDataStore.persistent ? @"persistent" : @"non-persistent", self.webView.customUserAgent.length ? @"set (hidden)" : @"default", atsSummary,
        PLDiagnosticAtom([bundle objectForInfoDictionaryKey:@"CFBundleShortVersionString"]), PLDiagnosticAtom([bundle objectForInfoDictionaryKey:@"CFBundleVersion"]),
        PLDiagnosticAtom(UIDevice.currentDevice.systemVersion), PLDiagnosticAtom([bundle objectForInfoDictionaryKey:@"EasyLaunchSourceCommit"]),
        PLDiagnosticAtom([bundle objectForInfoDictionaryKey:@"EasyLaunchPatchSHA256"]), self.diagnosticStartedDate,
        [self.diagnosticEvents componentsJoinedByString:@"\n"], [PLLaunchDiagnostics report]];
}

- (NSString *)pl_comparisonReport
{
    return self.displayingLoadError && self.diagnosticReport.length ? self.diagnosticReport :
        [self pl_diagnosticReportForError:nil source:@"manual comparison snapshot"];
}

- (void)pl_showComparisonDiagnostics:(UILongPressGestureRecognizer *)gesture
{
    if (gesture.state != UIGestureRecognizerStateBegan || self.presentedViewController || !self.viewIfLoaded.window) return;
    NSString *report = [self pl_comparisonReport];
    UIAlertController *alert = [UIAlertController alertControllerWithTitle:@"Diagnostics r10"
        message:@"Copy a local report for comparison. No automatic upload. Review host/path before sharing."
        preferredStyle:UIAlertControllerStyleAlert];
    [alert addAction:[UIAlertAction actionWithTitle:@"Copy diagnostics" style:UIAlertActionStyleDefault handler:^(UIAlertAction *action) {
        UIPasteboard.generalPasteboard.string = report;
    }]];
    [alert addAction:[UIAlertAction actionWithTitle:@"Cancel" style:UIAlertActionStyleCancel handler:nil]];
    [self presentViewController:alert animated:YES completion:nil];
}

- (void)pl_copyDiagnostics
{
    if (!self.diagnosticReport.length || !self.displayingLoadError) return;
    // Only an explicit tap writes the clipboard; never copy the raw error/URL.
    UIPasteboard.generalPasteboard.string = self.diagnosticReport;
    [self.diagnosticCopyButton setTitle:@"Copied — send this report" forState:UIControlStateNormal];
}

- (void)pl_setupLoadStatus
{
    self.loadStatusView = [UIView new];
    self.loadStatusView.backgroundColor = UIColor.blackColor;
    self.loadStatusView.translatesAutoresizingMaskIntoConstraints = NO;
    self.loadStatusView.accessibilityIdentifier = @"web-load-status";
    self.loadStatusView.hidden = YES;
    [self.view addSubview:self.loadStatusView];
    self.loadStatusLabel = [UILabel new];
    self.loadStatusLabel.textColor = UIColor.whiteColor;
    self.loadStatusLabel.textAlignment = NSTextAlignmentCenter;
    self.loadStatusLabel.numberOfLines = 0;
    self.retryButton = [UIButton buttonWithType:UIButtonTypeSystem];
    [self.retryButton setTitle:@"Try again" forState:UIControlStateNormal];
    self.retryButton.accessibilityIdentifier = @"web-retry";
    [self.retryButton addTarget:self action:@selector(pl_retryLoading) forControlEvents:UIControlEventTouchUpInside];
    self.diagnosticTextView = [UITextView new];
    self.diagnosticTextView.editable = NO;
    self.diagnosticTextView.selectable = YES;
    self.diagnosticTextView.dataDetectorTypes = UIDataDetectorTypeNone;
    self.diagnosticTextView.font = [UIFont monospacedSystemFontOfSize:12 weight:UIFontWeightRegular];
    self.diagnosticTextView.textColor = UIColor.whiteColor;
    self.diagnosticTextView.backgroundColor = [UIColor colorWithWhite:0.10 alpha:1];
    self.diagnosticTextView.accessibilityIdentifier = @"web-diagnostic-report";
    self.diagnosticTextView.hidden = YES;
    self.diagnosticCopyButton = [UIButton buttonWithType:UIButtonTypeSystem];
    [self.diagnosticCopyButton setTitle:@"Copy diagnostics" forState:UIControlStateNormal];
    [self.diagnosticCopyButton addTarget:self action:@selector(pl_copyDiagnostics) forControlEvents:UIControlEventTouchUpInside];
    self.diagnosticCopyButton.accessibilityIdentifier = @"web-copy-diagnostics";
    self.diagnosticCopyButton.hidden = YES;
    UIStackView *stack = [[UIStackView alloc] initWithArrangedSubviews:@[self.loadStatusLabel, self.diagnosticTextView, self.diagnosticCopyButton, self.retryButton]];
    stack.axis = UILayoutConstraintAxisVertical;
    stack.spacing = 12;
    stack.translatesAutoresizingMaskIntoConstraints = NO;
    UIScrollView *scroll = [UIScrollView new];
    scroll.translatesAutoresizingMaskIntoConstraints = NO;
    [self.loadStatusView addSubview:scroll];
    UIView *content = [UIView new];
    content.translatesAutoresizingMaskIntoConstraints = NO;
    [scroll addSubview:content];
    [content addSubview:stack];
    NSLayoutConstraint *preferredContentHeight = [content.heightAnchor constraintEqualToAnchor:scroll.frameLayoutGuide.heightAnchor];
    preferredContentHeight.priority = UILayoutPriorityDefaultLow;
    preferredContentHeight.active = YES;
    NSLayoutConstraint *reportHeight = [self.diagnosticTextView.heightAnchor constraintEqualToAnchor:self.loadStatusView.heightAnchor multiplier:0.50];
    reportHeight.priority = UILayoutPriorityDefaultHigh; // can collapse when hidden
    reportHeight.active = YES;
    UILayoutGuide *safe = self.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [self.loadStatusView.topAnchor constraintEqualToAnchor:safe.topAnchor],
        [self.loadStatusView.bottomAnchor constraintEqualToAnchor:safe.bottomAnchor],
        [self.loadStatusView.leadingAnchor constraintEqualToAnchor:safe.leadingAnchor],
        [self.loadStatusView.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor],
        [scroll.topAnchor constraintEqualToAnchor:self.loadStatusView.topAnchor],
        [scroll.bottomAnchor constraintEqualToAnchor:self.loadStatusView.bottomAnchor],
        [scroll.leadingAnchor constraintEqualToAnchor:self.loadStatusView.leadingAnchor],
        [scroll.trailingAnchor constraintEqualToAnchor:self.loadStatusView.trailingAnchor],
        [content.topAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.topAnchor],
        [content.bottomAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.bottomAnchor],
        [content.leadingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.leadingAnchor],
        [content.trailingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.trailingAnchor],
        [content.widthAnchor constraintEqualToAnchor:scroll.frameLayoutGuide.widthAnchor],
        [content.heightAnchor constraintGreaterThanOrEqualToAnchor:scroll.frameLayoutGuide.heightAnchor],
        [stack.centerYAnchor constraintEqualToAnchor:content.centerYAnchor],
        [stack.topAnchor constraintGreaterThanOrEqualToAnchor:content.topAnchor constant:16],
        [stack.bottomAnchor constraintLessThanOrEqualToAnchor:content.bottomAnchor constant:-16],
        [stack.leadingAnchor constraintEqualToAnchor:content.leadingAnchor constant:16],
        [stack.trailingAnchor constraintEqualToAnchor:content.trailingAnchor constant:-16]
    ]];
}

- (BOOL)pl_isSafeRequest:(NSURLRequest *)request
{
    NSString *method = request.HTTPMethod ?: @"GET";
    NSString *scheme = request.URL.scheme.lowercaseString;
    return request.URL.host.length && ([scheme isEqualToString:@"http"] || [scheme isEqualToString:@"https"]) &&
        ([method isEqualToString:@"GET"] || [method isEqualToString:@"HEAD"]);
}

- (void)pl_beginLoading
{
    self.displayingLoadError = NO;
    // Ordinary in-page navigation remains visible, like Flutter. This status
    // view is errors-only; the separate push cover has its own reveal guard.
    self.loadStatusView.hidden = YES;
    self.retryButton.hidden = YES;
    self.diagnosticTextView.hidden = YES;
    self.diagnosticCopyButton.hidden = YES;
    [self pl_updatePushCover];
    NSUInteger statusGeneration = ++self.loadStatusGeneration;
    // A cancelled/never-committed first navigation must not leave a blank screen.
    // This is a UI deadline, not an automatic reload or a TLS/ATS bypass.
    __weak typeof(self) weakSelf = self;
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(PLWebLoadingDeadline * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        [weakSelf pl_loadingDeadlineExpired:statusGeneration];
    });
}

- (void)pl_loadingDeadlineExpired:(NSUInteger)generation
{
    if (generation != self.loadStatusGeneration) return;
    self.activeNavigation = nil;
    [self.webView stopLoading];
    [self pl_showLoadError:[NSError errorWithDomain:NSURLErrorDomain code:NSURLErrorTimedOut userInfo:nil]
                  source:[NSString stringWithFormat:@"app UI deadline (%.0fs), NOT a WebKit error", PLWebLoadingDeadline]];
}

- (void)pl_hideLoadStatus
{
    self.loadStatusGeneration++;
    self.displayingLoadError = NO;
    self.loadStatusView.hidden = YES;
}

- (void)pl_showLoadError:(NSError *)error source:(NSString *)source
{
    [self pl_recordDiagnostic:[NSString stringWithFormat:@"failure: %@ %@ %ld", source, PLDiagnosticAtom(error.domain), (long)error.code] URL:nil];
    self.diagnosticReport = [self pl_diagnosticReportForError:error source:source];
    self.diagnosticTextView.text = self.diagnosticReport;
    [self.diagnosticTextView setContentOffset:CGPointZero animated:NO];
    self.diagnosticTextView.hidden = NO;
    self.diagnosticCopyButton.hidden = NO;
    [self.diagnosticCopyButton setTitle:@"Copy diagnostics" forState:UIControlStateNormal];
    self.navigationGeneration++; // Invalidate any queued redirect/process recovery.
    self.loadStatusGeneration++;
    self.displayingLoadError = YES;
    self.loadStatusView.hidden = NO;
    [self pl_updatePushCover];
    BOOL safeRetry = [self pl_isSafeRequest:self.retryRequest] && [self pl_isSafeRequest:self.mainFrameRequest];
    self.retryButton.hidden = !safeRetry;
    self.loadStatusLabel.text = [NSString stringWithFormat:@"Unable to load this page.\n(%@ %ld)\nCopy diagnostics and send the report.%@",
        PLDiagnosticAtom(error.domain), (long)error.code,
        safeRetry ? @"" : @"\nThis request cannot be safely repeated. Open the notification again to return to its link."];
    NSLog(@"[WebViewController] load failed: domain=%@ code=%ld host=%@ generation=%lu",
        error.domain, (long)error.code, self.url.host, (unsigned long)self.navigationGeneration);
}

- (void)pl_retryLoading
{
    // Read the current request here, never a URL captured by an old error callback.
    if (!self.displayingLoadError || ![self pl_isSafeRequest:self.retryRequest] ||
        ![self pl_isSafeRequest:self.mainFrameRequest]) return;
    [self prepareForPushNavigation];
    [self pl_recordDiagnostic:@"manual retry" URL:self.retryRequest.URL];
    [self.webView stopLoading];
    [self pl_loadRequest:self.retryRequest resetRedirects:YES];
}

- (void)pl_loadRequest:(NSURLRequest *)request resetRedirects:(BOOL)reset
{
    if (self.activeNavigation) [self.supersededNavigations addObject:self.activeNavigation];
    self.coverGeneration++;
    self.finishedNavigation = nil;
    self.awaitingPushNavigation = NO;
    self.diagnosticLoadCount++;
    self.diagnosticStage = @"loadRequest called; waiting for WebKit start";
    self.diagnosticDidStart = NO;
    self.diagnosticDidCommit = NO;
    self.diagnosticLastResponse = @"not observed for this load";
    [self pl_recordDiagnostic:[NSString stringWithFormat:@"loadRequest #%lu %@", (unsigned long)self.diagnosticLoadCount, PLDiagnosticAtom(request.HTTPMethod ?: @"GET")] URL:request.URL];
    if (reset) {
        [self.resumedRedirectURLs removeAllObjects];
        self.processRecoveryCount = 0;
        self.retryRequest = request;
    }
    self.navigationGeneration++;
    self.url = request.URL;
    self.lastServerRedirectURL = nil;
    self.mainFrameRequest = request;
    [self pl_beginLoading];
    self.activeNavigation = [self.webView loadRequest:request];
}

- (void)webView:(WKWebView *)webView didStartProvisionalNavigation:(WKNavigation *)navigation
{
    if ([self.supersededNavigations containsObject:navigation]) return;
    self.coverGeneration++;
    self.finishedNavigation = nil;
    if (navigation != self.activeNavigation) {
        // A link, form, back gesture or JavaScript started a new navigation.
        // Our own redirect continuations already have activeNavigation assigned.
        self.activeNavigation = navigation;
        self.lastServerRedirectURL = nil;
        self.navigationGeneration++;
        [self.resumedRedirectURLs removeAllObjects];
        self.processRecoveryCount = 0;
        self.retryRequest = self.mainFrameRequest;
        self.diagnosticLoadCount++;
        self.diagnosticDidCommit = NO;
        self.diagnosticLastResponse = @"not observed for this load";
    }
    self.diagnosticDidStart = YES;
    self.diagnosticStage = @"provisional started; waiting for response/commit";
    [self pl_recordDiagnostic:@"didStartProvisional" URL:webView.URL];
    [self pl_beginLoading];
}

- (void)webView:(WKWebView *)webView didCommitNavigation:(WKNavigation *)navigation
{
    if (navigation != self.activeNavigation) return;
    self.diagnosticDidCommit = YES;
    self.diagnosticStage = @"content committed";
    [self pl_recordDiagnostic:@"didCommit" URL:webView.URL];
    if (!self.pushCoverPending && !self.awaitingPushNavigation) [self pl_hideLoadStatus];
}

- (void)webView:(WKWebView *)webView didReceiveServerRedirectForProvisionalNavigation:(WKNavigation *)navigation
{
    if (navigation != self.activeNavigation) return;
    self.lastServerRedirectURL = webView.URL;
    self.diagnosticRedirectCount++;
    self.diagnosticStage = @"server redirect observed; waiting for next response/commit";
    [self pl_recordDiagnostic:@"server redirect" URL:webView.URL];
}

- (void)webView:(WKWebView *)webView didFailNavigation:(WKNavigation *)navigation withError:(NSError *)error
{
    if (navigation != self.activeNavigation) return;
    // Ignore cancellations (e.g. triggered by our own decidePolicyForNavigationAction)
    if ([error.domain isEqualToString:NSURLErrorDomain] && error.code == NSURLErrorCancelled) {
        [self pl_recordDiagnostic:@"didFailNavigation cancelled (ignored)" URL:nil];
        return;
    }

    NSLog(@"[WebViewController] navigation error (domain=%@ code=%ld): %@",
          error.domain, (long)error.code, error.localizedDescription);

    [self pl_showLoadError:error source:@"WebKit didFailNavigation"];
}

- (void)webView:(WKWebView *)webView decidePolicyForNavigationResponse:(WKNavigationResponse *)navigationResponse decisionHandler:(void (^)(WKNavigationResponsePolicy))decisionHandler
{
    // Preserve WebKit's documented default: allow only displayable responses.
    // No extra request, header/cookie dump, content inspection or redirect probe.
    if (navigationResponse.forMainFrame) {
        NSURLResponse *response = navigationResponse.response;
        NSInteger status = [response isKindOfClass:NSHTTPURLResponse.class] ? [(NSHTTPURLResponse *)response statusCode] : 0;
        self.diagnosticLastResponse = [NSString stringWithFormat:@"HTTP %ld; MIME=%@; displayable=%@", (long)status,
            PLDiagnosticAtom(response.MIMEType), navigationResponse.canShowMIMEType ? @"yes" : @"no"];
        self.diagnosticStage = @"main-frame response received";
        [self pl_recordDiagnostic:self.diagnosticLastResponse URL:response.URL];
    }
    decisionHandler(navigationResponse.canShowMIMEType ? WKNavigationResponsePolicyAllow : WKNavigationResponsePolicyCancel);
}

// Track navigation actions (this provides the redirect chain)
- (void)webView:(WKWebView *)webView decidePolicyForNavigationAction:(WKNavigationAction *)navigationAction decisionHandler:(void (^)(WKNavigationActionPolicy))decisionHandler
{
    NSURL *requestURL = navigationAction.request.URL;

    // Sub-frame navigations (iframes etc.) — allow them through without interference.
    // Must be checked first so that blob:/about:/data: URLs used by game launchers inside
    // iframes are never intercepted or sent to UIApplication.
    // Target-blank / new-window requests are handled by createWebViewWithConfiguration:.
    if (navigationAction.targetFrame && !navigationAction.targetFrame.isMainFrame) {
        decisionHandler(WKNavigationActionPolicyAllow);
        return;
    }
    [self pl_recordDiagnostic:[NSString stringWithFormat:@"main action type=%ld target=%@ method=%@", (long)navigationAction.navigationType,
        navigationAction.targetFrame ? @"main" : @"new-window", PLDiagnosticAtom(navigationAction.request.HTTPMethod ?: @"GET")] URL:requestURL];

    // Open non-http(s) URLs (deeplinks, tel:, mailto:, custom schemes, etc.) via the system.
    // Exclude blob:, about:, data: — WebKit must handle these natively; UIApplication cannot.
    if (requestURL) {
        NSString *scheme = requestURL.scheme.lowercaseString;
        BOOL isWebKitInternal = [scheme isEqualToString:@"blob"] ||
                                [scheme isEqualToString:@"about"] ||
                                [scheme isEqualToString:@"data"];
        if (scheme && ![scheme isEqualToString:@"http"] && ![scheme isEqualToString:@"https"] && !isWebKitInternal) {
            [self pl_recordDiagnostic:@"external scheme handed to system; WebKit action cancelled" URL:requestURL];
            if (@available(iOS 10.0, *)) {
                [[UIApplication sharedApplication] openURL:requestURL options:@{} completionHandler:nil];
            } else {
                [[UIApplication sharedApplication] openURL:requestURL];
            }
            decisionHandler(WKNavigationActionPolicyCancel);
            return;
        }
    }

    // Let WebKit perform ordinary links, JavaScript navigation and every
    // HTTP redirect itself. Reissuing a link request from inside this delegate
    // can cancel the redirect chain that the page has just started.
    if (requestURL && (!navigationAction.targetFrame || navigationAction.targetFrame.isMainFrame)) {
        self.navigationGeneration++;
        self.url = requestURL;
        self.mainFrameRequest = navigationAction.request;
    }

    decisionHandler(WKNavigationActionPolicyAllow);
}


// Handle requests to open new windows (e.g. target="_blank" or window.open())
- (WKWebView *)webView:(WKWebView *)webView createWebViewWithConfiguration:(WKWebViewConfiguration *)configuration forNavigationAction:(WKNavigationAction *)navigationAction windowFeatures:(WKWindowFeatures *)windowFeatures
{
    // When the web content tries to open a new window, override and load
    // the target URL in the existing webView instead of creating a new one.
    if (navigationAction.request.URL) {
        [self pl_recordDiagnostic:@"new window loaded in existing WebView" URL:navigationAction.request.URL];
        [self pl_loadRequest:navigationAction.request resetRedirects:YES];
    }
    return nil;
}

// Handle provisional failures (e.g., too many redirects, network interruptions)
- (void)webView:(WKWebView *)webView didFailProvisionalNavigation:(WKNavigation *)navigation withError:(NSError *)error
{
    if (navigation != self.activeNavigation) return;
    if ([error.domain isEqualToString:NSURLErrorDomain] && error.code == NSURLErrorCancelled) {
        [self pl_recordDiagnostic:@"didFailProvisional cancelled (ignored)" URL:nil];
        return;
    }
    if ([error.domain isEqualToString:NSURLErrorDomain] && error.code == NSURLErrorHTTPTooManyRedirects) {
        // WebKit's per-load redirect limit is lower than the QA site's 50 hops.
        // Continue only this failed GET/HEAD from its reported failing URL. Do
        // not replay POST bodies, resolve via URLSession, or restart at hop 1.
        NSURL *next = error.userInfo[NSURLErrorFailingURLErrorKey];
        if (![next isKindOfClass:NSURL.class]) {
            id text = error.userInfo[NSURLErrorFailingURLStringErrorKey];
            next = [text isKindOfClass:NSString.class] ? [NSURL URLWithString:text] : nil;
        }
        // Some OS versions report the original failing request. Prefer the
        // last redirect observed in this navigation so continuation makes progress.
        next = self.lastServerRedirectURL ?: next;
        NSString *method = self.mainFrameRequest.HTTPMethod ?: @"GET";
        BOOL safeMethod = [method isEqualToString:@"GET"] || [method isEqualToString:@"HEAD"];
        BOOL webURL = next.host.length && ([next.scheme.lowercaseString isEqualToString:@"https"] ||
                                          [next.scheme.lowercaseString isEqualToString:@"http"]);
        if (safeMethod && webURL && self.resumedRedirectURLs.count < 4 &&
            ![self.resumedRedirectURLs containsObject:next.absoluteString]) {
            [self.resumedRedirectURLs addObject:next.absoluteString];
            NSUInteger generation = self.navigationGeneration;
            NSMutableURLRequest *request = [NSMutableURLRequest requestWithURL:next
                cachePolicy:NSURLRequestReloadIgnoringLocalCacheData timeoutInterval:WebViewConfigNavigationTimeout];
            request.HTTPMethod = method;
            dispatch_async(dispatch_get_main_queue(), ^{
                if (generation != self.navigationGeneration || navigation != self.activeNavigation) return;
                [self pl_recordDiagnostic:@"continuing long redirect chain" URL:request.URL];
                NSLog(@"[WebViewController] Continuing long redirect chain (%lu/4), host=%@",
                      (unsigned long)self.resumedRedirectURLs.count, next.host);
                [self pl_loadRequest:request resetRedirects:NO];
            });
            return;
        }
    }
    NSLog(@"[WebViewController] provisional navigation failed: %@", error);
    [self pl_showLoadError:error source:@"WebKit didFailProvisionalNavigation"];
}

- (void)webView:(WKWebView *)webView didFinishNavigation:(WKNavigation *)navigation
{
    if (navigation != self.activeNavigation) return;
    self.diagnosticStage = @"finished";
    [self pl_recordDiagnostic:@"didFinish" URL:webView.URL];
    self.finishedNavigation = navigation;
    if (!self.pushCoverPending && !self.awaitingPushNavigation) [self pl_hideLoadStatus];
    NSLog(@"[WebViewController] finished loading: %@", webView.URL);
    self.processRecoveryCount = 0;
    // Restore only zoom limits; never replace WKWebView's internal scroll delegate.
    webView.scrollView.minimumZoomScale = 1.0;
    webView.scrollView.maximumZoomScale = 1.0;
    webView.scrollView.zoomScale = 1.0;
    [self pl_revealFinishedPush];
}

// Called when the WKWebView web-content process crashes or is killed by the OS
// (e.g. memory pressure). Without this the WebView stays blank forever.
- (void)webViewWebContentProcessDidTerminate:(WKWebView *)webView
{
    NSLog(@"[WebViewController] WKWebView content process terminated");
    [self pl_recordDiagnostic:@"WebKit content process terminated" URL:nil];
    if (self.processRecoveryCount >= 1 || ![self pl_isSafeRequest:self.mainFrameRequest]) {
        [self pl_showLoadError:[NSError errorWithDomain:WKErrorDomain code:WKErrorWebContentProcessTerminated userInfo:nil] source:@"WebKit content process terminated"];
        return;
    }
    self.processRecoveryCount++;
    NSUInteger generation = self.navigationGeneration;
    NSURLRequest *recoveryRequest = self.mainFrameRequest;
    [self pl_beginLoading];
    // Brief delay to let the process fully clean up before reloading
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.5 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        if (generation != self.navigationGeneration) return;
        // Preserve the safe request/method; never turn a failed POST into a GET.
        // webView.URL can still belong to the previous push at this point.
        [self pl_loadRequest:recoveryRequest resetRedirects:NO];
    });
}

#pragma mark - Back gesture

- (void)handleEdgePan:(UIScreenEdgePanGestureRecognizer *)gesture
{
    if (gesture.state == UIGestureRecognizerStateEnded) {
        if (self.webView.canGoBack) {
            [self.webView goBack];
        }
    }
}

- (BOOL)gestureRecognizer:(UIGestureRecognizer *)gestureRecognizer shouldRecognizeSimultaneouslyWithGestureRecognizer:(UIGestureRecognizer *)otherGestureRecognizer
{
    // Allow the web view's own gestures (scrolling) to work alongside the edge pan
    return YES;
}

- (void)viewWillAppear:(BOOL)animated {
    [super viewWillAppear:animated];

    // Add observer for keyboard notifications
    [[NSNotificationCenter defaultCenter] addObserver:self
                                             selector:@selector(keyboardWillShow:)
                                                 name:UIKeyboardWillShowNotification
                                               object:nil];
    [[NSNotificationCenter defaultCenter] addObserver:self
                                             selector:@selector(keyboardWillHide:)
                                                 name:UIKeyboardWillHideNotification
                                               object:nil];
}

- (void)viewWillDisappear:(BOOL)animated {
    [super viewWillDisappear:animated];
    [self pl_recordDiagnostic:@"viewWillDisappear" URL:nil];

    // Remove observer for keyboard notifications
    [[NSNotificationCenter defaultCenter] removeObserver:self name:UIKeyboardWillShowNotification object:nil];
    [[NSNotificationCenter defaultCenter] removeObserver:self name:UIKeyboardWillHideNotification object:nil];
}

- (void)keyboardWillShow:(NSNotification *)notification {
    // Reset zoom scale when keyboard is shown
    self.webView.scrollView.zoomScale = 1.0;
}

- (void)keyboardWillHide:(NSNotification *)notification {
    // Reset zoom scale when keyboard is hidden
    self.webView.scrollView.zoomScale = 1.0;
}
@end
