using System.Runtime.InteropServices;

namespace H2MLauncher.Core.Game
{
    internal class SendInputHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;

        public static void SendKey(char c)
        {
            ushort scanCode = (ushort)c;

            INPUT down = new INPUT();
            down.type = INPUT_KEYBOARD;
            down.u.ki.wVk = 0;
            down.u.ki.wScan = scanCode;
            down.u.ki.dwFlags = 0x0004; // KEYEVENTF_UNICODE

            INPUT up = down;
            up.u.ki.dwFlags = 0x0004 | KEYEVENTF_KEYUP; // KEYEVENTF_UNICODE | KEYUP

            INPUT[] inputs = new INPUT[] { down, up };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void SendString(string text)
        {
            foreach (char c in text)
            {
                SendKey(c);
            }
        }

        public static void PressEnter()
        {
            INPUT down = new INPUT();
            down.type = INPUT_KEYBOARD;
            down.u.ki.wVk = 0x0D; // VK_RETURN

            INPUT up = down;
            up.u.ki.dwFlags = KEYEVENTF_KEYUP;

            INPUT[] inputs = new INPUT[] { down, up };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
