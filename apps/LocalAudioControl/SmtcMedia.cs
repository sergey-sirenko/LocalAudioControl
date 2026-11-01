using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace LocalAudioControl
{
    /// <summary>
    /// Локальное управление текущим медиаплеером через Windows SMTC
    /// (GlobalSystemMediaTransportControlsSessionManager). В отличие от
    /// SendInput, команда не идёт в активное окно, поэтому при полноэкранном
    /// RDP переключается локальный Chrome/плеер, а не удалённый сеанс.
    /// WinRT вызывается через отражение: сборка не требует Windows SDK.
    /// </summary>
    internal static class SmtcMedia
    {
        private const string ManagerTypeName =
            "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType=WindowsRuntime";
        private const int TimeoutMilliseconds = 2000;

        private static readonly object Gate = new object();
        private static Type managerType;
        private static object manager;
        private static MethodInfo requestAsync;
        private static MethodInfo getCurrentSession;
        private static MethodInfo asTask;

        internal static bool TrySkipNext()
        {
            lock (Gate)
            {
                try
                {
                    object session = CurrentSession();
                    if (session == null) return false;

                    Type type = session.GetType();
                    object playback = type.GetMethod("GetPlaybackInfo").Invoke(session, null);
                    object controls = playback.GetType().GetProperty("Controls").GetValue(playback, null);
                    object enabled = controls.GetType().GetProperty("IsNextEnabled").GetValue(controls, null);
                    if (!(enabled is bool) || !(bool)enabled) return false;

                    object appId = type.GetProperty("SourceAppUserModelId").GetValue(session, null);
                    object operation = type.GetMethod("TrySkipNextAsync").Invoke(session, null);
                    bool completed;
                    object skipped = Await(operation, typeof(bool), out completed);
                    // Если операция не успела завершиться, клавишу не дублируем:
                    // она может сработать позже и переключить два трека.
                    if (!completed) return true;
                    if (skipped is bool && (bool)skipped)
                    {
                        Storage.Log("SmtcMedia: next in " + appId);
                        return true;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    manager = null;
                    Storage.Log(ex);
                    return false;
                }
            }
        }

        private static object CurrentSession()
        {
            if (manager == null)
            {
                if (managerType == null)
                {
                    managerType = Type.GetType(ManagerTypeName, true);
                    requestAsync = managerType.GetMethod("RequestAsync");
                    getCurrentSession = managerType.GetMethod("GetCurrentSession");
                }
                bool completed;
                object value = Await(requestAsync.Invoke(null, null), managerType, out completed);
                if (!completed) return null;
                manager = value;
            }
            return manager == null ? null : getCurrentSession.Invoke(manager, null);
        }

        private static object Await(object operation, Type resultType, out bool completed)
        {
            completed = false;
            if (asTask == null)
            {
                string path = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location),
                    "System.Runtime.WindowsRuntime.dll");
                Type extensions = Assembly.LoadFrom(path).GetType("System.WindowsRuntimeSystemExtensions", true);
                asTask = extensions.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsGenericType
                        && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition().Name == "IAsyncOperation`1");
            }
            var task = (Task)asTask.MakeGenericMethod(resultType).Invoke(null, new [] { operation });
            if (!task.Wait(TimeoutMilliseconds)) return null;
            completed = true;
            return task.GetType().GetProperty("Result").GetValue(task);
        }
    }
}
