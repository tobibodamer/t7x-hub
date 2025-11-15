using System;
using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using H2MLauncher.Core.Game.Models;
using H2MLauncher.Core.Services;
using H2MLauncher.Core.Settings;
using H2MLauncher.Core.Utilities;

using Microsoft.Extensions.Logging;

using Nogic.WritableOptions;

namespace H2MLauncher.Core.Game
{
    public sealed class T7XCommunicationService : IDisposable
    {
        // Mod executable file names (to automatically find game file in directory)
        private static readonly string[] GAME_EXECUTABLE_NAMES = ["t7x.exe"];

        // Strings to match game / mod window titles
        private static readonly string[] T7X_WINDOW_TITLE_STRINGS = ["T7x"];

        //Windows API constants
        private const int WM_CHAR = 0x0102; // Message code for sending a character
        private const int WM_KEYDOWN = 0x0100; // Message code for key down
        private const int WM_KEYUP = 0x0101;   // Message code for key up

        private readonly IWritableOptions<H2MLauncherSettings> _h2mLauncherSettings;
        private readonly IErrorHandlingService _errorHandlingService;
        private readonly ILogger<T7XCommunicationService> _logger;
        private readonly IDisposable? _optionsChangeRegistration;

        public IGameCommunicationService GameCommunication { get; }
        public IGameDetectionService GameDetection { get; }

        public T7XCommunicationService(IErrorHandlingService errorHandlingService, IWritableOptions<H2MLauncherSettings> options,
            ILogger<T7XCommunicationService> logger, IGameCommunicationService gameCommunicationService, IGameDetectionService gameDetectionService)
        {
            _errorHandlingService = errorHandlingService;
            _h2mLauncherSettings = options;
            _logger = logger;
            GameCommunication = gameCommunicationService;
            GameDetection = gameDetectionService;

            if (options.CurrentValue.AutomaticGameDetection)
            {
                GameDetection.StartGameDetection();
            }

            _optionsChangeRegistration = options.OnChange((settings, _) =>
            {
                if (!settings.AutomaticGameDetection && GameDetection.IsGameDetectionRunning)
                {
                    GameDetection.StopGameDetection();
                }
                else if (settings.AutomaticGameDetection && !GameDetection.IsGameDetectionRunning)
                {
                    GameDetection.StartGameDetection();
                }

                if (!settings.GameMemoryCommunication && GameCommunication.IsGameCommunicationRunning)
                {
                    GameCommunication.StopGameCommunication();
                }
                else if (settings.GameMemoryCommunication && !GameCommunication.IsGameCommunicationRunning
                    && GameDetection.DetectedGame is not null)
                {
                    GameCommunication.StartGameCommunication(GameDetection.DetectedGame.Process);
                }
            });
        }

        //Windows API functions to send input to a window
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(nint hWnd, StringBuilder text, int count);


        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindowEx(
            IntPtr hwndParent,      // Handle of the parent window
            IntPtr hwndChildAfter,  // Handle to a child window (to search *after*)
            string? lpszClass,      // The Class Name
            string? lpszWindow      // The Window Name (Caption)
        );


        #region Console Stuff

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();


        // These are to send keys to the attached console

