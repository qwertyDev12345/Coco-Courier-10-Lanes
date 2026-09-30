#import <Foundation/Foundation.h>

// Serializes the existing config.php token update. Never owns or returns a
// navigation URL: a late registration response cannot replace a tapped push.
@interface PLPushRegistration : NSObject
+ (instancetype)shared;
- (void)configureEndpoint:(NSString *)endpoint appleAppID:(NSString *)appleAppID;
// Main-thread completion, once; bounded even if an SDK callback is missing.
- (void)synchronizeWithCompletion:(void (^)(BOOL success))completion;
- (void)resumePendingWithCompletion:(void (^)(BOOL success))completion;
@end
