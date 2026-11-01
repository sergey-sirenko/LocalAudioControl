using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace LocalAudioControl
{
    /// <summary>
    /// Кнопки мыши Logi Options+ запускают команду через отдельный EXE. Лаунчер
    /// при этом оставляет фокус внутри активного окна на строке меню: в Notepad
    /// появляются подсказки клавиш (Ф, У, И...), и ввод уходит в меню. Само окно
    /// остаётся активным, поэтому достаточно дождаться, пока UI Automation
    /// покажет фокус в Menu/MenuBar/MenuItem, и отправить Esc. Проверка нужна
    /// несколько раз: меню может появиться с задержкой и мигнуть повторно.
    /// </summary>
    internal static class FocusGuard
    {
        private const int MenuType = 50009;       // ControlType.Menu
        private const int MenuBarType = 50010;    // ControlType.MenuBar
        private const int MenuItemType = 50011;   // ControlType.MenuItem
        private const byte EscapeKey = 0x1B;
        private const uint KeyUp = 0x0002;
        private const int RepairMilliseconds = 1500;
        private const int PollMilliseconds = 120;

        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static readonly object Gate = new object();
        private static Thread worker;
        private static long deadline;

        /// <summary>Команды, которые запускаются кнопкой мыши и могут оставить подсказки клавиш.</summary>
        internal static bool IsRepairCommand(string command)
        {
            switch (command)
            {
                case "--favorite1":
                case "--favorite2":
                case "--toggle-output":
                case "--next-track":
                case "--volume-up":
                case "--volume-down":
                    return true;
                default:
                    return false;
            }
        }

        internal static bool IsMenuId(int id)
        {
            return id == MenuType || id == MenuBarType || id == MenuItemType;
        }

        /// <summary>Запускает асинхронную проверку фокуса. Команду не задерживает.</summary>
        internal static void RepairSoon()
        {
            Interlocked.Exchange(ref deadline, DateTime.UtcNow.AddMilliseconds(RepairMilliseconds).Ticks);
            EnsureWorker();
            Wake.Set();
        }

        private static void EnsureWorker()
        {
            if (worker != null) return;
            lock (Gate)
            {
                if (worker != null) return;
                var thread = new Thread(Run) { IsBackground = true, Name = "Focus guard" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                worker = thread;
            }
        }

        private static void Run()
        {
            while (true)
            {
                Wake.WaitOne();
                long until = Interlocked.Read(ref deadline);
                while (DateTime.UtcNow.Ticks < until)
                {
                    try { RepairOnce(); }
                    catch (Exception ex) { Storage.Log(ex); }
                    long remaining = until - DateTime.UtcNow.Ticks;
                    if (remaining <= 0) break;
                    int wait = (int)Math.Min(PollMilliseconds, Math.Max(20, remaining / TimeSpan.TicksPerMillisecond));
                    Wake.WaitOne(wait);
                    until = Interlocked.Read(ref deadline);
                }
            }
        }

        private static void RepairOnce()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return;

            AutomationElement focused;
            try { focused = AutomationElement.FocusedElement; }
            catch { return; }
            if (focused == null) return;

            int type, pid;
            string className;
            try
            {
                type = focused.Current.ControlType.Id;
                pid = focused.Current.ProcessId;
                className = focused.Current.ClassName;
            }
            catch { return; }

            if (!IsMenuId(type)) return;

            uint foregroundPid;
            GetWindowThreadProcessId(foreground, out foregroundPid);
            if (foregroundPid != (uint)pid) return;
            if (GetForegroundWindow() != foreground) return;

            PressEscape();
            Storage.Log("FocusGuard: Esc in " + ProcessName(pid) + " [" + (className ?? "") + "]");
            Thread.Sleep(200);
        }

        private static void PressEscape()
        {
            keybd_event(EscapeKey, 0, 0, UIntPtr.Zero);
            keybd_event(EscapeKey, 0, KeyUp, UIntPtr.Zero);
        }

        private static string ProcessName(int pid)
        {
            try
            {
                using (var process = Process.GetProcessById(pid)) return process.ProcessName + ".exe";
            }
            catch { return "process " + pid; }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    }
}
