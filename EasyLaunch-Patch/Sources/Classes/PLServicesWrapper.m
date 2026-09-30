// PLServicesWrapper.m
// Чистый Objective-C (.m, не .mm) — SPM-фреймворки доступны напрямую.
//
// Добавьте пакеты в Xcode → File → Add Package Dependencies:
//   Firebase:    https://github.com/firebase/firebase-ios-sdk
//                Products: FirebaseCore, FirebaseMessaging
//   AppsFlyer:   https://github.com/AppsFlyerSDK/appsflyer-apple-sdk
//                Product: AppsFlyerLib
//
// Управление флагами: раскомментируйте #define после добавления пакетов.

#import "PLLaunchDiagnostics.h"

#define PL_HAS_FIREBASE    1
#define PL_HAS_APPSFLYER   1

#import "PLServicesWrapper.h"
#import "PluginBase/AppDelegateListener.h"

#ifdef PL_HAS_FIREBASE
  #import <FirebaseCore/FirebaseCore.h>
#  if __has_include(<FirebaseMessaging/FirebaseMessaging.h>)
#    import <FirebaseMessaging/FirebaseMessaging.h>
#  endif
#endif

#ifdef PL_HAS_APPSFLYER
  #import <AppsFlyerLib/AppsFlyerLib.h>
#endif

// ─────────────────────────────────────────────────────────────────────────────
// MARK: - AppsFlyer internal delegate
// ─────────────────────────────────────────────────────────────────────────────

#ifdef PL_HAS_APPSFLYER

@interface _PLAppsFlyerDelegate : NSObject <AppsFlyerLibDelegate>
@property (nonatomic, copy)   PLAttributionBlock completion;
@property (nonatomic, assign) BOOL               finished;
@property (nonatomic, strong) NSTimer           *timeoutTimer;
@end

@implementation _PLAppsFlyerDelegate

- (void)onConversionDataSuccess:(NSDictionary *)conversionInfo
{
    [self pl_finishWithData:conversionInfo error:nil];
}

- (void)onConversionDataFail:(NSError *)error
{
    NSLog(@"[PLServicesWrapper] AppsFlyer GCD error: %@", error);
    [self pl_finishWithData:nil error:error];
}

- (void)onAppOpenAttribution:(NSDictionary *)attributionData  {}
- (void)onAppOpenAttributionFailure:(NSError *)error          {}

- (void)pl_finishWithData:(NSDictionary *)data error:(NSError *)error
{
    if (![NSThread isMainThread]) {
        dispatch_async(dispatch_get_main_queue(), ^{ [self pl_finishWithData:data error:error]; });
        return;
    }
    if (self.finished) return;
    self.finished = YES;

    [self.timeoutTimer invalidate];
    self.timeoutTimer = nil;

    // Persist conversion data for lifetime of the app installation
    if (data) {
        NSError *serr = nil;
        NSData *d = [NSJSONSerialization dataWithJSONObject:data options:0 error:&serr];
        if (d) {
            [[NSUserDefaults standardUserDefaults] setObject:d forKey:@"PLAppsFlyerConversionData_v1"];
            [[NSUserDefaults standardUserDefaults] synchronize];
            NSLog(@"[PLServicesWrapper] Saved AppsFlyer conversion data (%lu bytes)", (unsigned long)d.length);
        } else {
            NSLog(@"[PLServicesWrapper] Failed to serialize conversion data: %@", serr);
        }
    }

    if (self.completion) {
        self.completion(data, error);
        self.completion = nil;
    }
}

@end

#endif   // PL_HAS_APPSFLYER

// ─────────────────────────────────────────────────────────────────────────────
// MARK: - Firebase Messaging delegate tracker
// ─────────────────────────────────────────────────────────────────────────────

NSNotificationName const PLFCMTokenDidUpdateNotification = @"PLFCMTokenDidUpdateNotification";

