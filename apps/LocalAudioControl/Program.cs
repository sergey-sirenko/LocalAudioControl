using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace LocalAudioControl
{
    internal static class Program
    {
        internal static readonly string Identity = WindowsIdentity.GetCurrent().User.Value + "." + Process.GetCurrentProcess().SessionId;
        internal static readonly string PipeName = "LocalAudioControl.v1." + Identity;
        [STAThread]
        private static int Main(string[] args)
        {
            string command = args.Length == 0 ? "--settings" : args[0];
            string output = null;
            if (command == "--status-file" && args.Length == 2) { command = "--status"; output = args[1]; }
            using (var gate = new Mutex(false, @"Local\LocalAudioControl.v1." + Identity))
            {
                bool acquired;
                try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    try
                    {
                        string response = Send(command);
                        if (output != null) File.WriteAllText(output, response); else Console.WriteLine(response);
                        return response.Contains("\"ok\":true") ? 0 : 1;
                    }
                    catch (Exception ex) { Storage.Log(ex); return 1; }
                }
                try
                {
                    // Read-only diagnostics should not create a background instance or settings.
                    if (command == "--status")
                    {
                        string response = Storage.Json.Serialize(new { ok = true, state = Audio.State(), devices = Audio.Devices(), running = false });
                        if (output != null) File.WriteAllText(output, response); else Console.WriteLine(response);
                        return 0;
                    }
                    if (command == "--quit") return 0;
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += (s, e) => Storage.Log(e.Exception);
                    using (var context = new TrayApplication(command)) Application.Run(context);
                    return 0;
                }
                catch (Exception ex)
                {
                    Storage.Log(ex);
                    if (command == "--status")
                    {
                        string response = Storage.Json.Serialize(new { ok = false, error = ex.Message });
                        if (output != null) File.WriteAllText(output, response); else Console.WriteLine(response);
                        return 1;
                    }
                    MessageBox.Show(ex.Message, "Local Audio Control", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { gate.ReleaseMutex(); }
            }
        }
        private static string Send(string command)
        {
            Exception last = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                    {
                        pipe.Connect(250);
                        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                        using (var reader = new StreamReader(pipe, Encoding.UTF8, true, 1024, true))
                        {
                            writer.WriteLine(command);
                            var read = reader.ReadLineAsync();
                            if (!read.Wait(5000)) throw new TimeoutException("Программа не ответила на команду.");
                            return read.Result ?? "{\"ok\":false}";
                        }
                    }
                }
                catch (IOException ex) { last = ex; }
                catch (TimeoutException ex) { last = ex; }
                Thread.Sleep(50);
            }
            throw new IOException("Не удалось связаться с Local Audio Control.", last);
        }
    }
    internal sealed class CommandServer : IDisposable
    {
        private readonly Control dispatcher;
        private readonly Func<string, string> execute;
        private readonly Action exit;
        private readonly string pipeName;
        private readonly Thread thread;
        private volatile bool stopped;
        private NamedPipeServerStream active;
        private readonly object sync = new object();
        internal CommandServer(Control dispatcher, Func<string, string> execute, Action exit, string pipeName = null)
        {
            this.dispatcher = dispatcher; this.execute = execute; this.exit = exit;
            this.pipeName = pipeName ?? Program.PipeName;
            thread = new Thread(Run) { IsBackground = true, Name = "Local audio commands" }; thread.Start();
        }
        private void Run()
        {
            while (!stopped)
            {
                try
                {
                    var security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.SetOwner(WindowsIdentity.GetCurrent().User);
                    security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous, 4096, 4096, security))
                    {
                        lock (sync) { if (stopped) return; active = pipe; }
                        pipe.WaitForConnection();
                        using (var reader = new StreamReader(pipe, Encoding.UTF8, true, 1024, true))
                        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true) { AutoFlush = true })
                        {
                            var read = reader.ReadLineAsync();
                            if (!read.Wait(2000)) continue;
                            string command = read.Result;
                            if (command == null || command.Length > 128) continue;
                            if (!stopped)
                            {
                                writer.WriteLine((string)dispatcher.Invoke(execute, command));
                                if (command == "--quit") dispatcher.BeginInvoke(exit);
                            }
                        }
                    }
                }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { if (!stopped) { Storage.Log(ex); Thread.Sleep(100); } }
                finally { lock (sync) { active = null; } }
            }
        }
        public void Dispose() { stopped = true; lock (sync) { if (active != null) active.Dispose(); } }
    }
}
