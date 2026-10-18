using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CapDock;

internal static class Program
{
    /// <summary>Выставляется пунктом «Перезапустить программу»: после закрытия формы запускаем себя заново.</summary>
    public static bool RestartRequested;

    [STAThread]
    static void Main()
    {
        // Одна копия программы: второй запуск (например, ярлык + автозагрузка) просто выходит
        var single = new Mutex(true, @"Local\CapDock.SingleInstance", out bool first);
        if (!first) { single.Dispose(); return; }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new CapDockApp());

        single.ReleaseMutex();
        single.Dispose();                          // до перезапуска — иначе новая копия решит, что она вторая
        if (RestartRequested) StartNewInstance();
    }

    /// <summary>Чем запускать программу: exe и аргументы (при запуске через «dotnet CapDock.dll» — так же).</summary>
    public static (string Exe, string Args) LaunchCommand()
    {
        string exe  = Environment.ProcessPath;
        string args = "";
        if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
            args = "\"" + typeof(Program).Assembly.Location + "\"";
        return (exe, args);
    }

    private static void StartNewInstance()
    {
        try
        {
            var (exe, args) = LaunchCommand();
            Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute  = false,
                WorkingDirectory = AppContext.BaseDirectory
            })?.Dispose();
        }
        catch { }
    }
}

/// <summary>Язык интерфейса: L.T("English", "Русский"). По умолчанию английский.</summary>
internal static class L
{
    public static bool Ru;
    public static string T(string en, string ru) => Ru ? ru : en;
}

/// <summary>Автозапуск вместе с Windows: значение в HKCU\...\Run.</summary>
internal static class AutoStart
{
    private const string RunKey  = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name    = AppSettings.AppName;
    private const string OldName = "DesktopDock";          // прежнее название программы

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string s && s.Length > 0;
        }
        catch { return false; }
    }

    /// <summary>Включить (записать путь к этой программе) или выключить. false — не удалось.</summary>
    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.DeleteValue(OldName, false);
            if (enabled)
            {
                var (exe, args) = Program.LaunchCommand();
                key.SetValue(Name, "\"" + exe + "\"" + (args.Length > 0 ? " " + args : ""));
            }
            else key.DeleteValue(Name, false);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Автозапуск был включён под старым названием — переносим на CapDock.</summary>
    public static void MigrateOldName()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(OldName) is string) Set(true);
        }
        catch { }
    }
}

// ============================================================================
//  Win32 / GDI+ / Shell
// ============================================================================
internal static class Native
{
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr hdcDest, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, uint rop);

    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
    [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint affinity);
    [DllImport("user32.dll")] public static extern bool GetLayeredWindowAttributes(IntPtr hWnd, out uint crKey, out byte bAlpha, out uint dwFlags);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);

    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowW(string cls, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string name);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, ref RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]    private static extern int    SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>Задать владельца top-level окна (GWLP_HWNDPARENT). IntPtr.Zero = без владельца.</summary>
    public static void SetOwner(IntPtr hWnd, IntPtr owner)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, -8, owner);
        else                  SetWindowLong32(hWnd, -8, owner.ToInt32());
    }

    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(
        IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc,
        ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    /// <summary>То же, но pptDst = NULL: окно остаётся на месте (двигаем его только через SetWindowPos).</summary>
    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(
        IntPtr hWnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize, IntPtr hdcSrc,
        ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hWnd, uint attr, ref int val, uint size);
    [DllImport("dwmapi.dll")] public static extern int DwmFlush();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfoW(string path, uint fileAttr, ref SHFILEINFO info, uint cbInfo, uint flags);

    [DllImport("shell32.dll")]
    public static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHFileOperationW(ref SHFILEOPSTRUCT op);

    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lp);
}

[StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
[StructLayout(LayoutKind.Sequential)] internal struct SIZE  { public int cx, cy; }
[StructLayout(LayoutKind.Sequential)] internal struct RECT  { public int Left, Top, Right, Bottom; }

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct BLENDFUNCTION
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPOS
{
    public IntPtr hwnd;
    public IntPtr hwndInsertAfter;
    public int    x;
    public int    y;
    public int    cx;
    public int    cy;
    public uint   flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint   biSize;
    public int    biWidth;
    public int    biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint   biCompression;
    public uint   biSizeImage;
    public int    biXPelsPerMeter;
    public int    biYPelsPerMeter;
    public uint   biClrUsed;
    public uint   biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFO
{
    public BITMAPINFOHEADER bmiHeader;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHFILEINFO
{
    public IntPtr hIcon;
    public int    iIcon;
    public uint   dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
}

/// <summary>Для SHFileOperation (перенос файла как в Проводнике: с отменой по Ctrl+Z, через любые диски).</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct SHFILEOPSTRUCT
{
    public IntPtr hwnd;
    public uint   wFunc;
    public string pFrom;                 // список путей, завершённый двумя нулями
    public string pTo;
    public ushort fFlags;
    [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
    public IntPtr hNameMappings;
    public string lpszProgressTitle;
}

[ComImport]
[Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IImageList
{
    [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
    [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
    [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
    [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
    [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
    [PreserveSig] int Draw(IntPtr pimldp);
    [PreserveSig] int Remove(int i);
    [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
}

/// <summary>IDesktopWallpaper (Windows 8+): какие обои стоят на каком мониторе, режим и цвет фона.</summary>
[ComImport]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWallpaper
{
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
    uint GetMonitorDevicePathCount();
    RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
    void SetBackgroundColor(uint color);
    uint GetBackgroundColor();
    void SetPosition(int position);
    int  GetPosition();
    void SetSlideshow(IntPtr items);
    IntPtr GetSlideshow();
    void SetSlideshowOptions(int options, uint slideshowTick);
    void GetSlideshowOptions(out int options, out uint slideshowTick);
    void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);
    int  GetStatus();
    void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

[ComImport]
[Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
internal class DesktopWallpaperClass { }

/// <summary>
/// Смешивание полупрозрачного пикселя с непрозрачным фоном ровно так, как это делает GDI+
/// при CompositingQuality.HighQuality: в линейном свете по кривой sRGB, с отбрасыванием дробной части
/// (подобрано и сверено с GDI+: расхождение не больше 1 из 255). Только таблицы — без Math.Pow на кадр.
/// </summary>
internal static class GdiBlend
{
    private static readonly float[] Lin8      = new float[257];        // sRGB 0..255 → линейный (+ ограничитель)
    private static readonly float[] LinPremul = new float[256 * 256];  // [a*256 + c]: цвет c/a → линейный
    private static readonly byte[]  FromLin   = new byte[65536];       // линейный ×65535 → нижняя граница ответа

    private static double ToLin(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    static GdiBlend()
    {
        for (int v = 0; v < 256; v++) Lin8[v] = (float)ToLin(v / 255.0);
        Lin8[256] = float.MaxValue;
        for (int a = 1; a < 256; a++)
            for (int c = 0; c < 256; c++)
                LinPremul[a * 256 + c] = (float)ToLin(Math.Min(1.0, c / (double)a));
        int v2 = 0;
        for (int i = 0; i < 65536; i++)
        {
            float lin = i / 65535f;
            while (Lin8[v2 + 1] <= lin) v2++;
            FromLin[i] = (byte)v2;
        }
    }

    /// <summary>pc — канал источника (premultiplied), pa — его альфа (1..254), d — канал непрозрачного фона.</summary>
    public static int Channel(uint pc, uint pa, uint d)
    {
        float al  = pa * (1f / 255f);
        float lin = al * LinPremul[pa * 256 + pc] + (1 - al) * Lin8[d];
        if (lin >= 1f) return 255;
        int v = FromLin[(int)(lin * 65535f)];
        while (Lin8[v + 1] <= lin) v++;            // наибольшее v, у которого Lin8[v] ≤ lin
        return v;
    }
}

/// <summary>32-битный DIB (сверху вниз) с memory DC — буфер для BitBlt / PrintWindow.</summary>
internal sealed class GdiDib
{
    public IntPtr DC, Bitmap, Bits, Old;
    public int    W, H;

    public bool Ensure(int w, int h)
    {
        if (Bitmap != IntPtr.Zero && W == w && H == h) return true;
        Free();

        IntPtr screenDC = Native.GetDC(IntPtr.Zero);
        try { DC = Native.CreateCompatibleDC(screenDC); }
        finally { Native.ReleaseDC(IntPtr.Zero, screenDC); }
        if (DC == IntPtr.Zero) return false;

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize        = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth       = w,
                biHeight      = -h,
                biPlanes      = 1,
                biBitCount    = 32,
                biCompression = 0
            }
        };

        Bitmap = Native.CreateDIBSection(DC, ref bmi, 0, out Bits, IntPtr.Zero, 0);
        if (Bitmap == IntPtr.Zero || Bits == IntPtr.Zero) { Free(); return false; }

        Old = Native.SelectObject(DC, Bitmap);
        W = w;
        H = h;
        return true;
    }

    public void Free()
    {
        if (DC != IntPtr.Zero && Old != IntPtr.Zero) Native.SelectObject(DC, Old);
        if (Bitmap != IntPtr.Zero) Native.DeleteObject(Bitmap);
        if (DC     != IntPtr.Zero) Native.DeleteDC(DC);
        DC = Bitmap = Bits = Old = IntPtr.Zero;
        W = H = 0;
    }
}

// ============================================================================
//  Настройки. Файл %AppData%\CapDock\settings.json:
//    AppSettings  — язык и список профилей;
//    DockSettings — один профиль = одна капсула: всё, что раньше было константами
//                   в коде, плюс режим фона, позиция, порядок, скрытые и запомненные ярлыки.
//  Меняются в окне «Настройки» (трей); большинство применяется после перезапуска.
// ============================================================================
internal enum BackdropMode { Blur = 0, Acrylic = 1 }

internal sealed class AppSettings
{
    public const string AppName = "CapDock";

    public string             Language { get; set; } = "en";       // "en" / "ru"
    public List<DockSettings> Profiles { get; set; } = new List<DockSettings>();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool StartWithWindows { get; set; }                     // хранится не в файле, а в реестре (AutoStart)

    private static string Dir      => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
    public  static string FilePath => Path.Combine(Dir, "settings.json");

    // Программа раньше называлась Desktop Dock — её настройки подхватываем
    private static string OldFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopDock", "settings.json");

    public DockSettings Default => Profiles[0];

    public static AppSettings Load()
    {
        AppSettings app = null;
        try
        {
            string path = File.Exists(FilePath) ? FilePath : File.Exists(OldFilePath) ? OldFilePath : null;
            if (path != null)
            {
                string json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("Profiles", out _))
                    app = JsonSerializer.Deserialize<AppSettings>(json);
                else
                {
                    // Старый файл: одна капсула, настройки прямо в корне
                    app = new AppSettings { Profiles = { JsonSerializer.Deserialize<DockSettings>(json) } };
                    if (doc.RootElement.TryGetProperty("Language", out var lang) && lang.ValueKind == JsonValueKind.String)
                        app.Language = lang.GetString();
                }
            }
        }
        catch { app = null; }

        app ??= new AppSettings();
        app.Normalize();
        return app;
    }

    public void Normalize()
    {
        Language = string.Equals(Language, "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
        Profiles ??= new List<DockSettings>();
        Profiles.RemoveAll(p => p == null);
        if (Profiles.Count == 0) Profiles.Add(new DockSettings());

        // Первый профиль — стандартный: его нельзя удалить
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Profiles.Count; i++)
        {
            var p = Profiles[i];
            p.Root = this;
            if (i == 0) p.Id = DockSettings.DefaultId;
            else if (string.IsNullOrWhiteSpace(p.Id) || p.Id == DockSettings.DefaultId || ids.Contains(p.Id))
                p.Id = Guid.NewGuid().ToString("N");
            ids.Add(p.Id);
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = i == 0 ? "Default" : "Profile " + (i + 1);
            p.Normalize();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string tmp  = FilePath + ".tmp";
            var    opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder       = JavaScriptEncoder.UnsafeRelaxedJsonEscaping   // кириллица в файле читаемая
            };
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, opts));
            File.Move(tmp, FilePath, true);                                    // атомарная замена
        }
        catch { }
    }
}

internal sealed class DockSettings
{
    public const string DefaultId = "default";

    // ---- Профиль ----
    public string Id      { get; set; }
    public string Name    { get; set; }
    public bool   Enabled { get; set; } = true;      // капсула открыта (у остальных профилей — и открывается при запуске CapDock)

    [System.Text.Json.Serialization.JsonIgnore] public AppSettings Root { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsDefault => Id == DefaultId;
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayName => IsDefault ? L.T("Default", "Стандартный") : Name;
    public override string ToString() => DisplayName;

    // ---- Ярлыки ----
    public string ShortcutsFolder  { get; set; } = DefaultFolder;   // по умолчанию — рабочий стол
    public int    IconSize         { get; set; } = 48;
    public int    IconSpacing      { get; set; } = 10;
    public int    EdgePadding      { get; set; } = 12;      // отступ первого/последнего ярлыка от края капсулы
    public int    ScrollStep       { get; set; } = 70;      // px за один щелчок колеса
    public double ScrollSpeed      { get; set; } = 5.0;     // скорость плавной прокрутки

    public bool   InvertScroll     { get; set; }            // обратное направление прокрутки колесом

    // Перетащили файл в капсулу: false — только запомнить путь (файл остаётся где был),
    // true — перенести сам файл в папку с ярлыками. Выключено, чтобы ничего не пропало из папок случайно.
    public bool         MoveDroppedFiles { get; set; }
    public List<string> ExtraItems       { get; set; } = new List<string>();   // запомненные файлы вне папки
    public List<string> HiddenItems      { get; set; } = new List<string>();   // скрытые ярлыки (ключи); файлы не трогаются

    // ---- Капсула ----
    public string Orientation      { get; set; } = "Vertical";   // "Vertical" / "Horizontal"
    public int    BarWidth         { get; set; } = 60;      // толщина (поперёк капсулы)
    public int    BarHeight        { get; set; } = 700;     // длина (вдоль капсулы)
    public bool   PositionLocked   { get; set; } = true;    // выключено — капсулу можно таскать мышью
    public int?   PositionX        { get; set; }            // null — у правого края, по центру по вертикали
    public int?   PositionY        { get; set; }

    // ---- Автоскрытие: капсула уезжает за ближайший край экрана и выезжает, когда к нему подвести курсор ----
    public bool   AutoHide            { get; set; }          // выключено, пока не включат
    public int    AutoHideShowMs      { get; set; } = 150;   // за сколько выезжает
    public int    AutoHideHideMs      { get; set; } = 250;   // за сколько уезжает
    public int    AutoHideDelayMs     { get; set; } = 700;   // через сколько уехать после ухода курсора
    public int    AutoHideShowDelayMs { get; set; } = 0;     // сколько держать курсор у края, чтобы выехала
    public int    AutoHidePeek        { get; set; } = 0;     // сколько пикселей видно, когда спрятана

    // ---- Фон ----
    public string BackgroundMode   { get; set; } = "Blur";
    public bool   LiveBackground   { get; set; } = true;    // фон берётся с экрана (видно живые обои)
    public int    BlurRadius       { get; set; } = 4;
    public int    BlurPasses       { get; set; } = 1;
    public string BlurTintColor    { get; set; } = "#1A1A1A";
    public int    BlurTintOpacity  { get; set; } = 50;      // %
    public int    AcrylicBlurRadius  { get; set; } = 6;
    public int    AcrylicBlurPasses  { get; set; } = 3;
    public int    AcrylicSaturation  { get; set; } = 125;   // %
    public int    AcrylicMilkiness   { get; set; } = 10;    // % белого в режиме Exclusion
    public string AcrylicTintColor   { get; set; } = "#1F1F1F";
    public int    AcrylicTintOpacity { get; set; } = 50;    // %
    public int    AcrylicNoise       { get; set; } = 5;     // ± уровней яркости
    public int    DownScale        { get; set; } = 4;       // во сколько раз уменьшать картинку перед размытием
    public int    CaptureMargin    { get; set; } = 64;      // поле вокруг капсулы, которое тоже размывается
    public int    LiveFps          { get; set; } = 30;      // живой фон с экрана
    public int    LiveWallpaperFps { get; set; } = 20;      // живые обои без захвата экрана (PrintWindow)
    public int    StaticRefreshMs  { get; set; } = 100;     // статичный фон: как часто проверять изменения

    // ---- Перетаскивание ярлыков и анимация ----
    public int    LongPressMs        { get; set; } = 400;
    public int    PressMoveTolerance { get; set; } = 8;
    public double ReorderSpeed       { get; set; } = 14.0;
    public int    LiftScale          { get; set; } = 15;    // % увеличения поднятого ярлыка
    public int    ShadowOpacity      { get; set; } = 55;    // %
    public int    AutoScrollZone     { get; set; } = 56;
    public int    AutoScrollSpeed    { get; set; } = 600;   // px/с
    public int    RenderIntervalMs   { get; set; } = 4;

    public List<string> Order      { get; set; } = new List<string>();

    public static string DefaultFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsHorizontal => string.Equals(Orientation, "Horizontal", StringComparison.OrdinalIgnoreCase);

    public BackdropMode GetMode() =>
        Enum.TryParse(BackgroundMode, true, out BackdropMode m) && Enum.IsDefined(typeof(BackdropMode), m)
            ? m : BackdropMode.Blur;

    /// <summary>Сохраняет весь файл настроек (все профили).</summary>
    public void Save() => Root?.Save();

    /// <summary>Независимая копия (для окна настроек: правки не трогают работающую капсулу до «Сохранить»).</summary>
    public DockSettings Clone()
    {
        var c = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(this));
        c.Root = Root;
        return c;
    }

    // То, что капсула меняет сама во время работы (перетаскивание, скрытие, бросок файлов) —
    // окно настроек это не перезаписывает старыми значениями.
    private static readonly HashSet<string> RuntimeOwned = new HashSet<string>
    {
        nameof(Id), nameof(Root), nameof(Order), nameof(ExtraItems), nameof(HiddenItems), nameof(PositionX), nameof(PositionY)
    };

    /// <summary>Переносит из src всё, что редактируется в окне настроек.</summary>
    public void CopyEditableFrom(DockSettings src)
    {
        foreach (var p in typeof(DockSettings).GetProperties())
            if (p.CanRead && p.CanWrite && !RuntimeOwned.Contains(p.Name))
                p.SetValue(this, p.GetValue(src));
    }

    /// <summary>Возвращает значения в допустимые рамки (файл могли поправить руками).</summary>
    public void Normalize()
    {
        Order       ??= new List<string>();
        ExtraItems  ??= new List<string>();
        HiddenItems ??= new List<string>();
        if (string.IsNullOrWhiteSpace(ShortcutsFolder)) ShortcutsFolder = DefaultFolder;
        Orientation = IsHorizontal ? "Horizontal" : "Vertical";

        BarWidth    = Math.Clamp(BarWidth, 24, 400);
        BarHeight   = Math.Clamp(BarHeight, BarWidth + 2, 4000);
        IconSize    = Math.Clamp(IconSize, 8, BarWidth);
        IconSpacing = Math.Clamp(IconSpacing, 0, 200);
        EdgePadding = Math.Clamp(EdgePadding, 0, 400);
        ScrollStep  = Math.Clamp(ScrollStep, 1, 1000);
        ScrollSpeed = Math.Clamp(ScrollSpeed, 0.5, 50);

        BlurRadius         = Math.Clamp(BlurRadius, 0, 50);
        BlurPasses         = Math.Clamp(BlurPasses, 1, 6);
        BlurTintOpacity    = Math.Clamp(BlurTintOpacity, 0, 100);
        AcrylicBlurRadius  = Math.Clamp(AcrylicBlurRadius, 0, 50);
        AcrylicBlurPasses  = Math.Clamp(AcrylicBlurPasses, 1, 6);
        AcrylicSaturation  = Math.Clamp(AcrylicSaturation, 0, 300);
        AcrylicMilkiness   = Math.Clamp(AcrylicMilkiness, 0, 50);
        AcrylicTintOpacity = Math.Clamp(AcrylicTintOpacity, 0, 100);
        AcrylicNoise       = Math.Clamp(AcrylicNoise, 0, 40);
        BlurTintColor      = NormalizeColor(BlurTintColor, "#1A1A1A");
        AcrylicTintColor   = NormalizeColor(AcrylicTintColor, "#1F1F1F");
        DownScale          = Math.Clamp(DownScale, 1, 16);
        CaptureMargin      = Math.Clamp(CaptureMargin, 0, 400);
        LiveFps            = Math.Clamp(LiveFps, 1, 120);
        LiveWallpaperFps   = Math.Clamp(LiveWallpaperFps, 1, 60);
        StaticRefreshMs    = Math.Clamp(StaticRefreshMs, 16, 5000);

        AutoHideShowMs      = Math.Clamp(AutoHideShowMs, 0, 3000);
        AutoHideHideMs      = Math.Clamp(AutoHideHideMs, 0, 3000);
        AutoHideDelayMs     = Math.Clamp(AutoHideDelayMs, 0, 30000);
        AutoHideShowDelayMs = Math.Clamp(AutoHideShowDelayMs, 0, 5000);
        AutoHidePeek        = Math.Clamp(AutoHidePeek, 0, 100);

        LongPressMs        = Math.Clamp(LongPressMs, 100, 3000);
        PressMoveTolerance = Math.Clamp(PressMoveTolerance, 1, 100);
        ReorderSpeed       = Math.Clamp(ReorderSpeed, 1, 100);
        LiftScale          = Math.Clamp(LiftScale, 0, 100);
        ShadowOpacity      = Math.Clamp(ShadowOpacity, 0, 100);
        AutoScrollZone     = Math.Clamp(AutoScrollZone, 0, 500);
        AutoScrollSpeed    = Math.Clamp(AutoScrollSpeed, 0, 5000);
        RenderIntervalMs   = Math.Clamp(RenderIntervalMs, 1, 100);
    }

    /// <summary>"#RRGGBB" → 0xRRGGBB.</summary>
    public static uint ParseColor(string s, uint def)
    {
        s = s?.Trim().TrimStart('#');
        return s != null && s.Length == 6 &&
               uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? v : def;
    }

    public static string FormatColor(uint rgb) => "#" + (rgb & 0xFFFFFF).ToString("X6");

    private static string NormalizeColor(string s, string def) =>
        FormatColor(ParseColor(s, ParseColor(def, 0)));
}

// ============================================================================
//  Обои рабочего стола: какой файл, как растянут, какой цвет фона —
//  и отрисовка нужного кусочка экрана ровно так, как его рисует Windows.
// ============================================================================
internal sealed class WallpaperInfo
{
    public const int PosCenter = 0, PosTile = 1, PosStretch = 2, PosFit = 3, PosFill = 4, PosSpan = 5;

    public string    ImagePath;                  // null — обоев нет, только цвет
    public int       Position   = PosFill;
    public Color     Background = Color.Black;
    public Rectangle Monitor;                    // монитор, на котором стоит док
    public Rectangle Virtual;                    // весь рабочий стол (для режима «Расширение»)
    public long      Stamp;                      // время изменения + размер файла

    // Картинка загружается один раз: при перетаскивании капсулы обои перерисовываются часто.
    // Render/Dispose вызывает только фоновый поток.
    private Bitmap _image;
    private bool   _imageLoaded;

    public void Dispose()
    {
        _image?.Dispose();
        _image       = null;
        _imageLoaded = false;
    }

    public bool SameAs(WallpaperInfo o) =>
        o != null &&
        string.Equals(ImagePath, o.ImagePath, StringComparison.OrdinalIgnoreCase) &&
        Position == o.Position && Background.ToArgb() == o.Background.ToArgb() &&
        Monitor == o.Monitor && Virtual == o.Virtual && Stamp == o.Stamp;

    /// <summary>Вызывать из UI-потока (COM-объект Shell).</summary>
    public static WallpaperInfo Query(Point dockCenter)
    {
        var info = new WallpaperInfo
        {
            Monitor = Screen.FromPoint(dockCenter).Bounds,
            Virtual = SystemInformation.VirtualScreen
        };

        string path = null;

        // 1) Реестр: файл и режим (Заполнение / По размеру / Растянуть / Замостить / По центру / Расширение)
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            if (key != null)
            {
                path = key.GetValue("WallPaper") as string;
                int style = ToInt(key.GetValue("WallpaperStyle"), 10);
                int tile  = ToInt(key.GetValue("TileWallpaper"), 0);
                info.Position = style switch
                {
                    2  => PosStretch,
                    6  => PosFit,
                    10 => PosFill,
                    22 => PosSpan,
                    _  => tile == 1 ? PosTile : PosCenter
                };
            }
        }
        catch { }

        // 2) Реестр: цвет фона (виден, если обоев нет или они «По размеру»/«По центру»)
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
            var parts = (key?.GetValue("Background") as string)?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts != null && parts.Length >= 3)
                info.Background = Color.FromArgb(Clamp255(ToInt(parts[0], 0)),
                                                 Clamp255(ToInt(parts[1], 0)),
                                                 Clamp255(ToInt(parts[2], 0)));
        }
        catch { }

        // 3) IDesktopWallpaper — точнее: свои обои у каждого монитора, слайд-шоу и т.п.
        try
        {
            var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
            try
            {
                uint count = dw.GetMonitorDevicePathCount();
                for (uint i = 0; i < count; i++)
                {
                    try
                    {
                        string id = dw.GetMonitorDevicePathAt(i);
                        RECT   r  = dw.GetMonitorRECT(id);
                        var    rc = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                        if (rc.Width <= 0 || rc.Height <= 0 || !rc.Contains(dockCenter)) continue;

                        info.Monitor = rc;
                        string p = dw.GetWallpaper(id);
                        if (!string.IsNullOrEmpty(p)) path = p;
                        break;
                    }
                    catch { }
                }

                int pos = dw.GetPosition();
                if (pos >= PosCenter && pos <= PosSpan) info.Position = pos;

                uint c = dw.GetBackgroundColor();                       // COLORREF = 0x00BBGGRR
                info.Background = Color.FromArgb((int)(c & 0xFF), (int)((c >> 8) & 0xFF), (int)((c >> 16) & 0xFF));
            }
            finally { Marshal.ReleaseComObject(dw); }
        }
        catch { }

        // Исходный файл удалён/переехал — Windows показывает свою копию TranscodedWallpaper.
        if (!string.IsNullOrEmpty(path) && !File.Exists(path))
        {
            string tw = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                     @"Microsoft\Windows\Themes\TranscodedWallpaper");
            path = File.Exists(tw) ? tw : null;
        }
        if (string.IsNullOrEmpty(path)) path = null;

        if (path != null)
        {
            try
            {
                var fi = new FileInfo(path);
                info.Stamp = fi.LastWriteTimeUtc.Ticks ^ (fi.Length << 20);
            }
            catch { }
        }

        info.ImagePath = path;
        return info;
    }

    /// <summary>Рисует обои для прямоугольника region (экранные координаты). Пиксели 0xAARRGGBB.</summary>
    public uint[] Render(Rectangle region)
    {
        using var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(255, Background));

            if (!_imageLoaded)
            {
                _image       = LoadImage(ImagePath);
                _imageLoaded = true;
            }
            var img = _image;
            if (img != null && img.Width > 0 && img.Height > 0)
            {
                Rectangle area = Position == PosSpan ? Virtual : Monitor;
                Rectangle vis  = Rectangle.Intersect(area, region);
                if (vis.Width > 0 && vis.Height > 0)
                {
                    g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode    = PixelOffsetMode.HighQuality;
                    g.CompositingQuality = CompositingQuality.HighQuality;
                    var clip = new Rectangle(vis.X - region.X, vis.Y - region.Y, vis.Width, vis.Height);
                    g.SetClip(clip);

                    if (Position == PosTile)
                    {
                        // Плитка начинается от левого верхнего угла монитора
                        using var tb = new TextureBrush(img, WrapMode.Tile);
                        tb.TranslateTransform(area.X - region.X, area.Y - region.Y);
                        g.FillRectangle(tb, clip);
                    }
                    else
                    {
                        RectangleF dest = Layout(area, img.Width, img.Height);
                        RectangleF part = RectangleF.Intersect(dest, vis);
                        if (part.Width > 0 && part.Height > 0)
                        {
                            // Рисуем только видимый кусок картинки — быстро даже для 8K-обоев
                            float kx  = img.Width  / dest.Width;
                            float ky  = img.Height / dest.Height;
                            var   src = new RectangleF((part.X - dest.X) * kx, (part.Y - dest.Y) * ky,
                                                       part.Width * kx, part.Height * ky);
                            var   pts = new[]
                            {
                                new PointF(part.Left  - region.X, part.Top    - region.Y),
                                new PointF(part.Right - region.X, part.Top    - region.Y),
                                new PointF(part.Left  - region.X, part.Bottom - region.Y)
                            };
                            using var attrs = new ImageAttributes();
                            attrs.SetWrapMode(WrapMode.TileFlipXY);   // без тёмной каймы по краям
                            g.DrawImage(img, pts, src, GraphicsUnit.Pixel, attrs);
                        }
                    }
                }
            }
        }
        return DockForm.ToPixels(bmp);
    }

    private RectangleF Layout(Rectangle area, int iw, int ih)
    {
        switch (Position)
        {
            case PosStretch:
                return area;

            case PosCenter:
                return new RectangleF(area.X + (area.Width - iw) / 2, area.Y + (area.Height - ih) / 2, iw, ih);

            case PosFit:
            {
                float s = Math.Min(area.Width / (float)iw, area.Height / (float)ih);
                float w = iw * s, h = ih * s;
                return new RectangleF(area.X + (area.Width - w) / 2f, area.Y + (area.Height - h) / 2f, w, h);
            }

            default:   // Заполнение и Расширение
            {
                float s = Math.Max(area.Width / (float)iw, area.Height / (float)ih);
                float w = iw * s, h = ih * s;
                return new RectangleF(area.X + (area.Width - w) / 2f, area.Y + (area.Height - h) / 2f, w, h);
            }
        }
    }

    private static Bitmap LoadImage(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            byte[] data = File.ReadAllBytes(path);        // файл не блокируется
            using var ms  = new MemoryStream(data);
            using var src = Image.FromStream(ms);
            var bmp = new Bitmap(src);
            ApplyExifOrientation(src, bmp);
            return bmp;
        }
        catch { return null; }
    }

    private static void ApplyExifOrientation(Image src, Bitmap dst)
    {
        try
        {
            if (Array.IndexOf(src.PropertyIdList, 0x0112) < 0) return;
            var item = src.GetPropertyItem(0x0112);
            if (item?.Value == null || item.Value.Length < 1) return;

            RotateFlipType t = item.Value[0] switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            };
            if (t != RotateFlipType.RotateNoneFlipNone) dst.RotateFlip(t);
        }
        catch { }
    }

    private static int ToInt(object v, int def) =>
        v is int i ? i : int.TryParse(v as string, out int r) ? r : def;

    private static int Clamp255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
}

