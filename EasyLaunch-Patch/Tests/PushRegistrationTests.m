#import <XCTest/XCTest.h>
#import "PLPushRegistration.h"

extern NSDictionary *PLTestAttribution;
@interface PLPushRegistration (TestAccess)
- (void)pl_fetchToken:(void (^)(NSString *))completion;
- (void)pl_sendToken:(NSString *)token completion:(void (^)(BOOL))completion;
- (void)pl_tokenChanged:(NSString *)token;
- (void)pl_expireGeneration:(NSUInteger)generation;
- (NSDictionary *)pl_bodyForToken:(NSString *)token;
@end

// Real coordinator; only Firebase and HTTP callbacks are controlled by tests.
@interface ControlledRegistration : PLPushRegistration
@property (nonatomic, strong) NSMutableArray *fetches;
@property (nonatomic, strong) NSMutableArray *posts;
@property (nonatomic, strong) NSMutableArray *tokens;
@end
@implementation ControlledRegistration
- (instancetype)init {
    self = [super init];
    if (self) { _fetches = [NSMutableArray array]; _posts = [NSMutableArray array]; _tokens = [NSMutableArray array]; }
    return self;
}
- (void)pl_fetchToken:(void (^)(NSString *))completion { [self.fetches addObject:[completion copy]]; }
- (void)pl_sendToken:(NSString *)token completion:(void (^)(BOOL))completion {
    [self.tokens addObject:token];
    [self.posts addObject:[completion copy]];
}
@end

