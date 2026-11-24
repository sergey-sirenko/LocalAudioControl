using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace LocalAudioControl
{
    internal static class YandexMusicBridge
    {
        private const string HostKey = @"HKEY_CURRENT_USER\Software\Google\Chrome\NativeMessagingHosts\local_audio_control.yandex_music";
        private const int ConnectTimeout = 750;
        private const int BrowserTimeout = 2000;
        private const byte Accepted = 1;
        private const byte NoTab = 2;
        private const byte NoButton = 3;

        private static string PipeName
        {
            get { return "LocalAudioControl.YandexMusic.v2." + WindowsIdentity.GetCurrent().User.Value + "." + Process.GetCurrentProcess().SessionId; }
        }

        internal static bool IsConfigured()
        {
            return Registry.GetValue(HostKey, "", null) is string;
        }

        internal static bool TryNextTrack(bool playingOnly)
        {
            int result;
            try
            {
                using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                    TokenImpersonationLevel.Identification))
                {
                    pipe.Connect(ConnectTimeout);
                    pipe.WriteByte(playingOnly ? (byte)1 : (byte)0);
                    var reply = new byte[1];
                    var read = pipe.ReadAsync(reply, 0, reply.Length);
                    result = read.Wait(BrowserTimeout + 250) && read.Result == 1 ? reply[0] : 0;
                }
            }
            catch (TimeoutException)
            {
                if (playingOnly) return false;
                throw new InvalidOperationException("Расширение Яндекс Музыки недоступно. Проверьте его в локальном Chrome.");
            }
            catch (AggregateException ex)
            {
                throw new InvalidOperationException("Не удалось получить ответ от расширения Яндекс Музыки.", ex);
            }

            switch (result)
            {
                case Accepted: return true;
                case NoTab: return false;
                case NoButton: throw new InvalidOperationException("Кнопка «Следующий трек» недоступна в плеере Яндекс Музыки.");
                default: throw new InvalidOperationException("Расширение Яндекс Музыки не подтвердило переключение трека.");
            }
        }

        // Entry point of the separate process launched by Chrome. Its standard
        // streams are reserved for the Chrome native messaging protocol.
        private static int Main()
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            var user = WindowsIdentity.GetCurrent().User;
            security.SetOwner(user);
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
                PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));

            try
            {
                using (var input = new BinaryReader(Console.OpenStandardInput(), Encoding.UTF8))
                using (var output = new BinaryWriter(Console.OpenStandardOutput(), Encoding.UTF8))
                using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 4096, 4096, security))
                {
                    var next = Encoding.UTF8.GetBytes("{\"command\":\"next\"}");
                    var nextIfPlaying = Encoding.UTF8.GetBytes("{\"command\":\"next\",\"playingOnly\":true}");
                    while (true)
                    {
                        pipe.WaitForConnection();
                        var mode = new byte[1];
                        var modeRead = pipe.ReadAsync(mode, 0, mode.Length);
                        if (!modeRead.Wait(ConnectTimeout)) return 1;
                        if (modeRead.Result != 1 || mode[0] > 1)
                        {
                            pipe.Disconnect();
                            continue;
                        }
                        var command = mode[0] == 1 ? nextIfPlaying : next;
                        output.Write(command.Length);
                        output.Write(command);
                        output.Flush();

                        var reply = Task.Factory.StartNew(() => ReadReply(input));
                        // Close this port if Chrome does not reply. The extension
                        // reconnects on a new port, so a late reply can't match another click.
                        if (!reply.Wait(BrowserTimeout)) return 1;
                        try
                        {
                            pipe.WriteByte(reply.Result);
                            pipe.WaitForPipeDrain();
                        }
                        catch (IOException) { }
                        pipe.Disconnect();
                    }
                }
            }
            catch (IOException) { return 1; }
            catch (AggregateException) { return 1; }
        }

        private static byte ReadReply(BinaryReader input)
        {
            int length = input.ReadInt32();
            if (length < 1 || length > 256) throw new IOException("Invalid native message length.");
            byte[] bytes = input.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException();
            var response = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(bytes));
            string result;
            if (response == null || !response.TryGetValue("result", out result)) return 0;
            switch (result)
            {
                case "accepted": return Accepted;
                case "no-tab": return NoTab;
                case "no-button": return NoButton;
                default: return 0;
            }
        }
    }
}