// ============================================================================
//  Tooltip
// ============================================================================
internal sealed class TooltipForm : Form
{
    private readonly Label _label;

    public TooltipForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        TopMost         = true;
        StartPosition   = FormStartPosition.Manual;
        BackColor       = Color.Black;

        _label = new Label
        {
            AutoSize  = false,
            Dock      = DockStyle.Fill,
            ForeColor = Color.White,
            BackColor = Color.Black,
            Font      = new Font("Segoe UI", 10f),
            TextAlign   = ContentAlignment.MiddleLeft,
            Padding     = new Padding(6, 0, 6, 0),
            UseMnemonic = false                 // «&» в имени файла — обычный символ
        };
        Controls.Add(_label);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080;
            cp.ExStyle |= 0x08000000;
            return cp;
        }
    }

    /// <summary>Ставит текст и подгоняет размер под него целиком. Возвращает новый размер.</summary>
    public Size SetText(string text)
    {
        if (text == _label.Text && Size == _lastSize) return _lastSize;   // при прокрутке меняется только место
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        var t     = TextRenderer.MeasureText(text, _label.Font, Size.Empty, flags);
        var size  = new Size(Math.Max(34, t.Width + _label.Padding.Horizontal + 6), Math.Max(26, t.Height + 8));
        _label.Text = text;
        Size = size;
        _lastSize = size;
        return size;
    }

    private Size _lastSize;
}

// ============================================================================
//  Окно «Настройки» (трей → Настройки…)
//  Сверху — профиль (каждый профиль = своя капсула), ниже — его настройки.
//  Правки копятся в копиях профилей и попадают в settings.json только по «Сохранить».
//  Большинство значений применяется после перезапуска программы.
// ============================================================================
internal sealed class SettingsForm : Form
{
    private readonly AppSettings  _app;
    private readonly Action       _onSaved;
    private readonly Action       _restart;
    private readonly List<(Action<DockSettings> Load, Action<DockSettings> Save)> _binds = new();
    private readonly TabControl   _tabs;
    private readonly Label        _status;

    // Профили: правим копии; у новых профилей оригинала нет (null)
    private readonly List<DockSettings>                     _edits  = new();
    private readonly Dictionary<DockSettings, DockSettings> _origOf = new();
    private readonly HashSet<DockSettings>                  _resetPos = new();
    private DockSettings _cur;
    private string       _language;                 // общее для всех профилей
    private bool         _autoStart;
    private bool         _switching;

    private ComboBox _profileBox;
    private TextBox  _nameBox;
    private CheckBox _enabledBox, _autoStartBox;
    private Button   _deleteBtn;

    // Координаты: правка на копии профиля, капсула двигается сразу (её оригинал — в _posPreviewed)
    private readonly CapDockApp                       _host;
    private readonly Dictionary<DockSettings, Point>  _posEdit      = new();
    private readonly HashSet<DockSettings>            _posPreviewed = new();
    private (TrackBar Bar, NumericUpDown Num) _posX, _posY;
    private bool _saved;

    private static string DefaultStatus => L.T(
        "Changes apply after CapDock restarts. Position, auto-hide, showing docks, background mode, live background, dock lock and Start with Windows apply right away.",
        "Изменения применяются после перезапуска CapDock. Положение, автоскрытие, показ капсул, режим фона, живой фон, закрепление и автозапуск — сразу.");

    public SettingsForm(AppSettings app, CapDockApp host, Icon icon, DockSettings select, Action onSaved, Action restart)
    {
        _app     = app;
        _host    = host;
        _onSaved = onSaved;
        _restart = restart;
        _language  = app.Language;
        _autoStart = AutoStart.IsEnabled();          // автозапуск живёт в реестре, а не в файле
        foreach (var p in app.Profiles)
        {
            var copy = p.Clone();
            _edits.Add(copy);
            _origOf[copy] = p;
        }
        _cur = _edits.FirstOrDefault(e => select != null && e.Id == select.Id) ?? _edits[0];

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode       = AutoScaleMode.Dpi;
        Text                = L.T("CapDock — Settings", "CapDock — настройки");
        Icon                = icon;
        Font                = SystemFonts.MessageBoxFont;
        StartPosition       = FormStartPosition.CenterScreen;
        ClientSize          = new Size(800, 720);
        MinimumSize         = new Size(800, 460);
        MaximizeBox         = false;
        ShowInTaskbar       = true;

        _tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 4) };

        // ---- Нижняя панель: подсказка и кнопки ----
        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3, RowCount = 2, Padding = new Padding(8, 6, 8, 8)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // промежуток между группами кнопок
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status = new Label
        {
            Text = DefaultStatus, AutoSize = true, MaximumSize = new Size(760, 0),
            ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 6)
        };
        bottom.Controls.Add(_status, 0, 0);
        bottom.SetColumnSpan(_status, 3);

        var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        left.Controls.Add(MakeButton(L.T("Reset to defaults", "Сбросить по умолчанию"), ResetToDefaults));
        left.Controls.Add(MakeButton(L.T("Settings file", "Файл настроек"), OpenSettingsFile));
        bottom.Controls.Add(left, 0, 1);

        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        right.Controls.Add(MakeButton(L.T("Save and restart", "Сохранить и перезапустить"), () => { if (SaveSettings()) _restart?.Invoke(); }));
        right.Controls.Add(MakeButton(L.T("Save", "Сохранить"), () => { if (SaveSettings()) Close(); }));
        var cancel = MakeButton(L.T("Cancel", "Отмена"), Close);
        right.Controls.Add(cancel);
        bottom.Controls.Add(right, 2, 1);
        CancelButton = cancel;

        Controls.Add(_tabs);
        Controls.Add(BuildProfileBar());
        Controls.Add(bottom);

        BuildPages();
        LoadCurrent();
        ResumeLayout(true);

        // Закрыли без «Сохранить» — капсулы, сдвинутые ползунками, возвращаются на место
        FormClosed += (_, __) =>
        {
            foreach (var orig in _posPreviewed) _host.EndPositionPreview(orig, _saved);
        };
    }

    // ------------------------------------------------------------------------
    //  Координаты капсулы
    // ------------------------------------------------------------------------
    private (TrackBar, NumericUpDown) PositionRow(TableLayoutPanel t, string label, int min, int max)
    {
        var bar = new TrackBar
        {
            Minimum = min, Maximum = max, TickStyle = TickStyle.None, AutoSize = false,
            Width = 230, Height = 30, LargeChange = 50, SmallChange = 1
        };
        var n = new NumericUpDown { Minimum = min, Maximum = max, Width = 72, TextAlign = HorizontalAlignment.Right };
        bool sync = false;
        bar.ValueChanged += (_, __) => { if (sync) return; sync = true; n.Value = bar.Value; sync = false; OnPositionChanged(); };
        n.ValueChanged   += (_, __) => { if (sync) return; sync = true; bar.Value = (int)n.Value; sync = false; OnPositionChanged(); };
        Row(t, label, bar, n, Unit("px"));
        return (bar, n);
    }

    private void OnPositionChanged()
    {
        if (_switching) return;
        var want = new Point((int)_posX.Num.Value, (int)_posY.Num.Value);
        _posEdit[_cur] = want;
        _resetPos.Remove(_cur);

        // Капсула открыта — двигаем её сразу; у края экрана она остановится, ползунки покажут где
        var orig = _origOf.TryGetValue(_cur, out var o) ? o : null;
        if (orig != null && _host.PreviewDockPosition(orig, want) is Point at)
        {
            _posPreviewed.Add(orig);
            if (at != want) { _posEdit[_cur] = at; ShowPosition(at); }
        }
    }

    private void ShowPosition(Point p)
    {
        bool was = _switching;
        _switching = true;
        SetPos(_posX, p.X);
        SetPos(_posY, p.Y);
        _switching = was;
    }

    private static void SetPos((TrackBar Bar, NumericUpDown Num) c, int v)
    {
        v = (int)Math.Clamp(v, c.Num.Minimum, c.Num.Maximum);
        c.Num.Value = v;
        c.Bar.Value = v;
    }

    // ------------------------------------------------------------------------
    //  Профили
    // ------------------------------------------------------------------------
    private Control BuildProfileBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Padding = new Padding(8, 8, 8, 4)
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _profileBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, Margin = new Padding(3, 5, 3, 3) };
        foreach (var e in _edits) _profileBox.Items.Add(e);
        _profileBox.SelectedItem = _cur;
        _profileBox.SelectedIndexChanged += (_, __) =>
        {
            if (_switching || _profileBox.SelectedItem is not DockSettings next || next == _cur) return;
            StoreCurrent();
            _cur = next;
            LoadCurrent();
        };
        var add = MakeButton(L.T("Add profile", "Добавить профиль"), AddProfile);
        _deleteBtn = MakeButton(L.T("Delete", "Удалить"), DeleteProfile);
        AddBarRow(bar, L.T("Profile", "Профиль"), _profileBox, add, _deleteBtn);

        _nameBox = new TextBox { Width = 230, Margin = new Padding(3, 5, 3, 3) };
        _nameBox.TextChanged += (_, __) =>
        {
            if (_switching || _cur.IsDefault) return;
            _cur.Name = _nameBox.Text;
            _switching = true;
            int i = _profileBox.Items.IndexOf(_cur);
            if (i >= 0) _profileBox.Items[i] = _cur;                  // обновить название в списке
            _switching = false;
        };
        AddBarRow(bar, L.T("Name", "Название"), _nameBox);

        _enabledBox   = new CheckBox { AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        _autoStartBox = new CheckBox
        {
            AutoSize = true, Margin = new Padding(3, 6, 3, 3),
            Text = L.T("Start CapDock with Windows", "Запускать CapDock вместе с Windows")
        };
        _autoStartBox.CheckedChanged += (_, __) => { if (!_switching) _autoStart = _autoStartBox.Checked; };
        AddBarRow(bar, "", _enabledBox, _autoStartBox);

        var hint = new Label
        {
            AutoSize = true, MaximumSize = new Size(760, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 4),
            Text = L.T("Each profile is a separate dock with its own settings, size and folder. The default profile can't be deleted. " +
                       "Other profiles open when CapDock starts if “Open” is checked; CapDock itself starts with Windows from the default profile.",
                       "Каждый профиль — отдельная капсула со своими настройками, размером и папкой. Стандартный профиль удалить нельзя. " +
                       "Остальные профили открываются при запуске CapDock, если отмечено «Открывать»; сам CapDock запускается с Windows по галочке стандартного профиля.")
        };
        int row = bar.RowCount++;
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bar.Controls.Add(hint, 0, row);
        bar.SetColumnSpan(hint, 2);
        return bar;
    }

    private static void AddBarRow(TableLayoutPanel t, string label, params Control[] controls)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, row);
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        flow.Controls.AddRange(controls);
        t.Controls.Add(flow, 1, row);
    }

    /// <summary>Значения с экрана → в копию текущего профиля.</summary>
    private void StoreCurrent()
    {
        foreach (var b in _binds) b.Save(_cur);
        if (!_cur.IsDefault) _cur.Name = string.IsNullOrWhiteSpace(_nameBox.Text) ? _cur.Name : _nameBox.Text.Trim();
        _cur.Enabled = _enabledBox.Checked;
    }

    /// <summary>Копия текущего профиля → на экран.</summary>
    private void LoadCurrent()
    {
        _switching = true;
        LoadFrom(_cur);
        _nameBox.Text         = _cur.DisplayName;
        _nameBox.Enabled      = !_cur.IsDefault;
        _deleteBtn.Enabled    = !_cur.IsDefault;
        _enabledBox.Text      = _cur.IsDefault
            ? L.T("Show this dock", "Показывать эту капсулу")
            : L.T("Open this dock (also every time CapDock starts)", "Открывать эту капсулу (и при каждом запуске CapDock)");
        _enabledBox.Checked   = _cur.Enabled;
        _autoStartBox.Visible = _cur.IsDefault;
        _autoStartBox.Checked = _autoStart;

        // Координаты: то, что уже двигали в этом окне, иначе — где капсула сейчас (или сохранённое место)
        var orig = _origOf.TryGetValue(_cur, out var o) ? o : null;
        Point? pos = _posEdit.TryGetValue(_cur, out var pe) ? pe : orig != null ? _host.GetDockPosition(orig) : null;
        ShowPosition(pos ?? Point.Empty);
        _switching = false;
    }

    private void AddProfile()
    {
        StoreCurrent();
        int n = _edits.Count + 1;
        while (_edits.Any(e => string.Equals(e.Name, L.T("Profile ", "Профиль ") + n, StringComparison.OrdinalIgnoreCase))) n++;
        var p = new DockSettings { Id = Guid.NewGuid().ToString("N"), Name = L.T("Profile ", "Профиль ") + n, Root = _app };
        p.Normalize();
        _edits.Add(p);
        _origOf[p] = null;

        _switching = true;
        _profileBox.Items.Add(p);
        _profileBox.SelectedItem = p;
        _switching = false;
        _cur = p;
        LoadCurrent();
        _status.Text = L.T("New profile added — set it up and press “Save”. Its dock opens right after saving.",
                           "Профиль добавлен — настройте его и нажмите «Сохранить». Его капсула откроется сразу после сохранения.");
    }

    private void DeleteProfile()
    {
        if (_cur.IsDefault) return;
        string q = string.Format(L.T("Delete profile “{0}”? Its dock will close. Files are not touched.",
                                     "Удалить профиль «{0}»? Его капсула закроется. Файлы не трогаются."), _cur.DisplayName);
        if (MessageBox.Show(this, q, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        var gone = _cur;
        _edits.Remove(gone);
        _origOf.Remove(gone);
        _switching = true;
        _profileBox.Items.Remove(gone);
        _cur = _edits[0];
        _profileBox.SelectedItem = _cur;
        _switching = false;
        LoadCurrent();
        _status.Text = L.T("The profile will be deleted when you press “Save”.", "Профиль удалится после нажатия «Сохранить».");
    }

    // ------------------------------------------------------------------------
    private void BuildPages()
    {
        string px = L.T("px", "px"), ms = L.T("ms", "мс"), fps = L.T("fps", "кадров/с");

        // ---------- Общие ----------
        var t = Page(L.T("General", "Общие"));
        Section(t, L.T("Language", "Язык"));
        Choice(t, L.T("Interface language", "Язык интерфейса"), new[] { "English", "Русский" },
               _ => _language == "ru" ? 1 : 0, (_, i) => _language = i == 1 ? "ru" : "en");
        Hint(t, L.T("Same for all profiles. Applies after restart.", "Общий для всех профилей. Применяется после перезапуска."));

        // ---------- Ярлыки ----------
        t = Page(L.T("Shortcuts", "Ярлыки"));
        Section(t, L.T("Shortcuts folder", "Папка с ярлыками"));
        FolderRow(t, L.T("Folder", "Папка"), s => s.ShortcutsFolder, (s, v) => s.ShortcutsFolder = v);
        Hint(t, L.T("Default: the desktop (including the shared “all users” shortcuts). All files and folders are shown except hidden ones.",
                    "По умолчанию — рабочий стол (вместе с общими ярлыками «для всех пользователей»). Показываются все файлы и папки, кроме скрытых."));

        Section(t, L.T("Dragging files into the dock", "Перетаскивание файлов в капсулу"));
        Check(t, L.T("Move dropped files into the shortcuts folder", "Переносить перетащенные файлы в папку с ярлыками"),
              s => s.MoveDroppedFiles, (s, v) => s.MoveDroppedFiles = v);
        Hint(t, L.T("Off (default): the dock only remembers the file, the real file stays where it was. " +
                    "On: the file itself is moved into the shortcuts folder and disappears from its old place (Ctrl+Z in Explorer undoes it). " +
                    "Either way it appears exactly where you drop it. Right-click a remembered file to remove it from the dock.",
                    "Выключено (по умолчанию): капсула только запоминает файл, настоящий файл остаётся на своём месте. " +
                    "Включено: сам файл переносится в папку с ярлыками и пропадает из старого места (отмена — Ctrl+Z в Проводнике). " +
                    "В обоих случаях он появляется там, куда его бросили. Запомненный файл убирается из капсулы правым кликом."));

        Section(t, L.T("Icons", "Иконки"));
        Num(t, L.T("Icon size", "Размер иконок"), 8, 400, px, s => s.IconSize, (s, v) => s.IconSize = (int)v,
            hint: L.T("No larger than the dock thickness (“Dock” tab).", "Не больше толщины капсулы (вкладка «Капсула»)."));
        Num(t, L.T("Space between icons", "Расстояние между иконками"), 0, 200, px, s => s.IconSpacing, (s, v) => s.IconSpacing = (int)v);
        Num(t, L.T("Padding at the dock ends", "Отступ от концов капсулы"), 0, 400, px, s => s.EdgePadding, (s, v) => s.EdgePadding = (int)v);

        Section(t, L.T("Mouse wheel", "Прокрутка колесом"));
        Choice(t, L.T("Scroll direction", "Направление прокрутки"),
               new[] { L.T("Normal", "Обычное"), L.T("Reversed", "Обратное") },
               s => s.InvertScroll ? 1 : 0, (s, i) => s.InvertScroll = i == 1);
        Hint(t, L.T("Normal: wheel up moves the icons top-to-bottom in a vertical dock and right-to-left in a horizontal one. Reversed swaps both.",
                    "Обычное: колесо вверх двигает ярлыки сверху вниз в вертикальной капсуле и справа налево в горизонтальной. Обратное меняет оба направления."));
        Num(t, L.T("Scroll step", "Шаг прокрутки"), 1, 1000, px, s => s.ScrollStep, (s, v) => s.ScrollStep = (int)v);
        Num(t, L.T("Smooth scroll speed", "Скорость плавной прокрутки"), 0.5m, 50, "", s => (decimal)s.ScrollSpeed,
            (s, v) => s.ScrollSpeed = (double)v, decimals: 1,
            hint: L.T("Higher — scrolling settles faster.", "Больше — прокрутка доезжает быстрее."));

        // ---------- Капсула ----------
        t = Page(L.T("Dock", "Капсула"));
        Section(t, L.T("Orientation", "Положение капсулы"));
        Choice(t, L.T("Orientation", "Ориентация"),
               new[] { L.T("Vertical", "Вертикально"), L.T("Horizontal", "Горизонтально") },
               s => s.IsHorizontal ? 1 : 0, (s, i) => s.Orientation = i == 1 ? "Horizontal" : "Vertical");
        Hint(t, L.T("When the orientation changes, the dock goes back to its default place: the right edge (vertical) or the bottom (horizontal).",
                    "При смене ориентации капсула встаёт на место по умолчанию: у правого края (вертикально) или внизу (горизонтально)."));

        Section(t, L.T("Size", "Размер"));
        Num(t, L.T("Thickness (across)", "Толщина (поперёк)"), 24, 400, px, s => s.BarWidth, (s, v) => s.BarWidth = (int)v);
        Num(t, L.T("Length (along)", "Длина (вдоль)"), 26, 4000, px, s => s.BarHeight, (s, v) => s.BarHeight = (int)v);

        Section(t, L.T("Position", "Положение на экране"));
        Check(t, L.T("Lock the dock in place", "Закрепить капсулу на месте"), s => s.PositionLocked, (s, v) => s.PositionLocked = v);
        Hint(t, L.T("On — the dock stays put and a click opens a shortcut.\n" +
                    "Off — drag the dock anywhere with the mouse; shortcuts don't open (so it's easier to drag). " +
                    "The new place is remembered. Applies right after saving.",
                    "Включено — капсула стоит на месте, клик открывает ярлык.\n" +
                    "Выключено — капсулу можно перетаскивать мышью по экрану, а ярлыки не открываются " +
                    "(чтобы удобнее было таскать). Новое место запоминается. Применяется сразу после сохранения."));

        // Точные координаты: капсула двигается сразу, пока ведёшь ползунок; «Отмена» вернёт её на место
        var vs = SystemInformation.VirtualScreen;
        _posX = PositionRow(t, L.T("Position X (left edge)", "Положение X (левый край)"), vs.Left, vs.Right - 1);
        _posY = PositionRow(t, L.T("Position Y (top edge)", "Положение Y (верхний край)"), vs.Top, vs.Bottom - 1);
        Hint(t, L.T("The dock moves right away while you drag the slider; it never leaves the screen. Save keeps the place, Cancel puts it back.",
                    "Капсула двигается сразу, пока ведёшь ползунок, и не выходит за экран. «Сохранить» оставит её там, «Отмена» вернёт на место."));
        var resetPos = MakeButton(L.T("Move the dock back to its default place", "Вернуть капсулу на место по умолчанию"), () =>
        {
            _resetPos.Add(_cur);
            _posEdit.Remove(_cur);
            var orig = _origOf.TryGetValue(_cur, out var o) ? o : null;
            var at   = orig != null ? _host.PreviewDefaultPosition(orig) : null;
            if (at is Point p) { _posPreviewed.Add(orig); ShowPosition(p); }
            _status.Text = L.T("The dock is at its default place — press “Save” to keep it there.",
                               "Капсула на месте по умолчанию — нажмите «Сохранить», чтобы оставить её там.");
        });
        AddFull(t, resetPos);

        Section(t, L.T("Auto-hide", "Автоскрытие"));
        Check(t, L.T("Hide the dock behind the nearest screen edge", "Прятать капсулу за ближайший край экрана"),
              s => s.AutoHide, (s, v) => s.AutoHide = v);
        Hint(t, L.T("A vertical dock hides behind the nearest left or right edge, a horizontal one behind the top or bottom edge. " +
                    "Move the cursor into the strip between the dock's place and that edge — it slides back; leave the strip — it hides again. " +
                    "Applies right after saving.",
                    "Вертикальная капсула прячется за ближайший левый или правый край, горизонтальная — за верхний или нижний. " +
                    "Подведите курсор в полосу между местом капсулы и этим краем — она выедет; уведите — снова спрячется. " +
                    "Применяется сразу после сохранения."));
        Num(t, L.T("Slide-in time", "Время появления"), 0, 3000, ms, s => s.AutoHideShowMs, (s, v) => s.AutoHideShowMs = (int)v);
        Num(t, L.T("Slide-out time", "Время скрытия"), 0, 3000, ms, s => s.AutoHideHideMs, (s, v) => s.AutoHideHideMs = (int)v);
        Num(t, L.T("Hide after the cursor leaves", "Спрятать после ухода курсора через"), 0, 30000, ms, s => s.AutoHideDelayMs, (s, v) => s.AutoHideDelayMs = (int)v);
        Num(t, L.T("Show after the cursor stays at the edge", "Показать, если курсор у края дольше"), 0, 5000, ms, s => s.AutoHideShowDelayMs, (s, v) => s.AutoHideShowDelayMs = (int)v);
        Num(t, L.T("Visible part when hidden", "Видимая часть, когда спрятана"), 0, 100, px, s => s.AutoHidePeek, (s, v) => s.AutoHidePeek = (int)v,
            hint: L.T("0 — hidden completely. A few pixels make it easy to see where the dock is.",
                      "0 — спрятана целиком. Несколько пикселей помогают видеть, где капсула."));

        // ---------- Фон ----------
        t = Page(L.T("Background", "Фон"));
        Section(t, L.T("Mode", "Режим"));
        Choice(t, L.T("Dock background", "Фон капсулы"),
               new[] { L.T("Blur", "Размытие"), L.T("Acrylic (like Windows 10)", "Акрил (как в Windows 10)") },
               s => s.GetMode() == BackdropMode.Acrylic ? 1 : 0,
               (s, i) => s.BackgroundMode = (i == 1 ? BackdropMode.Acrylic : BackdropMode.Blur).ToString());
        Check(t, L.T("Live background from the screen (live wallpapers and icons under the dock)",
                     "Живой фон с экрана (живые обои и ярлыки под капсулой)"),
              s => s.LiveBackground, (s, v) => s.LiveBackground = v);

        Section(t, L.T("Blur", "Размытие"));
        Slider(t, L.T("Blur strength", "Сила размытия"), 0, 50, "", s => s.BlurRadius, (s, v) => s.BlurRadius = v);
        Slider(t, L.T("Softness (passes)", "Мягкость (проходы)"), 1, 6, "", s => s.BlurPasses, (s, v) => s.BlurPasses = v);
        ColorRow(t, L.T("Tint color", "Цвет тонировки"), s => s.BlurTintColor, (s, v) => s.BlurTintColor = v);
        Slider(t, L.T("Tint", "Тонировка"), 0, 100, "%", s => s.BlurTintOpacity, (s, v) => s.BlurTintOpacity = v);

        Section(t, L.T("Acrylic", "Акрил"));
        Slider(t, L.T("Blur strength", "Сила размытия"), 0, 50, "", s => s.AcrylicBlurRadius, (s, v) => s.AcrylicBlurRadius = v);
        Slider(t, L.T("Softness (passes)", "Мягкость (проходы)"), 1, 6, "", s => s.AcrylicBlurPasses, (s, v) => s.AcrylicBlurPasses = v);
        Slider(t, L.T("Saturation", "Насыщенность"), 0, 300, "%", s => s.AcrylicSaturation, (s, v) => s.AcrylicSaturation = v);
        Slider(t, L.T("Milkiness", "Молочность"), 0, 50, "%", s => s.AcrylicMilkiness, (s, v) => s.AcrylicMilkiness = v);
        ColorRow(t, L.T("Tint color", "Цвет тонировки"), s => s.AcrylicTintColor, (s, v) => s.AcrylicTintColor = v);
        Slider(t, L.T("Tint", "Тонировка"), 0, 100, "%", s => s.AcrylicTintOpacity, (s, v) => s.AcrylicTintOpacity = v);
        Slider(t, L.T("Grain", "Зерно"), 0, 40, "", s => s.AcrylicNoise, (s, v) => s.AcrylicNoise = v);

        Section(t, L.T("Quality and speed", "Качество и скорость"));
        Num(t, L.T("Downscale before blurring", "Уменьшение перед размытием"), 1, 16, L.T("×", "раз"), s => s.DownScale, (s, v) => s.DownScale = (int)v,
            hint: L.T("Higher — stronger and faster blur, but coarser. Blur strength is measured in the downscaled picture.",
                      "Больше — размытие сильнее и быстрее, но грубее. Сила размытия считается в уменьшенной картинке."));
        Num(t, L.T("Margin around the dock", "Поле вокруг капсулы"), 0, 400, px, s => s.CaptureMargin, (s, v) => s.CaptureMargin = (int)v,
            hint: L.T("The background is taken a bit wider than the dock so the blur edges look natural.",
                      "Фон берётся чуть шире капсулы, чтобы края размытия были естественными."));
        Num(t, L.T("Live background from the screen", "Живой фон с экрана"), 1, 120, fps, s => s.LiveFps, (s, v) => s.LiveFps = (int)v);
        Num(t, L.T("Live wallpaper without screen capture", "Живые обои без захвата экрана"), 1, 60, fps, s => s.LiveWallpaperFps, (s, v) => s.LiveWallpaperFps = (int)v);
        Num(t, L.T("Static background check", "Проверка статичного фона"), 16, 5000, ms, s => s.StaticRefreshMs, (s, v) => s.StaticRefreshMs = (int)v);

        // ---------- Анимация ----------
        t = Page(L.T("Animation", "Анимация"));
        Section(t, L.T("Rearranging shortcuts", "Перестановка ярлыков"));
        Num(t, L.T("Long press to drag", "Долгое нажатие для перетаскивания"), 100, 3000, ms, s => s.LongPressMs, (s, v) => s.LongPressMs = (int)v);
        Num(t, L.T("Click move tolerance", "Допуск сдвига мыши при клике"), 1, 100, px, s => s.PressMoveTolerance, (s, v) => s.PressMoveTolerance = (int)v);
        Num(t, L.T("Rearrange speed", "Скорость перестановки"), 1, 100, "", s => (decimal)s.ReorderSpeed, (s, v) => s.ReorderSpeed = (double)v, decimals: 1);
        Num(t, L.T("Lifted icon enlargement", "Увеличение поднятого ярлыка"), 0, 100, "%", s => s.LiftScale, (s, v) => s.LiftScale = (int)v);
        Num(t, L.T("Lifted icon shadow", "Тень поднятого ярлыка"), 0, 100, "%", s => s.ShadowOpacity, (s, v) => s.ShadowOpacity = (int)v);
        Num(t, L.T("Auto-scroll zone at the ends", "Зона автопрокрутки у краёв"), 0, 500, px, s => s.AutoScrollZone, (s, v) => s.AutoScrollZone = (int)v);
        Num(t, L.T("Auto-scroll speed", "Скорость автопрокрутки"), 0, 5000, L.T("px/s", "px/с"), s => s.AutoScrollSpeed, (s, v) => s.AutoScrollSpeed = (int)v);

        Section(t, L.T("Rendering", "Отрисовка"));
        Num(t, L.T("Frame interval", "Интервал между кадрами"), 1, 100, ms, s => s.RenderIntervalMs, (s, v) => s.RenderIntervalMs = (int)v,
            hint: L.T("Animation runs in step with the monitor refresh rate; this is the shortest allowed time between frames. " +
                      "Higher — less CPU load, but less smooth animation. When nothing moves, the dock doesn't redraw at all.",
                      "Анимация идёт в такт с частотой обновления монитора; это самый короткий допустимый промежуток между кадрами. " +
                      "Больше — меньше нагрузка на процессор, но анимация менее плавная. Когда ничего не движется, капсула не перерисовывается вовсе."));
    }

    // ------------------------------------------------------------------------
    private void LoadFrom(DockSettings s)
    {
        foreach (var b in _binds) b.Load(s);
    }

    private bool SaveSettings()
    {
        StoreCurrent();
        foreach (var e in _edits)
        {
            if (Directory.Exists(e.ShortcutsFolder)) continue;
            _switching = true; _profileBox.SelectedItem = e; _switching = false;
            _cur = e;
            LoadCurrent();
            MessageBox.Show(this, L.T("Shortcuts folder not found:\n", "Папка с ярлыками не найдена:\n") + e.ShortcutsFolder,
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // Копии → в настоящие профили. Порядок, скрытые и запомненные ярлыки, позицию
        // капсула меняет сама во время работы — их не трогаем (CopyEditableFrom).
        var profiles = new List<DockSettings>();
        foreach (var e in _edits)
        {
            var orig = _origOf[e];
            bool orientationChanged = false;
            if (orig == null)                                  // новый профиль
            {
                orig = e;
                _origOf[e] = e;
            }
            else
            {
                bool wasHorizontal = orig.IsHorizontal;
                orig.CopyEditableFrom(e);
                orientationChanged = orig.IsHorizontal != wasHorizontal;
            }

            // Координаты, заданные ползунками, — сохраняем; сброс или смена ориентации (если координаты
            // не задавали) — место по умолчанию: старое подходило для другой ориентации
            bool hasPos = _posEdit.TryGetValue(e, out var pe);
            if (_resetPos.Contains(e) || (orientationChanged && !hasPos))
            {
                orig.PositionX = null;
                orig.PositionY = null;
            }
            else if (hasPos)
            {
                orig.PositionX = pe.X;
                orig.PositionY = pe.Y;
            }
            profiles.Add(orig);
        }
        _resetPos.Clear();

        _app.Profiles = profiles;
        _app.Language = _language;
        _app.Normalize();
        _app.Save();

        if (_autoStart != AutoStart.IsEnabled() && !AutoStart.Set(_autoStart))
            MessageBox.Show(this, L.T("Windows did not allow changing Start with Windows.", "Windows не дала изменить автозапуск."),
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);

        _saved = true;
        _onSaved?.Invoke();
        return true;
    }

    /// <summary>Значения по умолчанию для текущего профиля (сохраняются только по «Сохранить»).</summary>
    private void ResetToDefaults()
    {
        _switching = true;
        LoadFrom(new DockSettings());
        _enabledBox.Checked = true;
        _switching = false;
        _resetPos.Add(_cur);
        _posEdit.Remove(_cur);
        _status.Text = L.T("Default values are filled in for this profile — press “Save”. Shortcut order, hidden and remembered files are kept.",
                           "Для этого профиля подставлены значения по умолчанию — нажмите «Сохранить». Порядок, скрытые и запомненные ярлыки не меняются.");
    }

    private void OpenSettingsFile()
    {
        try
        {
            string file = AppSettings.FilePath;
            if (!File.Exists(file)) _app.Save();
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + file + "\"") { UseShellExecute = true })?.Dispose();
        }
        catch { }
    }

    // ------------------------------------------------------------------------
    //  Построение страниц
    // ------------------------------------------------------------------------
    private TableLayoutPanel Page(string title)
    {
        var page   = new TabPage(title) { Padding = new Padding(6), UseVisualStyleBackColor = true };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var table  = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Padding = new Padding(4, 0, 12, 12)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(table);
        page.Controls.Add(scroll);
        _tabs.TabPages.Add(page);
        return table;
    }

    private static void Section(TableLayoutPanel t, string text)
    {
        var l = new Label
        {
            Text = text, AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f, FontStyle.Bold),
            Margin = new Padding(0, t.Controls.Count == 0 ? 6 : 16, 3, 4)
        };
        AddFull(t, l);
    }

    private static void Hint(TableLayoutPanel t, string text)
    {
        var l = new Label
        {
            Text = text, AutoSize = true, MaximumSize = new Size(700, 0),
            ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 6)
        };
        AddFull(t, l);
    }

    private static void AddFull(TableLayoutPanel t, Control c)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(c, 0, row);
        t.SetColumnSpan(c, 2);
    }

    private static void Row(TableLayoutPanel t, string label, params Control[] controls)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label
        {
            Text = label, AutoSize = true, MaximumSize = new Size(225, 0),
            Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3)
        }, 0, row);

        var flow = new FlowLayoutPanel
        {
            AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left
        };
        flow.Controls.AddRange(controls);
        t.Controls.Add(flow, 1, row);
    }

    private static Label Unit(string text) =>
        new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(2, 6, 3, 3) };

    private static Button MakeButton(string text, Action click)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                             Padding = new Padding(6, 2, 6, 2), Margin = new Padding(3) };
        b.Click += (_, __) => click();
        return b;
    }

    private void Num(TableLayoutPanel t, string label, decimal min, decimal max, string unit,
                     Func<DockSettings, decimal> get, Action<DockSettings, decimal> set,
                     int decimals = 0, string hint = null)
    {
        var n = new NumericUpDown
        {
            Minimum = min, Maximum = max, DecimalPlaces = decimals, Width = 90,
            Increment = decimals > 0 ? 0.5m : 1, TextAlign = HorizontalAlignment.Right
        };
        Row(t, label, n, Unit(unit));
        if (hint != null) Hint(t, hint);
        _binds.Add((s => n.Value = Math.Clamp(get(s), min, max), s => set(s, n.Value)));
    }

    /// <summary>Ползунок + поле с числом (для «силы» и процентов).</summary>
    private void Slider(TableLayoutPanel t, string label, int min, int max, string unit,
                        Func<DockSettings, int> get, Action<DockSettings, int> set)
    {
        var bar = new TrackBar
        {
            Minimum = min, Maximum = max, TickStyle = TickStyle.None, AutoSize = false,
            Width = 230, Height = 30, LargeChange = Math.Max(1, (max - min) / 10), SmallChange = 1
        };
        var n = new NumericUpDown { Minimum = min, Maximum = max, Width = 64, TextAlign = HorizontalAlignment.Right };
        bool sync = false;
        bar.ValueChanged += (_, __) => { if (sync) return; sync = true; n.Value = bar.Value; sync = false; };
        n.ValueChanged   += (_, __) => { if (sync) return; sync = true; bar.Value = (int)n.Value; sync = false; };
        Row(t, label, bar, n, Unit(unit));
        _binds.Add((s => n.Value = Math.Clamp(get(s), min, max), s => set(s, (int)n.Value)));
    }

    private void Check(TableLayoutPanel t, string text, Func<DockSettings, bool> get, Action<DockSettings, bool> set)
    {
        var c = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        AddFull(t, c);
        _binds.Add((s => c.Checked = get(s), s => set(s, c.Checked)));
    }

    private void Choice(TableLayoutPanel t, string label, string[] items,
                        Func<DockSettings, int> get, Action<DockSettings, int> set)
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        cb.Items.AddRange(items);
        Row(t, label, cb);
        _binds.Add((s => cb.SelectedIndex = Math.Clamp(get(s), 0, items.Length - 1),
                    s => set(s, Math.Max(0, cb.SelectedIndex))));
    }

    private void ColorRow(TableLayoutPanel t, string label, Func<DockSettings, string> get, Action<DockSettings, string> set)
    {
        uint value = 0;
        var swatch = new Button { Width = 64, Height = 26, FlatStyle = FlatStyle.Flat, Margin = new Padding(3) };
        var text   = Unit("");
        void SetColor(uint v)
        {
            value            = v & 0xFFFFFF;
            swatch.BackColor = Color.FromArgb((int)(0xFF000000u | value));
            text.Text        = DockSettings.FormatColor(value);
        }
        swatch.Click += (_, __) =>
        {
            using var dlg = new ColorPickerForm(swatch.BackColor) { Icon = Icon };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                SetColor((uint)(dlg.Color.R << 16 | dlg.Color.G << 8 | dlg.Color.B));
        };
        Row(t, label, swatch, text);
        _binds.Add((s => SetColor(DockSettings.ParseColor(get(s), 0)), s => set(s, DockSettings.FormatColor(value))));
    }

    private void FolderRow(TableLayoutPanel t, string label, Func<DockSettings, string> get, Action<DockSettings, string> set)
    {
        var box = new TextBox { Width = 230, Margin = new Padding(3, 5, 3, 3) };
        var browse = MakeButton(L.T("Browse…", "Обзор…"), () =>
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = L.T("Shortcuts folder for the dock", "Папка с ярлыками для капсулы"), UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(box.Text) ? box.Text : DockSettings.DefaultFolder
            };
            if (dlg.ShowDialog(this) == DialogResult.OK) box.Text = dlg.SelectedPath;
        });
        var desktop = MakeButton(L.T("Desktop", "Рабочий стол"), () => box.Text = DockSettings.DefaultFolder);
        Row(t, label, box, browse, desktop);
        _binds.Add((s => box.Text = get(s), s => set(s, string.IsNullOrWhiteSpace(box.Text) ? DockSettings.DefaultFolder : box.Text.Trim())));
    }
}