#ifdef PL_HAS_FIREBASE
#if __has_include(<FirebaseMessaging/FirebaseMessaging.h>)

@interface _PLMessagingTracker : NSObject <FIRMessagingDelegate>
+ (instancetype)shared;
@end

@implementation _PLMessagingTracker

+ (instancetype)shared
{
    static _PLMessagingTracker *s = nil;
    static dispatch_once_t once;
    dispatch_once(&once, ^{ s = [_PLMessagingTracker new]; });
    return s;
}

- (void)messaging:(FIRMessaging *)messaging didReceiveRegistrationToken:(NSString *)fcmToken
{
    [PLLaunchDiagnostics record:PLLaunchEventFCMReceived value:fcmToken.length > 0];
    if (fcmToken.length == 0) {
        NSLog(@"[PLServicesWrapper] FCM token callback fired but token is empty");
        return;
    }
    NSLog(@"[PLServicesWrapper] FCM token received/updated: %.10s…", fcmToken.UTF8String);
    // Persist so firebasePushToken can return it synchronously on subsequent calls
    [[NSUserDefaults standardUserDefaults] setObject:fcmToken forKey:@"PLFCMToken"];
    [[NSUserDefaults standardUserDefaults] synchronize];
    [[NSNotificationCenter defaultCenter]
        postNotificationName:PLFCMTokenDidUpdateNotification
                      object:nil
                    userInfo:@{@"token": fcmToken}];
}

@end

#endif   // FirebaseMessaging available
#endif   // PL_HAS_FIREBASE
// ─────────────────────────────────────────────────────────────────────────────

@interface PLServicesWrapper ()
+ (void)pl_fetchTokenAfterAPNsUntil:(NSTimeInterval)deadline completion:(void (^)(NSString *, NSError *))completion;
@end

// Удерживаем делегат AF живым до получения callback
#ifdef PL_HAS_APPSFLYER
static _PLAppsFlyerDelegate *s_afDelegate = nil;
#endif

@implementation PLServicesWrapper

// Key for stored AppsFlyer conversion JSON
static NSString * const kPLAppsFlyerConversionKey = @"PLAppsFlyerConversionData_v1";
static NSData *s_pendingAPNsToken = nil;

// Retrieve stored conversion data (if any)
+ (nullable NSDictionary *)storedAppsFlyerConversionData
{
    NSData *d = [[NSUserDefaults standardUserDefaults] objectForKey:kPLAppsFlyerConversionKey];
    if (!d) return nil;
    NSError *err = nil;
    id obj = [NSJSONSerialization JSONObjectWithData:d options:0 error:&err];
    if (err || ![obj isKindOfClass:[NSDictionary class]]) {
        return nil;
    }
    return obj;
}

// Clear stored conversion data
+ (void)clearStoredAppsFlyerConversionData
{
    [[NSUserDefaults standardUserDefaults] removeObjectForKey:kPLAppsFlyerConversionKey];
    [[NSUserDefaults standardUserDefaults] synchronize];
}

// Возвращает Firebase project id (если есть)
+ (nullable NSString *)firebaseProjectId
{
#ifdef PL_HAS_FIREBASE
    if ([FIRApp defaultApp] == nil) return nil;
    FIRApp *app = [FIRApp defaultApp];
    if ([app respondsToSelector:@selector(options)]) {
        id opts = [app performSelector:@selector(options)];
        if (opts) {
            // Try `projectID` then `projectId` selectors if available
            if ([opts respondsToSelector:@selector(projectID)]) {
                id proj = [opts performSelector:@selector(projectID)];
                if ([proj isKindOfClass:[NSString class]] && [(NSString *)proj length] > 0) return proj;
            }
            if ([opts respondsToSelector:@selector(projectId)]) {
                id proj = [opts performSelector:@selector(projectId)];
                if ([proj isKindOfClass:[NSString class]] && [(NSString *)proj length] > 0) return proj;
            }
        }
    }
#endif
    return nil;
}

