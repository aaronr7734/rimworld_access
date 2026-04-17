#if DEBUG
using System;
using System.IO;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    public static class ReloadHelper
    {
        private static bool reloadInProgress;

        public static void Reload()
        {
            if (reloadInProgress)
            {
                Log.Warning("[RimWorld Access] Reload already in progress, ignoring duplicate trigger");
                return;
            }
            reloadInProgress = true;

            try
            {
                // Prefer the path that the running assembly captured during Initialize,
                // since Assembly.Location is empty for byte-loaded assemblies (every
                // reload after the first one).
                string currentDllPath = RimWorldAccessMod.DllPath;
                if (string.IsNullOrEmpty(currentDllPath))
                {
                    currentDllPath = Assembly.GetExecutingAssembly().Location;
                }
                if (string.IsNullOrEmpty(currentDllPath) || !File.Exists(currentDllPath))
                {
                    Log.Error($"[RimWorld Access] Cannot reload: DLL path not resolvable ({currentDllPath})");
                    return;
                }

                byte[] bytes = File.ReadAllBytes(currentDllPath);
                Log.Message($"[RimWorld Access] Reload: read {bytes.Length} bytes from {currentDllPath}");

                TolkHelper.Speak("Reloading RimWorld Access", SpeechPriority.High);

                RimWorldAccessMod.Teardown();

                Assembly newAsm = Assembly.Load(bytes);
                Log.Message($"[RimWorld Access] Loaded new assembly: {newAsm.FullName}");

                Type newModType = newAsm.GetType("RimWorldAccess.RimWorldAccessMod");
                if (newModType == null)
                {
                    Log.Error("[RimWorld Access] Reload failed: RimWorldAccessMod type not found in new assembly");
                    return;
                }

                MethodInfo initMethod = newModType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
                if (initMethod == null)
                {
                    Log.Error("[RimWorld Access] Reload failed: Initialize method not found in new assembly");
                    return;
                }

                initMethod.Invoke(null, new object[] { currentDllPath });

                Type newSpeechPriority = newAsm.GetType("RimWorldAccess.SpeechPriority");
                Type newTolk = newAsm.GetType("RimWorldAccess.TolkHelper");
                if (newTolk != null && newSpeechPriority != null)
                {
                    MethodInfo speak = newTolk.GetMethod("Speak", new[] { typeof(string), newSpeechPriority });
                    object normalPriority = Enum.Parse(newSpeechPriority, "Normal");
                    speak?.Invoke(null, new[] { (object)"RimWorld Access reloaded", normalPriority });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Reload failed: {ex.Message}\n{ex.StackTrace}");
                try
                {
                    TolkHelper.Speak("Reload failed: " + ex.Message, SpeechPriority.High);
                }
                catch { }
            }
            finally
            {
                reloadInProgress = false;
            }
        }
    }
}
#endif
