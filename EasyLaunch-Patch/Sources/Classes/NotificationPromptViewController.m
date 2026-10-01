#import "NotificationPromptViewController.h"
#import <QuartzCore/QuartzCore.h>
#import <math.h>

// Bake the fade into the background pixels once. Rotation scales one UIImage;
// there is no separate dimming view or layer that can move independently.
static UIImage *PLDimmedNotificationBackground(UIImage *source)
{
    if (!source || source.size.width <= 0 || source.size.height <= 0) return source;
    UIGraphicsImageRendererFormat *format = [UIGraphicsImageRendererFormat defaultFormat];
    format.scale = source.scale;
    format.opaque = YES;
    format.preferredRange = UIGraphicsImageRendererFormatRangeStandard;
    UIGraphicsImageRenderer *renderer = [[UIGraphicsImageRenderer alloc]
        initWithSize:source.size format:format];
    return [renderer imageWithActions:^(UIGraphicsImageRendererContext *context) {
        CGRect bounds = (CGRect){CGPointZero, source.size};
        [[UIColor blackColor] setFill];
        [context fillRect:bounds];
        [source drawInRect:bounds];
        NSArray *colors = @[(id)[UIColor colorWithWhite:0 alpha:0.45].CGColor,
                            (id)[UIColor colorWithWhite:0 alpha:0.65].CGColor];
        CGFloat stops[] = {0, 1};
        CGColorSpaceRef space = CGColorSpaceCreateDeviceRGB();
        CGGradientRef gradient = CGGradientCreateWithColors(space, (__bridge CFArrayRef)colors, stops);
        CGContextDrawLinearGradient(context.CGContext, gradient, CGPointZero,
                                    CGPointMake(0, source.size.height), 0);
        CGGradientRelease(gradient);
        CGColorSpaceRelease(space);
    }];
}

@interface NotificationPromptViewController ()
@property (nonatomic, strong) UIImageView *bgImageView;
@property (nonatomic, strong) UIView *contentView;
@property (nonatomic, strong) UILabel *titleLabel;
@property (nonatomic, strong) UILabel *messageLabel;
@property (nonatomic, strong) UIButton *allowButton;
@property (nonatomic, strong) UIButton *cancelButton;
@property (nonatomic, copy) NotificationPromptHandler allowHandler;
@property (nonatomic, copy) NotificationPromptHandler cancelHandler;
@property (nonatomic, strong) UIScrollView *scrollView;
@property (nonatomic, assign) BOOL handledAction;
@property (nonatomic, strong) NSLayoutConstraint *titleMinimumHeight;
@property (nonatomic, strong) NSLayoutConstraint *messageMinimumHeight;
@end

@implementation NotificationPromptViewController

