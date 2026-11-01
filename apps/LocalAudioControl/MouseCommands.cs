using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LocalAudioControl
{
    internal static class OutputToggle
    {
        internal static string Target(Preferences preferences, string currentId)
        {
            if (String.IsNullOrWhiteSpace(preferences.Favorite1) || String.IsNullOrWhiteSpace(preferences.Favorite2))
                throw new InvalidOperationException("Для переключения выберите два избранных устройства в настройках.");
            if (String.Equals(preferences.Favorite1, preferences.Favorite2, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Для переключения выберите два разных избранных устройства в настройках.");
            return String.Equals(currentId, preferences.Favorite1, StringComparison.OrdinalIgnoreCase)
                ? preferences.Favorite2 : preferences.Favorite1;
        }
    }

    internal static class MediaControls
    {
        internal const ushort NextTrackKey = 0xB0;
        internal const uint KeyUp = 0x0002;

        // Include the full native union: INPUT is 40 bytes on x64 and 28 on x86.
        [StructLayout(LayoutKind.Sequential)]
        internal struct Input { internal uint Type; internal InputData Data; }
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputData
        {
            [FieldOffset(0)] internal KeyboardInput Keyboard;
            [FieldOffset(0)] internal MouseInput Mouse;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct KeyboardInput
        {
            internal ushort Key, Scan;
            internal uint Flags, Time;
            internal UIntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput
        {
            internal int X, Y;
            internal uint MouseData, Flags, Time;
            internal UIntPtr ExtraInfo;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);

        internal static void NextTrack() { NextTrack(SmtcMedia.TrySkipNext, SendInput); }

        internal static void NextTrack(Func<bool> tryLocal, Func<uint, Input[], int, uint> send)
        {
            // Локальная SMTC-команда не зависит от активного окна и работает при RDP.
            if (tryLocal()) return;
            var down = new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = NextTrackKey } } };
            var up = new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = NextTrackKey, Flags = KeyUp } } };
            int size = Marshal.SizeOf(typeof(Input));
            uint sent = send(2, new [] { down, up }, size);
            if (sent != 2)
            {
                int error = Marshal.GetLastWin32Error();
                // If only key-down was inserted, release it without sending another press.
                if (sent == 1) send(1, new [] { up }, size);
                throw new Win32Exception(error, "Не удалось отправить команду «Следующий трек».");
            }
        }
    }
}
