using System;
using System.Reflection;
using HarmonyLib;

namespace RimWorldAccess
{
    /// <summary>
    /// RimTalk's own settings entry points, resolved once for the whole module: <c>RimTalk.Settings</c>
    /// (the Mod subclass that both hosts the static <c>Get()</c> every RimTalk surface reads its live
    /// settings through and is the type <see cref="Verse.LoadedModManager"/> opens the settings dialog
    /// for), plus the TTS addon's <c>TtsRuntime</c>/<c>TTSSettings</c> pair. Four gates resolved
    /// <c>RimTalk.Settings</c> or that pair independently, so a rename was caught in N places with N
    /// failure modes.
    ///
    /// The three types resolve OUTSIDE <see cref="Ready"/>, which covers only <c>Get</c>: the TTS
    /// types belong to a separately installed addon, and the consumers that take nothing but a type
    /// declare it on their own surface and bind their own members on it. Tying the addon's types to
    /// core RimTalk's <c>Get</c> would let either one's drift take the other's feature down.
    ///
    /// Reads only. Every write to a settings field stays at its own call site with its own
    /// MUTATION-C citation.
    /// </summary>
    internal static class RimTalkSettingsFacade
    {
        private static bool typesResolved;
        private static Type settingsType;
        private static Type ttsRuntimeType;
        private static Type ttsSettingsType;

        private static MethodInfo getMethod;

        private static readonly LazyReflectionGate members = new LazyReflectionGate("RimTalkSettingsFacade", ResolveMembers);

        /// <summary>RimTalk's <c>Settings</c> Mod subclass, null when RimTalk is not loaded.</summary>
        public static Type SettingsType
        {
            get { EnsureTypes(); return settingsType; }
        }

        /// <summary>The TTS addon's runtime singleton (<c>TtsRuntime.Current</c>), null when the addon is not loaded.</summary>
        public static Type TtsRuntimeType
        {
            get { EnsureTypes(); return ttsRuntimeType; }
        }

        /// <summary>The TTS addon's settings object, null when the addon is not loaded.</summary>
        public static Type TtsSettingsType
        {
            get { EnsureTypes(); return ttsSettingsType; }
        }

        /// <summary>True when <c>Settings.Get()</c> resolved.</summary>
        public static bool Ready
        {
            get { return members.Ensure(); }
        }

        /// <summary>
        /// The live <c>RimTalkSettings</c> instance, the way RimTalk's own overlay and debug window
        /// reach it. Null when the reflection declined or RimTalk has not built its settings yet.
        /// </summary>
        public static object Get()
        {
            if (!members.Ensure())
            {
                return null;
            }
            try
            {
                return getMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("RimTalkSettingsFacade.Get", ex);
                return null;
            }
        }

        private static void EnsureTypes()
        {
            if (typesResolved)
            {
                return;
            }
            typesResolved = true;

            settingsType = AccessTools.TypeByName("RimTalk.Settings");
            ttsRuntimeType = AccessTools.TypeByName("RimTalk.TTS.TtsRuntime");
            ttsSettingsType = AccessTools.TypeByName("RimTalk.TTS.Data.TTSSettings");
        }

        private static bool ResolveMembers(ReflectionSurface surface)
        {
            MethodInfo get = surface.Method(surface.Supplied("RimTalk.Settings", SettingsType), "Get", Type.EmptyTypes);

            if (!surface.Ready)
            {
                return false;
            }

            getMethod = get;
            return true;
        }
    }
}
