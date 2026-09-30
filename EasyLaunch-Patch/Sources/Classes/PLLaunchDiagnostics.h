#import <Foundation/Foundation.h>

// Only fixed event names and numeric values may enter this journal. Never pass
// URLs, tokens, payloads, NSError descriptions or request/response bodies.
typedef NS_ENUM(NSInteger, PLLaunchEvent) {
    PLLaunchEventProcessStart = 1,
    PLLaunchEventStoredTokenPresent,
    PLLaunchEventPushAccepted,
    PLLaunchEventPreloadPath,
    PLLaunchEventPermissionStatus,
    PLLaunchEventSkipAgeSeconds,
    PLLaunchEventPromptDecision,
    PLLaunchEventAllowTapped,
    PLLaunchEventSkipTapped,
    PLLaunchEventAuthorizationResult,
    PLLaunchEventAuthorizationError,
    PLLaunchEventSettingsOpened,
    PLLaunchEventAPNsRegistrationRequested,
    PLLaunchEventAPNsReceived,
    PLLaunchEventAPNsError,
    PLLaunchEventFCMReceived,
    PLLaunchEventFCMObserverReady,
    PLLaunchEventTokenSyncStarted,
    PLLaunchEventTokenSyncHTTP,
    PLLaunchEventTokenSyncError,
    PLLaunchEventConfigStarted,
    PLLaunchEventConfigHTTP,
    PLLaunchEventConfigError,
    PLLaunchEventConfigURLPresent,
    PLLaunchEventPreloadHandoff,
    PLLaunchEventWebRoute,
    PLLaunchEventForegroundPermission
};

@interface PLLaunchDiagnostics : NSObject
// Returned sequence identifies a request; pass it as operation on its result.
+ (NSInteger)record:(PLLaunchEvent)event value:(NSInteger)value;
+ (NSInteger)record:(PLLaunchEvent)event value:(NSInteger)value operation:(NSInteger)operation;
+ (NSString *)report;
@end
