using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using STM.Data;

namespace Utilities;


public static class Log
{
    private static readonly object LogFileLock = new();

    /// <summary>
    /// Writes a string message to a log file in the user's temporary directory.
    /// </summary>
    /// <param name="logMessage">The message to write to the log.</param>
    public static void Write(string logMessage, bool includeMethod = true)
    {
        if (includeMethod) logMessage = GetCallingMethod(2) + ": " + logMessage;

        // Get assembly name
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
        string assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

        // Simple console output
        Console.WriteLine(assemblyName + ": " + logMessage);

        // Combine the temporary path with the log file name.
        string filePath = Path.Combine(Path.GetTempPath(), assemblyName + "Log.txt");
        lock (LogFileLock)
        {
            try
            {
                // Create a StreamWriter in append mode. This will create the file if it doesn't exist,
                // or open it and add the new content to the end.
                using (StreamWriter writer = new StreamWriter(filePath, true))
                {
                    // Format the current date and time to prepend to the message.
                    string timestamp = DateTime.Now.ToString("HH:mm:ss"); //yyyy-MM-dd HH:mm:ss
                    // Write the log message and a new line.
                    writer.WriteLine($"[{timestamp}] {logMessage}");
                }
            }
            catch (Exception ex)
            {
                // Log any potential errors to the console.
                Console.WriteLine($"Error writing to log file: {ex.Message}");
            }
        }
    }

#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.

    /// <summary>
    /// Gets the method from the specified <paramref name="frame"/>.
    /// </summary>
    public static string GetCallingMethod(int frame)
    {
        StackTrace st = new();
        MethodBase mb = st.GetFrame(frame).GetMethod(); // 0 - GetCallingMethod, 1 - Log, 2 - actual function calling a Log method
        return /*mb.DeclaringType + "." +*/ mb.Name;
    }

    /// <summary>
    /// Returns a string representation of the calling stack up to the specified number of frames.
    /// </summary>
    /// <param name="frames">The number of frames to return from the calling stack.</param>
    /// <returns>
    /// A string with each frame on a new line, formatted as &lt;namespace&gt;.&lt;class&gt;.&lt;method&gt;.
    /// </returns>
    public static void WriteCallingStack(int frames)
    {
        //var sb = new StringBuilder();

        // StackTrace is a class in the System.Diagnostics namespace that provides
        // information about the call stack for the current thread.
        StackTrace stackTrace = new();

        // Start from frame 1 to exclude the current GetCallingStack method itself.
        // We iterate for the requested number of frames or until we reach the end of the stack.
        for (int i = 1; i <= frames && i < stackTrace.FrameCount; i++)
        {
            // Get the method information for the current frame.
            MethodBase method = stackTrace.GetFrame(i).GetMethod();

            if (method != null)
            {
                // Get the type (class) where the method is declared.
                Type declaringType = method.DeclaringType;

                string fullMethodName;
                if (declaringType != null)
                {
                    fullMethodName = $"{declaringType.FullName}.{method.Name}"; // Construct the full method name
                }
                else
                {
                    // If declaringType is null, we can't get the namespace and class.
                    // This might happen for dynamic methods or others.
                    fullMethodName = method.Name;
                }
                //sb.AppendLine(fullMethodName); // adds a new line also
                Write(String.Concat(Enumerable.Repeat("  ", i), fullMethodName), false);
            }
            else
                Write("error - method is null", false);
        }
        //return sb.ToString();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public static void DumpMainDataDefaults()
    {
        Type type = MainData.Defaults.GetType();
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Log.Write($"--- Public Properties of {type.Name} ---", false);
        // Iterate through the properties and print their names and values
        foreach (PropertyInfo property in properties)
        {
            string name = property.Name;
            object value = property.GetValue(MainData.Defaults);
            Log.Write($"{name}= {value}  ({property.PropertyType.Name})", false);
        }
    }
}

#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning restore CS8602 // Dereference of a possibly null reference.


public static class DebugConsole
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    public static void Show()
    {
        if (GetConsoleWindow() == IntPtr.Zero)
        {
            AllocConsole();
            SetFont(12);
            Console.Clear();
            //Log.DumpMainDataDefaults(); // this should be dumped only once
        }
    }

    public static void Hide()
    {
        if (GetConsoleWindow() != IntPtr.Zero)
            FreeConsole();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
           IntPtr hWnd,
           IntPtr hWndInsertAfter,
           int X, int Y, int cx, int cy,
           uint uFlags);

    // SetWindowPos flags from WinUser.h
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOACTIVATE = 0x0010;
    private const int HWND_TOPMOST = -1;
    private const int HWND_NOTOPMOST = -2;

    public static void SetAlwaysOnTop(bool enable = true)
    {
        IntPtr handle = GetConsoleWindow();
        if (handle == IntPtr.Zero) return;

        SetWindowPos(
            handle,
            (IntPtr)(enable ? HWND_TOPMOST : HWND_NOTOPMOST),
            0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE);
    }

    // FONT definition from Wincon.h
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CONSOLE_FONT_INFO_EX
    {
        public uint cbSize; // ULONG Unsigned long 4 bytes, long in C# is 64 bits!
        public uint nFont; // DWORD 32-bit unsigned integer 4 bytes
        public Coord dwFontSize; // COORD
        public uint FontFamily; // UINT 32-bit unsigned integer 4 bytes
        public uint FontWeight; // UINT
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FaceName; // WCHAR[32]
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X; // SHORT 16-bit signed
        public short Y; // SHORT

        public Coord(short x, short y) { X = x; Y = y; }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetCurrentConsoleFontEx(
        IntPtr consoleOutput,
        bool maximumWindow,
        ref CONSOLE_FONT_INFO_EX consoleCurrentFontEx);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    private const int STD_OUTPUT_HANDLE = -11; // Standard output (console)

    public static void SetFont(short fontSizeY = 12, string fontName = "Consolas")
    {
        IntPtr hnd = GetStdHandle(STD_OUTPUT_HANDLE);
        var info = new CONSOLE_FONT_INFO_EX();
        info.cbSize = (uint)Marshal.SizeOf(info);
        info.FaceName = fontName;
        info.dwFontSize = new Coord(0, fontSizeY); // 0 means automatic width
        info.FontFamily = 0x30 + 0x01 + 0x04;  // FF_MODERN + FIXED_PITCH + TMPF_TRUETYPE from wingdi.h
        info.FontWeight = 400; // normal weight

        SetCurrentConsoleFontEx(hnd, false, ref info);
    }
}