// ============================================================================
//  Выбор цвета: круг (оттенок × насыщенность) + яркость, и поля HEX / RGB / HSV / HSL / CMYK.
//  Внутри цвет хранится как HSV (double), чтобы оттенок не терялся у серых цветов.
// ============================================================================
internal sealed class ColorPickerForm : Form
{
    public Color Color => FromHsv(_h, _s, _v);

    private double _h, _s, _v;                    // 0..360, 0..1, 0..1
    private readonly Color _original;
    private bool _updating;

    private readonly ColorWheel   _wheel;
    private readonly ValueBar     _bar;
    private readonly Panel        _newSwatch, _oldSwatch;
    private readonly TextBox      _hex;
    private readonly NumericUpDown _r, _g, _b, _hh, _hs, _hv, _lh, _ls, _ll, _c, _m, _y, _k;

    public ColorPickerForm(Color initial)
    {
        _original = initial;
        (_h, _s, _v) = ToHsv(initial);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode   = AutoScaleMode.Dpi;
        Text            = L.T("Choose a color", "Выбор цвета");
        Font            = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = MinimizeBox = false;
        ShowInTaskbar   = false;
        StartPosition   = FormStartPosition.CenterParent;
        AutoSize        = true;
        AutoSizeMode    = AutoSizeMode.GrowAndShrink;
        Padding         = new Padding(10);

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 2 };

        _wheel = new ColorWheel { Size = new Size(260, 260), Margin = new Padding(3) };
        _wheel.Picked += (h, s) => { _h = h; _s = s; if (_v < 0.05) _v = 1; Sync(null); };
        _bar = new ValueBar { Size = new Size(28, 260), Margin = new Padding(8, 3, 12, 3) };
        _bar.Picked += v => { _v = v; Sync(null); };
        root.Controls.Add(_wheel, 0, 0);
        root.Controls.Add(_bar, 1, 0);

        // ---- Поля ----
        var fields = new TableLayoutPanel { AutoSize = true, ColumnCount = 5 };

        _newSwatch = new Panel { Size = new Size(70, 34), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 3, 0, 3) };
        _oldSwatch = new Panel { Size = new Size(70, 34), BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 3, 3, 3),
                                 BackColor = initial, Cursor = Cursors.Hand };
        _oldSwatch.Click += (_, __) => { (_h, _s, _v) = ToHsv(_original); Sync(null); };
        var swatches = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
        swatches.Controls.Add(_newSwatch);
        swatches.Controls.Add(_oldSwatch);
        AddRow(fields, L.T("New / old", "Новый / был"), swatches);

        _hex = new TextBox { Width = 100, CharacterCasing = CharacterCasing.Upper, MaxLength = 7 };
        _hex.TextChanged += (_, __) =>
        {
            if (_updating) return;
            string t = _hex.Text.Trim().TrimStart('#');
            if (t.Length == 3) t = string.Concat(t.Select(ch => new string(ch, 2)));    // #ABC → #AABBCC
            if (t.Length == 6 && int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            {
                (_h, _s, _v) = ToHsv(Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255));
                Sync(_hex);
            }
        };
        AddRow(fields, "HEX", _hex);

        _r = Num(255); _g = Num(255); _b = Num(255);
        AddRow(fields, "RGB", _r, _g, _b);
        _hh = Num(360); _hs = Num(100); _hv = Num(100);
        AddRow(fields, "HSV", _hh, _hs, _hv);
        _lh = Num(360); _ls = Num(100); _ll = Num(100);
        AddRow(fields, "HSL", _lh, _ls, _ll);
        _c = Num(100); _m = Num(100); _y = Num(100); _k = Num(100);
        AddRow(fields, "CMYK %", _c, _m, _y, _k);

        foreach (var n in new[] { _r, _g, _b }) n.ValueChanged += (_, __) =>
        {
            if (_updating) return;
            (_h, _s, _v) = ToHsv(Color.FromArgb((int)_r.Value, (int)_g.Value, (int)_b.Value));
            Sync(n);
        };
        foreach (var n in new[] { _hh, _hs, _hv }) n.ValueChanged += (_, __) =>
        {
            if (_updating) return;
            _h = (double)_hh.Value % 360; _s = (double)_hs.Value / 100; _v = (double)_hv.Value / 100;
            Sync(n);
        };
        foreach (var n in new[] { _lh, _ls, _ll }) n.ValueChanged += (_, __) =>
        {
            if (_updating) return;
            (_h, _s, _v) = HslToHsv((double)_lh.Value % 360, (double)_ls.Value / 100, (double)_ll.Value / 100);
            Sync(n);
        };
        foreach (var n in new[] { _c, _m, _y, _k }) n.ValueChanged += (_, __) =>
        {
            if (_updating) return;
            double k = (double)_k.Value / 100;
            int R = (int)Math.Round(255 * (1 - (double)_c.Value / 100) * (1 - k));
            int G = (int)Math.Round(255 * (1 - (double)_m.Value / 100) * (1 - k));
            int B = (int)Math.Round(255 * (1 - (double)_y.Value / 100) * (1 - k));
            (_h, _s, _v) = ToHsv(Color.FromArgb(R, G, B));
            Sync(n);
        };
        root.Controls.Add(fields, 2, 0);

        // ---- Кнопки ----
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        var cancel  = new Button { Text = L.T("Cancel", "Отмена"), AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(6, 2, 6, 2) };
        var ok      = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK, Padding = new Padding(14, 2, 14, 2) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        root.Controls.Add(buttons, 0, 1);
        root.SetColumnSpan(buttons, 3);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(root);
        Sync(null);
        ResumeLayout(true);
    }

    private static NumericUpDown Num(int max) =>
        new NumericUpDown { Minimum = 0, Maximum = max, Width = 58, TextAlign = HorizontalAlignment.Right, Margin = new Padding(3, 3, 0, 3) };

    private static void AddRow(TableLayoutPanel t, string label, params Control[] controls)
    {
        int row = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) }, 0, row);
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        flow.Controls.AddRange(controls);
        t.Controls.Add(flow, 1, row);
        t.SetColumnSpan(flow, 4);
    }

    /// <summary>Обновить всё по _h/_s/_v, кроме поля, которое сейчас правят (source).</summary>
    private void Sync(Control source)
    {
        _updating = true;
        var c = Color;
        _wheel.SetValue(_h, _s, _v);
        _bar.SetValue(_h, _s, _v);
        _newSwatch.BackColor = c;

        if (source != _hex) _hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        if (source != _r && source != _g && source != _b) { _r.Value = c.R; _g.Value = c.G; _b.Value = c.B; }
        if (source != _hh && source != _hs && source != _hv)
        {
            _hh.Value = (decimal)Math.Round(_h) % 360; _hs.Value = (decimal)Math.Round(_s * 100); _hv.Value = (decimal)Math.Round(_v * 100);
        }
        if (source != _lh && source != _ls && source != _ll)
        {
            var (lh, ls, ll) = HsvToHsl(_h, _s, _v);
            _lh.Value = (decimal)Math.Round(lh) % 360; _ls.Value = (decimal)Math.Round(ls * 100); _ll.Value = (decimal)Math.Round(ll * 100);
        }
        if (source != _c && source != _m && source != _y && source != _k)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double k = 1 - Math.Max(r, Math.Max(g, b));
            double d = 1 - k;
            _k.Value = (decimal)Math.Round(k * 100);
            _c.Value = d <= 0 ? 0 : (decimal)Math.Round((1 - r - k) / d * 100);
            _m.Value = d <= 0 ? 0 : (decimal)Math.Round((1 - g - k) / d * 100);
            _y.Value = d <= 0 ? 0 : (decimal)Math.Round((1 - b - k) / d * 100);
        }
        _updating = false;
    }

    // ------------------------------------------------------------------------
    //  Перевод между моделями
    // ------------------------------------------------------------------------
    public static Color FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        double r, g, b;
        if      (h < 60)  (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else              (r, g, b) = (c, 0, x);
        return Color.FromArgb((int)Math.Round((r + m) * 255), (int)Math.Round((g + m) * 255), (int)Math.Round((b + m) * 255));
    }

    public static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if      (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else               h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 0 ? 0 : d / max, max);
    }

    private static (double H, double S, double L) HsvToHsl(double h, double s, double v)
    {
        double l = v * (1 - s / 2);
        double sl = l <= 0 || l >= 1 ? 0 : (v - l) / Math.Min(l, 1 - l);
        return (h, sl, l);
    }

    private static (double H, double S, double V) HslToHsv(double h, double s, double l)
    {
        double v = l + s * Math.Min(l, 1 - l);
        return (h, v <= 0 ? 0 : 2 * (1 - l / v), v);
    }

    // ------------------------------------------------------------------------
    //  Круг: угол — оттенок, расстояние от центра — насыщенность (при текущей яркости)
    // ------------------------------------------------------------------------
    private sealed class ColorWheel : Control
    {
        public event Action<double, double> Picked;
        private double _h, _s, _v = -1;
        private Bitmap _img;

        public ColorWheel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Cross;
        }

        public void SetValue(double h, double s, double v)
        {
            if (Math.Abs(v - _v) > 0.001) { _img?.Dispose(); _img = null; }   // круг перерисовать с новой яркостью
            _h = h; _s = s; _v = v;
            Invalidate();
        }

        private unsafe Bitmap Render()
        {
            int w = Width, h = Height;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0, r = Math.Min(w, h) / 2.0 - 1;
                for (int y = 0; y < h; y++)
                {
                    uint* row = (uint*)((byte*)d.Scan0 + y * d.Stride);
                    for (int x = 0; x < w; x++)
                    {
                        double dx = x - cx, dy = cy - y, dist = Math.Sqrt(dx * dx + dy * dy);
                        double a = Math.Clamp(r - dist + 0.5, 0, 1);        // сглаженный край
                        if (a <= 0) { row[x] = 0; continue; }
                        double hue = Math.Atan2(dy, dx) * 180 / Math.PI;
                        var c = FromHsv(hue < 0 ? hue + 360 : hue, Math.Min(1, dist / r), _v);
                        row[x] = (uint)(a * 255) << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
                    }
                }
            }
            finally { bmp.UnlockBits(d); }
            return bmp;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
            _img ??= Render();
            e.Graphics.DrawImageUnscaled(_img, 0, 0);

            double r  = Math.Min(Width, Height) / 2.0 - 1, a = _h * Math.PI / 180;
            float  mx = (float)((Width - 1) / 2.0 + Math.Cos(a) * _s * r);
            float  my = (float)((Height - 1) / 2.0 - Math.Sin(a) * _s * r);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p1 = new Pen(Color.Black, 3f)) e.Graphics.DrawEllipse(p1, mx - 6, my - 6, 12, 12);
            using (var p2 = new Pen(Color.White, 1.5f)) e.Graphics.DrawEllipse(p2, mx - 6, my - 6, 12, 12);
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Pick(e.Location); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Pick(e.Location); }

        private void Pick(Point p)
        {
            double r  = Math.Min(Width, Height) / 2.0 - 1;
            double dx = p.X - (Width - 1) / 2.0, dy = (Height - 1) / 2.0 - p.Y;
            double hue = Math.Atan2(dy, dx) * 180 / Math.PI;
            Picked?.Invoke(hue < 0 ? hue + 360 : hue, Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / r));
        }

        protected override void Dispose(bool disposing) { if (disposing) _img?.Dispose(); base.Dispose(disposing); }
    }

    // ------------------------------------------------------------------------
    //  Полоса яркости: сверху — полная, снизу — чёрный
    // ------------------------------------------------------------------------
    private sealed class ValueBar : Control
    {
        public event Action<double> Picked;
        private double _h, _s, _v;

        public ValueBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public void SetValue(double h, double s, double v) { _h = h; _s = s; _v = v; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);
            var rc = new Rectangle(4, 0, Width - 8, Height);
            using (var br = new LinearGradientBrush(rc, FromHsv(_h, _s, 1), Color.Black, LinearGradientMode.Vertical))
                e.Graphics.FillRectangle(br, rc);
            e.Graphics.DrawRectangle(SystemPens.ControlDark, rc.X, rc.Y, rc.Width - 1, rc.Height - 1);

            int y = (int)Math.Round((1 - _v) * (Height - 1));
            using var pen = new Pen(Color.Black, 2f);
            e.Graphics.DrawRectangle(pen, 1, Math.Max(1, y - 3), Width - 3, 6);
            using var pen2 = new Pen(Color.White, 1f);
            e.Graphics.DrawRectangle(pen2, 2, Math.Max(2, y - 2), Width - 5, 4);
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Pick(e.Y); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Pick(e.Y); }
        private void Pick(int y) => Picked?.Invoke(Math.Clamp(1 - y / (double)(Height - 1), 0, 1));
    }
}

// ============================================================================
//  Приложение: один трей и одно окно настроек на все капсулы (профили).
//  Каждый включённый профиль — своя капсула (DockForm) со своими настройками.
// ============================================================================
internal sealed class CapDockApp : ApplicationContext
{
    private readonly AppSettings                    _app;
    private readonly Dictionary<string, DockForm>   _docks = new(StringComparer.OrdinalIgnoreCase);
    private readonly NotifyIcon                     _tray;
    private readonly Icon                           _trayIcon;
    private readonly Icon                           _formIcon;
    private SettingsForm                            _settingsForm;
    private MenuHostForm                            _menuHost;
    private bool                                    _exiting;

    public CapDockApp()
    {
        _app = AppSettings.Load();
        L.Ru = _app.Language == "ru";
        AutoStart.MigrateOldName();

        _trayIcon = LoadAppIcon(SystemInformation.SmallIconSize);
        _formIcon = LoadAppIcon(new Size(32, 32));

        var menu = new ContextMenuStrip();
        menu.Opening += (_, __) => BuildTrayMenu(menu);
        menu.Items.Add("…");                                   // без пунктов меню не откроется — заполняется в Opening

        _tray = new NotifyIcon { Icon = _trayIcon, Text = AppSettings.AppName, Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            var mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.NonPublic | BindingFlags.Instance);
            mi?.Invoke(_tray, null);
        };

