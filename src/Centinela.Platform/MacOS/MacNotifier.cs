using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Centinela.Core.Platform;
using static Centinela.Platform.MacOS.ObjC;

namespace Centinela.Platform.MacOS;

/// <summary>
/// System notifications with UserNotifications, through the Objective-C runtime. A click on a notification runs its
/// click action on the UI thread (<c>ui</c>). <see cref="TryCreate"/> is null where they cannot work (not macOS, or
/// not run from an .app); <see cref="Show"/> never throws.
/// </summary>
/// <remarks>
/// C# cannot catch an Objective-C exception (the process dies), so every precondition is checked before the call
/// that would raise one. The delegate class and the authorization block live as long as the process.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class MacNotifier : INotifier
{
    const string Framework = "/System/Library/Frameworks/UserNotifications.framework/UserNotifications";
    const string DelegateClassName = "CentinelaNotificationDelegate";
    const string DelegateMethodTypes = "v@:@@@?"; // void (self, _cmd, center, notification or response, block)
    const nuint AuthorizationAlert = 1 << 2; // UNAuthorizationOptionAlert
    const nuint PresentBannerAndList = (1 << 4) | (1 << 3); // no Sound: Centinela plays its own

    // The delegate's methods are static: they reach the live notifier through this.
    static volatile MacNotifier? _current;

    readonly SynchronizationContext _ui;
    readonly IntPtr _center;
    readonly NoticeActions _actions = new();
    volatile bool _disposed;

    MacNotifier(SynchronizationContext ui, IntPtr center)
    {
        _ui = ui;
        _center = center;
    }

    /// <summary>Null, never an exception, without libobjc, UserNotifications, an app bundle or the center class.</summary>
    public static MacNotifier? TryCreate(SynchronizationContext ui)
    {
        try
        {
            NativeLibrary.Load(Framework);
            // Without a bundle (dotnet run) UNUserNotificationCenter raises an Objective-C exception.
            var bundle = Send(objc_getClass("NSBundle"), sel_registerName("mainBundle"));
            if (Send(bundle, sel_registerName("bundleIdentifier")) == IntPtr.Zero) return null;
            var centerClass = objc_getClass("UNUserNotificationCenter");
            if (centerClass == IntPtr.Zero) return null;
            var delegateClass = DelegateClass();
            if (delegateClass == IntPtr.Zero) return null;

            var center = Send(centerClass, sel_registerName("currentNotificationCenter"));
            // Never released: the center holds its delegate weakly.
            var instance = Send(Send(delegateClass, sel_registerName("alloc")), sel_registerName("init"));
            var notifier = new MacNotifier(ui, center);
            _current = notifier;
            SendVoid(center, sel_registerName("setDelegate:"), instance);
            SendVoid(center, sel_registerName("requestAuthorizationWithOptions:completionHandler:"), AuthorizationAlert,
                IgnoreGrantBlock());
            return notifier;
        }
        catch (Exception)
        {
            return null; // DllNotFoundException off macOS
        }
    }

    public void Show(string title, string text, Action? onClick = null)
    {
        if (_disposed) return;
        var pool = objc_autoreleasePoolPush(); // the request is autoreleased
        try
        {
            var content = Send(Send(objc_getClass("UNMutableNotificationContent"), sel_registerName("alloc")),
                sel_registerName("init"));
            SetText(content, "setTitle:", title);
            SetText(content, "setBody:", text);
            var identifier = NewString(_actions.Add(onClick ?? (() => { })));
            var request = Send(objc_getClass("UNNotificationRequest"),
                sel_registerName("requestWithIdentifier:content:trigger:"), identifier, content, IntPtr.Zero);
            Release(identifier);
            SendVoid(_center, sel_registerName("addNotificationRequest:withCompletionHandler:"), request, IntPtr.Zero);
            Release(content);
        }
        catch (Exception)
        {
            // The notification does not show; the red frame and the log stay.
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_current != this) return; // a newer notifier owns the delegate
        _current = null;
        SendVoid(_center, sel_registerName("setDelegate:"), IntPtr.Zero);
    }

    static void SetText(IntPtr content, string setter, string value)
    {
        var text = NewString(value);
        SendVoid(content, sel_registerName(setter), text);
        Release(text);
    }

    /// <summary>CentinelaNotificationDelegate, made once per process: a second call finds it registered.</summary>
    static unsafe IntPtr DelegateClass()
    {
        var cls = objc_getClass(DelegateClassName);
        if (cls != IntPtr.Zero) return cls;
        cls = objc_allocateClassPair(objc_getClass("NSObject"), DelegateClassName, 0);
        if (cls == IntPtr.Zero) return IntPtr.Zero;
        class_addMethod(cls, sel_registerName("userNotificationCenter:willPresentNotification:withCompletionHandler:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&WillPresent, DelegateMethodTypes);
        class_addMethod(cls,
            sel_registerName("userNotificationCenter:didReceiveNotificationResponse:withCompletionHandler:"),
            (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DidReceive, DelegateMethodTypes);
        var protocol = objc_getProtocol("UNUserNotificationCenterDelegate");
        if (protocol != IntPtr.Zero) class_addProtocol(cls, protocol);
        objc_registerClassPair(cls);
        return cls;
    }

    // A managed exception escaping these two kills the process: everything is caught.

    /// <summary>Shows the notification with Centinela in front too.</summary>
    [UnmanagedCallersOnly]
    static void WillPresent(IntPtr self, IntPtr cmd, IntPtr center, IntPtr notification, IntPtr completionHandler)
    {
        try
        {
            CallBlock(completionHandler, PresentBannerAndList);
        }
        catch (Exception)
        {
            // Nothing to undo.
        }
    }

    /// <summary>A click: posts the notification's action to the UI thread, then always calls the block.</summary>
    [UnmanagedCallersOnly]
    static void DidReceive(IntPtr self, IntPtr cmd, IntPtr center, IntPtr response, IntPtr completionHandler)
    {
        try
        {
            if (_current is { } notifier && IdentifierOf(response) is { } id && notifier._actions.Take(id) is { } action)
                notifier._ui.Post(_ =>
                {
                    if (!notifier._disposed) action();
                }, null);
        }
        catch (Exception)
        {
            // The click runs nothing.
        }
        finally
        {
            CallBlock(completionHandler);
        }
    }

    /// <summary>response.notification.request.identifier, or null.</summary>
    static string? IdentifierOf(IntPtr response)
    {
        var pool = objc_autoreleasePoolPush(); // UTF8String's buffer is autoreleased; we do not know this thread's pool
        try
        {
            var request = Send(Send(response, sel_registerName("notification")), sel_registerName("request"));
            var identifier = Send(request, sel_registerName("identifier"));
            return Marshal.PtrToStringUTF8(Send(identifier, sel_registerName("UTF8String")));
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }
}
