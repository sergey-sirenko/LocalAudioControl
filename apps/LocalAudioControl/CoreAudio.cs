using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LocalAudioControl
{
    public sealed class AudioDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public override string ToString() { return Name; }
    }
    public sealed class AudioState
    {
        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public int Percent { get; set; }
        public bool Muted { get; set; }
        public string Error { get; set; }
    }
    public static class Audio
    {
        private static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        public static List<AudioDevice> Devices()
        {
            var result = new List<AudioDevice>();
            var enumerator = (IEnumerator)new Enumerator();
            ICollection collection = null;
            try
            {
                Check(enumerator.EnumAudioEndpoints(0, 1, out collection));
                uint count; Check(collection.GetCount(out count));
                for (uint i = 0; i < count; i++)
                {
                    IDevice device = null;
                    try { Check(collection.Item(i, out device)); result.Add(Describe(device)); }
                    finally { Release(device); }
                }
            }
            finally { Release(collection); Release(enumerator); }
            result.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
            return result;
        }
        private static AudioDevice Describe(IDevice device)
        {
            string id; Check(device.GetId(out id));
            IProperties store = null;
            string name = id;
            try
            {
                Check(device.OpenPropertyStore(0, out store));
                var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                Variant value;
                Check(store.GetValue(ref key, out value));
                try { if (value.Type == 31 && value.Pointer != IntPtr.Zero) name = Marshal.PtrToStringUni(value.Pointer); }
                finally { Native.PropVariantClear(ref value); }
            }
            finally { Release(store); }
            return new AudioDevice { Id = id, Name = name };
        }
        public static string DefaultId(int role)
        {
            var enumerator = (IEnumerator)new Enumerator(); IDevice device = null;
            try { Check(enumerator.GetDefaultAudioEndpoint(0, role, out device)); string id; Check(device.GetId(out id)); return id; }
            finally { Release(device); Release(enumerator); }
        }
        private static T WithVolume<T>(Func<IVolume, AudioDevice, T> action)
        {
            var enumerator = (IEnumerator)new Enumerator(); IDevice device = null; object volume = null;
            try
            {
                Check(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
                Guid iid = typeof(IVolume).GUID; IntPtr pointer;
                Check(device.Activate(ref iid, 23, IntPtr.Zero, out pointer));
                try { volume = Marshal.GetObjectForIUnknown(pointer); } finally { Marshal.Release(pointer); }
                return action((IVolume)volume, Describe(device));
            }
            finally { Release(volume); Release(device); Release(enumerator); }
        }
        public static AudioState State()
        {
            try { return WithVolume((v, d) => { float level; bool muted; Check(v.GetMasterVolumeLevelScalar(out level)); Check(v.GetMute(out muted)); return new AudioState { DeviceId = d.Id, DeviceName = d.Name, Percent = (int)Math.Round(level * 100), Muted = muted }; }); }
            catch (Exception ex) { return new AudioState { Percent = -1, DeviceName = "Нет устройства", Error = ex.Message }; }
        }
        public static void SetPercent(int percent)
        {
            if (percent < 0 || percent > 100) throw new ArgumentOutOfRangeException("percent", "Громкость должна быть от 0 до 100%.");
            WithVolume((v, d) =>
            {
                Guid context = Guid.Empty;
                float previous; Check(v.GetMasterVolumeLevelScalar(out previous));
                bool muted; Check(v.GetMute(out muted));
                bool targetMute = percent == 0 || (muted && percent <= (int)Math.Round(previous * 100));
                // Endpoint drivers can change gain when mute changes. Read back both
                // values after settling and repair only the value that differs.
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    float level; Check(v.GetMasterVolumeLevelScalar(out level));
                    if ((int)Math.Round(level * 100) != percent)
                        Check(v.SetMasterVolumeLevelScalar(percent / 100f, ref context));
                    Check(v.GetMute(out muted));
                    if (muted != targetMute) Check(v.SetMute(targetMute, ref context));
                    System.Threading.Thread.Sleep(60);
                    Check(v.GetMasterVolumeLevelScalar(out level)); Check(v.GetMute(out muted));
                    if ((int)Math.Round(level * 100) == percent && muted == targetMute) return true;
                }
                throw new InvalidOperationException("Устройство не подтвердило громкость и выключение звука. Повторите команду.");
            });
        }
        public static void SetMute(bool muted) { WithVolume((v, d) => { Guid context = Guid.Empty; Check(v.SetMute(muted, ref context)); return true; }); }
        private static void SetRole(string id, int role)
        {
            object policy = new Policy();
            try { Check(((IPolicy)policy).SetDefaultEndpoint(id, role)); }
            finally { Release(policy); }
        }
        public static void RestoreOutputs(string consoleId, string multimediaId)
        {
            SetRole(consoleId, 0); SetRole(multimediaId, 1);
        }
        public static void Select(string id)
        {
            if (!Devices().Exists(d => String.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Устройство отключено или недоступно. Подключите его или выберите другое в настройках.");
            string before0 = DefaultId(0), before1 = DefaultId(1);
            try
            {
                SetRole(id, 0); SetRole(id, 1);
                if (DefaultId(0) != id || DefaultId(1) != id) throw new InvalidOperationException("Windows не подтвердила переключение.");
            }
            catch (Exception original)
            {
                try { RestoreOutputs(before0, before1); }
                catch (Exception rollback) { throw new AggregateException(original, rollback); }
                throw;
            }
        }
    }
    internal static class Native
    {
        [DllImport("ole32.dll")] internal static extern int PropVariantClear(ref Variant value);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    }
    [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] internal struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class Enumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out ICollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface ICollection
    { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IProperties store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IProperties
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out Variant value);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IVolume
    {
        void RegisterControlChangeNotify(); void UnregisterControlChangeNotify(); void GetChannelCount(); void SetMasterVolumeLevel();
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel();
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(); void SetChannelVolumeLevelScalar(); void GetChannelVolumeLevel(); void GetChannelVolumeLevelScalar();
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] internal class Policy { }
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IPolicy
    {
        // These slots are never called; only the SetDefaultEndpoint ABI is used.
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat(); void GetProcessingPeriod();
        void SetProcessingPeriod(); void GetShareMode(); void SetShareMode(); void GetPropertyValue(); void SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
}