// Возвращает FCM token (push token) если доступен
+ (nullable NSString *)firebasePushToken
{
#ifdef PL_HAS_FIREBASE
#if __has_include(<FirebaseMessaging/FirebaseMessaging.h>)
    // This accessor is cache-only. A semaphore here could block the main queue
    // on which Firebase needs to deliver its token callback.
    NSString *stored = [[NSUserDefaults standardUserDefaults] stringForKey:@"PLFCMToken"];
    if (stored.length > 0) return stored;

    if (![FIRApp defaultApp]) return nil;
    return [FIRMessaging messaging].FCMToken;
#endif
#endif
    return nil;
}

+ (void)fetchFirebasePushTokenWithCompletion:(void (^)(NSString *, NSError *))completion
{
    if (!completion) return;
    if (!NSThread.isMainThread) {
        dispatch_async(dispatch_get_main_queue(), ^{ [self fetchFirebasePushTokenWithCompletion:completion]; });
        return;
    }
    __block BOOL finished = NO;
    void (^finish)(NSString *, NSError *) = ^(NSString *token, NSError *error) {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (finished) return;
            finished = YES;
            if (token.length) {
                NSString *previous = [NSUserDefaults.standardUserDefaults stringForKey:@"PLFCMToken"];
                [NSUserDefaults.standardUserDefaults setObject:token forKey:@"PLFCMToken"];
                if (![previous isEqualToString:token]) {
                    [NSNotificationCenter.defaultCenter postNotificationName:PLFCMTokenDidUpdateNotification object:nil userInfo:@{@"token": token}];
                }
            }
            completion(token, error);
        });
    };
    NSTimeInterval deadline = NSProcessInfo.processInfo.systemUptime + 6;
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(6 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
        finish(nil, [NSError errorWithDomain:NSURLErrorDomain code:NSURLErrorTimedOut userInfo:nil]);
    });
    [self pl_fetchTokenAfterAPNsUntil:deadline completion:finish];
}

+ (void)pl_fetchTokenAfterAPNsUntil:(NSTimeInterval)deadline completion:(void (^)(NSString *, NSError *))completion
{
#if defined(PL_HAS_FIREBASE) && __has_include(<FirebaseMessaging/FirebaseMessaging.h>)
    if ([FIRApp defaultApp]) {
        FIRMessaging *messaging = [FIRMessaging messaging];
        if (messaging.APNSToken.length) {
            [messaging tokenWithCompletion:completion];
            return;
        }
        if (NSProcessInfo.processInfo.systemUptime < deadline) {
            dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.2 * NSEC_PER_SEC)), dispatch_get_main_queue(), ^{
                [self pl_fetchTokenAfterAPNsUntil:deadline completion:completion];
            });
            return;
        }
    }
#endif
    completion(nil, [NSError errorWithDomain:@"PLPushRegistration" code:1 userInfo:nil]);
}

// ── Firebase ──────────────────────────────────────────────────────────────────

