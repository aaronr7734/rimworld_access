using System;
using System.Collections.Generic;
using System.Reflection;

namespace RimWorldAccess
{
    /// <summary>
    /// Invoke-time companion to <see cref="ReflectionSurface"/>: the surface guards resolve time,
    /// these primitives guard the call itself, null-safe, with a typed fallback on any miss or throw
    /// and one failure log per context per session. The standard for new facade read plumbing.
    /// Gated WRITES never route through the readers: a mutation keeps its own vehicle and its
    /// MUTATION-C citation at the write site, per the mutation doctrine.
    /// </summary>
    internal static class Guarded
    {
        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        /// <summary>Invokes a getter (or any zero-arg method) and casts, with a typed fallback.</summary>
        public static T Get<T>(MethodInfo getter, object instance, string context, T fallback = default(T))
        {
            if (getter == null || (instance == null && !getter.IsStatic))
            {
                return fallback;
            }
            try
            {
                return getter.Invoke(instance, null) is T value ? value : fallback;
            }
            catch (Exception ex)
            {
                Fail(context, ex);
                return fallback;
            }
        }

        /// <summary>Reads a field and casts, with a typed fallback.</summary>
        public static T FieldOf<T>(FieldInfo field, object instance, string context, T fallback = default(T))
        {
            if (field == null || (instance == null && !field.IsStatic))
            {
                return fallback;
            }
            try
            {
                return field.GetValue(instance) is T value ? value : fallback;
            }
            catch (Exception ex)
            {
                Fail(context, ex);
                return fallback;
            }
        }

        /// <summary>Invokes a method with arguments; null on any miss or throw.</summary>
        public static object Call(MethodInfo method, object instance, string context, params object[] args)
        {
            if (method == null || (instance == null && !method.IsStatic))
            {
                return null;
            }
            try
            {
                return method.Invoke(instance, args);
            }
            catch (Exception ex)
            {
                Fail(context, ex);
                return null;
            }
        }

        /// <summary>Runs a key handler's action; every throw is logged and spoken as <paramref name="failureKey"/>.</summary>
        public static void Run(string context, Action action, string failureKey)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"{context} failed: {ex.Message}");
                TolkHelper.Speak(failureKey.Loc());
            }
        }

        /// <summary>Reports a reflection call that threw, once per context for the session.</summary>
        private static void Fail(string context, Exception ex)
        {
            if (loggedFailures.Add(context))
            {
                ModLogger.Error(context + " failed: " + ex.Message);
            }
        }
    }
}
