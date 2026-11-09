using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Harmony targets for the raw GUI taps. On 1.7 the UnityStubs assembly is outside the runtime's
    /// JitPatchableAssemblies, so a private core reached only through a public overload can be
    /// inlined and a patch on it never runs; the public overloads callers invoke are the only
    /// reliable anchors. Where one public overload forwards into a sibling without being inlined,
    /// each tap's depth counter keeps the inner call silent.
    /// </summary>
    internal static class RawGuiOverloads
    {
        /// <summary>Every public static GUI.<paramref name="name"/> overload whose parameters begin with <paramref name="leading"/>.</summary>
        internal static IEnumerable<MethodBase> Of(string name, params Type[] leading)
        {
            return typeof(GUI).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == name && Begins(m.GetParameters(), leading))
                .Cast<MethodBase>();
        }

        private static bool Begins(ParameterInfo[] parameters, Type[] leading)
        {
            if (parameters.Length < leading.Length)
            {
                return false;
            }
            for (int i = 0; i < leading.Length; i++)
            {
                if (parameters[i].ParameterType != leading[i])
                {
                    return false;
                }
            }
            return true;
        }
    }
}
