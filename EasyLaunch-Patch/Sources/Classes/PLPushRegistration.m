#import "PLPushRegistration.h"
#import "PLServicesWrapper.h"
#import "PLLaunchDiagnostics.h"

static NSString *const PLRegistrationPendingKey = @"PLPushRegistrationPendingV1";

@interface PLPushRegistration ()
@property (nonatomic, copy) NSString *endpoint;
@property (nonatomic, copy) NSString *appleAppID;
@property (nonatomic, copy) NSString *observedToken;
@property (nonatomic, copy) NSString *sendingToken;
@property (nonatomic, strong) id tokenObserver;
@property (nonatomic, strong) NSMutableArray *waiters;
@property (nonatomic, strong) NSURLSessionDataTask *task;
@property (nonatomic) BOOL running;
@property (nonatomic) BOOL repeatRequired;
@property (nonatomic) NSUInteger generation;
@property (nonatomic) NSUInteger followupCount;
@property (nonatomic) NSUInteger attempt;
@property (nonatomic) NSUInteger tokenRevision;
@end

@implementation PLPushRegistration
+ (instancetype)shared {
    static PLPushRegistration *instance;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ instance = [PLPushRegistration new]; });
    return instance;
}
- (instancetype)init {
    self = [super init];
    if (self) _waiters = [NSMutableArray array];
    return self;
}
- (void)configureEndpoint:(NSString *)endpoint appleAppID:(NSString *)appleAppID {
    NSAssert(NSThread.isMainThread, @"Registration state belongs to main");
    // The app has one immutable backend per build. Do not mutate an in-flight
    // request's destination/configuration from an obsolete preload instance.
    if (!self.endpoint.length) {
        self.endpoint = endpoint;
        self.appleAppID = appleAppID;
    }
    if (self.tokenObserver || !self.endpoint.length) return;
    self.observedToken = [NSUserDefaults.standardUserDefaults stringForKey:@"PLFCMToken"];
    __weak typeof(self) weakSelf = self;
    self.tokenObserver = [NSNotificationCenter.defaultCenter addObserverForName:PLFCMTokenDidUpdateNotification
        object:nil queue:NSOperationQueue.mainQueue usingBlock:^(NSNotification *note) {
        id token = note.userInfo[@"token"];
        if ([token isKindOfClass:NSString.class] && [token length]) [weakSelf pl_tokenChanged:token];
    }];
    [PLLaunchDiagnostics record:PLLaunchEventFCMObserverReady value:1];
}
- (void)pl_tokenChanged:(NSString *)token {
    if ([self.observedToken isEqualToString:token]) return;
    self.observedToken = token;
    self.tokenRevision++;
    if (self.running) {
        // Token delivery while fetching is already included in that fetch.
        // A rotation DURING the POST needs one serialized follow-up POST.
        if (self.sendingToken && ![self.sendingToken isEqualToString:token]) self.repeatRequired = YES;
        return;
    }
    [self synchronizeWithCompletion:nil];
}
- (void)synchronizeWithCompletion:(void (^)(BOOL))completion {
    if (!NSThread.isMainThread) {
        dispatch_async(dispatch_get_main_queue(), ^{ [self synchronizeWithCompletion:completion]; });
        return;
    }
    if (completion) [self.waiters addObject:[completion copy]];
    // Always sync after Allow even if the token has not changed. Waiting for a
    // token-refresh event can miss the POST on the delayed-consent path.
    [NSUserDefaults.standardUserDefaults setBool:YES forKey:PLRegistrationPendingKey];
    if (self.running) return;
    self.running = YES;
    self.followupCount = 0;
    NSUInteger generation = ++self.generation;
    __weak typeof(self) weakSelf = self;
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(15 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        [weakSelf pl_expireGeneration:generation];
    });
    [self pl_beginGeneration:generation];
}
- (void)resumePendingWithCompletion:(void (^)(BOOL))completion {
    if (!NSThread.isMainThread) {
        dispatch_async(dispatch_get_main_queue(), ^{ [self resumePendingWithCompletion:completion]; });
        return;
    }
    if (self.running || [NSUserDefaults.standardUserDefaults boolForKey:PLRegistrationPendingKey]) {
        [self synchronizeWithCompletion:completion];
    } else if (completion) completion(YES);
}
- (void)pl_beginGeneration:(NSUInteger)generation {
    NSUInteger attempt = ++self.attempt;
    NSUInteger tokenRevision = self.tokenRevision;
    self.sendingToken = nil;
    self.repeatRequired = NO;
    [self pl_fetchToken:^(NSString *token) {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (!self.running || generation != self.generation || attempt != self.attempt || self.sendingToken) return;
            NSString *current = self.tokenRevision != tokenRevision ? self.observedToken : token;
            if (!current.length || !self.endpoint.length) { [self pl_finish:NO generation:generation]; return; }
            self.sendingToken = current;
            self.observedToken = current;
            [self pl_sendToken:current completion:^(BOOL success) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    if (!self.running || generation != self.generation || attempt != self.attempt) return;
                    if (self.repeatRequired && self.followupCount++ == 0) {
                        // Keep the ORIGINAL 15s deadline across token rotations.
                        [self pl_beginGeneration:generation];
                    } else {
                        [self pl_finish:success && !self.repeatRequired generation:generation];
                    }
                });
            }];
        });
    }];
}
- (void)pl_fetchToken:(void (^)(NSString *))completion {
    [PLServicesWrapper fetchFirebasePushTokenWithCompletion:^(NSString *token, NSError *error) {
        if (error) [PLLaunchDiagnostics record:PLLaunchEventTokenSyncError value:error.code];
        completion(token);
    }];
}
- (NSDictionary *)pl_bodyForToken:(NSString *)token {
    NSMutableDictionary *body = [[PLServicesWrapper storedAppsFlyerConversionData] mutableCopy] ?: [NSMutableDictionary dictionary];
    // Live registration fields must override persisted attribution, never the
    // other way round (stored conversion data may contain an old push_token).
    body[@"bundle_id"] = NSBundle.mainBundle.bundleIdentifier ?: @"";
    body[@"platform"] = @"ios";
    body[@"os"] = @"iOS";
    body[@"app_version"] = [NSBundle.mainBundle objectForInfoDictionaryKey:@"CFBundleShortVersionString"] ?: @"";
    body[@"store_id"] = self.appleAppID ?: @"";
    body[@"locale"] = NSLocale.preferredLanguages.firstObject ?: @"en";
    NSString *afID = [PLServicesWrapper appsFlyerDeviceId];
    if (afID.length) body[@"af_id"] = afID;
    body[@"firebase_project_id"] = [PLServicesWrapper firebaseProjectId] ?: @"";
    body[@"push_token"] = token;
    return body;
}
- (void)pl_sendToken:(NSString *)token completion:(void (^)(BOOL))completion {
    NSURL *url = [NSURL URLWithString:[self.endpoint stringByAppendingString:@"/config.php"]];
    NSData *data = [NSJSONSerialization dataWithJSONObject:[self pl_bodyForToken:token] options:0 error:nil];
    if (!url.host.length || !data) { completion(NO); return; }
    NSMutableURLRequest *request = [NSMutableURLRequest requestWithURL:url];
    request.HTTPMethod = @"POST";
    request.HTTPBody = data;
    request.timeoutInterval = 8;
    [request setValue:@"application/json" forHTTPHeaderField:@"Content-Type"];
    NSInteger operation = [PLLaunchDiagnostics record:PLLaunchEventTokenSyncStarted value:1];
    self.task = [NSURLSession.sharedSession dataTaskWithRequest:request completionHandler:^(NSData *responseData, NSURLResponse *response, NSError *error) {
        NSInteger status = [response isKindOfClass:NSHTTPURLResponse.class] ? [(NSHTTPURLResponse *)response statusCode] : 0;
        [PLLaunchDiagnostics record:PLLaunchEventTokenSyncHTTP value:status operation:operation];
        if (error) [PLLaunchDiagnostics record:PLLaunchEventTokenSyncError value:error.code operation:operation];
        BOOL success = !error && status >= 200 && status < 300;
        // Honor an explicit backend rejection; never interpret its URL as a
        // navigation instruction. A successful transport is not proof of binding.
        id json = responseData.length ? [NSJSONSerialization JSONObjectWithData:responseData options:0 error:nil] : nil;
        id ok = [json isKindOfClass:NSDictionary.class] ? json[@"ok"] : nil;
        if ([ok respondsToSelector:@selector(boolValue)] && ![ok boolValue]) success = NO;
        completion(success);
    }];
    [self.task resume];
}
- (void)pl_expireGeneration:(NSUInteger)generation {
    if (!self.running || generation != self.generation) return;
    [PLLaunchDiagnostics record:PLLaunchEventTokenSyncError value:NSURLErrorTimedOut];
    [self pl_finish:NO generation:generation];
}
- (void)pl_finish:(BOOL)success generation:(NSUInteger)generation {
    if (!self.running || generation != self.generation) return;
    self.running = NO;
    self.generation++; // Late SDK/HTTP completion cannot reopen a route.
    [self.task cancel];
    self.task = nil;
    self.sendingToken = nil;
    [NSUserDefaults.standardUserDefaults setBool:!success forKey:PLRegistrationPendingKey];
    NSArray *callbacks = [self.waiters copy];
    [self.waiters removeAllObjects];
    for (void (^callback)(BOOL) in callbacks) callback(success);
}
- (void)dealloc {
    if (self.tokenObserver) [NSNotificationCenter.defaultCenter removeObserver:self.tokenObserver];
    [self.task cancel];
}
@end
