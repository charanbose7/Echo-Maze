// Native iOS haptics bridge for Sonarfall.
//
// Unity merges every .m/.mm/.c/.cpp under Assets/Plugins/iOS into the generated Xcode project
// automatically, so this file needs no Xcode setup. Because it is Objective-C++ (.mm), the exported
// functions MUST be declared with C linkage or name mangling stops DllImport("__Internal") from
// finding them.
//
// Design notes:
//  * UIFeedbackGenerator (iOS 10+) is the system haptics API. It is what the OS itself uses, so the
//    feel matches the rest of the phone and it automatically honours the user's "System Haptics"
//    setting — an app neither can nor should override that.
//  * Generators are created ONCE and kept. prepare() warms the Taptic Engine so the next event
//    fires with minimal latency; it is called again right after each event because the engine only
//    stays warm for a short window.
//  * No availability checking is needed. On hardware without a Taptic Engine (iPads, iPhone 6s and
//    older) these calls are documented to do nothing rather than fail — every device that can run a
//    current iOS release has one.
//  * Deliberately NOT using [UIDevice valueForKey:@"_feedbackSupportLevel"] to detect the engine.
//    It is a private API and risks App Store rejection.
//
// All calls arrive on Unity's scripting thread, which on iOS is the app's main thread — the thread
// UIKit requires for feedback generators.

#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *sLight = nil;
static UIImpactFeedbackGenerator *sMedium = nil;
static UIImpactFeedbackGenerator *sHeavy = nil;
static UINotificationFeedbackGenerator *sNotification = nil;
static UISelectionFeedbackGenerator *sSelection = nil;
static BOOL sReady = NO;

extern "C" {

/// Build and warm the generators. Returns false only on pre-iOS-10 systems.
bool _EchoHapticsInit(void)
{
    if (sReady) return true;

    if (@available(iOS 10.0, *))
    {
        sLight        = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        sMedium       = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
        sHeavy        = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
        sNotification = [[UINotificationFeedbackGenerator alloc] init];
        sSelection    = [[UISelectionFeedbackGenerator alloc] init];

        [sLight prepare];
        [sMedium prepare];
        [sHeavy prepare];
        [sNotification prepare];
        [sSelection prepare];

        sReady = YES;
        return true;
    }

    return false;
}

/// style: 0 = light, 1 = medium, 2 = heavy.
void _EchoHapticsImpact(int style)
{
    if (!sReady) return;
    if (@available(iOS 10.0, *))
    {
        UIImpactFeedbackGenerator *g = (style <= 0) ? sLight : ((style == 1) ? sMedium : sHeavy);
        [g impactOccurred];
        [g prepare];   // stay warm for the next hit
    }
}

/// type: 0 = success, 1 = warning, 2 = error.
void _EchoHapticsNotification(int type)
{
    if (!sReady) return;
    if (@available(iOS 10.0, *))
    {
        UINotificationFeedbackType t = UINotificationFeedbackTypeSuccess;
        if (type == 1)      t = UINotificationFeedbackTypeWarning;
        else if (type >= 2) t = UINotificationFeedbackTypeError;

        [sNotification notificationOccurred:t];
        [sNotification prepare];
    }
}

/// The light tick used for discrete selection changes (buttons, toggles).
void _EchoHapticsSelection(void)
{
    if (!sReady) return;
    if (@available(iOS 10.0, *))
    {
        [sSelection selectionChanged];
        [sSelection prepare];
    }
}

} // extern "C"
