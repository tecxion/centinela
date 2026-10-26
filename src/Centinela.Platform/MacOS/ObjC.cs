using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Centinela.Platform.MacOS;

/// <summary>
/// The Objective-C runtime behind <see cref="MacNotifier"/>: classes, selectors, <c>objc_msgSend</c>, NSString and
/// blocks. <c>objc_msgSend</c> has one declaration per signature: on arm64 it must be called with the method's own
/// argument types, never as a variadic function.
/// </summary>
[SupportedOSPlatform("macos")]
static unsafe class ObjC
{
    const string Library = "/usr/lib/libobjc.A.dylib";
    const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
    const nuint Utf8Encoding = 4; // NSUTF8StringEncoding
    const int BlockIsGlobal = 1 << 28;

    static IntPtr _ignoreGrant;

    /// <summary>A block on 64 bits (Block_layout in libclosure).</summary>
    [StructLayout(LayoutKind.Explicit)]
    struct Block
    {
        [FieldOffset(0)] public IntPtr Isa;
        [FieldOffset(8)] public int Flags;
        [FieldOffset(12)] public int Reserved;
        [FieldOffset(16)] public IntPtr Invoke;
        [FieldOffset(24)] public BlockDescriptor* Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    // The runtime copies every string it keeps (class name, method types), so marshalled buffers may be freed.
    [DllImport(Library)]
    public static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Library)]
    public static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Library)]
    public static extern IntPtr objc_getProtocol([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Library)]
    public static extern IntPtr objc_allocateClassPair(IntPtr superclass,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, nuint extraBytes);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool class_addMethod(IntPtr cls, IntPtr selector, IntPtr implementation,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string types);

    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool class_addProtocol(IntPtr cls, IntPtr protocol);

    [DllImport(Library)]
    public static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(Library)]
    public static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(Library)]
    public static extern void objc_autoreleasePoolPop(IntPtr pool);

    /// <summary>id (id, SEL): getters, alloc, init, class methods without arguments.</summary>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    /// <summary>id (id, SEL, id, id, id): requestWithIdentifier:content:trigger:.</summary>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b, IntPtr c);

    /// <summary>id (id, SEL, const void *, NSUInteger, NSUInteger): initWithBytes:length:encoding:.</summary>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, byte* bytes, nuint length, nuint encoding);

    /// <summary>void (id, SEL): release.</summary>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr receiver, IntPtr selector);

    /// <summary>void (id, SEL, id): setters (setTitle:, setBody:, setDelegate:).</summary>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr receiver, IntPtr selector, IntPtr a);

    /// <summary>void (id, SEL, NSUInteger, id): requestAuthorizationWithOptions:completionHandler:.</summary>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr receiver, IntPtr selector, nuint a, IntPtr b);

    /// <summary>void (id, SEL, id, id): addNotificationRequest:withCompletionHandler:.</summary>
    [DllImport(Library, EntryPoint = "objc_msgSend")]
    public static extern void SendVoid(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b);

    /// <summary>A new NSString owned by the caller (+1: release it).</summary>
    public static IntPtr NewString(string value)
    {
        var bytes = new byte[Encoding.UTF8.GetByteCount(value) + 1]; // one spare byte: an empty array pins as null
        var length = Encoding.UTF8.GetBytes(value, bytes);
        fixed (byte* p = bytes)
            return Send(Send(objc_getClass("NSString"), sel_registerName("alloc")),
                sel_registerName("initWithBytes:length:encoding:"), p, (nuint)length, Utf8Encoding);
    }

    public static void Release(IntPtr instance) => SendVoid(instance, sel_registerName("release"));

    /// <summary>Calls a received block <c>void (^)(NSUInteger)</c>.</summary>
    public static void CallBlock(IntPtr block, nuint argument)
    {
        if (block != IntPtr.Zero) ((delegate* unmanaged<IntPtr, nuint, void>)((Block*)block)->Invoke)(block, argument);
    }

    /// <summary>Calls a received block <c>void (^)(void)</c>.</summary>
    public static void CallBlock(IntPtr block)
    {
        if (block != IntPtr.Zero) ((delegate* unmanaged<IntPtr, void>)((Block*)block)->Invoke)(block);
    }

    /// <summary>
    /// A global block <c>void (^)(BOOL granted, NSError *error)</c> that does nothing, for a completion handler that
    /// does not take nil. Made once, never freed: it lives as long as the process.
    /// </summary>
    public static IntPtr IgnoreGrantBlock()
    {
        if (_ignoreGrant != IntPtr.Zero) return _ignoreGrant;
        var isa = NativeLibrary.GetExport(NativeLibrary.Load(SystemLibrary), "_NSConcreteGlobalBlock");
        var block = (Block*)NativeMemory.Alloc((nuint)(sizeof(Block) + sizeof(BlockDescriptor)));
        var descriptor = (BlockDescriptor*)(block + 1);
        *descriptor = new BlockDescriptor { Reserved = 0, Size = (nuint)sizeof(Block) };
        *block = new Block
        {
            Isa = isa,
            Flags = BlockIsGlobal,
            Reserved = 0,
            Invoke = (IntPtr)(delegate* unmanaged<IntPtr, byte, IntPtr, void>)&IgnoreGrant,
            Descriptor = descriptor,
        };
        return _ignoreGrant = (IntPtr)block;
    }

    [UnmanagedCallersOnly]
    static void IgnoreGrant(IntPtr block, byte granted, IntPtr error) { }
}
