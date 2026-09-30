#import <XCTest/XCTest.h>
#import <UserNotifications/UserNotifications.h>
#import "PreloadViewController.h"
#import "NotificationPromptViewController.h"
#import "PLLaunchDiagnostics.h"
#import "WebViewController.h"
#import "PLPushRegistration.h"

@interface PreloadViewController (PermissionTestAccess)
- (void)pl_checkAndAskNotificationsIfNeededWithCompletion:(void (^)(void))completion;
- (void)pl_finishWithURL:(NSURL *)url;
- (NSDate *)pl_notificationNow;
- (void)pl_notificationStatusWithCompletion:(void (^)(UNAuthorizationStatus))completion;
- (void)pl_requestNotificationAuthorizationWithCompletion:(void (^)(BOOL, NSError *))completion;
- (void)pl_registerForRemoteNotifications;
- (void)pl_openNotificationSettingsWithCompletion:(void (^)(BOOL))completion;
- (void)pl_settingsDidReturn;
- (PLPushRegistration *)pl_registrationService;
@end
@interface WebViewController (ComparisonTestAccess)
- (NSString *)pl_comparisonReport;
@end
@interface PLPushRegistration (DeadlineTestAccess)
- (void)pl_expireGeneration:(NSUInteger)generation;
@end

@interface PermissionRegistration : PLPushRegistration
@property (nonatomic) NSUInteger sendCount;
@property (nonatomic) BOOL holdResponse;
@property (nonatomic, strong) XCTestExpectation *postReady;
@property (nonatomic, copy) void (^postReply)(BOOL);
@end
@implementation PermissionRegistration
- (void)pl_fetchToken:(void (^)(NSString *))completion { completion(@"unchanged-test-token"); }
- (void)pl_sendToken:(NSString *)token completion:(void (^)(BOOL))completion {
    self.sendCount++;
    self.postReply = completion;
    [self.postReady fulfill];
    if (!self.holdResponse) { self.postReply = nil; completion(YES); }
}
@end

// Only OS/clock and UIKit presentation boundaries are replaced. The production
// cooldown, Allow/Skip handlers, defaults and handoff are NOT overridden.
@interface OSBoundaryPreload : PreloadViewController
@property (nonatomic) UNAuthorizationStatus testStatus;
@property (nonatomic, strong) NSDate *testDate;
@property (nonatomic, strong) NotificationPromptViewController *prompt;
@property (nonatomic, strong) XCTestExpectation *promptReady;
@property (nonatomic, strong) XCTestExpectation *requestReady;
@property (nonatomic, copy) void (^authorizationReply)(BOOL, NSError *);
@property (nonatomic) NSUInteger registrationCount;
@property (nonatomic) NSUInteger requestCount;
@property (nonatomic) NSUInteger settingsCount;
@property (nonatomic) BOOL settingsOpenSuccess;
@property (nonatomic, strong) PermissionRegistration *registration;
@end
@implementation OSBoundaryPreload
- (PLPushRegistration *)pl_registrationService { return self.registration; }
- (NSDate *)pl_notificationNow { return self.testDate; }
- (void)pl_notificationStatusWithCompletion:(void (^)(UNAuthorizationStatus))completion {
    completion(self.testStatus);
}
- (void)pl_requestNotificationAuthorizationWithCompletion:(void (^)(BOOL, NSError *))completion {
    self.requestCount++;
    self.authorizationReply = completion;
    [self.requestReady fulfill];
}
- (void)pl_registerForRemoteNotifications { self.registrationCount++; }
- (void)pl_openNotificationSettingsWithCompletion:(void (^)(BOOL))completion {
    self.settingsCount++;
    completion(self.settingsOpenSuccess);
}
- (void)presentViewController:(UIViewController *)vc animated:(BOOL)animated completion:(void (^)(void))completion {
    self.prompt = (NotificationPromptViewController *)vc;
    [self.promptReady fulfill];
    if (completion) completion();
}
@end