+ (void)configureFirebase:(void (^ _Nullable)(NSError * _Nullable))completion
{
    if (![NSThread isMainThread]) {
        dispatch_async(dispatch_get_main_queue(), ^{ [self configureFirebase:completion]; });
        return;
    }
#ifdef PL_HAS_FIREBASE
    // Без @try/@catch (отключены Unity) — сначала проверяем plist
    NSString *plistPath = [[NSBundle mainBundle] pathForResource:@"GoogleService-Info"
                                                          ofType:@"plist"];
    if (!plistPath) {
        NSError *err = [NSError errorWithDomain:@"PLServicesWrapper"
                                           code:1001
                                       userInfo:@{
            NSLocalizedDescriptionKey: @"GoogleService-Info.plist not found in bundle"
        }];
        NSLog(@"[PLServicesWrapper] Firebase error: %@", err.localizedDescription);
        if (completion) completion(err);
        return;
    }

    if ([FIRApp defaultApp] == nil) [FIRApp configure];

    if ([FIRApp defaultApp] != nil) {
        NSLog(@"[PLServicesWrapper] Firebase configured");
#if __has_include(<FirebaseMessaging/FirebaseMessaging.h>)
        static dispatch_once_t messagingSetup;
        dispatch_once(&messagingSetup, ^{
            [FIRMessaging messaging].delegate = [_PLMessagingTracker shared];
            if (s_pendingAPNsToken) {
                [FIRMessaging messaging].APNSToken = s_pendingAPNsToken;
                s_pendingAPNsToken = nil;
            }
            // CustomAppController forwards the typed NSData callback directly.
            [PLLaunchDiagnostics record:PLLaunchEventAPNsRegistrationRequested value:2];
            [[UIApplication sharedApplication] registerForRemoteNotifications];
        });
#endif
        if (completion) completion(nil);
    } else {
        NSError *err = [NSError errorWithDomain:@"PLServicesWrapper"
                                           code:1002
                                       userInfo:@{
            NSLocalizedDescriptionKey: @"FIRApp.configure completed but defaultApp is nil"
        }];
        NSLog(@"[PLServicesWrapper] Firebase error: %@", err.localizedDescription);
        if (completion) completion(err);
    }
#else
    NSLog(@"[PLServicesWrapper] Firebase disabled (PL_HAS_FIREBASE not set)");
    if (completion) completion(nil);
#endif
}

+ (void)setAPNsDeviceToken:(NSData *)deviceToken
{
    if (![deviceToken isKindOfClass:NSData.class] || deviceToken.length == 0) return;
    dispatch_async(dispatch_get_main_queue(), ^{
#if defined(PL_HAS_FIREBASE) && __has_include(<FirebaseMessaging/FirebaseMessaging.h>)
        if ([FIRApp defaultApp]) {
            [FIRMessaging messaging].APNSToken = deviceToken;
        } else {
            s_pendingAPNsToken = [deviceToken copy];
        }
#endif
    });
}

+ (BOOL)isFirebaseConfigured
{
#ifdef PL_HAS_FIREBASE
    return [FIRApp defaultApp] != nil;
#else
    return NO;
#endif
}

// ── AppsFlyer device ID ─────────────────────────────────────────────────────────────────

+ (nullable NSString *)appsFlyerDeviceId
{
#ifdef PL_HAS_APPSFLYER
    NSString *uid = [[AppsFlyerLib shared] getAppsFlyerUID];
    if ([uid isKindOfClass:[NSString class]] && uid.length > 0) return uid;
#endif
    return nil;
}

// ── AppsFlyer ─────────────────────────────────────────────────────────────────

