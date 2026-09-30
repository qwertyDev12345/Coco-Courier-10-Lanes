#import "PLLaunchDiagnostics.h"

static NSString *const PLJournalKey = @"PLLaunchDiagnosticsV1";
static const NSUInteger PLJournalLimit = 80;

static NSString *PLLaunchEventName(PLLaunchEvent event) {
    switch (event) {
        case PLLaunchEventProcessStart: return @"process start (cold payload URL present)";
        case PLLaunchEventStoredTokenPresent: return @"persisted FCM token present";
        case PLLaunchEventPushAccepted: return @"push accepted (tap generation)";
        case PLLaunchEventPreloadPath: return @"preload path (1=push bypass,2=config,3=unity)";
        case PLLaunchEventPermissionStatus: return @"permission status (0=notDetermined,1=denied,2=authorized,3=provisional,4=ephemeral)";
        case PLLaunchEventSkipAgeSeconds: return @"previous skip age seconds (-1=absent)";
        case PLLaunchEventPromptDecision: return @"prompt decision (0=bypass,1=show,2=already handled this session)";
        case PLLaunchEventAllowTapped: return @"Allow tapped (OS status before request)";
        case PLLaunchEventSkipTapped: return @"Skip tapped";
        case PLLaunchEventAuthorizationResult: return @"OS authorization granted";
        case PLLaunchEventAuthorizationError: return @"OS authorization NSError.code";
        case PLLaunchEventSettingsOpened: return @"Settings URL opened (NOT permission granted)";
        case PLLaunchEventAPNsRegistrationRequested: return @"APNs registration requested (1=permission flow,2=Firebase init)";
        case PLLaunchEventAPNsReceived: return @"APNs token received (nonempty)";
        case PLLaunchEventAPNsError: return @"APNs registration NSError.code";
        case PLLaunchEventFCMReceived: return @"FCM token received (nonempty)";
        case PLLaunchEventFCMObserverReady: return @"FCM observer ready";
        case PLLaunchEventTokenSyncStarted: return @"token sync started (1=token present,0=skipped: no token)";
        case PLLaunchEventTokenSyncHTTP: return @"token sync HTTP status (NOT backend acceptance)";
        case PLLaunchEventTokenSyncError: return @"token sync NSError.code";
        case PLLaunchEventConfigStarted: return @"config started (token present)";
        case PLLaunchEventConfigHTTP: return @"config HTTP status";
        case PLLaunchEventConfigError: return @"config NSError.code";
        case PLLaunchEventConfigURLPresent: return @"config returned usable URL";
        case PLLaunchEventPreloadHandoff: return @"preload handoff (1=push,2=config/fallback,3=unity)";
        case PLLaunchEventWebRoute: return @"WebView route accepted";
        case PLLaunchEventForegroundPermission: return @"foreground OS permission snapshot";
    }
    return nil;
}

// Whitelist even data read back from preferences; do not render arbitrary
// dictionary contents if another version has written a different format.
static NSArray<NSDictionary *> *PLJournalEntries(NSUserDefaults *defaults) {
    id stored = [defaults objectForKey:PLJournalKey];
    if (![stored isKindOfClass:NSArray.class]) return @[];
    NSMutableArray *entries = [NSMutableArray array];
    for (id entry in stored) {
        if (![entry isKindOfClass:NSDictionary.class]) continue;
        BOOL valid = YES;
        for (NSString *key in @[@"seq", @"event", @"value", @"op", @"utc", @"pid"])
            if (![entry[key] isKindOfClass:NSNumber.class]) { valid = NO; break; }
        if (valid && PLLaunchEventName([entry[@"event"] integerValue])) [entries addObject:entry];
    }
    if (entries.count > PLJournalLimit)
        [entries removeObjectsInRange:NSMakeRange(0, entries.count - PLJournalLimit)];
    return entries;
}

@implementation PLLaunchDiagnostics
+ (NSInteger)record:(PLLaunchEvent)event value:(NSInteger)value {
    return [self record:event value:value operation:0];
}
+ (NSInteger)record:(PLLaunchEvent)event value:(NSInteger)value operation:(NSInteger)operation {
    if (!PLLaunchEventName(event)) return 0;
    @synchronized (self) {
        NSUserDefaults *defaults = NSUserDefaults.standardUserDefaults;
        NSMutableArray *entries = [PLJournalEntries(defaults) mutableCopy];
        NSInteger sequence = [entries.lastObject[@"seq"] integerValue] + 1;
        [entries addObject:@{@"seq": @(sequence), @"event": @(event), @"value": @(value),
                             @"op": @(operation), @"utc": @(NSDate.date.timeIntervalSince1970),
                             @"pid": @(NSProcessInfo.processInfo.processIdentifier)}];
        if (entries.count > PLJournalLimit) [entries removeObjectAtIndex:0];
        // Small bounded local history survives a normal process restart. No
        // synchronous disk flush or network request on the UI/SDK callback path.
        [defaults setObject:entries forKey:PLJournalKey];
        return sequence;
    }
}
+ (NSString *)report {
    NSArray *entries;
    @synchronized (self) { entries = PLJournalEntries(NSUserDefaults.standardUserDefaults); }
    NSISO8601DateFormatter *formatter = [NSISO8601DateFormatter new];
    formatter.formatOptions = NSISO8601DateFormatWithInternetDateTime | NSISO8601DateFormatWithFractionalSeconds;
    NSMutableString *report = [NSMutableString stringWithString:
        @"Startup journal (last 80 observations; UTC; spans processes)\nMissing event is not proof an operation never happened. HTTP success does not confirm token binding.\n"];
    for (NSDictionary *entry in entries) {
        NSString *date = [formatter stringFromDate:[NSDate dateWithTimeIntervalSince1970:[entry[@"utc"] doubleValue]]];
        [report appendFormat:@"#%ld %@ pid=%ld op=%ld %@ = %ld\n",
            (long)[entry[@"seq"] integerValue], date, (long)[entry[@"pid"] integerValue],
            (long)[entry[@"op"] integerValue], PLLaunchEventName([entry[@"event"] integerValue]),
            (long)[entry[@"value"] integerValue]];
    }
    if (!entries.count) [report appendString:@"No observations recorded.\n"];
    return report;
}
@end