        foreach (var p in _app.Profiles)
            if (p.Enabled) OpenDock(p);
    }

    /// <summary>Иконка программы (CapDock.ico внутри exe) нужного размера — чёткая, без растягивания.</summary>
    public static Icon LoadAppIcon(Size size)
    {
        try
        {
            using var s = typeof(CapDockApp).Assembly.GetManifestResourceStream("CapDock.ico");
            if (s != null) return new Icon(s, size);
        }
        catch { }
        return (Icon)SystemIcons.Application.Clone();
    }

    // ------------------------------------------------------------------------
    //  Капсулы
    // ------------------------------------------------------------------------
    private void OpenDock(DockSettings p)
    {
        if (_docks.ContainsKey(p.Id)) return;
        var dock = new DockForm(p, this);
        _docks[p.Id] = dock;
        dock.FormClosed += (_, __) => { if (_docks.TryGetValue(p.Id, out var d) && d == dock) _docks.Remove(p.Id); };
        dock.Show();
    }

    private void CloseDock(string id)
    {
        if (_docks.TryGetValue(id, out var dock)) { _docks.Remove(id); dock.Close(); }
    }

    private void SetProfileEnabled(DockSettings p, bool enabled)
    {
        p.Enabled = enabled;
        _app.Save();
        if (enabled) OpenDock(p); else CloseDock(p.Id);
    }

    /// <summary>Окно настроек сохранило изменения: открыть/закрыть капсулы, применить то, что можно без перезапуска.</summary>
    private void OnSettingsSaved()
    {
        var alive = new HashSet<string>(_app.Profiles.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var id in _docks.Keys.ToList())
            if (!alive.Contains(id)) CloseDock(id);              // профиль удалили

        foreach (var p in _app.Profiles)
        {
            if (p.Enabled && !_docks.ContainsKey(p.Id)) OpenDock(p);
            else if (!p.Enabled) CloseDock(p.Id);
            else _docks[p.Id].ApplyRuntimeSettings();
        }
    }

    // ------------------------------------------------------------------------
    //  Трей
    // ------------------------------------------------------------------------
    private void BuildTrayMenu(ContextMenuStrip menu)
    {
        foreach (ToolStripItem it in menu.Items.Cast<ToolStripItem>().ToList()) it.Dispose();
        menu.Items.Clear();

        var settings = new ToolStripMenuItem(L.T("Settings…", "Настройки…"), null, (_, __) => ShowSettings(null));
        settings.Font = new Font(settings.Font, FontStyle.Bold);
        menu.Items.Add(settings);
        menu.Items.Add(L.T("Restart CapDock", "Перезапустить CapDock"), null, (_, __) => Restart());
        menu.Items.Add(new ToolStripSeparator());

        if (_app.Profiles.Count == 1)
            AddDockItems(menu.Items, _app.Default);              // один профиль — всё сразу в меню
        else
            foreach (var p in _app.Profiles)
            {
                var sub = new ToolStripMenuItem(p.DisplayName) { Checked = _docks.ContainsKey(p.Id) };
                AddDockItems(sub.DropDownItems, p);
                menu.Items.Add(sub);
            }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("Exit", "Выход"), null, (_, __) => Exit());
    }

    private void AddDockItems(ToolStripItemCollection items, DockSettings p)
    {
        bool open = _docks.TryGetValue(p.Id, out var dock);
        items.Add(new ToolStripMenuItem(L.T("Show this dock", "Показывать эту капсулу"), null,
                                        (_, __) => SetProfileEnabled(p, !open)) { Checked = open });
        if (!open) return;

        items.Add(new ToolStripSeparator());
        items.Add(new ToolStripMenuItem(L.T("Background: blur", "Фон: размытие"), null,
                  (_, __) => dock.SetBackdropMode(BackdropMode.Blur)) { Checked = dock.Mode == BackdropMode.Blur });
        items.Add(new ToolStripMenuItem(L.T("Background: acrylic (like Windows 10)", "Фон: акрил (как в Windows 10)"), null,
                  (_, __) => dock.SetBackdropMode(BackdropMode.Acrylic)) { Checked = dock.Mode == BackdropMode.Acrylic });
        string problem = dock.LiveProblem;
        items.Add(new ToolStripMenuItem(problem == null
                      ? L.T("Live background from the screen (live wallpapers)", "Живой фон с экрана (живые обои)")
                      : L.T("Live background from the screen — unavailable: ", "Живой фон с экрана — недоступен: ") + problem,
                  null, (_, __) => dock.ToggleLiveBackground()) { Checked = p.LiveBackground });
        items.Add(new ToolStripSeparator());
        items.Add(L.T("Reload shortcuts", "Обновить ярлыки"), null, (_, __) => dock.ReloadAll());
        items.Add(L.T("Rebuild blur", "Пересобрать размытие"), null, (_, __) => dock.RebuildBlur());
        items.Add(L.T("Reset shortcut order", "Сбросить порядок ярлыков"), null, (_, __) => dock.ResetOrder());
        items.Add(L.T("Restart render thread", "Перезапустить поток рендера"), null, (_, __) => dock.RestartRenderThread());
    }

    public void ShowTrayMessage(string text)
    {
        try { _tray.ShowBalloonTip(6000, AppSettings.AppName, text, ToolTipIcon.Warning); } catch { }
    }

    /// <summary>
    /// Меню у курсора, которое закрывается кликом куда угодно. Капсула не активируется
    /// (WS_EX_NOACTIVATE), поэтому сначала делаем активным невидимое окно программы —
    /// как это делает сам трей; тогда клик по другому окну закрывает меню.
    /// </summary>
    public void ShowContextMenu(ContextMenuStrip menu, Point screen)
    {
        if (_menuHost == null || _menuHost.IsDisposed) _menuHost = new MenuHostForm();
        if (!_menuHost.Visible) _menuHost.Show();
        Native.SetForegroundWindow(_menuHost.Handle);
        menu.Closed += (_, __) => { try { if (_menuHost.Visible) _menuHost.Hide(); } catch { } };
        menu.Show(screen);
    }

    private sealed class MenuHostForm : Form
    {
        public MenuHostForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar   = false;
            StartPosition   = FormStartPosition.Manual;
            Bounds          = new Rectangle(-32000, -32000, 1, 1);
            Opacity         = 0;
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x00000080; return cp; }   // WS_EX_TOOLWINDOW: нет в Alt+Tab
        }
    }

    // ------------------------------------------------------------------------
    //  Настройки, перезапуск, выход
    // ------------------------------------------------------------------------
    public void ShowSettings(DockSettings select)
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized) _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_app, this, _formIcon, select, OnSettingsSaved, Restart);
        _settingsForm.FormClosed += (_, __) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    /// <summary>Пока открыты настройки, капсулы не прячутся (автоскрытие) — видно, что меняешь.</summary>
    public bool SettingsOpen => _settingsForm != null;

    // ---- Координаты капсулы из окна настроек (без перезапуска) ----
    public Point? GetDockPosition(DockSettings p) =>
        _docks.TryGetValue(p.Id, out var d) ? d.HomePosition
        : p.PositionX is int x && p.PositionY is int y ? new Point(x, y) : (Point?)null;

    public Point? PreviewDockPosition(DockSettings p, Point pt) =>
        _docks.TryGetValue(p.Id, out var d) ? d.PreviewPosition(pt.X, pt.Y) : (Point?)null;

    public Point? PreviewDefaultPosition(DockSettings p) =>
        _docks.TryGetValue(p.Id, out var d) ? d.PreviewDefaultPosition() : (Point?)null;

    /// <summary>keep — настройки сохранили (оставить на новом месте), иначе вернуть на сохранённое.</summary>
    public void EndPositionPreview(DockSettings p, bool keep)
    {
        if (!_docks.TryGetValue(p.Id, out var d)) return;
        if (keep) d.FinishPreview(); else d.RevertPosition();
    }

    public void Restart()
    {
        Program.RestartRequested = true;
        Exit();
    }

    private void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        _tray.Visible = false;
        _settingsForm?.Close();
        foreach (var id in _docks.Keys.ToList()) CloseDock(id);
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _trayIcon.Dispose();
        _formIcon.Dispose();
        _menuHost?.Dispose();
        base.ExitThreadCore();
    }
}

// ============================================================================
//  Основная форма: одна капсула (один профиль)
// ============================================================================
internal sealed class DockForm : Form
{
    // ------- НАСТРОЙКИ -------
    // Читаются из settings.json один раз при запуске (LoadConfig) — поэтому после изменения
    // в окне «Настройки» нужен перезапуск.
    private string ShortcutsFolder;
    private bool   Horizontal;                   // капсула лежит горизонтально (ярлыки идут слева направо)
    private int    Thick, Len;                   // толщина (поперёк) и длина (вдоль) капсулы
    private int    BarW, BarH;                   // размер окна: Thick×Len или Len×Thick
    private bool   InvertScroll;
    private int    IconSize, IconSpacing, Pad;
    private int    ScrollStep;
    private double AnimSpeed;
    private int    DownScale;
    private int    BlurRadius, BlurPasses;
    private int    BlurRefreshMs;
    private int    RenderIntervalMs;
    private uint   DockBgColor;                  // тонировка в режиме «Размытие», 0xAARRGGBB
    const  int    DefaultEdgeMargin = 20;       // позиция по умолчанию: отступ от края экрана

    // ------- Фон капсулы -------
    private int    CaptureMargin;                // берём фон чуть шире капсулы — края размытия естественные
    private int    LiveRefreshMs;                // живой фон с экрана
    private int    LiveWallpaperRefreshMs;       // живые обои через PrintWindow (запасной путь)

    // Акрил по рецепту Windows 10 (Fluent): размытие → насыщенность → Exclusion белым → тонировка → шум
    private int    AcrylicBlurRadius;            // радиус в уменьшенной картинке
    private int    AcrylicBlurPasses;            // 3 прохода box-blur ≈ гауссово размытие
    private float  AcrylicSaturation;
    private float  AcrylicExclusion;             // доля белого в режиме Exclusion — «молочность» акрила
    private uint   AcrylicTintColor;
    private float  AcrylicTintOpacity;           // больше — темнее и «матовее», меньше — ярче видны обои
    private int    AcrylicNoiseAmp;              // зерно: ± уровней яркости

    // ------- Перетаскивание ярлыков -------
    private int    LongPressMs;                  // сколько держать кнопку, чтобы «поднять» ярлык
    private int    PressMoveTolerance;           // сдвиг мыши (px) до долгого нажатия отменяет клик
    private double ReorderAnimSpeed;             // скорость, с которой ярлыки разъезжаются
    private double LiftScale;                    // во сколько крупнее поднятый ярлык (0.15 = +15%)
    private float  ShadowOpacity;
    private int    AutoScrollZone;               // у верхнего/нижнего края список прокручивается сам
    private double AutoScrollSpeed;              // px/с

    private int    DrawLen;                      // рисуемая длина капсулы (последний пиксель — край сглаживания)
    private int    Step;

    private void LoadConfig(DockSettings s)
    {
        ShortcutsFolder  = s.ShortcutsFolder;
        Horizontal       = s.IsHorizontal;
        Thick            = s.BarWidth;
        Len              = s.BarHeight;
        BarW             = Horizontal ? Len : Thick;
        BarH             = Horizontal ? Thick : Len;
        InvertScroll     = s.InvertScroll;
        IconSize         = s.IconSize;
        IconSpacing      = s.IconSpacing;
        Pad              = s.EdgePadding;
        ScrollStep       = s.ScrollStep;
        AnimSpeed        = s.ScrollSpeed;
        DownScale        = s.DownScale;
        BlurRadius       = s.BlurRadius;
        BlurPasses       = s.BlurPasses;
        BlurRefreshMs    = s.StaticRefreshMs;
        RenderIntervalMs = s.RenderIntervalMs;
        DockBgColor      = (uint)Math.Round(s.BlurTintOpacity * 255 / 100.0) << 24 |
                           DockSettings.ParseColor(s.BlurTintColor, 0x1A1A1A);

        CaptureMargin          = s.CaptureMargin;
        LiveRefreshMs          = Math.Max(1, 1000 / s.LiveFps);
        LiveWallpaperRefreshMs = Math.Max(1, 1000 / s.LiveWallpaperFps);

        AcrylicBlurRadius  = s.AcrylicBlurRadius;
        AcrylicBlurPasses  = s.AcrylicBlurPasses;
        AcrylicSaturation  = s.AcrylicSaturation / 100f;
        AcrylicExclusion   = s.AcrylicMilkiness / 100f;
        AcrylicTintColor   = DockSettings.ParseColor(s.AcrylicTintColor, 0x1F1F1F);
        AcrylicTintOpacity = s.AcrylicTintOpacity / 100f;
        AcrylicNoiseAmp    = s.AcrylicNoise;

        LongPressMs        = s.LongPressMs;
        PressMoveTolerance = s.PressMoveTolerance;
        ReorderAnimSpeed   = s.ReorderSpeed;
        LiftScale          = s.LiftScale / 100.0;
        ShadowOpacity      = s.ShadowOpacity / 100f;
        AutoScrollZone     = s.AutoScrollZone;
        AutoScrollSpeed    = s.AutoScrollSpeed;

        DrawLen = Len - 1;
        Step    = IconSize + IconSpacing;
        LoadAutoHideConfig(s);
    }

    /// <summary>Автоскрытие настраивается без перезапуска — читается и при сохранении настроек.</summary>
    private void LoadAutoHideConfig(DockSettings s)
    {
        _ahShowSec      = s.AutoHideShowMs / 1000.0;
        _ahHideSec      = s.AutoHideHideMs / 1000.0;
        _ahDelaySec     = s.AutoHideDelayMs / 1000.0;
        _ahShowDelaySec = s.AutoHideShowDelayMs / 1000.0;
        _ahPeek         = s.AutoHidePeek;
        _hideKeyX       = int.MinValue;              // пересчитать, куда уезжать
        _autoHide       = s.AutoHide;
    }

    // ------- Оси капсулы: «вдоль» (по ней идут ярлыки и прокрутка) и «поперёк» -------
    /// <summary>Координата точки экрана вдоль капсулы, от её начала.</summary>
    private int MainLocal(Point screen) => Horizontal ? screen.X - WinX : screen.Y - WinY;

    /// <summary>Вдоль/поперёк → x/y в окне капсулы.</summary>
    private PointF ToWindow(double main, double cross) =>
        Horizontal ? new PointF((float)main, (float)cross) : new PointF((float)cross, (float)main);

    /// <summary>Сколько можно прокрутить, если в капсуле count ярлыков.</summary>
    private double MaxScrollFor(int count) =>
        Math.Max(0, count * Step - IconSpacing + Pad * 2 - DrawLen);

    // ------- Состояние (общее для UI и рендер-потока — под _sync) -------
    private readonly object         _sync  = new object();
    private readonly List<DockItem> _files = new();
    private double _scrollPos     = 0.0;
    private double _targetScroll  = 0.0;
    private double _maxScroll     = 0.0;
    private int    _renderRequest = 1;
    private int    _wheelDelta;                     // UI → рендер-поток без блокировки (Interlocked)

    // Рендер-поток спит, пока нечего анимировать; будят его RequestRender (колесо, новый фон и т.п.)
    private readonly AutoResetEvent _renderWake = new AutoResetEvent(false);
    private const int    IdlePollMs      = 16;      // в простое: как часто проверять курсор (подсказка)
    // Раньше время кадра бралось из Environment.TickCount64 (шаг 15–16 мс) и почти на каждом 4-мс кадре
    // подменялось на 16 мс — прокрутка шла примерно вчетверо быстрее номинала, но зависела от частоты
    // кадров. Сохраняем ту же скорость, но по точным часам — одинаково при любой частоте кадров.
    private const double ScrollRateScale = 4.0;

    // ------- Графика (рендер-поток) -------
    // Фон капсулы — готовые пиксели 0xFFRRGGBB (BarW×BarH). Тройная буферизация без выделения памяти
    // на каждый кадр: фоновый поток пишет в «задний», рендер-поток показывает «передний».
    private uint[]          _bgFront;               // рендер-поток
    private uint[]          _bgPending;             // готов, ещё не подхвачен (Interlocked)
    private uint[]          _bgSpare;               // свободный (Interlocked)
    private byte[]          _capsuleMaskAlpha;
    private int[]           _maskEdgeIdx;           // пиксели, где маска капсулы не 255 (края и углы)
    private byte[]          _maskEdgeVal;
    private Bitmap          _dibBitmap;             // GDI+ поверх пикселей окна — только для «поднятых» ярлыков
    private Graphics        _dibG;
    private ImageAttributes _shadowAttrs;

    // ------- Layered -------
    private IntPtr _dibDC      = IntPtr.Zero;
    private IntPtr _dibSection = IntPtr.Zero;
    private IntPtr _dibBits    = IntPtr.Zero;
    private IntPtr _hwnd       = IntPtr.Zero;

    private volatile IntPtr _desktopHwnd = IntPtr.Zero;

    // ------- Фон капсулы (поток DockBackdrop) -------
    private Thread                  _backdropThread;
    private volatile bool           _backdropStop;
    private readonly AutoResetEvent _backdropWake = new AutoResetEvent(false);
    private int                     _forceBg = 1;
    private volatile int            _mode;
    private WallpaperInfo           _pendingWallpaper;      // UI → фоновый поток (Interlocked)
    private WallpaperInfo           _lastWallpaperInfo;     // UI-поток
    private Rectangle               _capRect;               // что захватываем (экранные координаты)
    private int                     _dockOffX, _dockOffY;   // где капсула внутри _capRect
    private uint[]                  _wallLayer, _iconLayer, _liveLayer, _composite, _lastComposite;
    private uint                    _wallKey;               // цвет фона Windows (0xRRGGBB)
    private readonly GdiDib         _pw = new GdiDib();     // буфер для PrintWindow
    private int                     _liveFails, _liveSkip, _deskCheck;
    private volatile bool           _liveWallpaper;         // запасной путь: последний кадр — живые обои

    // ------- Живой фон: пиксели прямо с экрана за капсулой -------
    // Капсула исключена из захвата экрана (WDA_EXCLUDEFROMCAPTURE), поэтому BitBlt видит то,
    // что под ней: обои (в т.ч. Wallpaper Engine / Lively) и ярлыки. Окна, лежащие ПОВЕРХ дока,
    // маскируются — под ними оставляем последний «чистый» кадр.
    private volatile bool              _screenCapture;      // режим включён и работает
    private int                        _screenReset;        // UI → фоновый поток: начать слой заново
    private string                     _screenStatus = "";  // почему живой фон недоступен
    private uint[]                     _screenLayer;
    private byte[]                     _occMask;
    private readonly List<Rectangle>   _occRects = new List<Rectangle>();
    private readonly GdiDib            _sc = new GdiDib();  // буфер для BitBlt с экрана
    private Native.EnumWindowsProc     _occProc;
    private int                        _selfSeen;
    private sbyte[]                 _noise;

    // ------- Капсулу перетаскивают (позиция не закреплена): UI → фоновый поток -------
    private sealed class BackdropGeometry
    {
        public Rectangle Cap;
        public int       OffX, OffY;
    }
    private BackdropGeometry        _pendingGeometry;       // Interlocked
    private WallpaperInfo           _curWallpaper;          // фоновый поток: по нему перерисовываются обои

    // ------- Tooltip -------
    private TooltipForm _tooltip;
    private int _tooltipIdx  = -1;
    private int _tooltipX    = -9999;
    private int _tooltipY    = -9999;

    // ------- Рендер-поток -------
    private Thread _renderThread;
    private volatile bool _shutdown;
    private volatile int  _renderGen;

    // ------- UI-таймеры -------
    private System.Windows.Forms.Timer _hoverCheckTimer;
    private System.Windows.Forms.Timer _guardTimer;
    private System.Windows.Forms.Timer _longPressTimer;
    private System.Windows.Forms.Timer _wallpaperTimer;

    // ------- Перетаскивание -------
    private sealed class DragState
    {
        public DockItem Item;
        public double   GrabOffset;     // где внутри иконки её «взяли» (px от верха иконки)
        public double   Top;            // текущий верх иконки в координатах капсулы
        public int      StartIndex;
    }

    private DragState _drag;            // меняется только в UI-потоке (под _sync)
    private DockItem  _pressItem;       // UI-поток: ярлык, на котором нажали кнопку
    private Point     _pressPoint;
    private bool      _pressCancelled;
    private int       _noButtonFrames;  // рендер-поток
    private int       _endDragQueued;

    // ------- Файлы, которые перетаскивают в капсулу из Проводника -------
    private volatile bool _dropActive;  // над капсулой держат файлы
    private int           _dropIndex = -1;   // куда они встанут (под _sync); -1 — никуда

    // ------- Защита от скрытия / привязка к рабочему столу -------
    private const int  SW_SHOWNOACTIVATE = 4;
    private const uint SWP_NOSIZE        = 0x0001;
    private const uint SWP_NOMOVE        = 0x0002;
    private const uint SWP_NOZORDER      = 0x0004;
    private const uint SWP_NOACTIVATE    = 0x0010;
    private const uint SWP_HIDEWINDOW    = 0x0080;
    private const uint SWP_ASYNCWINDOWPOS = 0x4000;
    private const uint GW_HWNDNEXT       = 2;
    private const uint GW_HWNDPREV       = 3;
    private const int  GWL_EXSTYLE       = -20;
    private const int  WS_EX_TOPMOST     = 0x00000008;
    private const int  WS_EX_LAYERED     = 0x00080000;

    // Windows 10 не даёт исключить layered-окно из захвата экрана (код 8), но исключение,
    // выставленное обычному окну, сохраняется и после WS_EX_LAYERED. Поэтому окно создаётся
    // без WS_EX_LAYERED, а на время SetWindowDisplayAffinity стиль снимается.
    private bool _layeredOff = true;
    private const int  GuardIntervalMs   = 50;    // как часто проверяем, что окно на месте
    private const int  RepinCooldownMs   = 250;   // не чаще, чем раз в 250 мс трогаем Z-порядок

    private volatile bool _closing;
    private bool _allowZChange;                   // true только пока Z-порядок меняем мы сами
    private long _lastPinTick;

    // ------- Настройки (профиль этой капсулы) и приложение (трей, окно настроек) -------
    private readonly DockSettings _settings;
    private readonly CapDockApp   _app;

    // «Домашнее» место капсулы (левый верхний угол на экране). Меняется только в UI-потоке
    // (перетаскивание, координаты в настройках).
    private volatile int DockX;
    private volatile int DockY;

    // Где окно сейчас на самом деле: дом + сдвиг автоскрытия. От него считаются клики и подсказка.
    private volatile int  _slideX, _slideY;
    private volatile bool _slideBusy;               // капсула выезжает/уезжает — её двигает рендер-поток
    private volatile bool _fullyHidden;             // спрятана целиком — фон снимать незачем
    private int WinX => DockX + _slideX;
    private int WinY => DockY + _slideY;

    // ------- Автоскрытие (рендер-поток) -------
    private volatile bool _autoHide;
    private double _ahShowSec, _ahHideSec, _ahDelaySec, _ahShowDelaySec;
    private int    _ahPeek;
    private double _hideT;                          // 0 — на месте, 1 — спрятана
    private double _zoneEnter = -1, _zoneLeave;     // когда курсор вошёл в зону у края / вышел из неё
    private Point  _hideOff;                        // куда уезжать (сдвиг от дома); Empty — некуда
    private Rectangle _hideZone;                    // где курсор вызывает капсулу: от неё до края экрана
    private int    _hideKeyX = int.MinValue, _hideKeyY;
    private double _hideKeyTime;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>Курсор на экране (одна точка входа для прокрутки курсором и автоскрытия).</summary>
    private static Point CursorPos => Cursor.Position;

    // ------- Прокрутка перетаскиванием (нажать и вести, не дожидаясь долгого нажатия) -------
    private volatile bool _panning;
    private bool   _pressActive;                    // кнопка нажата внутри капсулы
    private double _panStartScroll;
    private int    _panStartMain;
    private double _panLastMain, _panLastT, _panVel;

    // ------- Перетаскивание всей капсулы -------
    private volatile bool _positionLocked;
    private volatile bool _moving;
    private Point         _moveStart;       // где нажали (экран)
    private Point         _moveOrigin;      // где тогда была капсула

    private bool          _liveApplied;     // с каким LiveBackground сейчас работает живой фон

    internal sealed class DockItem
    {
        public string Name;
        public string Path;
        public string Key;      // имя файла — по нему запоминается порядок
        public Bitmap Icon;
        public uint[] Px;       // пиксели иконки (premultiplied ARGB) — для быстрой отрисовки 1:1
        public Bitmap Shadow;   // тень «поднятой» иконки (создаётся при первом перетаскивании)
        public bool   External; // запомненный файл вне папки (его можно убрать из капсулы)
        public int    Cross;    // отступ иконки поперёк капсулы
        public double AnimY;    // текущее начало иконки вдоль капсулы, в координатах списка (анимируется)
        public double Lift;     // 0 — лежит на месте, 1 — поднята
    }

    // ------------------------------------------------------------------------
    public DockForm(DockSettings profile, CapDockApp app)
    {
        Native.timeBeginPeriod(1);

        _settings = profile;
        _app      = app;
        LoadConfig(_settings);
        _mode           = (int)_settings.GetMode();
        _positionLocked = _settings.PositionLocked;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        TopMost         = false;
        MinimizeBox     = false;
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.Manual;
        BackColor       = Color.Black;
        if (!_positionLocked) Cursor = Cursors.SizeAll;
        AllowDrop = true;              // файлы и ярлыки можно перетащить в капсулу

        Text = AppSettings.AppName + " — " + _settings.DisplayName;
        var start = InitialPosition(_settings);
        DockX = start.X;
        DockY = start.Y;
        SetBounds(DockX, DockY, BarW, BarH);

        _capsuleMaskAlpha = BuildCapsuleMaskAlpha();
        BuildMaskEdges();
        _noise            = BuildNoise();
        _shadowAttrs      = new ImageAttributes();

        _tooltip = new TooltipForm();

        InitDibSection();
        _dibBitmap = new Bitmap(BarW, BarH, BarW * 4, PixelFormat.Format32bppPArgb, _dibBits);
        _dibG      = Graphics.FromImage(_dibBitmap);
        _dibG.SmoothingMode      = SmoothingMode.AntiAlias;
        _dibG.InterpolationMode  = InterpolationMode.HighQualityBicubic;
        _dibG.PixelOffsetMode    = PixelOffsetMode.HighQuality;
        _dibG.CompositingQuality = CompositingQuality.HighQuality;
        _dibG.CompositingMode    = CompositingMode.SourceOver;

        // Область захвата фона: капсула + поля вокруг (в пределах экрана)
        var geo = ComputeGeometry(DockX, DockY);
        _capRect  = geo.Cap;
        _dockOffX = geo.OffX;
        _dockOffY = geo.OffY;

        _desktopHwnd = FindDesktopHwnd();
        RefreshWallpaperInfo(true);
        try { BackdropTick(); } catch { }                         // первый фон — сразу, до показа окна
        _bgFront = Interlocked.Exchange(ref _bgPending, null);

        ApplyItems(LoadItems());

        _hoverCheckTimer = new System.Windows.Forms.Timer { Interval = 30 };
        _hoverCheckTimer.Tick += (_, __) => CheckCursorAndHideTooltip();
        _hoverCheckTimer.Start();

        _longPressTimer = new System.Windows.Forms.Timer { Interval = LongPressMs };
        _longPressTimer.Tick += (_, __) => OnLongPress();

        // Слайд-шоу и т.п. не всегда присылают WM_SETTINGCHANGE — иногда сверяемся сами.
        _wallpaperTimer = new System.Windows.Forms.Timer { Interval = 15000 };
        _wallpaperTimer.Tick += (_, __) =>
        {
            _wallpaperTimer.Interval = 15000;
            RefreshWallpaperInfo(false);
        };
        _wallpaperTimer.Start();

        // Страж: если окно свернули/спрятали/сдвинули/подняли над другими — возвращаем.
        // Запускается в OnShown, когда окно уже показано.
        _guardTimer = new System.Windows.Forms.Timer { Interval = GuardIntervalMs };
        _guardTimer.Tick += (_, __) => EnsureShownAndPinned(false);
    }

    /// <summary>
    /// Сохранённая позиция (если капсула там помещается на экране) или по умолчанию:
    /// вертикальная — у правого края по центру, горизонтальная — внизу по центру над панелью задач.
    /// </summary>
    private Point InitialPosition(DockSettings s)
    {
        if (s.PositionX is int sx && s.PositionY is int sy)
        {
            var center = new Point(sx + BarW / 2, sy + BarH / 2);
            foreach (var scr in Screen.AllScreens)
                if (scr.Bounds.Contains(center)) return ClampToScreen(sx, sy, scr.Bounds);
            // монитор, на котором стояла капсула, отключили — ставим по умолчанию
        }
        return DefaultPosition(s);
    }

    private Point DefaultPosition(DockSettings s)
    {
        // Дополнительные профили — у противоположного края, чтобы не лечь поверх стандартной капсулы
        if (Horizontal)
        {
            var w = Screen.PrimaryScreen.WorkingArea;
            int y = s.IsDefault ? w.Bottom - BarH - DefaultEdgeMargin : w.Top + DefaultEdgeMargin;
            return new Point(w.Left + (w.Width - BarW) / 2, y);
        }
        var b = Screen.PrimaryScreen.Bounds;
        int x = s.IsDefault ? b.Right - BarW - DefaultEdgeMargin : b.Left + DefaultEdgeMargin;
        return new Point(x, b.Top + (b.Height - BarH) / 2);
    }