+ (void)startAppsFlyerWithDevKey:(NSString *)devKey
                      appleAppId:(NSString *)appleAppId
                gcdWaitTimeout:(NSTimeInterval)timeout
                      completion:(PLAttributionBlock)completion
{
#ifdef PL_HAS_APPSFLYER
    if (devKey.length == 0) {
        NSLog(@"[PLServicesWrapper] AppsFlyer devKey is empty, skipping");
        if (completion) completion(nil, nil);
        return;
    }

    // Настройка и запуск должны быть на главном потоке
    dispatch_async(dispatch_get_main_queue(), ^{
        _PLAppsFlyerDelegate *delegate = [_PLAppsFlyerDelegate new];
        delegate.completion = completion;
        delegate.finished   = NO;
        s_afDelegate = delegate;   // удерживаем сильной ссылкой

        AppsFlyerLib *af = [AppsFlyerLib shared];
        af.appsFlyerDevKey = devKey;
        af.appleAppID      = appleAppId;
        af.delegate        = delegate;
#ifdef DEBUG
        af.isDebug = YES;
#endif

        // Таймер: если GCD не придёт — идём дальше
        NSTimeInterval t = (timeout > 0) ? timeout : 3.0;
        delegate.timeoutTimer = [NSTimer scheduledTimerWithTimeInterval:t
                                                                repeats:NO
                                                                  block:^(NSTimer *_) {
            NSLog(@"[PLServicesWrapper] AppsFlyer GCD timeout");
            [delegate pl_finishWithData:nil error:nil];
        }];

        [af start];
        NSLog(@"[PLServicesWrapper] AppsFlyer started (timeout=%.0fs)", t);

            // Register to receive Unity's open-URL notification so we can forward
            // deep-links to AppsFlyer without touching UnityAppController.
            extern NSString* const kUnityOnOpenURL;
            [[NSNotificationCenter defaultCenter] addObserverForName:kUnityOnOpenURL
                                                              object:nil
                                                               queue:[NSOperationQueue mainQueue]
                                                          usingBlock:^(NSNotification * _Nonnull note) {
                NSDictionary *info = note.userInfo;
                if (!info) return;
                NSURL *u = info[@"url"];
                if (u && [u isKindOfClass:[NSURL class]]) {
                    [PLServicesWrapper handleOpenURL:u options:nil];
                }
            }];

            // Ensure AppsFlyer start is called when app becomes active (per integration guide)
            [[NSNotificationCenter defaultCenter] addObserverForName:UIApplicationDidBecomeActiveNotification
                                                              object:nil
                                                               queue:[NSOperationQueue mainQueue]
                                                          usingBlock:^(NSNotification * _Nonnull note) {
                AppsFlyerLib *af2 = [AppsFlyerLib shared];
                if (af2 && [af2 respondsToSelector:@selector(start)]) {
                    dispatch_async(dispatch_get_main_queue(), ^{
                        [af2 start];
                    });
                }
            }];
    });

#else
    NSLog(@"[PLServicesWrapper] AppsFlyer disabled (PL_HAS_APPSFLYER not set)");
    if (completion) completion(nil, nil);
#endif
}


// Forward openURL to AppsFlyer SDK if available
+ (void)handleOpenURL:(NSURL *)url options:(NSDictionary<UIApplicationOpenURLOptionsKey,id> * _Nullable)options
{
#ifdef PL_HAS_APPSFLYER
    AppsFlyerLib *af = [AppsFlyerLib shared];
    if (!af) return;
    // Prefer typed API if available
    SEL sel = NSSelectorFromString(@"handleOpenUrl:options:");
    if ([af respondsToSelector:sel]) {
        #pragma clang diagnostic push
        #pragma clang diagnostic ignored "-Warc-performSelector-leaks"
        [af performSelector:sel withObject:url withObject:options];
        #pragma clang diagnostic pop
        return;
    }

    // Fallback for older SDKs: handleOpenURL without options
    SEL sel2 = NSSelectorFromString(@"handleOpenURL:");
    if ([af respondsToSelector:sel2]) {
        #pragma clang diagnostic push
        #pragma clang diagnostic ignored "-Warc-performSelector-leaks"
        [af performSelector:sel2 withObject:url];
        #pragma clang diagnostic pop
    }
#endif
}


// Forward universal links / userActivity to AppsFlyer
+ (void)continueUserActivity:(NSUserActivity *)userActivity restorationHandler:(void (^)(NSArray * _Nullable))restorationHandler
{
#ifdef PL_HAS_APPSFLYER
    AppsFlyerLib *af = [AppsFlyerLib shared];
    if (!af) return;
    SEL sel = NSSelectorFromString(@"continueUserActivity:restorationHandler:");
    if ([af respondsToSelector:sel]) {
        #pragma clang diagnostic push
        #pragma clang diagnostic ignored "-Warc-performSelector-leaks"
        [af performSelector:sel withObject:userActivity withObject:restorationHandler];
        #pragma clang diagnostic pop
    }
#endif
}

@end
