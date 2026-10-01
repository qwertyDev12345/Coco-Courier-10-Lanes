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
    _bgImageView.translatesAutoresizingMaskIntoConstraints = YES;
    _bgImageView.clipsToBounds = YES;
    [self.view addSubview:_bgImageView];

    // Scroll only when the complete text and buttons exceed the safe viewport.
    _scrollView = [UIScrollView new];
    _scrollView.translatesAutoresizingMaskIntoConstraints = YES;
    _scrollView.contentInsetAdjustmentBehavior = UIScrollViewContentInsetAdjustmentNever;
    _scrollView.alwaysBounceVertical = NO;
    [self.view addSubview:_scrollView];
    // Container for labels/buttons
    _contentView = [UIView new];
    _contentView.translatesAutoresizingMaskIntoConstraints = YES;
    _contentView.backgroundColor = [UIColor clearColor];
    [_scrollView addSubview:_contentView];

    _titleLabel = [UILabel new];
    _titleLabel.translatesAutoresizingMaskIntoConstraints = YES;
    _titleLabel.text = title ?: @"";
    _titleLabel.textColor = [UIColor whiteColor];
    _titleLabel.font = [UIFont systemFontOfSize:28 weight:UIFontWeightBold];
    _titleLabel.textAlignment = NSTextAlignmentCenter;
    _titleLabel.numberOfLines = 0;
    _titleLabel.lineBreakMode = NSLineBreakByWordWrapping;
    [_titleLabel setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisVertical];
    [self.contentView addSubview:_titleLabel];

    _messageLabel = [UILabel new];
    _messageLabel.translatesAutoresizingMaskIntoConstraints = YES;
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
    _allowButton.translatesAutoresizingMaskIntoConstraints = YES;
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
    _cancelButton.translatesAutoresizingMaskIntoConstraints = YES;
    [_cancelButton setTitle:@"Not Now" forState:UIControlStateNormal];
    _cancelButton.backgroundColor = [UIColor clearColor];
    [_cancelButton setTitleColor:[UIColor colorWithWhite:1.0 alpha:0.40] forState:UIControlStateNormal];
    [_cancelButton setTitleColor:[UIColor colorWithWhite:1.0 alpha:0.20] forState:UIControlStateHighlighted];
    _cancelButton.titleLabel.font = [UIFont systemFontOfSize:14 weight:UIFontWeightRegular];
    [_cancelButton addTarget:self action:@selector(onCancel:) forControlEvents:UIControlEventTouchUpInside];
    [self.contentView addSubview:_cancelButton];

    return self;
}

// Measure the complete string independently of UILabel's previous frame.
// Include a small rounding margin so the final line cannot lose its descenders.
- (CGFloat)heightForLabel:(UILabel *)label width:(CGFloat)width
{
    NSMutableParagraphStyle *paragraph = [NSMutableParagraphStyle new];
    paragraph.lineBreakMode = NSLineBreakByWordWrapping;
    paragraph.alignment = label.textAlignment;
    CGRect textBounds = [label.text boundingRectWithSize:CGSizeMake(width, CGFLOAT_MAX)
        options:NSStringDrawingUsesLineFragmentOrigin | NSStringDrawingUsesFontLeading
        attributes:@{NSFontAttributeName:label.font, NSParagraphStyleAttributeName:paragraph}
        context:nil];
    return ceil(MAX(textBounds.size.height, label.font.lineHeight)) + 2;
}

- (void)viewDidLayoutSubviews
{
    [super viewDidLayoutSubviews];
    self.bgImageView.frame = self.view.bounds;
    CGRect viewport = UIEdgeInsetsInsetRect(self.view.bounds, self.view.safeAreaInsets);
    self.scrollView.frame = viewport;
    CGFloat width = MIN(480, floor(CGRectGetWidth(viewport) * 0.82));
    if (width <= 0) return;

    CGFloat titleHeight = [self heightForLabel:self.titleLabel width:width];
    CGFloat messageHeight = [self heightForLabel:self.messageLabel width:width];
    CGFloat y = 0;
    self.titleLabel.frame = CGRectMake(0, y, width, titleHeight);
    y += titleHeight + 12;
    self.messageLabel.frame = CGRectMake(0, y, width, messageHeight);
    y += messageHeight + 22;
    self.allowButton.frame = CGRectMake(0, y, width, 48);
    y += 48 + 12;
    self.cancelButton.frame = CGRectMake(0, y, width, 44);
    CGFloat contentHeight = y + 44;
    CGFloat pageHeight = MAX(CGRectGetHeight(viewport), contentHeight + 32);
    self.contentView.frame = CGRectMake(floor((CGRectGetWidth(viewport) - width) / 2),
        floor((pageHeight - contentHeight) / 2), width, contentHeight);
    self.scrollView.contentSize = CGSizeMake(CGRectGetWidth(viewport), pageHeight);
    CGFloat maxOffset = MAX(0, pageHeight - CGRectGetHeight(viewport));
    self.scrollView.contentOffset = CGPointMake(0, MIN(MAX(0, self.scrollView.contentOffset.y), maxOffset));
}

- (void)viewDidAppear:(BOOL)animated
{
    [super viewDidAppear:animated];
    NSLog(@"[EasyLaunch] Notification prompt layout r4 explicit frames (2026-10-01); title length=%lu frame=%@",
        (unsigned long)self.titleLabel.text.length, NSStringFromCGRect(self.titleLabel.frame));
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