    /// <summary>Капсула целиком в пределах экрана (если влезает).</summary>
    private Point ClampToScreen(int x, int y, Rectangle b)
    {
        x = Math.Max(b.Left, Math.Min(x, b.Right  - BarW));
        y = Math.Max(b.Top,  Math.Min(y, b.Bottom - BarH));
        return new Point(x, y);
    }

    /// <summary>Что снимать для фона при капсуле в (x, y): капсула + поля вокруг, в пределах рабочего стола.</summary>
    private BackdropGeometry ComputeGeometry(int x, int y)
    {
        var cap = Rectangle.Intersect(
            new Rectangle(x - CaptureMargin, y - CaptureMargin, BarW + CaptureMargin * 2, BarH + CaptureMargin * 2),
            SystemInformation.VirtualScreen);
        return new BackdropGeometry { Cap = cap, OffX = x - cap.X, OffY = y - cap.Y };
    }

    // ------------------------------------------------------------------------
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (!_layeredOff) cp.ExStyle |= WS_EX_LAYERED;   // добавляется в OnHandleCreated
            cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hwnd = Handle;

        try { int v = 1; Native.DwmSetWindowAttribute(_hwnd, 2, ref v, 4); } catch { }

        // Не прятать окно при Aero Peek (DWMWA_EXCLUDED_FROM_PEEK = 12).
        try { int v = 1; Native.DwmSetWindowAttribute(_hwnd, 12, ref v, 4); } catch { }

        // Без владельца: у окна-владельца свой Z-порядок, и опустить дочернее окно
        // ниже владельца нельзя — привязка к рабочему столу тогда не сработала бы.
        try { Native.SetOwner(_hwnd, IntPtr.Zero); } catch { }

        // Сразу над рабочим столом, под всеми окнами.
        PinToDesktopLevel(true);

        // Живой фон: исключаем капсулу из захвата экрана и снимаем то, что под ней.
        // Пока окно ещё не layered — так это работает и на Windows 10.
        ApplyLiveBackground(true);     // если Windows не позволит — покажем уведомление в трее

