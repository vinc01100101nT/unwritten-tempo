using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace UnwrittenLauncherUpdater;

/// <summary>The whole of what the player sees: a small window with one line of text saying what is happening. No
/// buttons — it does its job and hands over. Drawn with Windows' own calls, not a window toolkit, because the toolkit
/// is nearly all of the launcher's 100 MB and this program has to be a small download.
/// <para>The colours are the game's shared palette and the lettering is the game's own font, both read from the same
/// files the launcher reads (prototypes/ui-tokens.json, the font in the Unity project), carried inside this program.</para></summary>
static unsafe class Window
{
    const int Width = 420, Height = 110, Margin = 18, FontHeight = 17;

    static nint window, font, fill;
    static uint ink;
    static volatile string line = "";

    public static void Open(string title, string firstLine)
    {
        line = firstLine;
        ReadPalette();
        LoadFont();

        var dpi = (int)GetDpiForSystem();
        font = CreateFontW(-FontHeight * dpi / 96, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "DejaVu Sans Condensed");

        var instance = GetModuleHandleW(null);
        fixed (char* name = "UnwrittenLauncherUpdater")
        {
            var kind = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &Handle, hInstance = instance,
                hCursor = LoadCursorW(0, 32512), lpszClassName = name,
            };
            RegisterClassExW(&kind);

            // A title bar with a close button and nothing else to press; the size given is the inside of the window.
            const uint style = 0x00C00000 | 0x00080000 | 0x00020000;
            var box = new RECT { right = Width * dpi / 96, bottom = Height * dpi / 96 };
            AdjustWindowRectEx(&box, style, 0, 0);
            int w = box.right - box.left, h = box.bottom - box.top;
            window = CreateWindowExW(0, name, title, style | 0x10000000,
                                     (GetSystemMetrics(0) - w) / 2, (GetSystemMetrics(1) - h) / 2, w, h,
                                     0, 0, instance, 0);
        }
    }

    /// <summary>Change the line of text. Safe from any thread.</summary>
    public static void Say(string text)
    {
        line = text;
        if (window != 0) InvalidateRect(window, null, 1);
    }

    /// <summary>Close the window, which ends <see cref="Run"/>. Safe from any thread.</summary>
    public static void Close() => PostMessageW(window, 0x0010, 0, 0);

    /// <summary>Keep the window alive until it is closed.</summary>
    public static void Run()
    {
        MSG message;
        while (GetMessageW(&message, 0, 0, 0) > 0)
        {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }

    /// <summary>Draw the window's inside: the palette's fill, and the line of text in the middle, wrapped if long.</summary>
    public static void Paint(nint surface, int width, int height, string text)
    {
        var all = new RECT { right = width, bottom = height };
        FillRect(surface, &all, fill);
        SetBkMode(surface, 1);
        SetTextColor(surface, ink);
        var before = SelectObject(surface, font);

        // Measured first, so a line that wraps is still centred top to bottom.
        const uint wrapCentred = 0x0010 | 0x0001 | 0x0800;
        var margin = Margin * width / Width;
        var needs = new RECT { left = margin, right = width - margin };
        fixed (char* t = text)
        {
            DrawTextW(surface, t, text.Length, &needs, wrapCentred | 0x0400);
            var tall = needs.bottom - needs.top;
            var at = new RECT { left = margin, right = width - margin, top = (height - tall) / 2, bottom = (height + tall) / 2 };
            DrawTextW(surface, t, text.Length, &at, wrapCentred);
        }
        SelectObject(surface, before);
    }

    [UnmanagedCallersOnly]
    static nint Handle(nint hwnd, uint message, nint w, nint l)
    {
        switch (message)
        {
            case 0x000F:
                PAINTSTRUCT paint;
                var surface = BeginPaint(hwnd, &paint);
                RECT inside;
                GetClientRect(hwnd, &inside);
                Paint(surface, inside.right, inside.bottom, line);
                EndPaint(hwnd, &paint);
                return 0;
            case 0x0014:
                return 1;
            case 0x0002:
                PostQuitMessage(0);
                return 0;
        }
        return DefWindowProcW(hwnd, message, w, l);
    }

    static Stream Carried(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException(name + " is not inside this program.");

    public static void ReadPalette()
    {
        using var tokens = JsonDocument.Parse(Carried("ui-tokens.json"));
        var palette = tokens.RootElement.GetProperty("palette");
        fill = CreateSolidBrush(Colour(palette, "PanelRaise"));
        ink = Colour(palette, "TextMain");
    }

    /// <summary>A palette colour the way Windows' drawing calls take one: blue, green, red from the top byte down.</summary>
    static uint Colour(JsonElement palette, string name)
    {
        var rgb = Convert.ToUInt32(palette.GetProperty(name).GetProperty("hex").GetString()![1..], 16);
        return (rgb & 0xFF) << 16 | (rgb & 0xFF00) | rgb >> 16;
    }

    public static void LoadFont()
    {
        using var file = Carried("font.ttf");
        var bytes = new byte[file.Length];
        file.ReadExactly(bytes);
        uint added = 0;
        fixed (byte* b = bytes) AddFontMemResourceEx(b, (uint)bytes.Length, 0, &added);
    }

    public static void UseFontAt(int dpi) =>
        font = CreateFontW(-FontHeight * dpi / 96, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "DejaVu Sans Condensed");

    [StructLayout(LayoutKind.Sequential)]
    struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public delegate* unmanaged<nint, uint, nint, nint, nint> lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public char* lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore, fIncUpdate;
        public fixed byte rgbReserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam, lParam;
        public uint time;
        public int x, y;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern nint GetModuleHandleW(string? name);
    [DllImport("user32")] static extern ushort RegisterClassExW(WNDCLASSEXW* kind);
    [DllImport("user32", CharSet = CharSet.Unicode)]
    static extern nint CreateWindowExW(uint exStyle, char* kind, string title, uint style, int x, int y, int w, int h,
                                       nint parent, nint menu, nint instance, nint param);
    [DllImport("user32")] static extern nint DefWindowProcW(nint hwnd, uint message, nint w, nint l);
    [DllImport("user32")] static extern int GetMessageW(MSG* message, nint hwnd, uint min, uint max);
    [DllImport("user32")] static extern int TranslateMessage(MSG* message);
    [DllImport("user32")] static extern nint DispatchMessageW(MSG* message);
    [DllImport("user32")] static extern void PostQuitMessage(int code);
    [DllImport("user32")] static extern int PostMessageW(nint hwnd, uint message, nint w, nint l);
    [DllImport("user32")] static extern int InvalidateRect(nint hwnd, RECT* area, int erase);
    [DllImport("user32")] static extern nint BeginPaint(nint hwnd, PAINTSTRUCT* paint);
    [DllImport("user32")] static extern int EndPaint(nint hwnd, PAINTSTRUCT* paint);
    [DllImport("user32")] static extern int GetClientRect(nint hwnd, RECT* inside);
    [DllImport("user32")] static extern int FillRect(nint surface, RECT* area, nint brush);
    [DllImport("user32")] static extern int DrawTextW(nint surface, char* text, int length, RECT* area, uint how);
    [DllImport("user32")] static extern nint LoadCursorW(nint instance, nint which);
    [DllImport("user32")] static extern int GetSystemMetrics(int which);
    [DllImport("user32")] static extern uint GetDpiForSystem();
    [DllImport("user32")] static extern int AdjustWindowRectEx(RECT* box, uint style, int menu, uint exStyle);
    [DllImport("gdi32")] static extern nint CreateSolidBrush(uint colour);
    [DllImport("gdi32")] static extern uint SetTextColor(nint surface, uint colour);
    [DllImport("gdi32")] static extern int SetBkMode(nint surface, int mode);
    [DllImport("gdi32")] static extern nint SelectObject(nint surface, nint thing);
    [DllImport("gdi32", CharSet = CharSet.Unicode)]
    static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic,
                                   uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
                                   uint quality, uint pitchAndFamily, string face);
    [DllImport("gdi32")] static extern nint AddFontMemResourceEx(byte* font, uint size, nint reserved, uint* added);
}
