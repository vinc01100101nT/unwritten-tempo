using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace UnwrittenLauncherUpdater;

/// <summary>The Mac twin of <see cref="Window"/>: the same small window with one line of text, drawn with the Mac's
/// own window calls (AppKit, reached through the Objective-C runtime), not a toolkit — so the program stays a small
/// download. The same palette colours and the same font, carried inside the program.</summary>
static unsafe partial class MacWindow
{
    const double Width = 420, Height = 110, Margin = 18, FontHeight = 17;

    static nint app, window, label, font;
    static volatile string line = "";

    public static void Open(string title, string firstLine)
    {
        line = firstLine;
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        app = Send(Class("NSApplication"), "sharedApplication");
        // A regular app: it has a Dock icon and comes to the front, as any app the player opened does.
        SendLong(app, "setActivationPolicy:", 0);

        font = LoadFont();
        var (fill, ink) = ReadPalette();

        // A title bar with a close button and a minimise button, nothing else to press.
        const ulong titled = 1, closable = 2, miniaturizable = 4;
        window = Send(Class("NSWindow"), "alloc");
        window = ((delegate* unmanaged<nint, nint, Rect, ulong, ulong, byte, nint>)MsgSend)(
            window, Sel("initWithContentRect:styleMask:backing:defer:"), new Rect(0, 0, Width, Height),
            titled | closable | miniaturizable, 2, 0);
        SendPtr(window, "setTitle:", Text(title));
        SendByte(window, "setReleasedWhenClosed:", 0);

        label = Send(Class("NSTextField"), "alloc");
        label = ((delegate* unmanaged<nint, nint, Rect, nint>)MsgSend)(
            label, Sel("initWithFrame:"), new Rect(Margin, 0, Width - 2 * Margin, Height));
        SendByte(label, "setEditable:", 0);
        SendByte(label, "setSelectable:", 0);
        SendByte(label, "setBezeled:", 0);
        SendByte(label, "setDrawsBackground:", 0);
        SendPtr(label, "setFont:", font);
        SendPtr(label, "setTextColor:", ink);
        SendLong(label, "setAlignment:", 1);
        SendByte(Send(label, "cell"), "setWraps:", 1);
        // The fill is painted by the inside of the window itself, so it is the same colour in every picture of it.
        var inside = Send(Send(Class("NSView"), "alloc"), "init");
        SendByte(inside, "setWantsLayer:", 1);
        SendPtr(Send(inside, "layer"), "setBackgroundColor:", Send(fill, "CGColor"));
        SendPtr(window, "setContentView:", inside);
        SendPtr(inside, "addSubview:", label);
        Show(line);

        Send(window, "center");
        SendPtr(app, "setDelegate:", Delegate());
    }

    /// <summary>Change the line of text. Safe from any thread: the change is handed to the main thread, the only
    /// one AppKit may be touched from.</summary>
    public static void Say(string text)
    {
        line = text;
        if (window != 0) dispatch_async_f(MainQueue, 0, &ShowLine);
    }

    /// <summary>Close the window and end <see cref="Run"/>. Safe from any thread.</summary>
    public static void Close()
    {
        if (window != 0) dispatch_async_f(MainQueue, 0, &Quit);
    }

    /// <summary>Show the window and keep it alive until it is closed.</summary>
    public static void Run()
    {
        SendPtr(window, "makeKeyAndOrderFront:");
        SendByte(app, "activateIgnoringOtherApps:", 1);
        Send(app, "run");
    }

    static void Show(string text)
    {
        SendPtr(label, "setStringValue:", Text(text));
        // Centred top to bottom: the label is made as tall as its text needs, then placed in the middle.
        var fits = ((delegate* unmanaged<nint, nint, Size, Size>)MsgSend)(
            Send(label, "cell"), Sel("cellSizeForBounds:"), new Size(Width - 2 * Margin, Height));
        ((delegate* unmanaged<nint, nint, Rect, void>)MsgSend)(
            label, Sel("setFrame:"), new Rect(Margin, (Height - fits.h) / 2, Width - 2 * Margin, fits.h));
    }

    [UnmanagedCallersOnly] static void ShowLine(nint _) => Show(line);

    [UnmanagedCallersOnly] static void Quit(nint _) => SendPtr(app, "terminate:");

