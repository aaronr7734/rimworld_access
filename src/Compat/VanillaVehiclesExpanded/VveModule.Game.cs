using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Vanilla Vehicles Expanded. Its gizmos, settings and stat additions ride the generic readers
    /// and Vehicle Framework's module; the one gap is the research lock it transpiles into the
    /// Research window's Start button tooltip (a vehicle project stays locked until a wreck of that
    /// vehicle is restored), which our research detail view rebuilds without the tooltip. VVE's own
    /// public appender is invoked so the reason text and its conditions stay VVE's.
    /// </summary>
    internal sealed class VveModule : CompatModule
    {
        public override string TargetPackageId => "OskarPotocki.VanillaVehiclesExpanded";

        public override void Activate(Harmony harmony)
        {
            var surface = new ReflectionSurface("VveModule");
            Type patchType = surface.Type("VanillaVehiclesExpanded.MainTabWindow_Research_DrawStartButton_Patch");
            MethodInfo addLockedReason = surface.Method(patchType, "AddLockedReason",
                new[] { typeof(ResearchProjectDef), typeof(List<string>) });
            if (!surface.Ready)
            {
                return;
            }

            ResearchLockedReasonProviders.Register(
                (Action<ResearchProjectDef, List<string>>)Delegate.CreateDelegate(
                    typeof(Action<ResearchProjectDef, List<string>>), addLockedReason));
        }
    }
}