@interface PermissionFlowTests : XCTestCase
@end
@implementation PermissionFlowTests
- (void)setUp {
    [super setUp];
    [self clearPreferences];
}
- (void)tearDown {
    [self clearPreferences];
    [super tearDown];
}
- (void)clearPreferences {
    for (NSString *key in @[@"PLLastNotificationDeniedAt", @"PLAskedForNotifications",
                            @"PLLaunchMode", @"PLLastEndpointURLString", @"PLLaunchDiagnosticsV1", @"PLPushRegistrationPendingV1"])
        [NSUserDefaults.standardUserDefaults removeObjectForKey:key];
}
- (OSBoundaryPreload *)preload {
    OSBoundaryPreload *preload = [OSBoundaryPreload new];
    preload.testDate = [NSDate dateWithTimeIntervalSince1970:1800000000];
    preload.testStatus = UNAuthorizationStatusNotDetermined;
    preload.registration = [PermissionRegistration new];
    preload.config = [PreloadConfig configWithAppsDevKey:@"" appleAppId:@"test" endpointURL:@"https://example.invalid"];
    return preload;
}
- (void)choose:(NSString *)handler on:(OSBoundaryPreload *)preload {
    // NotificationPrompt invokes these only AFTER dismissal; its one-shot and
    // dismissal behavior is covered separately by RoutingTests.
    void (^action)(void) = [preload.prompt valueForKey:handler];
    XCTAssertNotNil(action);
    if (action) action();
}
- (void)startPrompt:(OSBoundaryPreload *)preload completion:(void (^)(void))completion {
    preload.promptReady = [self expectationWithDescription:@"production prompt chosen"];
    [preload pl_checkAndAskNotificationsIfNeededWithCompletion:completion];
    [self waitForExpectations:@[preload.promptReady] timeout:5];
}
- (void)allow:(OSBoundaryPreload *)preload {
    preload.requestReady = [self expectationWithDescription:@"OS authorization requested"];
    [self choose:@"allowHandler" on:preload];
    [self waitForExpectations:@[preload.requestReady] timeout:5];
}
- (void)verifyGrantedFlowAfterSkip:(BOOL)afterSkip {
    OSBoundaryPreload *preload = [self preload];
    preload.registration.holdResponse = YES;
    preload.registration.postReady = [self expectationWithDescription:@"registration POST after Allow"];
    if (afterSkip) {
        OSBoundaryPreload *earlier = [self preload];
        earlier.testDate = [preload.testDate dateByAddingTimeInterval:-3 * 24 * 60 * 60];
        XCTestExpectation *skipped = [self expectationWithDescription:@"Skip completes"];
        [self startPrompt:earlier completion:^{ [skipped fulfill]; }];
        [self choose:@"cancelHandler" on:earlier];
        [self waitForExpectations:@[skipped] timeout:5];
        XCTAssertEqualObjects([NSUserDefaults.standardUserDefaults objectForKey:@"PLLastNotificationDeniedAt"], earlier.testDate);
        XCTAssertEqual(earlier.requestCount, 0u);
        XCTAssertEqual(earlier.registrationCount, 0u);
    }
    XCTestExpectation *completed = [self expectationWithDescription:@"Allow completes once on main"];
    __block NSUInteger completions = 0;
    [self startPrompt:preload completion:^{
        XCTAssertTrue(NSThread.isMainThread);
        completions++;
        [completed fulfill];
    }];
    [self allow:preload];
    XCTAssertEqual(completions, 0u); // Must wait for OS reply, not merely Allow tap.
    // Real iOS can reply off-main.
    void (^reply)(BOOL, NSError *) = preload.authorizationReply;
    preload.authorizationReply = nil;
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_DEFAULT, 0), ^{ reply(YES, nil); });
    [self waitForExpectations:@[preload.registration.postReady] timeout:5];
    XCTAssertEqual(completions, 0u); // Grant alone is not a completed handoff.
    void (^postReply)(BOOL) = preload.registration.postReply;
    preload.registration.postReply = nil;
    postReply(YES);
    [self waitForExpectations:@[completed] timeout:5];
    XCTAssertEqual(completions, 1u);
    XCTAssertEqual(preload.requestCount, 1u);
    XCTAssertEqual(preload.registrationCount, 1u);
    XCTAssertEqual(preload.settingsCount, 0u);
    XCTAssertEqual(preload.registration.sendCount, 1u); // Also when token did not change after Skip.
    XCTAssertTrue([NSUserDefaults.standardUserDefaults boolForKey:@"PLAskedForNotifications"]);
    XCTAssertNil([NSUserDefaults.standardUserDefaults objectForKey:@"PLLastNotificationDeniedAt"]);
    // The journal preserves Skip even though the routing preference is cleared.
    NSString *report = [PLLaunchDiagnostics report];
    XCTAssertTrue([report containsString:@"OS authorization granted = 1"]);
    XCTAssertEqual([report containsString:@"Skip tapped = 1"], afterSkip);
    XCTestExpectation *second = [self expectationWithDescription:@"session recheck bypasses prompt"];
    [preload pl_checkAndAskNotificationsIfNeededWithCompletion:^{ [second fulfill]; }];
    [self waitForExpectations:@[second] timeout:5];
    XCTAssertEqual(preload.requestCount, 1u);
}
- (void)testImmediateAllowUsesProductionPermissionFlow { [self verifyGrantedFlowAfterSkip:NO]; }
- (void)testSkipThenAllowAtExactly72HoursUsesSameProductionPermissionFlow { [self verifyGrantedFlowAfterSkip:YES]; }
- (void)testBefore72HoursBypassesPromptWithoutRequestingPermission {
    OSBoundaryPreload *preload = [self preload];
    [NSUserDefaults.standardUserDefaults setObject:[preload.testDate dateByAddingTimeInterval:-(3 * 24 * 60 * 60 - 1)] forKey:@"PLLastNotificationDeniedAt"];
    XCTestExpectation *done = [self expectationWithDescription:@"cooldown bypass"];
    [preload pl_checkAndAskNotificationsIfNeededWithCompletion:^{ [done fulfill]; }];
    [self waitForExpectations:@[done] timeout:5];
    XCTAssertNil(preload.prompt);
    XCTAssertEqual(preload.requestCount, 0u);
    XCTAssertEqual(preload.registrationCount, 0u);
}
- (void)testAlreadyAuthorizedBypassesPromptEvenWithOldSkip {
    for (NSNumber *status in @[@(UNAuthorizationStatusAuthorized), @(UNAuthorizationStatusProvisional), @(UNAuthorizationStatusEphemeral)]) {
        OSBoundaryPreload *preload = [self preload];
        preload.testStatus = status.integerValue;
        [NSUserDefaults.standardUserDefaults setObject:[preload.testDate dateByAddingTimeInterval:-4 * 86400] forKey:@"PLLastNotificationDeniedAt"];
        XCTestExpectation *done = [self expectationWithDescription:@"authorized bypass"];
        [preload pl_checkAndAskNotificationsIfNeededWithCompletion:^{ [done fulfill]; }];
        [self waitForExpectations:@[done] timeout:5];
        XCTAssertNil(preload.prompt);
        XCTAssertEqual(preload.requestCount, 0u);
    }
}
- (void)testOSDenialStartsCooldownAndDoesNotRegisterAPNs {
    OSBoundaryPreload *preload = [self preload];
    XCTestExpectation *done = [self expectationWithDescription:@"denied completes"];
    [self startPrompt:preload completion:^{ [done fulfill]; }];
    [self allow:preload];
    preload.authorizationReply(NO, nil);
    [self waitForExpectations:@[done] timeout:5];
    XCTAssertEqual(preload.registrationCount, 0u);
    XCTAssertEqualObjects([NSUserDefaults.standardUserDefaults objectForKey:@"PLLastNotificationDeniedAt"], preload.testDate);
}
- (void)testPreviouslyDeniedOpensSettingsWithoutClaimingAuthorization {
    for (NSNumber *success in @[@NO, @YES]) {
        OSBoundaryPreload *preload = [self preload];
        preload.testStatus = UNAuthorizationStatusDenied;
        preload.settingsOpenSuccess = success.boolValue;
        [NSUserDefaults.standardUserDefaults setObject:[preload.testDate dateByAddingTimeInterval:-4 * 86400] forKey:@"PLLastNotificationDeniedAt"];
        XCTestExpectation *done = [self expectationWithDescription:@"Settings open completes"];
        [self startPrompt:preload completion:^{ [done fulfill]; }];
        [self choose:@"allowHandler" on:preload];
        if (success.boolValue) [preload pl_settingsDidReturn];
        [self waitForExpectations:@[done] timeout:5];
        XCTAssertEqual(preload.settingsCount, 1u);
        XCTAssertEqual(preload.requestCount, 0u);
        XCTAssertEqual(preload.registrationCount, 0u);
        XCTAssertEqual(preload.registration.sendCount, 0u);
        XCTAssertFalse([[PLLaunchDiagnostics report] containsString:@"OS authorization granted = 1"]);
    }
}
- (void)testSettingsReturnChecksPermissionAndWaitsForRegistration {
    OSBoundaryPreload *preload = [self preload];
    preload.testStatus = UNAuthorizationStatusDenied;
    preload.settingsOpenSuccess = YES;
    preload.registration.holdResponse = YES;
    preload.registration.postReady = [self expectationWithDescription:@"post after real Settings return"];
    XCTestExpectation *done = [self expectationWithDescription:@"Settings authorization completes"];
    __block NSUInteger completionCount = 0;
    [self startPrompt:preload completion:^{ completionCount++; [done fulfill]; }];
    [self choose:@"allowHandler" on:preload];
    XCTAssertEqual(completionCount, 0u); // Opening Settings must NOT complete.
    XCTAssertEqual(preload.registration.sendCount, 0u);
    preload.testStatus = UNAuthorizationStatusAuthorized;
    [preload pl_settingsDidReturn];
    [preload pl_settingsDidReturn]; // Duplicate activation must be harmless.
    [self waitForExpectations:@[preload.registration.postReady] timeout:5];
    XCTAssertEqual(completionCount, 0u);
    preload.registration.postReply(YES);
    [self waitForExpectations:@[done] timeout:5];
    XCTAssertEqual(completionCount, 1u);
    XCTAssertEqual(preload.registrationCount, 1u);
    XCTAssertEqual(preload.registration.sendCount, 1u);
    XCTAssertNil([NSUserDefaults.standardUserDefaults objectForKey:@"PLLastNotificationDeniedAt"]);
}