@interface PushRegistrationTests : XCTestCase
@property (nonatomic, strong) ControlledRegistration *registration;
@end
@implementation PushRegistrationTests
- (void)setUp {
    [super setUp];
    PLTestAttribution = nil;
    [NSUserDefaults.standardUserDefaults removeObjectForKey:@"PLPushRegistrationPendingV1"];
    [NSUserDefaults.standardUserDefaults removeObjectForKey:@"PLFCMToken"];
    self.registration = [ControlledRegistration new];
    [self.registration configureEndpoint:@"https://example.invalid" appleAppID:@"1234"];
}
- (void)tearDown {
    [self.registration pl_expireGeneration:[[self.registration valueForKey:@"generation"] unsignedIntegerValue]];
    [self.registration.fetches removeAllObjects];
    [self.registration.posts removeAllObjects];
    self.registration = nil;
    PLTestAttribution = nil;
    [NSUserDefaults.standardUserDefaults removeObjectForKey:@"PLPushRegistrationPendingV1"];
    [NSUserDefaults.standardUserDefaults removeObjectForKey:@"PLFCMToken"];
    [super tearDown];
}
- (void)drain {
    XCTestExpectation *done = [self expectationWithDescription:@"registration callback queue"];
    dispatch_async(dispatch_get_main_queue(), ^{ [done fulfill]; });
    [self waitForExpectations:@[done] timeout:30];
}
- (void)deliverToken:(NSString *)token fetch:(NSUInteger)index {
    void (^callback)(NSString *) = self.registration.fetches[index];
    callback(token);
    [self drain];
}
- (void)deliverSuccess:(BOOL)success post:(NSUInteger)index {
    void (^callback)(BOOL) = self.registration.posts[index];
    callback(success);
    [self drain];
}
- (void)testUnchangedTokenStillPostsAfterExplicitAllowWithoutRefreshEvent {
    [NSUserDefaults.standardUserDefaults setObject:@"same-token" forKey:@"PLFCMToken"];
    __block NSUInteger completions = 0;
    [self.registration synchronizeWithCompletion:^(BOOL success) { XCTAssertTrue(success); completions++; }];
    XCTAssertEqual(self.registration.fetches.count, 1u);
    [self deliverToken:@"same-token" fetch:0];
    XCTAssertEqualObjects(self.registration.tokens, (@[@"same-token"]));
    XCTAssertEqual(completions, 0u);
    [self deliverSuccess:YES post:0];
    XCTAssertEqual(completions, 1u);
    XCTAssertFalse([NSUserDefaults.standardUserDefaults boolForKey:@"PLPushRegistrationPendingV1"]);
}
- (void)testConcurrentWaitersJoinOnePostAndCompleteExactlyOnce {
    __block NSUInteger completions = 0;
    for (NSUInteger i = 0; i < 3; i++)
        [self.registration synchronizeWithCompletion:^(BOOL success) { XCTAssertTrue(success); completions++; }];
    [self deliverToken:@"current-token" fetch:0];
    XCTAssertEqual(self.registration.fetches.count, 1u);
    XCTAssertEqual(self.registration.posts.count, 1u);
    XCTAssertEqual(completions, 0u);
    [self deliverSuccess:YES post:0];
    [self deliverSuccess:YES post:0];
    XCTAssertEqual(completions, 3u);
}
- (void)testFailureRetainsPendingAndNextRunRetries {
    __block NSUInteger completions = 0;
    [self.registration synchronizeWithCompletion:^(BOOL success) { XCTAssertFalse(success); completions++; }];
    [self deliverToken:@"token" fetch:0];
    [self deliverSuccess:NO post:0];
    XCTAssertEqual(completions, 1u);
    XCTAssertTrue([NSUserDefaults.standardUserDefaults boolForKey:@"PLPushRegistrationPendingV1"]);
    [self.registration.fetches removeAllObjects];
    [self.registration.posts removeAllObjects];
    self.registration = [ControlledRegistration new]; // New process/service instance reads persisted flag.
    [self.registration configureEndpoint:@"https://example.invalid" appleAppID:@"1234"];
    [self.registration resumePendingWithCompletion:^(BOOL success) { XCTAssertTrue(success); completions++; }];
    [self deliverToken:@"token" fetch:0];
    [self deliverSuccess:YES post:0];
    XCTAssertEqual(completions, 2u);
    XCTAssertFalse([NSUserDefaults.standardUserDefaults boolForKey:@"PLPushRegistrationPendingV1"]);
}
- (void)testMissingTokenCallbackExpiresAndLateCallbackCannotReopenHandoff {
    __block NSUInteger completions = 0;
    [self.registration synchronizeWithCompletion:^(BOOL success) { XCTAssertFalse(success); completions++; }];
    NSUInteger old = [[self.registration valueForKey:@"generation"] unsignedIntegerValue];
    [self.registration pl_expireGeneration:old];
    [self.registration pl_expireGeneration:old];
    XCTAssertEqual(completions, 1u);
    [self.registration resumePendingWithCompletion:^(BOOL success) { XCTAssertTrue(success); completions++; }];
    [self deliverToken:@"stale" fetch:0];
    XCTAssertEqual(self.registration.posts.count, 0u);
    [self deliverToken:@"fresh" fetch:1];
    [self.registration pl_expireGeneration:old];
    XCTAssertEqual(completions, 1u);
    [self deliverSuccess:YES post:0];
    XCTAssertEqual(completions, 2u);
}
- (void)testTokenRotationDuringPostIsSerializedAndOldResponseCannotFinishNewPost {
    __block NSUInteger completions = 0;
    [self.registration synchronizeWithCompletion:^(BOOL success) { XCTAssertTrue(success); completions++; }];
    [self deliverToken:@"old-token" fetch:0];
    [self.registration pl_tokenChanged:@"rotated-token"];
    XCTAssertEqual(self.registration.posts.count, 1u); // No parallel token POST.
    [self deliverSuccess:YES post:0];
    XCTAssertEqual(self.registration.fetches.count, 2u);
    [self deliverToken:@"rotated-token" fetch:1];
    [self deliverSuccess:YES post:0]; // Stale callback from previous attempt.
    XCTAssertEqual(completions, 0u);
    [self deliverSuccess:YES post:1];
    XCTAssertEqual(completions, 1u);
    XCTAssertEqualObjects(self.registration.tokens, (@[@"old-token", @"rotated-token"]));
}
- (void)testRepeatedRotationsCannotExtendOriginalDeadline {
    __block NSUInteger completions = 0;
    [self.registration synchronizeWithCompletion:^(BOOL success) { XCTAssertFalse(success); completions++; }];
    NSUInteger original = [[self.registration valueForKey:@"generation"] unsignedIntegerValue];
    [self deliverToken:@"old" fetch:0];
    [self.registration pl_tokenChanged:@"new"];
    [self deliverSuccess:YES post:0];
    [self deliverToken:@"new" fetch:1];
    [self.registration pl_expireGeneration:original];
    [self deliverSuccess:YES post:1];
    XCTAssertEqual(completions, 1u);
    XCTAssertTrue([NSUserDefaults.standardUserDefaults boolForKey:@"PLPushRegistrationPendingV1"]);
}
- (void)testRotationDuringFetchCannotBeOverwrittenByAnOlderSDKReply {
    [self.registration synchronizeWithCompletion:nil];
    [self.registration pl_tokenChanged:@"new-token"];
    [self deliverToken:@"old-token" fetch:0];
    XCTAssertEqualObjects(self.registration.tokens, (@[@"new-token"]));
    [self deliverSuccess:YES post:0];
}
- (void)testLiveTokenCannotBeOverwrittenByStoredAttribution {
    PLTestAttribution = @{@"push_token": @"stale-token", @"store_id": @"stale-id", @"os": @"wrong", @"campaign": @"keep-me"};
    NSDictionary *body = [self.registration pl_bodyForToken:@"fresh-token"];
    XCTAssertEqualObjects(body[@"push_token"], @"fresh-token");
    XCTAssertEqualObjects(body[@"store_id"], @"1234");
    XCTAssertEqualObjects(body[@"os"], @"iOS");
    XCTAssertEqualObjects(body[@"campaign"], @"keep-me");
}
@end