- (instancetype)initWithTitle:(NSString *)title
                      message:(NSString *)message
              backgroundImage:(UIImage *)image
                 allowHandler:(NotificationPromptHandler)allowHandler
                  cancelHandler:(NotificationPromptHandler)cancelHandler
{
    self = [super initWithNibName:nil bundle:nil];
    if (!self) return nil;

    _allowHandler = [allowHandler copy];
    _cancelHandler = [cancelHandler copy];

    self.modalPresentationStyle = UIModalPresentationFullScreen;
    self.view.backgroundColor = [UIColor blackColor];

    // Background image — использует тот же задник что и экран загрузки
    UIImage *background = image ?: [UIImage imageNamed:@"LaunchBackground"];
    _bgImageView = [[UIImageView alloc] initWithImage:PLDimmedNotificationBackground(background)];
    _bgImageView.contentMode = UIViewContentModeScaleAspectFill;
    _bgImageView.translatesAutoresizingMaskIntoConstraints = NO;
    _bgImageView.clipsToBounds = YES;
    [self.view addSubview:_bgImageView];

    // Scroll only when the complete text and buttons exceed the safe viewport.
    _scrollView = [UIScrollView new];
    _scrollView.translatesAutoresizingMaskIntoConstraints = NO;
    _scrollView.contentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentNever;
    _scrollView.alwaysBounceVertical = NO;
    [self.view addSubview:_scrollView];
    UIView *page = [UIView new];
    page.translatesAutoresizingMaskIntoConstraints = NO;
    [_scrollView addSubview:page];

    // Container for labels/buttons
    _contentView = [UIView new];
    _contentView.translatesAutoresizingMaskIntoConstraints = NO;
    _contentView.backgroundColor = [UIColor clearColor];
    [page addSubview:_contentView];

    _titleLabel = [UILabel new];
    _titleLabel.translatesAutoresizingMaskIntoConstraints = NO;
    _titleLabel.text = title ?: @"";
    _titleLabel.textColor = [UIColor whiteColor];
    _titleLabel.font = [UIFont systemFontOfSize:28 weight:UIFontWeightBold];
    _titleLabel.textAlignment = NSTextAlignmentCenter;
    _titleLabel.numberOfLines = 0;
    _titleLabel.lineBreakMode = NSLineBreakByWordWrapping;
    [_titleLabel setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisVertical];
    [self.contentView addSubview:_titleLabel];

    _messageLabel = [UILabel new];
    _messageLabel.translatesAutoresizingMaskIntoConstraints = NO;
    _messageLabel.text = message ?: @"";
    _messageLabel.textColor = [UIColor colorWithWhite:1.0 alpha:0.9];
    _messageLabel.font = [UIFont systemFontOfSize:15 weight:UIFontWeightRegular];
    _messageLabel.textAlignment = NSTextAlignmentCenter;
    _messageLabel.numberOfLines = 0;
    _messageLabel.lineBreakMode = NSLineBreakByWordWrapping;
    [_messageLabel setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisVertical];
    [self.contentView addSubview:_messageLabel];

    // Кнопка «Allow» — яркая, акцентная
    _allowButton = [UIButton buttonWithType:UIButtonTypeCustom];
    _allowButton.translatesAutoresizingMaskIntoConstraints = NO;
    [_allowButton setTitle:@"Allow" forState:UIControlStateNormal];
    _allowButton.backgroundColor = [UIColor colorWithRed:0.18 green:0.55 blue:1.00 alpha:1.0];
    [_allowButton setTitleColor:[UIColor whiteColor] forState:UIControlStateNormal];
    [_allowButton setTitleColor:[UIColor colorWithWhite:1.0 alpha:0.6] forState:UIControlStateHighlighted];
    _allowButton.titleLabel.font = [UIFont systemFontOfSize:17 weight:UIFontWeightBold];
    _allowButton.layer.cornerRadius = 14;
    _allowButton.layer.shadowColor = [UIColor colorWithRed:0.18 green:0.55 blue:1.00 alpha:1.0].CGColor;
    _allowButton.layer.shadowOffset = CGSizeMake(0, 4);
    _allowButton.layer.shadowOpacity = 0.55;
    _allowButton.layer.shadowRadius = 10;
    [_allowButton addTarget:self action:@selector(onAllow:) forControlEvents:UIControlEventTouchUpInside];
    [self.contentView addSubview:_allowButton];

    // Кнопка «Not Now» — тусклая, без фона, просто текст
    _cancelButton = [UIButton buttonWithType:UIButtonTypeCustom];
    _cancelButton.translatesAutoresizingMaskIntoConstraints = NO;
    [_cancelButton setTitle:@"Not Now" forState:UIControlStateNormal];
    _cancelButton.backgroundColor = [UIColor clearColor];
    [_cancelButton setTitleColor:[UIColor colorWithWhite:1.0 alpha:0.40] forState:UIControlStateNormal];
    [_cancelButton setTitleColor:[UIColor colorWithWhite:1.0 alpha:0.20] forState:UIControlStateHighlighted];
    _cancelButton.titleLabel.font = [UIFont systemFontOfSize:14 weight:UIFontWeightRegular];
    [_cancelButton addTarget:self action:@selector(onCancel:) forControlEvents:UIControlEventTouchUpInside];
    [self.contentView addSubview:_cancelButton];

    // Explicit measured minimum heights prevent a previous landscape measurement
    // from clipping wrapped text when the viewport becomes narrow.
    self.titleMinimumHeight = [self.titleLabel.heightAnchor constraintGreaterThanOrEqualToConstant:0];
    self.messageMinimumHeight = [self.messageLabel.heightAnchor constraintGreaterThanOrEqualToConstant:0];
    self.titleMinimumHeight.active = YES;
    self.messageMinimumHeight.active = YES;

    // Layout
    [NSLayoutConstraint activateConstraints:@[
        [self.bgImageView.topAnchor constraintEqualToAnchor:self.view.topAnchor],
        [self.bgImageView.bottomAnchor constraintEqualToAnchor:self.view.bottomAnchor],
        [self.bgImageView.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [self.bgImageView.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],

        [self.scrollView.topAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.topAnchor],
        [self.scrollView.bottomAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.bottomAnchor],
        [self.scrollView.leadingAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.leadingAnchor],
        [self.scrollView.trailingAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.trailingAnchor],
        [page.topAnchor constraintEqualToAnchor:self.scrollView.contentLayoutGuide.topAnchor],
        [page.bottomAnchor constraintEqualToAnchor:self.scrollView.contentLayoutGuide.bottomAnchor],
        [page.leadingAnchor constraintEqualToAnchor:self.scrollView.contentLayoutGuide.leadingAnchor],
        [page.trailingAnchor constraintEqualToAnchor:self.scrollView.contentLayoutGuide.trailingAnchor],
        [page.widthAnchor constraintEqualToAnchor:self.scrollView.frameLayoutGuide.widthAnchor],
        [page.heightAnchor constraintGreaterThanOrEqualToAnchor:self.scrollView.frameLayoutGuide.heightAnchor],
        ({ NSLayoutConstraint *c = [page.heightAnchor constraintEqualToAnchor:self.scrollView.frameLayoutGuide.heightAnchor];
           c.priority = UILayoutPriorityDefaultLow; c; }),
        [self.contentView.centerXAnchor constraintEqualToAnchor:page.centerXAnchor],
        [self.contentView.centerYAnchor constraintEqualToAnchor:page.centerYAnchor],
        [self.contentView.topAnchor constraintGreaterThanOrEqualToAnchor:page.topAnchor constant:16],
        [self.contentView.bottomAnchor constraintLessThanOrEqualToAnchor:page.bottomAnchor constant:-16],
        ({ NSLayoutConstraint *c = [self.contentView.widthAnchor constraintEqualToAnchor:page.widthAnchor multiplier:0.82];
           c.priority = UILayoutPriorityDefaultHigh; c; }),
        [self.contentView.widthAnchor constraintLessThanOrEqualToConstant:480],

        [self.titleLabel.topAnchor constraintEqualToAnchor:self.contentView.topAnchor],
        [self.titleLabel.leadingAnchor constraintEqualToAnchor:self.contentView.leadingAnchor],
        [self.titleLabel.trailingAnchor constraintEqualToAnchor:self.contentView.trailingAnchor],

        [self.messageLabel.topAnchor constraintEqualToAnchor:self.titleLabel.bottomAnchor constant:12],
        [self.messageLabel.leadingAnchor constraintEqualToAnchor:self.contentView.leadingAnchor],
        [self.messageLabel.trailingAnchor constraintEqualToAnchor:self.contentView.trailingAnchor],

        [self.allowButton.topAnchor constraintEqualToAnchor:self.messageLabel.bottomAnchor constant:22],
        [self.allowButton.leadingAnchor constraintEqualToAnchor:self.contentView.leadingAnchor],
        [self.allowButton.trailingAnchor constraintEqualToAnchor:self.contentView.trailingAnchor],
        [self.allowButton.heightAnchor constraintEqualToConstant:48],

        [self.cancelButton.topAnchor constraintEqualToAnchor:self.allowButton.bottomAnchor constant:12],
        [self.cancelButton.leadingAnchor constraintEqualToAnchor:self.contentView.leadingAnchor],
        [self.cancelButton.trailingAnchor constraintEqualToAnchor:self.contentView.trailingAnchor],
        [self.cancelButton.heightAnchor constraintEqualToConstant:44],

        [self.cancelButton.bottomAnchor constraintEqualToAnchor:self.contentView.bottomAnchor],
    ]];

    return self;
}

- (void)viewDidAppear:(BOOL)animated
{
    [super viewDidAppear:animated];
    NSLog(@"[EasyLaunch] Notification prompt layout r3 baked background (2026-10-01)");
}

- (BOOL)updateTextHeight:(UILabel *)label constraint:(NSLayoutConstraint *)constraint
{
    CGFloat width = CGRectGetWidth(label.bounds);
    if (width <= 0) return NO;
    label.preferredMaxLayoutWidth = width;
    CGFloat height = ceil([label sizeThatFits:CGSizeMake(width, CGFLOAT_MAX)].height);
    if (fabs(constraint.constant - height) < 0.5) return NO;
    constraint.constant = height;
    return YES;
}

- (void)viewDidLayoutSubviews
{
    [super viewDidLayoutSubviews];
    BOOL titleChanged = [self updateTextHeight:self.titleLabel constraint:self.titleMinimumHeight];
    BOOL messageChanged = [self updateTextHeight:self.messageLabel constraint:self.messageMinimumHeight];
    if (titleChanged || messageChanged) {
        // A second pass incorporates the full text into the scroll content size.
        // Constants change only when measured height changes, avoiding a layout loop.
        [self.view layoutIfNeeded];
    }
}

- (void)onAllow:(id)sender
{
    if (self.handledAction) return;
    self.handledAction = YES;
    self.allowButton.enabled = self.cancelButton.enabled = NO;
    [self dismissViewControllerAnimated:YES completion:^{
        if (self.allowHandler) self.allowHandler();
    }];
}

- (void)onCancel:(id)sender
{
    if (self.handledAction) return;
    self.handledAction = YES;
    self.allowButton.enabled = self.cancelButton.enabled = NO;
    [self dismissViewControllerAnimated:YES completion:^{
        if (self.cancelHandler) self.cancelHandler();
    }];
}

@end