    /// <summary>Closing the window ends the program, as it does on Windows.</summary>
    static nint Delegate()
    {
        var kind = objc_allocateClassPair(Class("NSObject"), "UnwrittenLauncherUpdaterDelegate", 0);
        delegate* unmanaged<nint, nint, nint, byte> lastClosed = &ClosesWithWindow;
        class_addMethod(kind, Sel("applicationShouldTerminateAfterLastWindowClosed:"), (nint)lastClosed, "c@:@");
        objc_registerClassPair(kind);
        return Send(Send(kind, "alloc"), "init");
    }

    [UnmanagedCallersOnly] static byte ClosesWithWindow(nint self, nint cmd, nint sender) => 1;

    static Stream Carried(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException(name + " is not inside this program.");

    static (nint fill, nint ink) ReadPalette()
    {
        using var tokens = JsonDocument.Parse(Carried("ui-tokens.json"));
        var palette = tokens.RootElement.GetProperty("palette");
        return (Colour(palette, "PanelRaise"), Colour(palette, "TextMain"));
    }

    static nint Colour(JsonElement palette, string name)
    {
        var rgb = Convert.ToUInt32(palette.GetProperty(name).GetProperty("hex").GetString()![1..], 16);
        return ((delegate* unmanaged<nint, nint, double, double, double, double, nint>)MsgSend)(
            Class("NSColor"), Sel("colorWithSRGBRed:green:blue:alpha:"),
            (rgb >> 16) / 255.0, (rgb >> 8 & 0xFF) / 255.0, (rgb & 0xFF) / 255.0, 1.0);
    }

    /// <summary>The game's font, made from the bytes carried inside this program. A Core Text font is also an AppKit
    /// font, so the one call gives what the label takes.</summary>
    static nint LoadFont()
    {
        using var file = Carried("font.ttf");
        var bytes = new byte[file.Length];
        file.ReadExactly(bytes);
        nint data;
        fixed (byte* b = bytes) data = CFDataCreate(0, b, bytes.Length);
        var provider = CGDataProviderCreateWithCFData(data);
        var graphics = CGFontCreateWithDataProvider(provider);
        return CTFontCreateWithGraphicsFont(graphics, FontHeight, 0, 0);
    }

    static nint Text(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s + "\0");
        fixed (byte* b = bytes)
            return ((delegate* unmanaged<nint, nint, byte*, nint>)MsgSend)(Class("NSString"), Sel("stringWithUTF8String:"), b);
    }

    [StructLayout(LayoutKind.Sequential)] readonly record struct Rect(double x, double y, double w, double h);
    [StructLayout(LayoutKind.Sequential)] readonly record struct Size(double w, double h);

    static readonly nint MsgSend = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libobjc.A.dylib"), "objc_msgSend");
    static readonly nint MainQueue = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/libSystem.B.dylib"), "_dispatch_main_q");

    static nint Class(string name) => objc_getClass(name);
    static nint Sel(string name) => sel_registerName(name);
    static nint Send(nint to, string message) => ((delegate* unmanaged<nint, nint, nint>)MsgSend)(to, Sel(message));
    static nint SendPtr(nint to, string message, nint arg = 0) =>
        ((delegate* unmanaged<nint, nint, nint, nint>)MsgSend)(to, Sel(message), arg);
    static void SendLong(nint to, string message, long arg) =>
        ((delegate* unmanaged<nint, nint, long, void>)MsgSend)(to, Sel(message), arg);
    static void SendByte(nint to, string message, byte arg) =>
        ((delegate* unmanaged<nint, nint, byte, void>)MsgSend)(to, Sel(message), arg);

    [DllImport("/usr/lib/libobjc.A.dylib")] static extern nint objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] static extern nint sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] static extern nint objc_allocateClassPair(nint super, string name, nint extra);
    [DllImport("/usr/lib/libobjc.A.dylib")] static extern void objc_registerClassPair(nint kind);
    [DllImport("/usr/lib/libobjc.A.dylib")] static extern byte class_addMethod(nint kind, nint sel, nint imp, string types);
    [DllImport("/usr/lib/libSystem.B.dylib")]
    static extern void dispatch_async_f(nint queue, nint context, delegate* unmanaged<nint, void> work);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    static extern nint CFDataCreate(nint allocator, byte* bytes, nint length);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    static extern nint CGDataProviderCreateWithCFData(nint data);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    static extern nint CGFontCreateWithDataProvider(nint provider);
    [DllImport("/System/Library/Frameworks/CoreText.framework/CoreText")]
    static extern nint CTFontCreateWithGraphicsFont(nint font, double size, nint matrix, nint attributes);
}