        _layeredOff = false;
        int ex = Native.GetWindowLong(_hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_LAYERED) == 0) Native.SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED);

        RequestRender();
        if (_renderThread   == null) StartRenderThread();
        if (_backdropThread == null) StartBackdropThread();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        EnsureShownAndPinned(true);
        _guardTimer?.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;               // при выходе защиту отключаем, иначе не закроемся
        _guardTimer?.Stop();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _shutdown     = true;
        _backdropStop = true;
        try { _backdropWake.Set(); } catch { }
        try { _renderWake.Set(); } catch { }

        bool renderDone = true, backdropDone = true;
        try { if (_renderThread   != null) renderDone   = _renderThread.Join(500);   } catch { }
        try { if (_backdropThread != null) backdropDone = _backdropThread.Join(1500); } catch { }

        // Капсулу могут закрыть, пока программа работает дальше (профиль выключили) — таймеры освобождаем
        _guardTimer?.Stop();
        _guardTimer?.Dispose();
        _hoverCheckTimer?.Stop();
        _hoverCheckTimer?.Dispose();
        _longPressTimer?.Stop();
        _longPressTimer?.Dispose();
        _wallpaperTimer?.Stop();
        _wallpaperTimer?.Dispose();

        _tooltip?.Dispose();
        _itemMenu?.Dispose();

        if (renderDone)
        {
            _dibG?.Dispose();
            _dibBitmap?.Dispose();
            _shadowAttrs?.Dispose();

            lock (_sync)
                foreach (var f in _files) { f.Icon?.Dispose(); f.Shadow?.Dispose(); }

            // DIB окна читает и фоновый поток (проверка самозахвата) — освобождаем, только если он остановлен
            if (backdropDone)
            {
                if (_dibSection != IntPtr.Zero) Native.DeleteObject(_dibSection);
                if (_dibDC      != IntPtr.Zero) Native.DeleteDC(_dibDC);
            }
        }

        if (backdropDone)
        {
            _bbUpG?.Dispose();
            _bbUp?.Dispose();
            _bbSmall?.Dispose();
            _bbWrap?.Dispose();
            _curWallpaper?.Dispose();
            _pw.Free();
            _sc.Free();
        }

        Native.timeEndPeriod(1);
        base.OnFormClosed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }

    private void RequestRender()
    {
        Interlocked.Exchange(ref _renderRequest, 1);
        try { _renderWake.Set(); } catch { }
    }

    // ========================================================================
    //  WndProc — защита от скрытия (Win+D, Show Desktop, minimize) + мышь
    // ========================================================================
    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEMOVE         = 0x0200;
        const int WM_LBUTTONDOWN       = 0x0201;
        const int WM_LBUTTONUP         = 0x0202;
        const int WM_LBUTTONDBLCLK     = 0x0203;
        const int WM_RBUTTONDOWN       = 0x0204;
        const int WM_RBUTTONUP         = 0x0205;
        const int WM_MOUSEWHEEL        = 0x020A;
        const int WM_CAPTURECHANGED    = 0x0215;
        const int WM_NCHITTEST         = 0x0084;
        const int WM_MOUSEACTIVATE     = 0x0021;
        const int WM_SYSCOMMAND        = 0x0112;
        const int WM_SHOWWINDOW        = 0x0018;
        const int WM_ERASEBKGND        = 0x0014;
        const int WM_NCACTIVATE        = 0x0086;
        const int WM_WINDOWPOSCHANGING = 0x0046;
        const int WM_SETTINGCHANGE     = 0x001A;
        const int SPI_SETDESKWALLPAPER = 0x0014;
        const int MA_NOACTIVATE        = 3;
        const int SC_MINIMIZE          = 0xF020;
        const int WM_SIZE              = 0x0005;
        const long SIZE_MINIMIZED      = 1;

        // ---- Защита от Win+D / «Показать рабочий стол» / Win+M ----
        if (m.Msg == WM_WINDOWPOSCHANGING && !_closing)
        {
            var wp = Marshal.PtrToStructure<WINDOWPOS>(m.LParam);
            bool changed = false;

            // Windows пытается увести окно за экран (так сворачиваются окна).
            if ((wp.flags & SWP_NOMOVE) == 0 && (wp.x <= -32000 || wp.y <= -32000))
            {
                wp.flags |= SWP_NOMOVE | SWP_NOSIZE;
                changed = true;
            }

            // Windows пытается скрыть окно.
            if ((wp.flags & SWP_HIDEWINDOW) != 0)
            {
                wp.flags &= ~SWP_HIDEWINDOW;
                changed = true;
            }

            // Поднять окно над остальными (HWND_TOP / HWND_TOPMOST) может только сама программа.
            if (!_allowZChange && (wp.flags & SWP_NOZORDER) == 0 &&
                (wp.hwndInsertAfter == IntPtr.Zero || wp.hwndInsertAfter == new IntPtr(-1)))
            {
                wp.flags |= SWP_NOZORDER;
                changed = true;
            }

            if (changed)
            {
                Marshal.StructureToPtr(wp, m.LParam, false);
                base.WndProc(ref m);
                return;
            }
        }

        if (m.Msg == WM_SYSCOMMAND &&
            (m.WParam.ToInt64() & 0xFFF0) == SC_MINIMIZE)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        // Окно спрятали (SW_HIDE) — сразу возвращаем.
        if (m.Msg == WM_SHOWWINDOW && m.WParam == IntPtr.Zero && !_closing)
        {
            base.WndProc(ref m);
            QueueGuard();
            return;
        }

        // Окно свернули (ShowWindow(SW_MINIMIZE), Win+D, Win+M) — сразу возвращаем.
        if (m.Msg == WM_SIZE && m.WParam.ToInt64() == SIZE_MINIMIZED && !_closing)
        {
            base.WndProc(ref m);
            QueueGuard();
            return;
        }

        if (m.Msg == WM_ERASEBKGND)
        {
            m.Result = (IntPtr)1;
            return;
        }

        if (m.Msg == WM_NCACTIVATE)
        {
            m.Result = (IntPtr)1;
            return;
        }

        switch (m.Msg)
        {
            case WM_NCHITTEST:
            {
                int sx = LowWord(m.LParam);
                int sy = HighWord(m.LParam);
                if (IsPointInDock(sx, sy)) { m.Result = (IntPtr)1; return; }
                m.Result = (IntPtr)(-1); return;
            }

            case WM_MOUSEACTIVATE:
                // MA_NOACTIVATE: окно не активируется и не поднимается над другими,
                // но нажатие кнопки до нас доходит (нужно для долгого нажатия).
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;

            case WM_MOUSEWHEEL:
            {
                // Колесо вверх по умолчанию: вертикальная капсула — ярлыки едут сверху вниз,
                // горизонтальная — справа налево. «Обратная прокрутка» меняет обе.
                // Без блокировки: UI-поток не ждёт, пока рендер-поток дорисует кадр, —
                // щелчки колеса копятся и забираются на ближайшем кадре.
                int delta = HighWord(m.WParam);
                int sign  = (Horizontal ? -1 : 1) * (InvertScroll ? -1 : 1);
                Interlocked.Add(ref _wheelDelta, delta * sign);
                RequestRender();
                return;
            }

            case WM_LBUTTONDOWN:
            case WM_LBUTTONDBLCLK:
                OnDockMouseDown(LowWord(m.LParam), HighWord(m.LParam));
                m.Result = IntPtr.Zero;
                return;

            case WM_MOUSEMOVE:
                OnDockMouseMove();
                break;

            case WM_LBUTTONUP:
                OnDockMouseUp(LowWord(m.LParam), HighWord(m.LParam));
                m.Result = IntPtr.Zero;
                return;

            // Правую кнопку обрабатываем сами целиком: если отдать WM_RBUTTONDOWN в WinForms, он
            // захватывает мышь до WM_RBUTTONUP, который мы не пропускаем дальше, — захват оставался,
            // следующий клик в любом окне уходил в капсулу, и меню то не открывалось, то не закрывалось.
            case WM_RBUTTONDOWN:
                m.Result = IntPtr.Zero;
                return;

            case WM_RBUTTONUP:
                if (Capture) Capture = false;
                ShowItemMenu(LowWord(m.LParam), HighWord(m.LParam));
                m.Result = IntPtr.Zero;
                return;

            case WM_CAPTURECHANGED:
                if (m.LParam != _hwnd) OnCaptureLost();
                break;

            case WM_SETTINGCHANGE:
                if (m.WParam.ToInt64() == SPI_SETDESKWALLPAPER)
                {
                    RefreshWallpaperInfo(true);
                    // Windows дописывает файл обоев с задержкой — проверим ещё раз чуть позже.
                    if (_wallpaperTimer != null)
                    {
                        _wallpaperTimer.Stop();
                        _wallpaperTimer.Interval = 1500;
                        _wallpaperTimer.Start();
                    }
                }
                break;
        }
        base.WndProc(ref m);
    }

    private static int LowWord(IntPtr v)  => unchecked((short)(long)v);
    private static int HighWord(IntPtr v) => unchecked((short)((long)v >> 16));

    // ========================================================================
    //  МЫШЬ: клик запускает, долгое нажатие — «поднимает» ярлык для перестановки
    // ========================================================================
    private void OnDockMouseDown(int lx, int ly)
    {
        if (_drag != null || _moving) return;

        // Позиция не закреплена: нажатие тянет всю капсулу, ярлыки не открываются
        if (!_positionLocked)
        {
            if (!IsPointInDock(lx + WinX, ly + WinY)) return;
            _moving     = true;
            _moveStart  = Cursor.Position;
            _moveOrigin = new Point(DockX, DockY);
            _pressItem  = null;
            Capture     = true;
            PostHideTooltip();
            return;
        }

        if (!IsPointInDock(lx + WinX, ly + WinY)) return;

        DockItem hit = null;
        lock (_sync)
        {
            int idx = HoverIndex(lx, ly);
            if (idx >= 0) hit = _files[idx];
        }

        _pressItem      = hit;
        _pressActive    = true;
        _pressPoint     = new Point(lx + WinX, ly + WinY);
        _pressCancelled = false;
        _longPressTimer.Stop();
        if (hit != null) _longPressTimer.Start();   // долгое нажатие на ярлыке — поднять его
        Capture = true;                             // кнопку отпустят где угодно — WM_LBUTTONUP всё равно придёт нам
    }

    private void OnDockMouseMove()
    {
        if (_moving)
        {
            var p = Cursor.Position;
            MoveDockTo(_moveOrigin.X + p.X - _moveStart.X, _moveOrigin.Y + p.Y - _moveStart.Y);
            return;
        }

        if (_panning) { UpdatePan(); return; }
        if (!_pressActive || _pressCancelled || _drag != null) return;

        // Мышь сдвинули раньше, чем сработало долгое нажатие, — это не клик, а прокрутка курсором
        var c = CursorPos;
        if (Math.Abs(c.X - _pressPoint.X) > PressMoveTolerance ||
            Math.Abs(c.Y - _pressPoint.Y) > PressMoveTolerance)
        {
            _pressCancelled = true;
            _longPressTimer.Stop();
            StartPan();
        }
    }

    // ------------------------------------------------------------------------
    //  Прокрутка курсором: нажал, повёл — ярлыки едут за курсором; отпустил — чуть докатываются
    // ------------------------------------------------------------------------
    private void StartPan()
    {
        int main = Horizontal ? _pressPoint.X : _pressPoint.Y;
        lock (_sync) _panStartScroll = _scrollPos;
        _panStartMain = main;
        _panLastMain  = main;
        _panLastT     = _clock.Elapsed.TotalSeconds;
        _panVel       = 0;
        _panning      = true;
        PostHideTooltip();
        UpdatePan();
    }

    private void UpdatePan()
    {
        var    c    = CursorPos;
        int    main = Horizontal ? c.X : c.Y;
        double now  = _clock.Elapsed.TotalSeconds;
        double dtm  = now - _panLastT;
        if (dtm > 0.001)
        {
            double v = (main - _panLastMain) / dtm;          // px/с, сглаживаем — для докатывания
            _panVel = _panVel * 0.6 + v * 0.4;
            _panLastMain = main;
            _panLastT    = now;
        }

        lock (_sync)
        {
            double s = Math.Min(Math.Max(_panStartScroll - (main - _panStartMain), 0), _maxScroll);
            _scrollPos = _targetScroll = s;
        }
        RequestRender();
    }

    private void EndPan()
    {
        _panning = false;
        // Отпустили на ходу — ярлыки ещё немного едут и плавно останавливаются (как после колеса)
        if (_clock.Elapsed.TotalSeconds - _panLastT < 0.08 && Math.Abs(_panVel) > 50)
        {
            lock (_sync)
                _targetScroll = Math.Min(Math.Max(_scrollPos - _panVel * 0.12, 0), _maxScroll);
            RequestRender();
        }
    }

    private void OnLongPress()
    {
        _longPressTimer.Stop();

        var item = _pressItem;
        if (item == null || _pressCancelled || _drag != null || !IsPrimaryButtonDown()) return;

        var c = Cursor.Position;
        lock (_sync)
        {
            int idx = _files.IndexOf(item);
            if (idx < 0) return;

            double top = item.AnimY - _scrollPos;
            _drag = new DragState
            {
                Item       = item,
                GrabOffset = MainLocal(c) - top,
                Top        = top,
                StartIndex = idx
            };
        }

        _pressItem = null;                     // нажатие ушло в перетаскивание — запуска не будет
        if (!Capture) Capture = true;
        Cursor.Current = Horizontal ? Cursors.SizeWE : Cursors.SizeNS;
        RequestRender();
    }

    private void OnDockMouseUp(int lx, int ly)
    {
        _longPressTimer.Stop();

        if (_moving)
        {
            EndMove();
            return;
        }

        _pressActive = false;
        if (_panning)
        {
            EndPan();
        }
        else if (_drag != null)
        {
            EndDrag();
        }
        else if (_pressItem != null && !_pressCancelled)
        {
            DockItem hit = null;
            if (IsPointInDock(lx + WinX, ly + WinY))
            {
                lock (_sync)
                {
                    int idx = HoverIndex(lx, ly);
                    if (idx >= 0) hit = _files[idx];
                }
            }
            if (hit != null && hit == _pressItem) Launch(hit.Path);
        }

        _pressItem = null;
        if (Capture) Capture = false;
    }

    private void OnCaptureLost()
    {
        _longPressTimer.Stop();
        _pressItem   = null;
        _pressActive = false;
        if (_moving) EndMove();
        if (_panning) EndPan();
        if (_drag != null) EndDrag();
    }

    // ========================================================================
    //  ПЕРЕТАСКИВАНИЕ КАПСУЛЫ (позиция не закреплена)
    // ========================================================================
    private void MoveDockTo(int x, int y)
    {
        var center = new Point(x + BarW / 2, y + BarH / 2);
        var p      = ClampToScreen(x, y, Screen.FromPoint(center).Bounds);
        if (p.X == DockX && p.Y == DockY) return;

        DockX = p.X;
        DockY = p.Y;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, WinX, WinY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        // Фон теперь снимаем в новом месте
        Interlocked.Exchange(ref _pendingGeometry, ComputeGeometry(p.X, p.Y));
        RequestBackdropRefresh(true);
    }

    private void EndMove()
    {
        _moving = false;
        if (Capture) Capture = false;

        _settings.PositionX = DockX;
        _settings.PositionY = DockY;
        _settings.Save();

        RefreshWallpaperInfo(false);     // капсулу могли перенести на другой монитор
        EnsureShownAndPinned(true);      // над рабочим столом, под окнами — и на новом месте
    }

    /// <summary>Включить/выключить закрепление позиции (сразу, без перезапуска).</summary>
    private void SetPositionLocked(bool locked)
    {
        if (locked == _positionLocked) return;
        if (_moving) EndMove();
        _positionLocked = locked;
        Cursor = locked ? Cursors.Default : Cursors.SizeAll;
    }

    /// <summary>Отпустили перетаскиваемый ярлык: он «доезжает» до своего места, порядок сохраняется.</summary>
    private void EndDrag()
    {
        List<string> order = null;
        lock (_sync)
        {
            var d = _drag;
            if (d == null) return;
            _drag = null;

            d.Item.AnimY = d.Top + _scrollPos;
            if (_files.IndexOf(d.Item) != d.StartIndex)
                order = _files.Select(f => f.Key).ToList();
        }

        Cursor.Current = Cursors.Default;
        RequestRender();

        if (order != null)
        {
            _settings.Order = order;
            _settings.Save();
        }
    }

    /// <summary>Из рендер-потока: попросить UI-поток завершить перетаскивание.</summary>
    private void QueueEndDrag()
    {
        if (Interlocked.Exchange(ref _endDragQueued, 1) != 0) return;
        try
        {
            BeginInvoke((Action)(() =>
            {
                Interlocked.Exchange(ref _endDragQueued, 0);
                if (_drag == null) return;
                EndDrag();
                if (Capture) Capture = false;
            }));
        }
        catch { Interlocked.Exchange(ref _endDragQueued, 0); }
    }

    private static bool IsPrimaryButtonDown()
    {
        int vk = SystemInformation.MouseButtonsSwapped ? 0x02 : 0x01;   // VK_RBUTTON : VK_LBUTTON
        return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    private static void Launch(string path)
    {
        try { Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true })?.Dispose(); }
        catch { }
    }

    // ========================================================================
    //  ЗАЩИТА ОТ СКРЫТИЯ + ПРИВЯЗКА К УРОВНЮ РАБОЧЕГО СТОЛА
    //  Окно всегда видно (Win+D / Win+M / «Свернуть все» его не прячут)
    //  и лежит прямо над рабочим столом — под всеми обычными окнами.
    // ========================================================================
    private void QueueGuard()
    {
        try
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke((Action)(() => EnsureShownAndPinned(true)));
        }
        catch { }
    }

    private void EnsureShownAndPinned(bool force)
    {
        if (_closing || IsDisposed || !IsHandleCreated) return;

        try
        {
            IntPtr h = Handle;
            bool changed = false;

            // 1) Окно свернули или спрятали — возвращаем, не активируя.
            if (Native.IsIconic(h) || !Native.IsWindowVisible(h))
            {
                Native.ShowWindow(h, SW_SHOWNOACTIVATE);
                changed = true;
                force   = true;
            }

            // 2) Окно сдвинули (например, в -32000,-32000) — возвращаем на место.
            //    Пока капсула выезжает/прячется, её двигает рендер-поток — не мешаем.
            var r = new RECT();
            if (!_slideBusy && Native.GetWindowRect(h, ref r) &&
                (Math.Abs(r.Left - WinX) > 2 || Math.Abs(r.Top - WinY) > 2))
            {
                Native.SetWindowPos(h, IntPtr.Zero, WinX, WinY, 0, 0,
                                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                changed = true;
            }

            // 3) Z-порядок: прямо над рабочим столом, под остальными окнами.
            if (PinToDesktopLevel(force)) changed = true;

            if (changed) RequestRender();
        }
        catch { }
    }

    /// <summary>Ставит окно сразу над рабочим столом. true — если Z-порядок реально меняли.</summary>
    private bool PinToDesktopLevel(bool force)
    {
        IntPtr h = Handle;

        // explorer.exe мог перезапуститься — handle рабочего стола тогда другой.
        if (_desktopHwnd == IntPtr.Zero || !Native.IsWindow(_desktopHwnd))
        {
            _desktopHwnd = FindDesktopHwnd();
            RequestBackdropRefresh(true);
        }

        IntPtr desk = _desktopHwnd;
        if (desk == IntPtr.Zero)
        {
            // Рабочий стол не нашли — как раньше: просто не topmost.
            if (!force) return false;
            SetZ(h, new IntPtr(-2));
            return true;
        }

        if (IsDirectlyAboveDesktop(h, desk)) return false;

        long now = Environment.TickCount64;
        if (!force && now - _lastPinTick < RepinCooldownMs) return false;
        _lastPinTick = now;

        // Окно, лежащее прямо над рабочим столом. Встаём сразу за ним (= над рабочим столом).
        IntPtr above = Native.GetWindow(desk, GW_HWNDPREV);
        if (above == h) return false;

        IntPtr insertAfter;
        if (above == IntPtr.Zero)
            insertAfter = IntPtr.Zero;                 // HWND_TOP: рабочий стол и так выше всех
        else if ((Native.GetWindowLong(above, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0)
            insertAfter = new IntPtr(-2);              // HWND_NOTOPMOST: выше рабочего стола, ниже topmost-окон
        else
            insertAfter = above;                       // сразу под нижним обычным окном

        SetZ(h, insertAfter);
        return true;
    }

    /// <summary>
    /// true, если между нами и рабочим столом нет видимых окон, перекрывающихся с доком
    /// (то есть мы никого не закрываем и сами не провалились под рабочий стол).
    /// </summary>
    private bool IsDirectlyAboveDesktop(IntPtr h, IntPtr desk)
    {
        var me = new RECT();
        if (!Native.GetWindowRect(h, ref me)) return true;

        int guard = 0;
        for (IntPtr p = Native.GetWindow(h, GW_HWNDNEXT);
             p != IntPtr.Zero && guard < 512;
             p = Native.GetWindow(p, GW_HWNDNEXT), guard++)
        {
            if (p == desk) return true;                // дошли до рабочего стола — порядок верный

            if (!Native.IsWindowVisible(p) || Native.IsIconic(p)) continue;

            var r = new RECT();
            if (!Native.GetWindowRect(p, ref r)) continue;

            bool overlap = r.Left < me.Right && r.Right > me.Left &&
                           r.Top  < me.Bottom && r.Bottom > me.Top;
            if (overlap) return false;                 // мы лежим выше обычного окна и перекрываем его
        }
        return false;                                  // рабочий стол ниже нас не найден — мы под ним
    }

    private void SetZ(IntPtr hwnd, IntPtr insertAfter)
    {
        _allowZChange = true;
        try { Native.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE); }
        finally { _allowZChange = false; }
    }

    /// <summary>
    /// Включает/выключает живой фон. Капсула исключается из захвата экрана — иначе она «видела» бы
    /// сама себя. Побочный эффект: док не виден на скриншотах и в записи экрана.
    /// </summary>
    private void ApplyLiveBackground(bool notify)
    {
        if (!IsHandleCreated) return;

        bool ok = false;
        string why = "";
        _liveApplied = _settings.LiveBackground;
        if (_settings.LiveBackground)
        {
            if (Environment.OSVersion.Version.Build < 19041)
                why = L.T("Windows 10 version 2004 or newer is required", "нужна Windows 10 версии 2004 или новее");
            else
            {
                try
                {
                    const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
                    if (!SetCaptureAffinity(WDA_EXCLUDEFROMCAPTURE, out int err))
                        why = L.T("Windows refused (code ", "Windows отказала (код ") + err + ")";
                    else if (!Native.GetWindowDisplayAffinity(_hwnd, out uint a) || a != WDA_EXCLUDEFROMCAPTURE)
                        why = L.T("Windows did not apply the capture exclusion", "Windows не применила исключение из захвата");
                    else
                        ok = true;
                }
                catch (Exception ex) { why = ex.Message; }
            }
        }

        if (!ok)
        {
            try { SetCaptureAffinity(0, out _); } catch { }   // WDA_NONE — док снова виден в захвате
        }

        _screenStatus = why;
        _selfSeen     = 0;
        Interlocked.Exchange(ref _screenReset, 1);
        _screenCapture = ok;
        RequestBackdropRefresh(true);

        if (notify && _settings.LiveBackground && !ok)
            _app.ShowTrayMessage(L.T("Live background unavailable: ", "Живой фон недоступен: ") + why + L.T(". Using the wallpaper image.", ". Используется картинка обоев."));
    }

    /// <summary>
    /// SetWindowDisplayAffinity, которая работает и на Windows 10: layered-окну там Windows
    /// отказывает (код 8), поэтому на время вызова снимаем WS_EX_LAYERED — выставленное
    /// значение сохраняется, когда стиль возвращается.
    /// </summary>
    private bool SetCaptureAffinity(uint affinity, out int error)
    {
        error = 0;
        if (Native.GetWindowDisplayAffinity(_hwnd, out uint cur) && cur == affinity) return true;
        if (Native.SetWindowDisplayAffinity(_hwnd, affinity)) return true;
        error = Marshal.GetLastWin32Error();

        lock (_sync)                   // чтобы рендер-поток не звал UpdateLayeredWindow без стиля
        {
            int ex = Native.GetWindowLong(_hwnd, GWL_EXSTYLE);
            if ((ex & WS_EX_LAYERED) == 0) return false;

            Native.SetWindowLong(_hwnd, GWL_EXSTYLE, ex & ~WS_EX_LAYERED);
            bool ok = Native.SetWindowDisplayAffinity(_hwnd, affinity);
            error = ok ? 0 : Marshal.GetLastWin32Error();
            Native.SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
            RequestRender();           // layered-окно без UpdateLayeredWindow пустое — перерисовать
            return ok;
        }
    }

    /// <summary>Из фонового потока: живой фон не работает — откатываемся на картинку обоев.</summary>
    private void DisableScreenCaptureFromWorker(string why)
    {
        _screenCapture = false;
        _screenStatus  = why;
        try
        {
            BeginInvoke((Action)(() =>
            {
                try { SetCaptureAffinity(0, out _); } catch { }
                _app.ShowTrayMessage(L.T("Live background turned off: ", "Живой фон отключён: ") + why + L.T(". Using the wallpaper image.", ". Используется картинка обоев."));
            }));
        }
        catch { }
    }

    // ------------------------------------------------------------------------
    //  Команды из трея и окна настроек (CapDockApp)
    // ------------------------------------------------------------------------
    public DockSettings Profile => _settings;
    public BackdropMode Mode    => (BackdropMode)_mode;

    /// <summary>Почему живой фон сейчас не работает (null — работает или выключен).</summary>
    public string LiveProblem =>
        _settings.LiveBackground && !_screenCapture && _screenStatus.Length > 0 ? _screenStatus : null;

    // ---- Координаты из окна настроек: капсула двигается сразу, пока ведут ползунок ----
    public Point HomePosition => new Point(DockX, DockY);

    /// <summary>Передвинуть (без сохранения). Возвращает, где капсула оказалась — она не выходит за экран.</summary>
    public Point PreviewPosition(int x, int y)
    {
        MoveDockTo(x, y);
        return HomePosition;
    }

    public Point PreviewDefaultPosition()
    {
        var d = DefaultPosition(_settings);
        return PreviewPosition(d.X, d.Y);
    }

    /// <summary>Настройки закрыли без сохранения — вернуться на сохранённое место.</summary>
    public void RevertPosition()
    {
        var p = InitialPosition(_settings);
        MoveDockTo(p.X, p.Y);
        FinishPreview();
    }

    /// <summary>Двигали из настроек — теперь обновить обои (мог смениться монитор) и слой окна.</summary>
    public void FinishPreview()
    {
        RefreshWallpaperInfo(false);
        EnsureShownAndPinned(true);
    }

    /// <summary>Настройки сохранены. Режим фона, живой фон, закрепление позиции и автоскрытие — сразу, остальное — после перезапуска.</summary>
    public void ApplyRuntimeSettings()
    {
        LoadAutoHideConfig(_settings);
        SetPositionLocked(_settings.PositionLocked);
        if ((int)_settings.GetMode() != _mode) SetBackdropMode(_settings.GetMode());
        if (_settings.LiveBackground != _liveApplied) ApplyLiveBackground(true);
    }

    public void SetBackdropMode(BackdropMode mode)
    {
        _mode = (int)mode;
        _settings.BackgroundMode = mode.ToString();
        _settings.Save();
        RequestBackdropRefresh(true);
    }

    public void ToggleLiveBackground()
    {
        _settings.LiveBackground = !_settings.LiveBackground;
        _settings.Save();
        ApplyLiveBackground(true);
    }

    public void RebuildBlur() => RefreshWallpaperInfo(true);

    public void RestartRenderThread()
    {
        var old = _renderThread;
        _renderGen++;                              // старый поток увидит это и завершится
        try { old?.Join(500); } catch { }
        RequestRender();
        StartRenderThread();
    }

    public void ReloadAll()
    {
        if (_drag != null) EndDrag();
        _pressItem = null;
        ApplyItems(LoadItems());
        RequestBackdropRefresh(true);
    }

    /// <summary>
    /// Стандартный порядок: забываем порядок, запомненные (перетащенные без переноса) файлы
    /// и скрытые ярлыки — скрытые возвращаются. Сами файлы не трогаются.
    /// </summary>
    public void ResetOrder()
    {
        string text = L.T(
            "Reset the shortcut order of “{0}”?\n\nFiles you dragged in without moving them will disappear from the dock, and hidden shortcuts will come back. No files are deleted or moved.",
            "Сбросить порядок ярлыков капсулы «{0}»?\n\nФайлы, перетащенные без переноса, пропадут из капсулы, а скрытые ярлыки вернутся. Сами файлы не удаляются и не перемещаются.");
        if (MessageBox.Show(string.Format(text, _settings.DisplayName), AppSettings.AppName,
                            MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        ResetOrderNow();
    }

    private void ResetOrderNow()
    {
        _settings.Order       = new List<string>();
        _settings.ExtraItems  = new List<string>();
        _settings.HiddenItems = new List<string>();
        _settings.Save();
        ReloadAll();
    }

    // ========================================================================
    //  РЕНДЕР-ПОТОК
    // ========================================================================
    private void StartRenderThread()
    {
        int gen = ++_renderGen;
        var t = new Thread(() => RenderThreadProc(gen))
        {
            IsBackground = true,
            Name = "DockRenderer",
            Priority = ThreadPriority.AboveNormal
        };
        _renderThread = t;
        t.Start();
    }

    /// <summary>
    /// Пока что-то движется — по кадру на каждое обновление экрана (DwmFlush ждёт следующий кадр
    /// рабочего стола), но не чаще, чем раз в RenderIntervalMs. Когда всё стоит — поток спит и
    /// просыпается от RequestRender (колесо, новый фон…) или раз в IdlePollMs — проверить курсор.
    /// Раньше поток крутился вхолостую (SpinWait) 250 раз в секунду: по ядру процессора на каждую
    /// капсулу, и несколько капсул отнимали время друг у друга — прокрутка то ползла, то догоняла рывком.
    /// </summary>
    private void RenderThreadProc(int gen)
    {
        var    sw       = Stopwatch.StartNew();
        double minFrame = RenderIntervalMs / 1000.0;
        double last     = sw.Elapsed.TotalSeconds;
        bool   vsync    = true;

        while (!_shutdown && gen == _renderGen && !IsDisposed)
        {
            double now = sw.Elapsed.TotalSeconds;
            double dt  = Math.Min(now - last, 0.05);
            last = now;

            bool animating = false;
            try { animating = UpdateLoop(dt); } catch { }
            if (_shutdown || gen != _renderGen) break;

            if (animating)
            {
                if (vsync && Native.DwmFlush() != 0) vsync = false;   // без композиции — просто по таймеру
                double spent = sw.Elapsed.TotalSeconds - now;
                if (spent < minFrame) Thread.Sleep(Math.Max(1, (int)Math.Ceiling((minFrame - spent) * 1000)));
            }
            else
            {
                try { _renderWake.WaitOne(IdlePollMs); } catch { break; }
                last = sw.Elapsed.TotalSeconds - 1.0 / 60;           // первый шаг после простоя — обычный кадр, без рывка
            }
        }
    }

    // ========================================================================
    //  ПОИСК РАБОЧЕГО СТОЛА
    // ========================================================================
    private static IntPtr FindDesktopHwnd()
    {
        var progman = Native.FindWindowW("Progman", null);
        if (progman != IntPtr.Zero)
        {
            var view = Native.FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero) return progman;
        }

        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            var view = Native.FindWindowExW(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero) { found = hwnd; return false; }
            return true;
        }, IntPtr.Zero);

        return found != IntPtr.Zero ? found : progman;
    }

    /// <summary>
    /// Окно, в котором рисуются обои (туда же встраиваются Wallpaper Engine / Lively).
    /// Windows 11 24H2+: WorkerW — дочернее окно Progman; раньше — отдельное WorkerW сразу за окном с ярлыками.
    /// </summary>
    private static IntPtr FindWallpaperHost(IntPtr desk)
    {
        IntPtr progman = Native.FindWindowW("Progman", null);
        if (progman != IntPtr.Zero)
        {
            IntPtr w = Native.FindWindowExW(progman, IntPtr.Zero, "WorkerW", null);
            if (w != IntPtr.Zero) return w;
        }
        if (desk != IntPtr.Zero && desk != progman)
            return Native.FindWindowExW(IntPtr.Zero, desk, "WorkerW", null);
        return IntPtr.Zero;
    }

    private static bool HasVisibleChild(IntPtr parent)
    {
        IntPtr c = IntPtr.Zero;
        for (int i = 0; i < 64; i++)
        {
            c = Native.FindWindowExW(parent, c, null, null);
            if (c == IntPtr.Zero) return false;
            if (Native.IsWindowVisible(c)) return true;
        }
        return false;
    }

    // ========================================================================
    //  ФОН КАПСУЛЫ — отдельный поток DockBackdrop.
    //  Живой фон (по умолчанию): ~30 раз в секунду снимаем экран прямо за капсулой —
    //  видно и живые обои (Wallpaper Engine / Lively), и ярлыки, ровно как на экране.
    //  Запасной вариант (живой фон выключен или недоступен):
    //  1) обои рисуем сами (файл + режим «Заполнение/По размеру/…»), как это делает Windows;
    //  2) ярлыки рабочего стола снимаем PrintWindow и кладём поверх обоев.
    //  Затем размываем: «Размытие» (как раньше) или «Акрил» (как в Windows 10).
    // ========================================================================
    private void StartBackdropThread()
    {
        _backdropStop   = false;
        _backdropThread = new Thread(BackdropThreadProc)
        {
            IsBackground = true,
            Name         = "DockBackdrop",
            Priority     = ThreadPriority.BelowNormal
        };
        _backdropThread.Start();
    }

    private void BackdropThreadProc()
    {
        while (!_backdropStop)
        {
            // Спрятана за краем экрана — фон никто не видит; как только начнёт выезжать, её разбудят
            if (!_fullyHidden) try { BackdropTick(); } catch { }
            int wait = _fullyHidden ? 250 : _screenCapture ? LiveRefreshMs : _liveWallpaper ? LiveWallpaperRefreshMs : BlurRefreshMs;
            try { _backdropWake.WaitOne(wait); } catch { break; }
        }
    }

    private void RequestBackdropRefresh(bool forceRebuild)
    {
        if (forceRebuild) Interlocked.Exchange(ref _forceBg, 1);
        try { _backdropWake.Set(); } catch { }
    }

    /// <summary>UI-поток: узнать, какие обои стоят; если поменялись — перерисовать.</summary>
    private void RefreshWallpaperInfo(bool force)
    {
        WallpaperInfo wi;
        try { wi = WallpaperInfo.Query(new Point(DockX + BarW / 2, DockY + BarH / 2)); }
        catch { return; }

        if (!force && wi.SameAs(_lastWallpaperInfo)) return;
        _lastWallpaperInfo = wi;
        Interlocked.Exchange(ref _pendingWallpaper, wi);
        RequestBackdropRefresh(true);
    }

    private void BackdropTick()
    {
        bool force = Interlocked.Exchange(ref _forceBg, 0) != 0;

        // 0) Капсулу передвинули — снимаем фон в новом месте, все слои заново
        var geo = Interlocked.Exchange(ref _pendingGeometry, null);
        if (geo != null && (geo.Cap != _capRect || geo.OffX != _dockOffX || geo.OffY != _dockOffY))
        {
            _capRect  = geo.Cap;
            _dockOffX = geo.OffX;
            _dockOffY = geo.OffY;
            _wallLayer = _iconLayer = _liveLayer = _composite = _lastComposite = _screenLayer = null;
            _occMask   = null;
            force      = true;
        }

        int n = _capRect.Width * _capRect.Height;
        if (n <= 0) return;

        // 1) Обои (статичная картинка) — перерисовываем, когда они сменились или капсулу сдвинули
        var wi = Interlocked.Exchange(ref _pendingWallpaper, null);
        if (wi != null)
        {
            if (_curWallpaper != wi) _curWallpaper?.Dispose();
            _curWallpaper = wi;
            _wallLayer    = null;
            force         = true;
        }
        if (_wallLayer == null)
        {
            var cur = _curWallpaper;
            uint[] layer = null;
            try { layer = cur?.Render(_capRect); } catch { }
            _wallLayer = layer ?? Solid(n, cur?.Background ?? Color.Black);
            _wallKey   = cur != null ? (uint)cur.Background.ToArgb() & 0xFFFFFF : 0;
        }
        _iconLayer ??= new uint[n];
        _liveLayer ??= new uint[n];
        _composite ??= new uint[n];

        // 2) Окно рабочего стола: explorer мог перезапуститься или перенести ярлыки
        //    из Progman в WorkerW — раз в ~2 с проверяем заново.
        IntPtr desk = _desktopHwnd;
        if (desk == IntPtr.Zero || !Native.IsWindow(desk) || ++_deskCheck >= 20)
        {
            _deskCheck = 0;
            IntPtr found = FindDesktopHwnd();
            if (found != IntPtr.Zero && found != desk)
            {
                desk = found;
                _desktopHwnd = found;
            }
        }

        // 3) Откуда фон: живой — прямо с экрана; иначе — картинка обоев + ярлыки
        if (Interlocked.Exchange(ref _screenReset, 0) != 0)
        {
            _screenLayer = null;
            force        = true;
        }

        if (_screenCapture)
        {
            if (_screenLayer == null)
            {
                // пока экран не снят (или где его закрывают окна) — статичный фон
                if (_lastComposite != null) Array.Copy(_lastComposite, _composite, n);
                else                        BuildStaticComposite(desk, _composite);
                _screenLayer = (uint[])_composite.Clone();
            }
            UpdateScreenLayer();
            Array.Copy(_screenLayer, _composite, n);
        }
        else
        {
            BuildStaticComposite(desk, _composite);
        }

        // 4) Картинка та же и режим не меняли — размытие не пересчитываем
        if (!force && _lastComposite != null && _composite.AsSpan().SequenceEqual(_lastComposite))
            return;

        // Пишем в свободный буфер (без новой памяти на каждый кадр)
        var bg = Interlocked.Exchange(ref _bgSpare, null);
        if (bg == null || bg.Length != BarW * BarH) bg = new uint[BarW * BarH];
        try { BuildBackground(_composite, (BackdropMode)_mode, bg); }
        catch
        {
            Interlocked.Exchange(ref _bgSpare, bg);
            Interlocked.Exchange(ref _forceBg, 1);                // не вышло — попробуем на следующем тике
            return;
        }

        var fresh = _composite;
        _composite     = _lastComposite ?? new uint[n];
        _lastComposite = fresh;

        // Рендер-поток подхватит; прежний готовый, который он не успел взять, снова свободен
        var skipped = Interlocked.Exchange(ref _bgPending, bg);
        if (skipped != null) Interlocked.Exchange(ref _bgSpare, skipped);
        RequestRender();
    }

    /// <summary>Статичный фон: картинка обоев (или окно живых обоев) + ярлыки рабочего стола.</summary>
    private void BuildStaticComposite(IntPtr desk, uint[] dst)
    {
        int n = dst.Length;
        bool live = TryCaptureLiveWallpaper(desk);
        _liveWallpaper = live;
        uint[] baseLayer = live ? _liveLayer : _wallLayer;

        if (desk != IntPtr.Zero && CaptureWindowRegion(desk, _iconLayer))
            CompositeDesktop(_iconLayer, baseLayer, dst);
        else
            Array.Copy(baseLayer, dst, n);
    }

    // ------------------------------------------------------------------------
    //  ЖИВОЙ ФОН: снимок экрана за капсулой
    // ------------------------------------------------------------------------
    private unsafe void UpdateScreenLayer()
    {
        int cw = _capRect.Width, ch = _capRect.Height, n = cw * ch;
        _occMask ??= new byte[n];

        // 1) Где область закрыта окнами, лежащими ПОВЕРХ дока, — там на экране не фон
        int covered = ComputeOcclusion(_occMask);
        if (covered >= n) return;                              // всё закрыто — снимать нечего

        // 2) Снимок экрана. Сама капсула в него не попадает (исключена из захвата).
        if (!_sc.Ensure(cw, ch)) return;
        IntPtr screenDC = Native.GetDC(IntPtr.Zero);
        bool ok;
        try { ok = Native.BitBlt(_sc.DC, 0, 0, cw, ch, screenDC, _capRect.X, _capRect.Y, 0x00CC0020); }   // SRCCOPY
        finally { Native.ReleaseDC(IntPtr.Zero, screenDC); }
        if (!ok) return;                                       // экран заблокирован, окно UAC и т.п.
        Native.GdiFlush();

        uint* src = (uint*)_sc.Bits;

        // Абсолютно чёрный кадр — экран заблокирован/выключен: фон не портим
        bool any = false;
        for (int i = 0; i < n; i += 3)
            if (_occMask[i] == 0 && (src[i] & 0xFFFFFFu) != 0) { any = true; break; }
        if (!any) return;

        // 3) Страховка: если в кадр попала сама капсула, исключение из захвата не работает —
        //    иначе док размывал бы сам себя по кругу.
        if (LooksLikeOwnFrame(src, cw))
        {
            if (++_selfSeen >= 3) DisableScreenCaptureFromWorker(L.T("Windows does not exclude the dock from screen capture", "Windows не исключает док из захвата экрана"));
            return;
        }
        _selfSeen = 0;

        // 4) Обновляем только открытые пиксели
        fixed (uint* dst = _screenLayer)
        fixed (byte* m = _occMask)
        {
            for (int i = 0; i < n; i++)
                if (m[i] == 0) dst[i] = src[i] | 0xFF000000u;
        }
    }

    /// <summary>Маска пикселей области захвата, закрытых окнами, которые лежат выше дока. Возвращает их число.</summary>
    private int ComputeOcclusion(byte[] mask)
    {
        Array.Clear(mask);
        _occRects.Clear();
        _occProc ??= OccludersProc;
        Native.EnumWindows(_occProc, IntPtr.Zero);            // окна перечисляются сверху вниз

        int cw = _capRect.Width;
        foreach (var r0 in _occRects)
        {
            var r = Rectangle.Intersect(r0, _capRect);
            if (r.Width <= 0 || r.Height <= 0) continue;
            int x0 = r.X - _capRect.X, y0 = r.Y - _capRect.Y;
            for (int y = 0; y < r.Height; y++)
                Array.Fill(mask, (byte)1, (y0 + y) * cw + x0, r.Width);
        }

        int covered = 0;
        for (int i = 0; i < mask.Length; i++) covered += mask[i];
        return covered;
    }

    private bool OccludersProc(IntPtr h, IntPtr lp)
    {
        if (h == _hwnd) return false;                          // дошли до дока: ниже только рабочий стол
        if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;

        const int WS_EX_TRANSPARENT         = 0x00000020;
        const int WS_EX_LAYERED             = 0x00080000;
        const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
        int ex = Native.GetWindowLong(h, GWL_EXSTYLE);

        // Прозрачные «сквозные» оверлеи (NVIDIA, Discord и т.п.) ничего не закрывают
        if ((ex & WS_EX_TRANSPARENT) != 0 && (ex & (WS_EX_LAYERED | WS_EX_NOREDIRECTIONBITMAP)) != 0) return true;
        if ((ex & WS_EX_LAYERED) != 0)
        {
            // Окна с попиксельной прозрачностью (UpdateLayeredWindow: Rainmeter, виджеты, питомцы)
            // GetLayeredWindowAttributes не отдают. Обычно они почти целиком прозрачные, и
            // сквозь них видно обои, поэтому за непрозрачные не считаем. Иначе один полноэкранный
            // скин Rainmeter навсегда «закрыл» бы весь фон.
            if (!Native.GetLayeredWindowAttributes(h, out _, out byte alpha, out uint flags)) return true;
            if ((flags & 0x1) != 0) return true;                // LWA_COLORKEY — с «дырами»
            if ((flags & 0x2) != 0 && alpha == 0) return true;  // LWA_ALPHA = 0 — полностью прозрачное
        }

        // Скрытые системой окна (другие рабочие столы, «спящие» UWP)
        if (Native.DwmGetWindowAttribute(h, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;   // DWMWA_CLOAKED

        // Видимые границы окна (без невидимой рамки для изменения размера)
        if (Native.DwmGetWindowAttribute(h, 9, out RECT r, Marshal.SizeOf<RECT>()) != 0)   // DWMWA_EXTENDED_FRAME_BOUNDS
        {
            r = new RECT();
            if (!Native.GetWindowRect(h, ref r)) return true;
        }

        var rc = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        if (rc.Width > 0 && rc.Height > 0 && rc.IntersectsWith(_capRect)) _occRects.Add(rc);
        return true;
    }

    /// <summary>Похож ли снимок на то, что нарисовал сам док (значит, исключение из захвата не сработало).</summary>
    private unsafe bool LooksLikeOwnFrame(uint* src, int cw)
    {
        if (_dibBits == IntPtr.Zero) return false;
        uint* own = (uint*)_dibBits;

        long sum = 0; int count = 0;
        // Только середина капсулы: без скруглённых концов и краёв сглаживания
        int endM = Thick / 2 + 2, sideM = 8;
        int mx = Horizontal ? endM : sideM, my = Horizontal ? sideM : endM;
        for (int y = my; y < BarH - my; y += 5)
        {
            int cy = _dockOffY + y;
            if (cy >= _capRect.Height) break;
            for (int x = mx; x < BarW - mx; x += 5)
            {
                int cx = _dockOffX + x;
                if (cx >= cw) break;
                int ci = cy * cw + cx;
                uint o = own[y * BarW + x];
                if (_occMask[ci] != 0 || (o >> 24) != 255 || _capsuleMaskAlpha[y * BarW + x] != 255) continue;
                sum += Dist(src[ci], o);
                count++;
            }
        }
        return count >= 200 && sum < count * 3L;
    }

    /// <summary>PrintWindow окна и вырезка области _capRect. Пиксели 0xFFRRGGBB.</summary>
    private unsafe bool CaptureWindowRegion(IntPtr hwnd, uint[] dst)
    {
        var wr = new RECT();
        if (!Native.GetWindowRect(hwnd, ref wr)) return false;

        int ww = wr.Right - wr.Left, wh = wr.Bottom - wr.Top;
        if (ww <= 0 || wh <= 0) return false;

        // Часть окна, попадающая в область захвата (окно рабочего стола может начинаться
        // не в 0,0 — например, если слева от основного монитора есть ещё один).
        int rx = _capRect.X - wr.Left, ry = _capRect.Y - wr.Top;
        var reg = Rectangle.Intersect(new Rectangle(rx, ry, _capRect.Width, _capRect.Height),
                                      new Rectangle(0, 0, ww, wh));
        if (reg.Width <= 0 || reg.Height <= 0) return false;

        // PrintWindow рисует окно от его левого верхнего угла, лишнее просто отсекается —
        // поэтому буфер нужен только до правого нижнего угла нашей области, а не на всё окно.
        int bw = reg.Right, bh = reg.Bottom;
        if ((long)bw * bh > 64L * 1024 * 1024) return false;
        if (!_pw.Ensure(bw, bh)) return false;

        uint* bits = (uint*)_pw.Bits;
        for (int y = reg.Top; y < reg.Bottom; y++)
            new Span<uint>(bits + (long)y * bw + reg.Left, reg.Width).Clear();

        bool ok = Native.PrintWindow(hwnd, _pw.DC, 0x2);           // PW_RENDERFULLCONTENT
        if (!ok) ok = Native.PrintWindow(hwnd, _pw.DC, 0);
        if (!ok) return false;
        Native.GdiFlush();

        Array.Clear(dst);
        int cw = _capRect.Width;
        int ox = reg.Left - rx, oy = reg.Top - ry;
        fixed (uint* d = dst)
        {
            for (int y = 0; y < reg.Height; y++)
            {
                uint* s = bits + (long)(reg.Top + y) * bw + reg.Left;
                uint* t = d + (long)(oy + y) * cw + ox;
                for (int x = 0; x < reg.Width; x++) t[x] = s[x] | 0xFF000000u;
            }
        }
        return true;
    }

    /// <summary>Wallpaper Engine / Lively: снять окно-хост обоев. false — живых обоев нет (или снять не удалось).</summary>
    private bool TryCaptureLiveWallpaper(IntPtr desk)
    {
        if (_liveSkip > 0) { _liveSkip--; return false; }

        IntPtr host = FindWallpaperHost(desk);
        if (host == IntPtr.Zero || !HasVisibleChild(host)) return false;

        // Тёмные обои — тоже обои: отбрасываем только пустой (чисто чёрный) кадр,
        // который бывает, когда PrintWindow не смог снять картинку.
        if (CaptureWindowRegion(host, _liveLayer) && HasContent(_liveLayer))
        {
            _liveFails = 0;
            return true;
        }

        // Не получается — пробуем реже (примерно раз в 5 секунд)
        if (++_liveFails >= 3) { _liveFails = 0; _liveSkip = 50; }
        return false;
    }

    /// <summary>
    /// Кладёт снимок ярлыков на обои. PrintWindow на новых Windows рисует ярлыки на пустом
    /// (чёрном) фоне — обоев там нет. Такой «ключевой» цвет превращаем в прозрачность
    /// (как «цвет в альфу» в редакторах) и подкладываем под ярлыки настоящие обои.
    /// Если же снимок уже содержит обои (старые Windows) — берём его как есть.
    /// </summary>
    private void CompositeDesktop(uint[] icons, uint[] wall, uint[] dst)
    {
        int  n     = dst.Length;
        uint keyBg = _wallKey;

        int byWall = 0, byBlack = 0, byBg = 0, samples = 0;
        for (int i = 0; i < n; i += 3)
        {
            samples++;
            uint p = icons[i], w = wall[i];
            if (((p ^ w) & 0xFFFFFFu) == 0) { byWall++; continue; }
            // Чистый чёрный там, где обои не чёрные, — фон снимка без обоев. Проверяем до допуска
            // ниже: на тёмных обоях чёрный фон снимка иначе «похож на обои», и обои пропадали.
            if ((p & 0xFFFFFFu) == 0) { byBlack++; continue; }
            if (Dist(p, w) <= 24) { byWall++; continue; }
            if (Dist(p, 0) <= 6 && Dist(w, 0) > 30) byBlack++;
            else if (keyBg != 0 && Dist(p, keyBg) <= 6 && Dist(w, keyBg) > 30) byBg++;
        }

        uint key      = byBg > byBlack ? keyBg : 0u;
        int  keyVotes = Math.Max(byBg, byBlack);
        if (keyVotes <= byWall || keyVotes * 20 < samples)
        {
            Array.Copy(icons, dst, n);                 // снимок уже с обоями
            return;
        }

        int kr = (int)((key >> 16) & 0xFF), kg = (int)((key >> 8) & 0xFF), kb = (int)(key & 0xFF);
        for (int i = 0; i < n; i++)
        {
            uint p  = icons[i];
            int  pr = (int)((p >> 16) & 0xFF), pg = (int)((p >> 8) & 0xFF), pb = (int)(p & 0xFF);

            // минимальная непрозрачность ярлыка, при которой он даёт такой пиксель поверх ключа
            int a = Math.Max(AlphaOver(pr, kr), Math.Max(AlphaOver(pg, kg), AlphaOver(pb, kb)));
            if (a >= 255) { dst[i] = p | 0xFF000000u; continue; }

            uint w   = wall[i];
            int  inv = 255 - a;
            int  r   = pr + Div255(((int)((w >> 16) & 0xFF) - kr) * inv);
            int  g   = pg + Div255(((int)((w >> 8)  & 0xFF) - kg) * inv);
            int  b   = pb + Div255(((int)( w        & 0xFF) - kb) * inv);
            dst[i] = 0xFF000000u | (uint)Clamp(r) << 16 | (uint)Clamp(g) << 8 | (uint)Clamp(b);
        }
    }

    private static int AlphaOver(int p, int k)
    {
        if (p > k) return k >= 255 ? 255 : ((p - k) * 255 + (254 - k)) / (255 - k);
        if (p < k) return k <= 0   ? 255 : ((k - p) * 255 + (k - 1)) / k;
        return 0;
    }

    private static int Div255(int v) => v >= 0 ? (v + 127) / 255 : -((-v + 127) / 255);
    private static int Clamp(int v)  => v < 0 ? 0 : v > 255 ? 255 : v;

    private static int Dist(uint a, uint b)
    {
        int dr = Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF));
        int dg = Math.Abs((int)((a >> 8)  & 0xFF) - (int)((b >> 8)  & 0xFF));
        int db = Math.Abs((int)( a        & 0xFF) - (int)( b        & 0xFF));
        return Math.Max(dr, Math.Max(dg, db));
    }

    private static uint[] Solid(int n, Color c)
    {
        var a = new uint[n];
        Array.Fill(a, 0xFF000000u | (uint)(c.R << 16 | c.G << 8 | c.B));
        return a;
    }

    /// <summary>Есть ли в кадре хоть что-то, кроме чистого чёрного (хотя бы 1% пикселей).</summary>
    private static bool HasContent(uint[] px)
    {
        int lit = 0, count = 0;
        for (int i = 0; i < px.Length; i += 7)
        {
            if ((px[i] & 0xFFFFFFu) != 0) lit++;
            count++;
        }
        return count > 0 && lit * 100 >= count;
    }

    internal static unsafe uint[] ToPixels(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var px   = new uint[w * h];
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            fixed (uint* dst = px)
            {
                for (int y = 0; y < h; y++)
                {
                    uint* s = (uint*)((byte*)data.Scan0 + y * data.Stride);
                    uint* d = dst + (long)y * w;
                    for (int x = 0; x < w; x++) d[x] = s[x] | 0xFF000000u;
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return px;
    }

    // ------------------------------------------------------------------------
    //  Размытие + оформление капсулы → готовый фон BarW×BarH
    // ------------------------------------------------------------------------
    // Буферы размытия: живут, пока не изменится размер (раньше — три новые картинки на каждый кадр)
    private Bitmap          _bbSmall, _bbUp;
    private Graphics        _bbUpG;
    private ImageAttributes _bbWrap;
    private byte[]          _bbTmp;

    /// <summary>Размытие + оформление капсулы → готовые пиксели фона (BarW×BarH) в dst. Фоновый поток.</summary>
    private unsafe void BuildBackground(uint[] src, BackdropMode mode, uint[] dst)
    {
        bool acrylic = mode == BackdropMode.Acrylic;
        int  cw = _capRect.Width, ch = _capRect.Height;
        int  sw = (cw + DownScale - 1) / DownScale;
        int  sh = (ch + DownScale - 1) / DownScale;

        // 1) Уменьшаем в DownScale раз и размываем
        if (_bbSmall == null || _bbSmall.Width != sw || _bbSmall.Height != sh)
        {
            _bbSmall?.Dispose();
            _bbSmall = new Bitmap(sw, sh, PixelFormat.Format32bppArgb);
        }
        var small = _bbSmall;
        Downsample(src, cw, ch, small, DownScale);

        int radius = acrylic ? AcrylicBlurRadius : BlurRadius;
        int passes = acrylic ? AcrylicBlurPasses : BlurPasses;
        if (_bbTmp == null || _bbTmp.Length < sw * sh * 4) _bbTmp = new byte[sw * sh * 4];
        for (int i = 0; i < passes; i++) ApplyBoxBlur(small, radius, _bbTmp);

        // 2) Увеличиваем обратно (бикубически, без тёмной каймы по краям) — сразу только кусок под
        //    капсулой: та же картинка, сдвинутая на её место, поэтому пиксели те же, что и раньше
        int uw = sw * DownScale, uh = sh * DownScale;
        if (_bbUp == null)
        {
            _bbUp  = new Bitmap(BarW, BarH, PixelFormat.Format32bppArgb);
            _bbUpG = Graphics.FromImage(_bbUp);
            _bbUpG.CompositingMode   = CompositingMode.SourceCopy;
            _bbUpG.InterpolationMode = InterpolationMode.HighQualityBicubic;
            _bbUpG.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            _bbWrap = new ImageAttributes();
            _bbWrap.SetWrapMode(WrapMode.TileFlipXY);
        }
        _bbUpG.Clear(Color.Black);
        _bbUpG.DrawImage(small, new Rectangle(-_dockOffX, -_dockOffY, uw, uh), 0, 0, sw, sh, GraphicsUnit.Pixel, _bbWrap);

        // Капсула за краем увеличенной картинки (бывает у края экрана) — как раньше, берём крайний пиксель
        int maxX = Math.Min(BarW, uw - _dockOffX) - 1, maxY = Math.Min(BarH, uh - _dockOffY) - 1;

        // 3) Красим
        uint  tint = acrylic ? AcrylicTintColor : (DockBgColor & 0xFFFFFF);
        float tr   = (tint >> 16) & 0xFF, tg = (tint >> 8) & 0xFF, tb = tint & 0xFF;
        float ta   = acrylic ? AcrylicTintOpacity : ((DockBgColor >> 24) & 0xFF) / 255f;

        var ud = _bbUp.LockBits(new Rectangle(0, 0, BarW, BarH), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            fixed (uint* dbase = dst)
            for (int y = 0; y < BarH; y++)
            {
                byte* srow = (byte*)ud.Scan0 + Math.Max(0, Math.Min(y, maxY)) * ud.Stride;
                uint* drow = dbase + y * BarW;

                for (int x = 0; x < BarW; x++)
                {
                    byte* p  = srow + Math.Max(0, Math.Min(x, maxX)) * 4;
                    float cb = p[0], cg = p[1], cr = p[2];

                    if (acrylic)
                    {
                        // насыщенность ×1.25
                        float l = 0.2126f * cr + 0.7152f * cg + 0.0722f * cb;
                        cr = Math.Clamp(l + (cr - l) * AcrylicSaturation, 0f, 255f);
                        cg = Math.Clamp(l + (cg - l) * AcrylicSaturation, 0f, 255f);
                        cb = Math.Clamp(l + (cb - l) * AcrylicSaturation, 0f, 255f);

                        // Exclusion с белым ≈10%: c + e·(255 − 2c)
                        cr += AcrylicExclusion * (255f - 2f * cr);
                        cg += AcrylicExclusion * (255f - 2f * cg);
                        cb += AcrylicExclusion * (255f - 2f * cb);
                    }

                    // тонировка — равномерно по всей капсуле, без затемнения у краёв
                    cr += (tr - cr) * ta;  cg += (tg - cg) * ta;  cb += (tb - cb) * ta;

                    int ir = (int)(cr + 0.5f), ig = (int)(cg + 0.5f), ib = (int)(cb + 0.5f);
                    if (acrylic)
                    {
                        int nz = _noise[y * BarW + x];     // зерно акрила
                        ir += nz; ig += nz; ib += nz;
                    }

                    drow[x] = 0xFF000000u | (uint)Clamp(ir) << 16 | (uint)Clamp(ig) << 8 | (uint)Clamp(ib);
                }
            }
        }
        finally { _bbUp.UnlockBits(ud); }
    }

    /// <summary>Уменьшение усреднением блоков f×f.</summary>
    private static unsafe void Downsample(uint[] src, int w, int h, Bitmap dst, int f)
    {
        int sw = dst.Width, sh = dst.Height;
        var d  = dst.LockBits(new Rectangle(0, 0, sw, sh), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < sh; y++)
            {
                uint* row = (uint*)((byte*)d.Scan0 + y * d.Stride);
                int   y0  = y * f, y1 = Math.Min(y0 + f, h);

                for (int x = 0; x < sw; x++)
                {
                    int x0 = x * f, x1 = Math.Min(x0 + f, w);
                    int r = 0, g = 0, b = 0, c = 0;

                    for (int yy = y0; yy < y1; yy++)
                    {
                        int o = yy * w;
                        for (int xx = x0; xx < x1; xx++)
                        {
                            uint p = src[o + xx];
                            r += (int)((p >> 16) & 0xFF);
                            g += (int)((p >> 8)  & 0xFF);
                            b += (int)( p        & 0xFF);
                            c++;
                        }
                    }

                    if (c == 0) c = 1;
                    row[x] = 0xFF000000u | (uint)(r / c) << 16 | (uint)(g / c) << 8 | (uint)(b / c);
                }
            }
        }
        finally { dst.UnlockBits(d); }
    }

    // ------------------------------------------------------------------------
    private static unsafe void ApplyBoxBlur(Bitmap bmp, int radius, byte[] tmp)
    {
        if (radius < 1 || bmp.Width < 2 || bmp.Height < 2) return;

        int w = bmp.Width, h = bmp.Height;
        int winSize = radius * 2 + 1;
        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        try
        {
            int stride = data.Stride;
            byte* src  = (byte*)data.Scan0;

            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                int sumB = src[row + 0] * (radius + 1);
                int sumG = src[row + 1] * (radius + 1);
                int sumR = src[row + 2] * (radius + 1);

                for (int i = 1; i <= radius; i++)
                {
                    int o = row + Math.Min(i, w - 1) * 4;
                    sumB += src[o + 0]; sumG += src[o + 1]; sumR += src[o + 2];
                }

                int t = y * w * 4;
                tmp[t + 0] = (byte)(sumB / winSize);
                tmp[t + 1] = (byte)(sumG / winSize);
                tmp[t + 2] = (byte)(sumR / winSize);
                tmp[t + 3] = 255;

                for (int x = 1; x < w; x++)
                {
                    int rm = x - 1 - radius; if (rm < 0) rm = 0;
                    int ad = x + radius;      if (ad >= w) ad = w - 1;
                    int or_ = row + rm * 4;
                    int oa  = row + ad * 4;
                    sumB += src[oa + 0] - src[or_ + 0];
                    sumG += src[oa + 1] - src[or_ + 1];
                    sumR += src[oa + 2] - src[or_ + 2];

                    int o = t + x * 4;
                    tmp[o + 0] = (byte)(sumB / winSize);
                    tmp[o + 1] = (byte)(sumG / winSize);
                    tmp[o + 2] = (byte)(sumR / winSize);
                    tmp[o + 3] = 255;
                }
            }

            for (int x = 0; x < w; x++)
            {
                int col = x * 4;
                int sumB = tmp[col + 0] * (radius + 1);
                int sumG = tmp[col + 1] * (radius + 1);
                int sumR = tmp[col + 2] * (radius + 1);

                for (int i = 1; i <= radius; i++)
                {
                    int o = (Math.Min(i, h - 1) * w + x) * 4;
                    sumB += tmp[o + 0]; sumG += tmp[o + 1]; sumR += tmp[o + 2];
                }

                src[col + 0] = (byte)(sumB / winSize);
                src[col + 1] = (byte)(sumG / winSize);
                src[col + 2] = (byte)(sumR / winSize);
                src[col + 3] = 255;

                for (int y = 1; y < h; y++)
                {
                    int rm = y - 1 - radius; if (rm < 0) rm = 0;
                    int ad = y + radius;      if (ad >= h) ad = h - 1;
                    int or_ = (rm * w + x) * 4;
                    int oa  = (ad * w + x) * 4;
                    sumB += tmp[oa + 0] - tmp[or_ + 0];
                    sumG += tmp[oa + 1] - tmp[or_ + 1];
                    sumR += tmp[oa + 2] - tmp[or_ + 2];

                    int o = y * stride + x * 4;
                    src[o + 0] = (byte)(sumB / winSize);
                    src[o + 1] = (byte)(sumG / winSize);
                    src[o + 2] = (byte)(sumR / winSize);
                    src[o + 3] = 255;
                }
            }
        }
        finally { bmp.UnlockBits(data); }
    }

    /// <summary>Зерно акрила. Фиксированное — не «кипит» при обновлении фона.</summary>
    private sbyte[] BuildNoise()
    {
        var rnd = new Random(20171017);
        var n   = new sbyte[BarW * BarH];
        for (int i = 0; i < n.Length; i++)
            n[i] = (sbyte)rnd.Next(-AcrylicNoiseAmp, AcrylicNoiseAmp + 1);
        return n;
    }

    // ========================================================================
    //  ГЛАВНЫЙ ЦИКЛ (рендер-поток)
    // ========================================================================
    /// <summary>Один шаг: анимации + кадр, если что-то изменилось. dt — секунды с прошлого шага (точные часы).
    /// true — что-то ещё движется, следующий кадр нужен сразу.</summary>
    private bool UpdateLoop(double dt)
    {
        // Новый фон от фонового потока; прежний буфер отдаём ему обратно
        var nb = Interlocked.Exchange(ref _bgPending, null);
        if (nb != null)
        {
            var old  = _bgFront;
            _bgFront = nb;
            if (old != null) Interlocked.Exchange(ref _bgSpare, old);
            Interlocked.Exchange(ref _renderRequest, 1);
        }

        bool moving;
        lock (_sync)
        {
            bool render = Interlocked.Exchange(ref _renderRequest, 0) != 0;
            moving = false;

            int wheel = Interlocked.Exchange(ref _wheelDelta, 0);
            if (wheel != 0)
            {
                _targetScroll -= wheel / 120.0 * ScrollStep;
                _targetScroll  = Math.Min(Math.Max(_targetScroll, 0), _maxScroll);
            }

            double diff = _targetScroll - _scrollPos;
            if (Math.Abs(diff) > 0.05)
            {
                double factor = 1.0 - Math.Exp(-dt * AnimSpeed * ScrollRateScale);
                _scrollPos += diff * factor;
                if (Math.Abs(_targetScroll - _scrollPos) <= 0.05) _scrollPos = _targetScroll;
                moving = true;
            }
            else if (_scrollPos != _targetScroll)
            {
                _scrollPos = _targetScroll;
                render = true;
            }

            if (UpdateDrag(dt))       moving = true;
            if (UpdateDropTarget(dt)) moving = true;
            if (AnimateItems(dt))     moving = true;
            if (_drag != null || _dropActive) moving = true;      // курсор нужно отслеживать каждый кадр

            if (render || moving) RenderFrame();

            UpdateTooltipFromRenderThread();
        }

        if (UpdateAutoHide(dt)) moving = true;
        return moving;
    }

    // ========================================================================
    //  АВТОСКРЫТИЕ: капсула уезжает за ближайший край экрана (вертикальная — за левый/правый,
    //  горизонтальная — за верхний/нижний) и выезжает, когда курсор попадает в полосу от её места
    //  до этого края. Двигается из рендер-потока — плавно, в такт с монитором.
    // ========================================================================
    private bool UpdateAutoHide(double dt)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double target;

        // Пока с капсулой что-то делают — она на месте
        bool hold = !_autoHide || _moving || _panning || _pressActive || _drag != null || _dropActive ||
                    _itemMenu != null || _app.SettingsOpen;
        if (hold)
        {
            target     = 0;
            _zoneEnter = -1;
            _zoneLeave = now;
        }
        else
        {
            UpdateHideTarget(now);
            if (_hideOff == Point.Empty) target = 0;                  // уезжать некуда (край общий с другим монитором)
            else if (_hideZone.Contains(CursorPos))
            {
                if (_zoneEnter < 0) _zoneEnter = now;
                _zoneLeave = now;
                // Спрятана целиком — ждём задержку появления; уже выглядывает — сразу назад
                target = _hideT < 1 || now - _zoneEnter >= _ahShowDelaySec ? 0 : 1;
            }
            else
            {
                _zoneEnter = -1;
                target = now - _zoneLeave >= _ahDelaySec ? 1 : 0;     // ушли из зоны — уехать через задержку
            }
        }

        if (_hideT != target)
        {
            if (target < _hideT) RequestBackdropRefresh(false);       // выезжает — свежий фон сразу
            double dur  = target > _hideT ? _ahHideSec : _ahShowSec;
            double step = dur <= 0 ? 1 : dt / dur;
            _hideT = target > _hideT ? Math.Min(target, _hideT + step) : Math.Max(target, _hideT - step);
        }

        double e  = _hideT * _hideT * (3 - 2 * _hideT);              // плавно трогается и плавно останавливается
        int    sx = (int)Math.Round(_hideOff.X * e), sy = (int)Math.Round(_hideOff.Y * e);
        if (sx != _slideX || sy != _slideY)
        {
            _slideX = sx;
            _slideY = sy;
            // Асинхронно: рендер-поток не ждёт UI-поток (а тот может ждать блокировку рендера)
            Native.SetWindowPos(_hwnd, IntPtr.Zero, DockX + sx, DockY + sy, 0, 0,
                                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
        }

        bool busy = _hideT != target;
        _slideBusy   = busy || (_hideT > 0 && _hideT < 1);
        _fullyHidden = !_slideBusy && _hideT >= 1 && _ahPeek == 0;
        return busy;
    }

    /// <summary>Куда уезжать и где «зона вызова». Пересчёт — когда капсулу передвинули (и раз в 2 с — мониторы).</summary>
    private void UpdateHideTarget(double now)
    {
        int hx = DockX, hy = DockY;
        if (hx == _hideKeyX && hy == _hideKeyY && now - _hideKeyTime < 2) return;
        _hideKeyX = hx; _hideKeyY = hy; _hideKeyTime = now;

        var home = new Rectangle(hx, hy, BarW, BarH);
        var scr  = Screen.FromPoint(new Point(hx + BarW / 2, hy + BarH / 2)).Bounds;
        var others = Screen.AllScreens.Select(s => s.Bounds).Where(b => b != scr).ToList();

        // Два края поперёк капсулы; сначала ближний. Край, за которым другой монитор, не подходит —
        // капсула не спряталась бы, а переехала на соседний экран.
        var options = Horizontal
            ? new[]
              {
                  (Dist: home.Top - scr.Top,        Off: new Point(0, scr.Top - BarH + _ahPeek - hy), Zone: Rectangle.FromLTRB(home.Left, scr.Top, home.Right, home.Bottom)),
                  (Dist: scr.Bottom - home.Bottom,  Off: new Point(0, scr.Bottom - _ahPeek - hy),     Zone: Rectangle.FromLTRB(home.Left, home.Top, home.Right, scr.Bottom))
              }
            : new[]
              {
                  (Dist: home.Left - scr.Left,      Off: new Point(scr.Left - BarW + _ahPeek - hx, 0), Zone: Rectangle.FromLTRB(scr.Left, home.Top, home.Right, home.Bottom)),
                  (Dist: scr.Right - home.Right,    Off: new Point(scr.Right - _ahPeek - hx, 0),       Zone: Rectangle.FromLTRB(home.Left, home.Top, scr.Right, home.Bottom))
              };

        _hideOff  = Point.Empty;
        _hideZone = Rectangle.Empty;
        foreach (var o in options.OrderBy(o => o.Dist))
        {
            var hidden = new Rectangle(hx + o.Off.X, hy + o.Off.Y, BarW, BarH);
            if (others.Any(b => b.IntersectsWith(hidden))) continue;
            _hideOff  = o.Off;
            _hideZone = o.Zone;
            break;
        }
    }

    /// <summary>Автопрокрутка, когда что-то тянут к началу или концу капсулы. ly — координата вдоль капсулы. Под _sync.</summary>
    private bool AutoScrollNearEdges(double ly, double maxScroll, double dt)
    {
        if (maxScroll <= 0 || AutoScrollZone <= 0) return false;

        double v = 0;
        if (ly < AutoScrollZone)
            v = -AutoScrollSpeed * Math.Min(1.0, (AutoScrollZone - ly) / AutoScrollZone);
        else if (ly > DrawLen - AutoScrollZone)
            v =  AutoScrollSpeed * Math.Min(1.0, (ly - (DrawLen - AutoScrollZone)) / AutoScrollZone);
        if (v == 0) return false;

        double ns = Math.Min(Math.Max(_scrollPos + v * dt, 0), maxScroll);
        if (ns == _scrollPos) return false;
        _scrollPos = _targetScroll = ns;
        return true;
    }

    /// <summary>Перетаскивание: иконка едет за курсором, остальные расступаются. Под _sync.</summary>
    private bool UpdateDrag(double dt)
    {
        var d = _drag;
        if (d == null) { _noButtonFrames = 0; return false; }

        int idx = _files.IndexOf(d.Item);
        if (idx < 0) { QueueEndDrag(); return false; }

        // Кнопку отпустили, а WM_LBUTTONUP почему-то не пришёл — завершаем сами.
        if (!IsPrimaryButtonDown()) { if (++_noButtonFrames > 30) QueueEndDrag(); }
        else _noButtonFrames = 0;

        double ly      = MainLocal(Cursor.Position);
        bool   changed = AutoScrollNearEdges(ly, _maxScroll, dt);

        double top = Math.Min(Math.Max(ly - d.GrabOffset, 0), DrawLen - IconSize);
        if (Math.Abs(top - d.Top) > 0.01) { d.Top = top; changed = true; }
        d.Item.AnimY = d.Top + _scrollPos;

        // Новое место — по центру перетаскиваемой иконки
        int target = (int)Math.Round((d.Item.AnimY - Pad) / Step);
        target = Math.Min(Math.Max(target, 0), _files.Count - 1);
        if (target != idx)
        {
            _files.RemoveAt(idx);
            _files.Insert(target, d.Item);
            changed = true;
        }
        return changed;
    }

    /// <summary>Над капсулой держат файлы: куда их вставить (по курсору) + автопрокрутка у краёв. Под _sync.</summary>
    private bool UpdateDropTarget(double dt)
    {
        if (!_dropActive) return false;

        double ly      = MainLocal(Cursor.Position);
        bool   changed = AutoScrollNearEdges(ly, MaxScrollFor(_files.Count + 1), dt);

        int idx = DropIndexAt(ly);
        if (idx != _dropIndex) { _dropIndex = idx; changed = true; }
        return changed;
    }

    /// <summary>Номер, на который встанет брошенный файл: между ярлыками, ближайшими к точке ly (вдоль капсулы). Под _sync.</summary>
    private int DropIndexAt(double ly)
    {
        double content = ly + _scrollPos;
        int    idx     = (int)Math.Floor((content - (Pad + IconSize / 2.0)) / Step) + 1;
        return Math.Min(Math.Max(idx, 0), _files.Count);
    }

    /// <summary>Плавно подвигает ярлыки к их местам и анимирует «подъём». Под _sync.</summary>
    private bool AnimateItems(double dt)
    {
        bool   moving   = false;
        double k        = 1.0 - Math.Exp(-dt * ReorderAnimSpeed);
        var    dragItem = _drag?.Item;

        for (int i = 0; i < _files.Count; i++)
        {
            var it = _files[i];

            double liftTarget = it == dragItem ? 1.0 : 0.0;
            if (it.Lift != liftTarget)
            {
                it.Lift += (liftTarget - it.Lift) * k;
                if (Math.Abs(liftTarget - it.Lift) < 0.01) it.Lift = liftTarget;
                moving = true;
            }

            if (it == dragItem) continue;

            // Над капсулой держат файл — ярлыки расступаются, освобождая место под него
            int    slot   = _dropIndex >= 0 && i >= _dropIndex ? i + 1 : i;
            double target = Pad + slot * Step;
            double diff   = target - it.AnimY;
            if (diff != 0)
            {
                if (Math.Abs(diff) < 0.3) it.AnimY = target;
                else                      it.AnimY += diff * k;
                moving = true;
            }
        }
        return moving;
    }

    // ========================================================================
    //  РЕНДЕР КАДРА
    // ========================================================================
    /// <summary>
    /// Кадр рисуется прямо в пиксели окна: фон — копированием, ярлыки 1:1 — смешиванием по альфе,
    /// маска капсулы — только по краям. GDI+ остался лишь для «поднятых» ярлыков (увеличение и тень).
    /// Раньше всё шло через GDI+ в отдельный холст и потом ещё раз копировалось в окно.
    /// </summary>
    private unsafe void RenderFrame()
    {
        uint* dst = (uint*)_dibBits;
        int   n   = BarW * BarH;

        // Фон
        var bg = _bgFront;
        if (bg != null && bg.Length == n) fixed (uint* s = bg) Buffer.MemoryCopy(s, dst, n * 4L, n * 4L);
        else new Span<uint>(dst, n).Fill(0xFF1A1A1A);

        // Сначала ярлыки, лежащие на месте…
        bool anyLifted = false;
        for (int i = 0; i < _files.Count; i++)
        {
            var item = _files[i];
            if (item.Lift > 0) { anyLifted = true; continue; }
            if (item.Px == null) continue;

            int pos = (int)Math.Round(item.AnimY - _scrollPos);
            if (pos + IconSize < 0 || pos > DrawLen) continue;

            if (Horizontal) BlendIcon(dst, item.Px, pos, item.Cross);
            else            BlendIcon(dst, item.Px, item.Cross, pos);
        }

        // …потом поднятые (перетаскиваемый и тот, что «приземляется») — поверх остальных
        if (anyLifted)
            for (int i = 0; i < _files.Count; i++)
            {
                var item = _files[i];
                if (item.Lift > 0 && item.Icon != null) DrawLifted(item);
            }

        // Форма капсулы: скруглённые концы и сглаженный край
        var idx = _maskEdgeIdx;
        var val = _maskEdgeVal;
        for (int k = 0; k < idx.Length; k++)
        {
            uint m = val[k];
            if (m == 0) { dst[idx[k]] = 0; continue; }
            uint p = dst[idx[k]];                                  // как и раньше: c·m/255 с отбрасыванием
            dst[idx[k]] = ((p >> 24) & 0xFF) * m / 255 << 24 | ((p >> 16) & 0xFF) * m / 255 << 16 |
                          ((p >> 8) & 0xFF) * m / 255 << 8 | (p & 0xFF) * m / 255;
        }

        PushToLayered();
    }

    /// <summary>
    /// Иконка (premultiplied ARGB, IconSize×IconSize) поверх непрозрачных пикселей окна в (x, y), с обрезкой
    /// по краям. Полупрозрачные пиксели смешиваются так же, как раньше это делал GDI+ (см. GdiBlend), —
    /// картинка та же, но в ~18 раз быстрее.
    /// </summary>
    private unsafe void BlendIcon(uint* dst, uint[] src, int x, int y)
    {
        int x0 = Math.Max(0, -x), y0 = Math.Max(0, -y);
        int x1 = Math.Min(IconSize, BarW - x), y1 = Math.Min(IconSize, BarH - y);
        if (x0 >= x1 || y0 >= y1) return;

        fixed (uint* sbase = src)
        for (int iy = y0; iy < y1; iy++)
        {
            uint* s = sbase + iy * IconSize;
            uint* d = dst + (y + iy) * BarW + x;
            for (int ix = x0; ix < x1; ix++)
            {
                uint sp = s[ix];
                uint a  = sp >> 24;
                if (a == 0) continue;
                if (a == 255) { d[ix] = sp; continue; }
                uint dp = d[ix];
                d[ix] = 0xFF000000u |
                        (uint)GdiBlend.Channel((sp >> 16) & 0xFF, a, (dp >> 16) & 0xFF) << 16 |
                        (uint)GdiBlend.Channel((sp >> 8)  & 0xFF, a, (dp >> 8)  & 0xFF) << 8 |
                        (uint)GdiBlend.Channel( sp        & 0xFF, a,  dp        & 0xFF);
            }
        }
    }

    /// <summary>Список пикселей, где маска капсулы не 255, — только их и надо обрабатывать каждый кадр.</summary>
    private void BuildMaskEdges()
    {
        var idx = new List<int>();
        var val = new List<byte>();
        for (int i = 0; i < _capsuleMaskAlpha.Length; i++)
            if (_capsuleMaskAlpha[i] != 255) { idx.Add(i); val.Add(_capsuleMaskAlpha[i]); }
        _maskEdgeIdx = idx.ToArray();
        _maskEdgeVal = val.ToArray();
    }

    private void DrawLifted(DockItem item)
    {
        double lift = item.Lift;
        float  size = (float)(IconSize * (1.0 + LiftScale * lift));
        double main = item.AnimY - _scrollPos + IconSize / 2.0;
        if (main + size < 0 || main - size > DrawLen) return;
        var    c    = ToWindow(main, Thick / 2.0);
        float  cx   = c.X, cy = c.Y;

        item.Shadow ??= BuildShadow(item.Icon);
        if (item.Shadow != null)
        {
            float k  = size / IconSize;
            float sw = item.Shadow.Width * k, sh = item.Shadow.Height * k;
            var   dr = new Rectangle((int)Math.Round(cx - sw / 2f),
                                     (int)Math.Round(cy - sh / 2f + 3f * (float)lift),
                                     (int)Math.Round(sw), (int)Math.Round(sh));
            _shadowAttrs.SetColorMatrix(new ColorMatrix { Matrix33 = (float)lift });
            _dibG.DrawImage(item.Shadow, dr, 0, 0, item.Shadow.Width, item.Shadow.Height,
                            GraphicsUnit.Pixel, _shadowAttrs);
        }

        _dibG.DrawImage(item.Icon, cx - size / 2f, cy - size / 2f, size, size);
    }

    /// <summary>Мягкая тень по силуэту иконки.</summary>
    private unsafe Bitmap BuildShadow(Bitmap icon)
    {
        const int S = 8;
        try
        {
            int iw = icon.Width, ih = icon.Height;
            int w  = iw + S * 2, h = ih + S * 2;
            var a  = new byte[w * h];

            var sd = icon.LockBits(new Rectangle(0, 0, iw, ih), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < ih; y++)
                {
                    byte* row = (byte*)sd.Scan0 + y * sd.Stride;
                    for (int x = 0; x < iw; x++) a[(y + S) * w + x + S] = row[x * 4 + 3];
                }
            }
            finally { icon.UnlockBits(sd); }

            BlurAlpha(a, w, h, 3);
            BlurAlpha(a, w, h, 3);

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
            var dd  = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    uint* row = (uint*)((byte*)dd.Scan0 + y * dd.Stride);
                    for (int x = 0; x < w; x++)
                        row[x] = (uint)(a[y * w + x] * ShadowOpacity) << 24;   // чёрный, premultiplied
                }
            }
            finally { bmp.UnlockBits(dd); }
            return bmp;
        }
        catch { return null; }
    }

    private static void BlurAlpha(byte[] a, int w, int h, int r)
    {
        var tmp = new byte[a.Length];
        int win = r * 2 + 1;

        for (int y = 0; y < h; y++)
        {
            int row = y * w, sum = 0;
            for (int x = -r; x <= r; x++) if (x >= 0 && x < w) sum += a[row + x];
            for (int x = 0; x < w; x++)
            {
                tmp[row + x] = (byte)(sum / win);
                int add = x + r + 1, rem = x - r;
                if (add < w)  sum += a[row + add];
                if (rem >= 0) sum -= a[row + rem];
            }
        }

        for (int x = 0; x < w; x++)
        {
            int sum = 0;
            for (int y = -r; y <= r; y++) if (y >= 0 && y < h) sum += tmp[y * w + x];
            for (int y = 0; y < h; y++)
            {
                a[y * w + x] = (byte)(sum / win);
                int add = y + r + 1, rem = y - r;
                if (add < h)  sum += tmp[add * w + x];
                if (rem >= 0) sum -= tmp[rem * w + x];
            }
        }
    }

    // ========================================================================
    //  DIB + UpdateLayeredWindow + маска капсулы
    // ========================================================================
    private void InitDibSection()
    {
        IntPtr screenDC = Native.GetDC(IntPtr.Zero);
        try { _dibDC = Native.CreateCompatibleDC(screenDC); }
        finally { Native.ReleaseDC(IntPtr.Zero, screenDC); }

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize        = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth       = BarW,
                biHeight      = -BarH,
                biPlanes      = 1,
                biBitCount    = 32,
                biCompression = 0
            }
        };

        _dibSection = Native.CreateDIBSection(_dibDC, ref bmi, 0, out _dibBits, IntPtr.Zero, 0);
        Native.SelectObject(_dibDC, _dibSection);
    }

    // ------------------------------------------------------------------------
    /// <summary>Показать пиксели окна (они уже готовы в DIB).</summary>
    private void PushToLayered()
    {
        var size  = new SIZE  { cx = BarW, cy = BarH };
        var srcPt = new POINT { X = 0, Y = 0 };

        var blend = new BLENDFUNCTION
        {
            BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1
        };

        // Позицию не передаём: её меняет только UI-поток (SetWindowPos), иначе при
        // перетаскивании капсулы рендер-поток возвращал бы её на старое место.
        IntPtr screenDC = Native.GetDC(IntPtr.Zero);
        try
        {
            Native.UpdateLayeredWindow(_hwnd, screenDC, IntPtr.Zero, ref size,
                                       _dibDC, ref srcPt, 0, ref blend, 2);
        }
        finally { Native.ReleaseDC(IntPtr.Zero, screenDC); }
    }

    // ========================================================================
    //  КАПСУЛА + МАСКА
    // ========================================================================
    private GraphicsPath BuildCapsulePath()
    {
        var   path = new GraphicsPath();
        float t    = Thick, r = Thick / 2f;
        if (Horizontal)
        {
            path.AddArc(0, 0, t, t, 90, 180);                 // левый полукруг
            path.AddLine(r, 0, DrawLen - r, 0);
            path.AddArc(DrawLen - t, 0, t, t, 270, 180);      // правый полукруг
        }
        else
        {
            path.AddArc(0, 0, t, t, 180, 180);                // верхний полукруг
            path.AddLine(t, r, t, DrawLen - r);
            path.AddArc(0, DrawLen - t, t, t, 0, 180);        // нижний полукруг
        }
        path.CloseFigure();
        return path;
    }

    private unsafe byte[] BuildCapsuleMaskAlpha()
    {
        using var bmp = new Bitmap(BarW, BarH, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var br = new SolidBrush(Color.White);
            using var path = BuildCapsulePath();
            g.FillPath(br, path);
        }

        var bytes = new byte[BarW * BarH];
        var data = bmp.LockBits(new Rectangle(0, 0, BarW, BarH),
                                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte* scan = (byte*)data.Scan0;
            for (int y = 0; y < BarH; y++)
            {
                byte* row = scan + y * data.Stride;
                int off = y * BarW;
                for (int x = 0; x < BarW; x++)
                    bytes[off + x] = row[x * 4 + 3];
            }
        }
        finally { bmp.UnlockBits(data); }
        return bytes;
    }

    // ========================================================================
    //  ИКОНКИ
    // ========================================================================
    /// <summary>
    /// Ярлыки в сохранённом порядке (новые — в конец): содержимое папки + запомненные файлы,
    /// которые перетащили в капсулу без переноса. Ключ порядка: у файлов из папки — имя,
    /// у запомненных — полный путь.
    /// </summary>
    private List<DockItem> LoadItems()
    {
        var result  = new List<DockItem>();
        var entries = new List<(string Path, string Key, bool External)>();
        var names   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hidden  = new HashSet<string>(_settings.HiddenItems, StringComparer.OrdinalIgnoreCase);   // скрыты в капсуле

        foreach (var folder in ShortcutFolders())
        {
            try
            {
                foreach (var p in Directory.GetDirectories(folder).Concat(Directory.GetFiles(folder)))
                {
                    string name = Path.GetFileName(p);
                    if (string.IsNullOrEmpty(name) || !names.Add(name) || hidden.Contains(name) || IsHiddenOrSystem(p)) continue;
                    entries.Add((p, name, false));
                    seen.Add(p);
                }
            }
            catch { }
        }

        // Запомненные файлы: несуществующие пропускаем, но из списка не удаляем —
        // флешку или сетевой диск могли просто отключить.
        foreach (var p in _settings.ExtraItems)
            if (!string.IsNullOrEmpty(p) && seen.Add(p) && (File.Exists(p) || Directory.Exists(p)))
                entries.Add((p, p, true));

        var rank  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var saved = _settings.Order ?? new List<string>();
        for (int i = 0; i < saved.Count; i++)
            if (!string.IsNullOrEmpty(saved[i])) rank.TryAdd(saved[i], i);

        var ordered = entries
            .Select((e, i) => (e, i))
            .OrderBy(t => rank.TryGetValue(t.e.Key, out int r) ? r : int.MaxValue)
            .ThenBy(t => t.i)
            .Select(t => t.e);

        foreach (var e in ordered)
        {
            var item = CreateItem(e.Path, e.Key, e.External);
            if (item != null) result.Add(item);
        }

        for (int i = 0; i < result.Count; i++) result[i].AnimY = Pad + i * Step;
        return result;
    }

    /// <summary>Откуда берутся ярлыки. Рабочий стол Windows = личная папка + общая («для всех пользователей»).</summary>
    private List<string> ShortcutFolders()
    {
        var folders = new List<string>();
        if (!Directory.Exists(ShortcutsFolder)) return folders;
        folders.Add(ShortcutsFolder);
        if (SamePath(ShortcutsFolder, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)))
        {
            string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(common) && Directory.Exists(common)) folders.Add(common);
        }
        return folders;
    }

    private static bool IsHiddenOrSystem(string path)
    {
        try { return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0; }
        catch { return true; }
    }

    /// <summary>Ярлык для файла или папки (иконка грузится здесь — вызывать вне _sync). null — иконку не достать.</summary>
    private DockItem CreateItem(string path, string key, bool external)
    {
        string name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) return null;

        Image raw = LoadHighResIcon(path) ?? LoadFallbackIcon(path);
        if (raw == null) return null;
        Bitmap icon = NormalizeIcon(raw, IconSize);
        raw.Dispose();
        if (icon == null) return null;

        // Расширение прячем только у файлов: у папки «My.Project» точка — часть имени
        string display = Directory.Exists(path) ? name : Path.GetFileNameWithoutExtension(name);
        if (string.IsNullOrEmpty(display)) display = name;

        return new DockItem
        {
            Name     = display,
            Path     = path,
            Key      = key,
            External = external,
            Icon     = icon,
            Px       = PremultipliedPixels(icon),
            Cross    = (Thick - IconSize) / 2
        };
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                                 StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Подменяет список ярлыков (старые иконки освобождаются).</summary>
    private void ApplyItems(List<DockItem> items)
    {
        List<DockItem> old;
        lock (_sync)
        {
            old = new List<DockItem>(_files);
            _files.Clear();
            _files.AddRange(items);
            _drag         = null;
            _scrollPos    = 0;
            _targetScroll = 0;

            _maxScroll = MaxScrollFor(_files.Count);
        }

        foreach (var f in old) { f.Icon?.Dispose(); f.Shadow?.Dispose(); }
        RequestRender();
    }

    // ========================================================================
    //  ФАЙЛЫ, ПЕРЕТАЩЕННЫЕ В КАПСУЛУ (из Проводника и т.п.)
    // ========================================================================
    private DragDropEffects DropEffectFor(DragEventArgs e)
    {
        if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return DragDropEffects.None;
        // Курсор подсказывает, что будет: перенос в папку или «ссылка» (файл остаётся на месте)
        var want = _settings.MoveDroppedFiles ? DragDropEffects.Move : DragDropEffects.Link;
        if ((e.AllowedEffect & want) != 0) return want;
        if ((e.AllowedEffect & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
        return e.AllowedEffect & (DragDropEffects.Move | DragDropEffects.Link);
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Effect    = DropEffectFor(e);
        _dropActive = e.Effect != DragDropEffects.None;
        if (_dropActive) PostHideTooltip();
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effect    = DropEffectFor(e);
        _dropActive = e.Effect != DragDropEffects.None;   // место вставки считает рендер-поток по курсору
    }

    protected override void OnDragLeave(EventArgs e)
    {
        base.OnDragLeave(e);
        _dropActive = false;
        lock (_sync) _dropIndex = -1;                     // ярлыки съезжаются обратно
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        _dropActive = false;

        var paths = e.Data?.GetData(DataFormats.FileDrop) as string[];
        int index;
        lock (_sync)
        {
            if (_dropIndex < 0) _dropIndex = DropIndexAt(MainLocal(Cursor.Position));
            index = _dropIndex;                            // место остаётся раздвинутым, пока файлы не встанут
        }

        // Перенос делаем сами — источнику (Проводнику) удалять нечего
        e.Effect = _settings.MoveDroppedFiles ? DragDropEffects.None : DragDropEffects.Link;

        // Обрабатываем после того, как Проводник получит ответ (перенос может быть долгим)
        if (paths == null || paths.Length == 0) { lock (_sync) _dropIndex = -1; return; }
        BeginInvoke((Action)(() => AddDroppedFiles(paths, index)));
    }

    /// <summary>Ставит брошенные файлы в капсулу на место index (и переносит их в папку, если так настроено).</summary>
    private void AddDroppedFiles(string[] paths, int index)
    {
        bool move    = _settings.MoveDroppedFiles;
        var  added   = new List<(string Path, string Key, bool External)>();
        var  moved   = new List<string>();     // откуда перенесли (старый запомненный ярлык убрать)
        var  failed  = new List<string>();
        var  folders = ShortcutFolders();

        foreach (var raw in paths)
        {
            string p;
            try { p = Path.GetFullPath(raw); } catch { continue; }
            string name = Path.GetFileName(p.TrimEnd('\\'));
            if (string.IsNullOrEmpty(name) || (!File.Exists(p) && !Directory.Exists(p))) continue;   // диск целиком и т.п.

            // Уже лежит в папке с ярлыками — только переставляем
            if (folders.Any(f => SamePath(Path.GetDirectoryName(p), f)))
            {
                added.Add((p, name, false));
                continue;
            }

            if (move)
            {
                string dest = UniquePath(ShortcutsFolder, name);
                if (ShellMove(p, dest))
                {
                    _settings.ExtraItems.RemoveAll(x => SamePath(x, p));
                    moved.Add(p);
                    added.Add((dest, Path.GetFileName(dest), false));
                }
                else failed.Add(name);
            }
            else
            {
                if (!_settings.ExtraItems.Any(x => SamePath(x, p))) _settings.ExtraItems.Add(p);
                added.Add((p, p, true));
            }
        }

        // Скрытый ярлык бросили в капсулу снова — показываем его
        foreach (var a in added) _settings.HiddenItems.RemoveAll(k => string.Equals(k, a.Key, StringComparison.OrdinalIgnoreCase));

        // Уже есть в капсуле — переставляем его; нового — создаём (иконку грузим вне блокировки)
        var fresh   = new List<DockItem>();
        var created = new HashSet<DockItem>();
        foreach (var a in added)
        {
            if (fresh.Any(f => SamePath(f.Path, a.Path))) continue;          // один файл дважды в списке
            DockItem existing;
            lock (_sync) existing = _files.FirstOrDefault(f => SamePath(f.Path, a.Path));
            if (existing != null) { fresh.Add(existing); continue; }

            var item = CreateItem(a.Path, a.Key, a.External);
            if (item != null) { fresh.Add(item); created.Add(item); }
        }

        List<string> order;
        var dispose = new List<DockItem>();
        lock (_sync)
        {
            // Старые ярлыки перенесённых файлов (были запомнены по старому пути)
            foreach (var src in moved)
            {
                var old = _files.FirstOrDefault(f => SamePath(f.Path, src));
                if (old == null) continue;
                if (_files.IndexOf(old) < index) index--;
                _files.Remove(old);
                dispose.Add(old);
            }

            index = Math.Min(Math.Max(index, 0), _files.Count);
            foreach (var it in fresh)
            {
                int at = _files.IndexOf(it);
                if (at < 0) continue;
                _files.RemoveAt(at);               // уже был в капсуле — переезжает на новое место
                if (at < index) index--;
            }

            // Новые появляются прямо в раздвинутой щели, переставленные доезжают туда сами
            for (int i = 0; i < fresh.Count; i++)
                if (created.Contains(fresh[i])) fresh[i].AnimY = Pad + (index + i) * Step;
            _files.InsertRange(index, fresh);

            _dropIndex = -1;
            _maxScroll = MaxScrollFor(_files.Count);
            order      = _files.Select(f => f.Key).ToList();
        }
        foreach (var d in dispose) { d.Icon?.Dispose(); d.Shadow?.Dispose(); }

        _settings.Order = order;
        _settings.Save();
        RequestRender();

        if (failed.Count > 0)
            _app.ShowTrayMessage(L.T("Could not move to the shortcuts folder: ", "Не удалось перенести в папку с ярлыками: ") +
                            string.Join(", ", failed));
    }

    /// <summary>Свободное имя в папке: «name.ext», «name (2).ext», …</summary>
    private static string UniquePath(string folder, string name)
    {
        string dest = Path.Combine(folder, name);
        if (!File.Exists(dest) && !Directory.Exists(dest)) return dest;

        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            dest = Path.Combine(folder, $"{stem} ({i}){ext}");
            if (!File.Exists(dest) && !Directory.Exists(dest)) return dest;
        }
    }

    /// <summary>Перенос как в Проводнике: между дисками, с окном прогресса и отменой по Ctrl+Z.</summary>
    private static bool ShellMove(string from, string to)
    {
        const uint   FO_MOVE           = 0x0001;
        const ushort FOF_NOCONFIRMMKDIR = 0x0200;
        const ushort FOF_ALLOWUNDO     = 0x0040;
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc  = FO_MOVE,
                pFrom  = from + "\0",            // + завершающий ноль от маршалинга = двойной ноль
                pTo    = to + "\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR
            };
            int rc = Native.SHFileOperationW(ref op);
            return rc == 0 && !op.fAnyOperationsAborted && (File.Exists(to) || Directory.Exists(to));
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------------
    //  Правый клик: меню ярлыка (открыть, показать в папке, скрыть) или самой капсулы
    // ------------------------------------------------------------------------
    private ContextMenuStrip _itemMenu;

    private void ShowItemMenu(int lx, int ly)
    {
        if (_drag != null || _moving) return;
        if (!IsPointInDock(lx + WinX, ly + WinY)) return;

        DockItem hit = null;
        lock (_sync) { int idx = NearestIndex(lx, ly); if (idx >= 0) hit = _files[idx]; }

        PostHideTooltip();
        _itemMenu?.Close();
        var menu = new ContextMenuStrip();

        if (hit != null)
        {
            var open = new ToolStripMenuItem(L.T("Open", "Открыть"), null, (_, __) => Launch(hit.Path));
            open.Font = new Font(open.Font, FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add(L.T("Show in folder", "Показать в папке"), null, (_, __) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + hit.Path + "\"") { UseShellExecute = true })?.Dispose(); }
                catch { }
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Hide from the dock (the file is not touched)", "Скрыть из капсулы (сам файл не трогается)"),
                           null, (_, __) => HideItem(hit));
        }
        else
        {
            menu.Items.Add(L.T("Settings…", "Настройки…"), null, (_, __) => _app.ShowSettings(_settings));
            menu.Items.Add(L.T("Reload shortcuts", "Обновить ярлыки"), null, (_, __) => ReloadAll());
            menu.Items.Add(L.T("Reset shortcut order", "Сбросить порядок ярлыков"), null, (_, __) => ResetOrder());
        }

        _itemMenu = menu;
        menu.Closed += (_, __) => BeginInvoke((Action)(() =>
        {
            if (_itemMenu == menu) _itemMenu = null;
            menu.Dispose();
        }));
        _app.ShowContextMenu(menu, Cursor.Position);
    }

    /// <summary>Ярлык, ближайший к точке вдоль капсулы (с половиной промежутка с каждой стороны). Под _sync.</summary>
    private int NearestIndex(int localX, int localY)
    {
        double main = Horizontal ? localX : localY;
        int    best = -1;
        double bestD = Step / 2.0 + 1;
        for (int i = 0; i < _files.Count; i++)
        {
            double d = Math.Abs(main - (_files[i].AnimY - _scrollPos + IconSize / 2.0));
            if (d <= bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Убирает ярлык из капсулы, ничего не делая с файлом. Файл из папки запоминается как скрытый
    /// (вернётся по «Сбросить порядок ярлыков»), запомненный файл просто забывается.
    /// </summary>
    private void HideItem(DockItem item)
    {
        List<string> order;
        lock (_sync)
        {
            if (!_files.Remove(item)) return;
            _maxScroll    = MaxScrollFor(_files.Count);
            _targetScroll = Math.Min(_targetScroll, _maxScroll);
            order         = _files.Select(f => f.Key).ToList();
        }
        item.Icon?.Dispose();
        item.Shadow?.Dispose();

        if (item.External) _settings.ExtraItems.RemoveAll(x => SamePath(x, item.Path));
        else if (!_settings.HiddenItems.Contains(item.Key, StringComparer.OrdinalIgnoreCase)) _settings.HiddenItems.Add(item.Key);
        _settings.Order = order;
        _settings.Save();
        RequestRender();
    }

    /// <summary>Пиксели картинки как premultiplied ARGB, строка за строкой.</summary>
    private static unsafe uint[] PremultipliedPixels(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var px = new uint[w * h];
        var d  = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            fixed (uint* dst = px)
                for (int y = 0; y < h; y++)
                    Buffer.MemoryCopy((byte*)d.Scan0 + y * d.Stride, dst + y * w, w * 4L, w * 4L);
        }
        finally { bmp.UnlockBits(d); }
        return px;
    }

    // ------------------------------------------------------------------------
    private static Bitmap NormalizeIcon(Image src, int size)
    {
        if (src == null || src.Width <= 0 || src.Height <= 0) return null;

        var content = FindOpaqueBounds(src);
        if (content.Width <= 0 || content.Height <= 0)
            content = new Rectangle(0, 0, src.Width, src.Height);

        float sw = content.Width;
        float sh = content.Height;
        float scale = Math.Min(size / sw, size / sh);

        int dw = Math.Max(1, (int)Math.Round(sw * scale));
        int dh = Math.Max(1, (int)Math.Round(sh * scale));
        int dx = (size - dw) / 2;
        int dy = (size - dh) / 2;

        var bmp = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode    = PixelOffsetMode.HighQuality;
            g.SmoothingMode      = SmoothingMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.CompositingMode    = CompositingMode.SourceOver;

            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY);

            g.DrawImage(src,
                new Rectangle(dx, dy, dw, dh),
                content.X, content.Y, content.Width, content.Height,
                GraphicsUnit.Pixel, attrs);
        }
        return bmp;
    }

    private static unsafe Rectangle FindOpaqueBounds(Image src)
    {
        using var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(src, 0, 0, src.Width, src.Height);
        }

        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
            byte* scan = (byte*)data.Scan0;

            for (int y = 0; y < bmp.Height; y++)
            {
                byte* row = scan + y * data.Stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    byte a = row[x * 4 + 3];
                    if (a > 16)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < 0) return new Rectangle(0, 0, bmp.Width, bmp.Height);
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }
        finally { bmp.UnlockBits(data); }
    }

    private static Image LoadHighResIcon(string path)
    {
        const uint SHGFI_SYSICONINDEX = 0x000004000;
        const int  SHIL_LARGE         = 0;
        const int  SHIL_EXTRALARGE    = 2;
        const int  SHIL_JUMBO         = 4;
        const int  ILD_TRANSPARENT    = 0x1;

        try
        {
            var shfi = new SHFILEINFO();
            IntPtr r = Native.SHGetFileInfoW(path, 0, ref shfi,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX);
            if (r == IntPtr.Zero) return null;
            int idx = shfi.iIcon;

            foreach (int size in new[] { SHIL_JUMBO, SHIL_EXTRALARGE, SHIL_LARGE })
            {
                Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                IImageList list = null;
                try
                {
                    if (Native.SHGetImageList(size, ref iid, out list) != 0 || list == null) continue;

                    IntPtr hIcon = IntPtr.Zero;
                    if (list.GetIcon(idx, ILD_TRANSPARENT, ref hIcon) != 0 || hIcon == IntPtr.Zero) continue;

                    try
                    {
                        using var tmp = Icon.FromHandle(hIcon);
                        using var clone = (Icon)tmp.Clone();
                        return clone.ToBitmap();
                    }
                    finally { Native.DestroyIcon(hIcon); }
                }
                finally
                {
                    if (list != null) Marshal.ReleaseComObject(list);
                }
            }
        }
        catch { }
        return null;
    }

    private static Image LoadFallbackIcon(string path)
    {
        try
        {
            using var ico = Icon.ExtractAssociatedIcon(path);
            return ico?.ToBitmap();
        }
        catch { return null; }
    }

    // ========================================================================
    //  ГЕОМЕТРИЯ
    // ========================================================================
    private bool IsPointInDock(int screenX, int screenY)
    {
        int lx = screenX - WinX;
        int ly = screenY - WinY;
        int main  = Horizontal ? lx : ly;     // вдоль капсулы
        int cross = Horizontal ? ly : lx;     // поперёк
        if (cross < 0 || cross > Thick || main < 0 || main >= DrawLen) return false;

        // Скруглённые концы
        float r = Thick / 2f;
        if (main < r)
        {
            float dx = cross - r, dy = main - r;
            return dx * dx + dy * dy <= r * r;
        }
        if (main > DrawLen - r)
        {
            float dx = cross - r, dy = main - (DrawLen - r);
            return dx * dx + dy * dy <= r * r;
        }
        return true;
    }

    /// <summary>Ярлык под точкой (координаты капсулы). Вызывать под _sync.</summary>
    private int HoverIndex(int localX, int localY)
    {
        int main  = Horizontal ? localX : localY;
        int cross = Horizontal ? localY : localX;
        if (cross < 0 || cross > Thick) return -1;
        for (int i = 0; i < _files.Count; i++)
        {
            double start = _files[i].AnimY - _scrollPos;
            if (main >= start && main <= start + IconSize) return i;
        }
        return -1;
    }

    private bool IsDockVisibleAt(int screenX, int screenY)
    {
        IntPtr under = Native.WindowFromPoint(new POINT { X = screenX, Y = screenY });
        return under == _hwnd;
    }

    // ========================================================================
    //  TOOLTIP
    // ========================================================================
    private void UpdateTooltipFromRenderThread()
    {
        if (_drag != null || _moving || _panning || _slideBusy || _hideT > 0)
        {
            if (_tooltipIdx >= 0) PostHideTooltip();
            return;
        }

        var cursor = Cursor.Position;

        if (!IsPointInDock(cursor.X, cursor.Y))
        {
            if (_tooltipIdx >= 0) PostHideTooltip();
            return;
        }

        if (!IsDockVisibleAt(cursor.X, cursor.Y))
        {
            if (_tooltipIdx >= 0) PostHideTooltip();
            return;
        }

        int lx = cursor.X - WinX;
        int ly = cursor.Y - WinY;
        int idx = HoverIndex(lx, ly);

        if (idx < 0)
        {
            if (_tooltipIdx >= 0) PostHideTooltip();
            return;
        }

        // Центр иконки на экране — к нему привязана подсказка
        var item   = _files[idx];
        var center = ToWindow(item.AnimY - _scrollPos + IconSize / 2.0, Thick / 2.0);
        int ax     = WinX + (int)Math.Round(center.X);
        int ay     = WinY + (int)Math.Round(center.Y);

        if (idx != _tooltipIdx || ax != _tooltipX || ay != _tooltipY)
        {
            _tooltipIdx = idx;
            _tooltipX   = ax;
            _tooltipY   = ay;
            PostTooltip(item.Name, ax, ay);
        }
    }

    // ------------------------------------------------------------------------
    //  Подсказка обновляется в UI-потоке. При быстрой прокрутке позиция меняется каждый кадр —
    //  раньше на каждый кадр уходило отдельное сообщение, они забивали очередь UI-потока, и из-за
    //  этого запаздывали щелчки колеса и сама подсказка. Теперь рендер-поток только запоминает,
    //  что показать, а в очереди всегда не больше одного сообщения — оно берёт самое свежее.
    // ------------------------------------------------------------------------
    private readonly object _tipLock = new object();
    private string _tipName;                // null — спрятать
    private int    _tipAx, _tipAy;
    private int    _tipPosted;

    private void PostTooltip(string name, int ax, int ay)
    {
        lock (_tipLock) { _tipName = name; _tipAx = ax; _tipAy = ay; }
        if (Interlocked.Exchange(ref _tipPosted, 1) != 0) return;      // уже в очереди — возьмёт свежее
        try { BeginInvoke((Action)ApplyTooltip); }
        catch { Interlocked.Exchange(ref _tipPosted, 0); }
    }

    private void ApplyTooltip()
    {
        Interlocked.Exchange(ref _tipPosted, 0);
        string name; int ax, ay;
        lock (_tipLock) { name = _tipName; ax = _tipAx; ay = _tipAy; }
        if (name != null) ShowTooltip(name, ax, ay);
        else try { if (!IsDisposed && _tooltip.Visible) _tooltip.Hide(); } catch { }
    }

    /// <summary>
    /// UI-поток. Размер — по настоящей ширине текста (раньше ширина угадывалась по числу букв,
    /// и с широким шрифтом последняя буква переносилась на невидимую строку: «rename» → «renam»).
    /// Вертикальная капсула: подсказка слева (нет места — справа); горизонтальная: сверху (нет места — снизу).
    /// </summary>
    private void ShowTooltip(string name, int ax, int ay)
    {
        try
        {
            if (IsDisposed) return;
            var size = _tooltip.SetText(name);
            int w = size.Width, h = size.Height, gap = 12;
            var sb = Screen.FromPoint(new Point(WinX + BarW / 2, WinY + BarH / 2)).Bounds;

            int x, y;
            if (Horizontal)
            {
                x = ax - w / 2;
                y = WinY - h - gap;
                if (y < sb.Top) y = WinY + BarH + gap;
            }
            else
            {
                x = WinX - w - gap;
                y = ay - h / 2;
                if (x < sb.Left) x = WinX + BarW + gap;
            }
            x = Math.Max(sb.Left, Math.Min(x, sb.Right  - w));
            y = Math.Max(sb.Top,  Math.Min(y, sb.Bottom - h));

            _tooltip.Location = new Point(x, y);
            if (!_tooltip.Visible) _tooltip.Show();
            else if (Native.IsIconic(_tooltip.Handle)) Native.ShowWindow(_tooltip.Handle, SW_SHOWNOACTIVATE);
        }
        catch { }
    }

    private void PostHideTooltip()
    {
        _tooltipIdx = -1;
        _tooltipX   = -9999;
        _tooltipY   = -9999;
        PostTooltip(null, 0, 0);
    }

    private void CheckCursorAndHideTooltip()
    {
        if (_tooltipIdx < 0) return;

        var cur = Cursor.Position;
        if (!IsPointInDock(cur.X, cur.Y)) { PostHideTooltip(); return; }
        if (!IsDockVisibleAt(cur.X, cur.Y)) { PostHideTooltip(); return; }
    }
}