- (void)testColdPushWaitsForRegistrationAndKeepsLatestURL {
    OSBoundaryPreload *preload = [self preload];
    preload.registration.holdResponse = YES;
    preload.registration.postReady = [self expectationWithDescription:@"cold registration POST"];
    XCTestExpectation *opened = [self expectationWithDescription:@"latest cold push opened"];
    __block NSUInteger count = 0;
    preload.onOpenURL = ^(NSURL *url) { count++; XCTAssertEqualObjects(url.path, @"/777"); [opened fulfill]; };
    [preload acceptPushURL:[NSURL URLWithString:@"https://example.invalid/hello"]];
    [self waitForExpectations:@[preload.registration.postReady] timeout:5];
    [preload acceptPushURL:[NSURL URLWithString:@"https://example.invalid/777"]];
    XCTAssertEqual(count, 0u);
    preload.registration.postReply(YES);
    [self waitForExpectations:@[opened] timeout:5];
    XCTAssertEqual(count, 1u);
    XCTAssertEqual(preload.registration.sendCount, 1u);
    XCTAssertEqual(preload.requestCount, 0u); // No new permission prompt on push.
}
- (void)testColdRegistrationTimeoutCannotHangOrReplacePushOnLateSuccess {
    OSBoundaryPreload *preload = [self preload];
    preload.registration.holdResponse = YES;
    preload.registration.postReady = [self expectationWithDescription:@"registration POST stalled"];
    __block NSUInteger opens = 0;
    preload.onOpenURL = ^(NSURL *url) { opens++; XCTAssertEqualObjects(url.path, @"/777"); };
    [preload acceptPushURL:[NSURL URLWithString:@"https://example.invalid/777"]];
    [self waitForExpectations:@[preload.registration.postReady] timeout:5];
    NSUInteger generation = [[preload.registration valueForKey:@"generation"] unsignedIntegerValue];
    [preload.registration pl_expireGeneration:generation];
    XCTAssertEqual(opens, 1u);
    XCTAssertTrue(preload.hasFinished);
    XCTAssertTrue([NSUserDefaults.standardUserDefaults boolForKey:@"PLPushRegistrationPendingV1"]);
    void (^lateReply)(BOOL) = preload.registration.postReply;
    preload.registration.postReply = nil;
    lateReply(YES);
    XCTestExpectation *drained = [self expectationWithDescription:@"late response ignored"];
    dispatch_async(dispatch_get_main_queue(), ^{ [drained fulfill]; });
    [self waitForExpectations:@[drained] timeout:5];
    XCTAssertEqual(opens, 1u);
}
- (void)testLatestPushWinsDuringRealDelayedPermissionFlow {
    OSBoundaryPreload *preload = [self preload];
    [NSUserDefaults.standardUserDefaults setObject:[preload.testDate dateByAddingTimeInterval:-4 * 86400] forKey:@"PLLastNotificationDeniedAt"];
    preload.promptReady = [self expectationWithDescription:@"handoff permission prompt"];
    XCTestExpectation *opened = [self expectationWithDescription:@"latest URL opens once"];
    __block NSUInteger openCount = 0;
    preload.onOpenURL = ^(NSURL *url) {
        openCount++;
        XCTAssertEqualObjects(url.path, @"/777");
        [opened fulfill];
    };
    [preload pl_finishWithURL:[NSURL URLWithString:@"https://example.invalid/config"]];
    [self waitForExpectations:@[preload.promptReady] timeout:5];
    [self allow:preload];
    [preload acceptPushURL:[NSURL URLWithString:@"https://example.invalid/hello"]];
    [preload acceptPushURL:[NSURL URLWithString:@"https://example.invalid/777"]];
    XCTAssertEqual(openCount, 0u);
    preload.authorizationReply(YES, nil);
    [self waitForExpectations:@[opened] timeout:5];
    [preload acceptPushURL:[NSURL URLWithString:@"https://example.invalid/stale"]];
    XCTAssertTrue(preload.hasFinished);
    XCTAssertEqual(openCount, 1u);
}
- (void)testStartupJournalPersistsNumericObservationsAndCorrelatesRequests {
    NSInteger first = [PLLaunchDiagnostics record:PLLaunchEventTokenSyncStarted value:1];
    NSInteger second = [PLLaunchDiagnostics record:PLLaunchEventTokenSyncStarted value:1];
    [PLLaunchDiagnostics record:PLLaunchEventTokenSyncHTTP value:200 operation:second];
    [PLLaunchDiagnostics record:PLLaunchEventTokenSyncError value:-1001 operation:first];
    NSArray *stored = [NSUserDefaults.standardUserDefaults arrayForKey:@"PLLaunchDiagnosticsV1"];
    XCTAssertEqual(stored.count, 4u);
    // All persistence fields are numeric: no payload/URL/token can be persisted.
    for (NSDictionary *entry in stored)
        for (id value in entry.allValues) XCTAssertTrue([value isKindOfClass:NSNumber.class]);
    NSString *report = [PLLaunchDiagnostics report];
    // The preprocessor does not treat Objective-C message brackets as grouping:
    // a format-argument comma inside XCTAssertTrue would split its arguments.
    NSString *httpOperation = [NSString stringWithFormat:@"op=%ld token sync HTTP status", (long)second];
    NSString *failedOperation = [NSString stringWithFormat:@"op=%ld token sync NSError.code = -1001", (long)first];
    XCTAssertTrue([report containsString:httpOperation]);
    XCTAssertTrue([report containsString:failedOperation]);
    XCTAssertTrue([report containsString:@"NOT backend acceptance"]);
}
- (void)testStartupJournalIsBoundedAndThreadSafe {
    dispatch_apply(120, dispatch_get_global_queue(QOS_CLASS_DEFAULT, 0), ^(size_t index) {
        [PLLaunchDiagnostics record:PLLaunchEventFCMReceived value:1];
    });
    NSArray *stored = [NSUserDefaults.standardUserDefaults arrayForKey:@"PLLaunchDiagnosticsV1"];
    XCTAssertEqual(stored.count, 80u);
    XCTAssertEqual([stored.firstObject[@"seq"] integerValue], 41);
    XCTAssertEqual([stored.lastObject[@"seq"] integerValue], 120);
}
- (void)testJournalRejectsMalformedOrUnknownPersistedEvents {
    [NSUserDefaults.standardUserDefaults setObject:@[
        @{@"event": @1, @"value": @"secret-token"},
        @{@"seq": @1, @"event": @999, @"value": @1, @"op": @0, @"utc": @1, @"pid": @1}
    ] forKey:@"PLLaunchDiagnosticsV1"];
    XCTAssertFalse([[PLLaunchDiagnostics report] containsString:@"secret-token"]);
    XCTAssertEqual([PLLaunchDiagnostics record:(PLLaunchEvent)999 value:1], 0);
    XCTAssertEqual([PLLaunchDiagnostics record:PLLaunchEventSkipTapped value:1], 1);
}
- (void)testManualReportDoesNotLoadViewOrChangeNavigationState {
    WebViewController *web = [[WebViewController alloc] initWithURL:[NSURL URLWithString:@"https://example.invalid/test"]];
    [PLLaunchDiagnostics record:PLLaunchEventSkipTapped value:1];
    NSString *report = [web pl_comparisonReport];
    XCTAssertTrue([report containsString:@"EASYLAUNCH DIAG r10"]);
    XCTAssertTrue([report containsString:@"none (manual snapshot)"]);
    XCTAssertTrue([report containsString:@"Skip tapped = 1"]);
    XCTAssertFalse(web.isViewLoaded);
    XCTAssertFalse([[web valueForKey:@"displayingLoadError"] boolValue]);
    XCTAssertEqual([[web valueForKey:@"diagnosticLoadCount"] unsignedIntegerValue], 0u);
}
@end