        const int STD_INPUT_HANDLE = -10;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteConsoleInput(
            IntPtr hConsoleInput,
            INPUT_RECORD[] lpBuffer,
            uint nLength,
            out uint lpNumberOfEventsWritten);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT_RECORD
        {
            public short EventType;
            public KEY_EVENT_RECORD KeyEvent;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEY_EVENT_RECORD
        {
            public bool bKeyDown;
            public short wRepeatCount;
            public short wVirtualKeyCode;
            public short wVirtualScanCode;
            public char UnicodeChar;
            public int dwControlKeyState;
        }

        const short KEY_EVENT = 0x0001;

        #endregion


        private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

        private static IEnumerable<(nint, string)> EnumerateProcessWindowHandles(int processId)
        {
            var result = new List<(nint, string)>();
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid == processId)
                {
                    var sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    result.Add((hWnd, sb.ToString()));
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private bool TryFindValidGameFile(out string fileName)
        {
            if (string.IsNullOrEmpty(_h2mLauncherSettings.CurrentValue.GameLocation))
            {
                foreach (string exeFileName in GAME_EXECUTABLE_NAMES)
                {
                    // no location set, try relative path
                    fileName = Path.GetFullPath(exeFileName);
                    if (File.Exists(fileName))
                    {
                        return true;
                    }
                }
            }

            string userDefinedLocation = Path.GetFullPath(_h2mLauncherSettings.CurrentValue.GameLocation);

            if (!Path.Exists(userDefinedLocation))
            {
                // neither dir or file exists
                fileName = userDefinedLocation;
                return false;
            }

            if (File.GetAttributes(userDefinedLocation).HasFlag(FileAttributes.Directory))
            {
                // is a directory, get full file name
                foreach (string exeFileName in GAME_EXECUTABLE_NAMES)
                {
                    fileName = Path.Combine(userDefinedLocation, exeFileName);
                    if (File.Exists(fileName))
                    {
                        return true;
                    }
                }
            }

            // is a file?
            fileName = userDefinedLocation;
            return File.Exists(userDefinedLocation);
        }

        public void Launch()
        {
            ReleaseCapture();

            try
            {
                // Check if the process is already running
                if (GameDetection.DetectedGame is not null)
                {
                    _errorHandlingService.HandleError($"{Path.GetFileName(GameDetection.DetectedGame.FileName)} is already running.");
                    return;
                }

                // Proceed to launch the process if it's not running
                if (TryFindValidGameFile(out string gameFileName) &&
                    !string.IsNullOrEmpty(gameFileName))
                {
                    ProcessStartInfo startInfo = new(gameFileName)
                    {
                        WorkingDirectory = Path.GetDirectoryName(gameFileName),
                    };

                    Process.Start(startInfo);
                }
                else
                {
                    _errorHandlingService.HandleException(
                        new FileNotFoundException("H2M executable was not found."),
                            $"The H2M executable could not be found at '{gameFileName}'!");
                }
            }
            catch (Exception ex)
            {
                _errorHandlingService.HandleException(ex, "Error launching h2m-mod.");
            }
        }

        public Task<bool> JoinServer(string ip, string port, string? password = null)
        {
            // TODO: (if necessary) disconnect first, wait for disconnect then connect.
            // Right now the games takes way too long to disconnect
            string connectCommand = $"connect {ip}:{port}";

            if (password is not null)
            {
                connectCommand += $";password {password}";
            }

            return ExecuteCommandAsync([connectCommand]);
        }

        public Task<bool> Disconnect()
        {
            return ExecuteCommandAsync(["disconnect"]);
        }

        public async Task<bool> ExecuteCommandAsync(string[] commands, bool bringGameWindowToForeground = true)
        {
            Process? process = FindProcess();
            if (process == null)
            {
                _errorHandlingService.HandleError("Could not find the h2m-mod terminal window.");
                return false;
            }

            // Try to get handle of the real console
            nint consoleHandle = GetConsoleHandle(process, freeConsole: false);

            try
            {
                if (consoleHandle == IntPtr.Zero)
                {
                    // Console not available, work with fancy custom console
                    ExecuteCommandsInFancyConsole(process, commands);
                }
                else
                {
                    // Write directly to console input
                    foreach (string command in commands)
                    {
                        if (!WriteToConsoleInput(command + "\r"))
                        {
                            _logger.LogWarning("Could not write command {command} to console input", command);
                        }

                        // Sleep for 1ms to allow the command to be processed
                        await Task.Delay(1);
                    }
                }

                if (bringGameWindowToForeground)
                {
                    // Set game as foreground window
                    var hGameWindow = FindT7XWindow(process);
                    SetForegroundWindow(hGameWindow);
                }

                return true;
            }
            finally
            {
                if (consoleHandle != nint.Zero)
                {
                    FreeConsole();
                }
            }
        }

        /// <summary>
        /// Execute commands in the fancy custom T7X console by sending messages to the text box.
        /// </summary>
        private static bool ExecuteCommandsInFancyConsole(Process process, string[] commands)
        {
            // Grab the handle of the console window
            nint hWindow = FindT7XWindow(process, console: true);
            if (hWindow == IntPtr.Zero)
            {
                return false;
            }

            // 2. Find the child "Edit" control (the textbox) inside Notepad
            IntPtr editHandle = FindWindowEx(hWindow, IntPtr.Zero, "Edit", null);
            if (editHandle == IntPtr.Zero)
            {
                return false;
            }

            foreach (string command in commands)
            {
                foreach (char c in command)
                {
                    PostMessage(editHandle, WM_CHAR, c, nint.Zero);
                }

                // Simulate pressing the Enter key
                PostMessage(editHandle, WM_KEYDOWN, 13, nint.Zero);
                PostMessage(editHandle, WM_KEYUP, 13, nint.Zero);
            }

            return true;
        }

        /// <summary>
        /// Make lParam for <see cref="PostMessage(nint, uint, nint, nint)"/> or <see cref="SendMessage(nint, uint, nint, nint)"/>.
        /// </summary>        
        private static IntPtr MakeLParamForKey(
            ushort repeatCount,
            byte scanCode,
            bool extended,
            bool altDown,
            bool previousKeyState,
            bool transitionState)
        {
            // Build as unsigned 32-bit to avoid sign issues.
            uint l = 0;

            // Bits 0-15: repeat count
            l |= (uint)(repeatCount & 0xFFFF);

            // Bits 16-23: scan code
            l |= (uint)(scanCode & 0xFFu) << 16;

            // Bit 24: extended
            if (extended) l |= 1u << 24;

            // Bit 29: context (ALT)
            if (altDown) l |= 1u << 29;

            // Bit 30: previous key state
            if (previousKeyState) l |= 1u << 30;

            // Bit 31: transition state (0 = keydown, 1 = keyup)
            if (transitionState) l |= 1u << 31;

            return new IntPtr(unchecked((int)l));
        }

        private static void SendTildeToWindow(nint hWnd)
        {
            // Typical scan code for OEM_3 on many keyboards is 0x29 (but layouts vary).
            // For WM_KEYDOWN/WM_KEYUP lParam we include a scan code field; but many apps ignore lParam.
            byte scan = 0x29; // common value, not guaranteed for all layouts
            IntPtr lParamDown = MakeLParamForKey(1, scan, false, false, false, false);
            IntPtr lParamUp = MakeLParamForKey(1, scan, false, false, true, true);

            // Send WM_KEYDOWN then WM_KEYUP
            SendMessage(hWnd, WM_KEYDOWN, 192, lParamDown);
            SendMessage(hWnd, WM_KEYUP, 192, lParamUp);
        }

        private static IEnumerable<INPUT_RECORD> CreateKeyPressInput(char character)
        {
            short keyCode = 0;
            short scanCode = 0;

            if (character == '\r')
            {
                keyCode = 0x0D;       // VK_RETURN
                scanCode = 0x1C;      // Scan code for Enter
            }

            // Key down
            yield return new()
            {
                EventType = KEY_EVENT,
                KeyEvent = new KEY_EVENT_RECORD
                {
                    bKeyDown = true,
                    wRepeatCount = 1,
                    wVirtualKeyCode = keyCode,
                    wVirtualScanCode = scanCode,
                    UnicodeChar = character,
                    dwControlKeyState = 0
                }
            };

            // Key up
            yield return new()
            {
                EventType = KEY_EVENT,
                KeyEvent = new KEY_EVENT_RECORD
                {
                    bKeyDown = false,
                    wRepeatCount = 1,
                    wVirtualKeyCode = keyCode,
                    wVirtualScanCode = scanCode,
                    UnicodeChar = character,
                    dwControlKeyState = 0
                }
            };
        }

        private static bool WriteToConsoleInput(string str)
        {
            IntPtr hInput = GetStdHandle(STD_INPUT_HANDLE);
            var inputBuffer = str.SelectMany(CreateKeyPressInput).ToArray();

            return WriteConsoleInput(hInput, inputBuffer, (uint)inputBuffer.Length, out _);
        }

        public nint GetGameWindowHandle()
        {
            if (GameDetection.DetectedGame is null)
            {
                return nint.Zero;
            }

            return FindT7XWindow(GameDetection.DetectedGame.Process);
        }

        public static Process? FindProcess()
        {
            // find processes with matching title
            var processesWithTitle = Process.GetProcesses().Where(p =>
                T7X_WINDOW_TITLE_STRINGS.Any(str => p.MainWindowTitle.Contains(str, StringComparison.OrdinalIgnoreCase))).ToList();

            // find process that loaded BO3 binary
            var gameProc = processesWithTitle.FirstOrDefault(p =>
                p.Modules.OfType<ProcessModule>().Any(m => m.ModuleName.Equals(Constants.GAME_EXECUTABLE_NAME)));

            return gameProc;
        }

        private static nint GetConsoleHandle(Process process, bool freeConsole = true)
        {
            // Now, check if this window handle is the console window for that process.
            // A reliable way is to try attaching to the console. If it succeeds, it's a console.
            if (AttachConsole((uint)process.Id))
            {
                try
                {
                    return GetConsoleWindow();
                }
                finally
                {
                    if (freeConsole)
                        FreeConsole(); // Detach immediately
                }
            }

            return nint.Zero;
        }

        private static string? GetWindowTitle(nint hWnd)
        {
            const int length = 256;
            StringBuilder sb = new(length);

            if (GetWindowText(hWnd, sb, length) > 0)
            {
                return sb.ToString();
            }

            return null;
        }

        private static nint FindT7XWindow(Process process, bool console = false)
        {
            // find game window / console
            foreach ((nint hChild, string title) in EnumerateProcessWindowHandles(process.Id))
            {
                if (title is not null && console == title.Equals("T7x Console"))
                {
                    // if its not the console, its probably the game window
                    return hChild;
                }
            }

            // otherwise return just the main window, whatever it is
            return process.MainWindowHandle;
        }

        public void StartGameCommunication()
        {
            if (GameCommunication.IsGameCommunicationRunning ||
                GameDetection.DetectedGame is not DetectedGame detectedGame ||
                detectedGame.Process.HasExited)
            {
                return;
            }

            GameCommunication.StartGameCommunication(detectedGame.Process);
        }

        public void Dispose()
        {
            _optionsChangeRegistration?.Dispose();
        }
    }
}
