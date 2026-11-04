using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    internal static partial class ClCompat
    {
        /// <summary>
        /// The "Tags and Loadout Composition" section the mod transpiles into the Gear tab
        /// (<c>Panel_GearTab</c>): its two dialog buttons and one status row per prosthetic of the
        /// pawn's active tags, worded by the panel's own private <c>ProstheticStatus</c>.
        /// </summary>
        internal static class Gear
        {
            private static readonly MethodInfo prostheticStatusMethod;
            private static readonly MethodInfo loadoutItemsGetter;
            private static readonly MethodInfo hypotheticalProstheticsMethod;
            private static readonly MethodInfo satisfiedCountMethod;
            private static readonly bool ready;

            static Gear()
            {
                var surface = new ReflectionSurface("ClCompat.Gear");
                Type panelType = surface.Type("Inventory.Panel_GearTab");
                Type slotsType = surface.Type("Inventory.ProstheticSlots");
                Type prostheticUtilityType = surface.Type("Inventory.ProstheticUtility");

                loadoutItemsGetter = surface.Property(loadoutType, "Items")?.GetGetMethod(true);
                satisfiedCountMethod = surface.Method(prostheticUtilityType, "SatisfiedCount",
                    new[] { typeof(Pawn), typeof(ThingDef) });
                if (slotsType != null && loadoutType != null && stateType != null && itemType != null)
                {
                    prostheticStatusMethod = surface.Method(panelType, "ProstheticStatus",
                        new[] { typeof(Pawn), loadoutType, slotsType, itemType });
                    hypotheticalProstheticsMethod = surface.Method(loadoutType, "HypotheticalProsthetics",
                        new[] { stateType, typeof(BodyDef) });
                }

                ready = ClCompat.Ready && surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>The prosthetic items of the tags active in the current state, each once, in tag order.</summary>
            public static List<object> ActiveProsthetics(object loadout)
            {
                var result = new List<object>();
                if (!ready)
                {
                    return result;
                }
                foreach (object item in ToList(Guarded.Get<IEnumerable>(loadoutItemsGetter, loadout, "ClCompat.Gear.Items")))
                {
                    if (ItemIsProsthetic(item) && !result.Contains(item))
                    {
                        result.Add(item);
                    }
                }
                return result;
            }

            /// <summary>Where the current state's prosthetics would go, the placement the status reads conflicts from.</summary>
            public static object CurrentSlots(Pawn pawn, object loadout)
            {
                return ready
                    ? Guarded.Call(hypotheticalProstheticsMethod, loadout, "ClCompat.Gear.CurrentSlots",
                        LoadoutCurrentState(loadout), pawn.RaceProps.body)
                    : null;
            }

            public static string ProstheticStatus(Pawn pawn, object loadout, object slots, object item)
            {
                return ready
                    ? Guarded.Call(prostheticStatusMethod, null, "ClCompat.Gear.ProstheticStatus", pawn, loadout, slots, item) as string ?? ""
                    : "";
            }

            public static int SatisfiedCount(Pawn pawn, ThingDef def)
            {
                return ready && Guarded.Call(satisfiedCountMethod, null, "ClCompat.Gear.SatisfiedCount", pawn, def) is int count
                    ? count
                    : 0;
            }
        }
    }
}
