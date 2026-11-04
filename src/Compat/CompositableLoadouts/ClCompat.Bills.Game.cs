using System;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    internal static partial class ClCompat
    {
        /// <summary>
        /// The mod's "X per Tag" bill repeat mode: the def, the bill-to-tag map on
        /// <c>LoadoutManager</c>, the count it targets, and the panel it transpiles into
        /// Dialog_BillConfig (<c>Panel_BillConfig.Draw</c>).
        /// </summary>
        internal static class Bills
        {
            private const string PerTagDefName = "W_PerTag"; // l10n-exempt: def name

            private static readonly MethodInfo tagForMethod;
            private static readonly MethodInfo setTagForBillMethod;
            private static readonly MethodInfo colonistCountForMethod;
            private static readonly MethodInfo desiredTargetCountMethod;
            private static readonly MethodInfo tagItemForMethod;
            private static readonly MethodInfo itemCopyToMethod;
            private static readonly MethodInfo panelDrawMethod;
            private static readonly bool ready;

            private static BillRepeatModeDef perTagMode;

            static Bills()
            {
                var surface = new ReflectionSurface("ClCompat.Bills");
                Type billUtilityType = surface.Type("Inventory.BillUtility");
                Type panelType = surface.Type("Inventory.Panel_BillConfig");

                tagForMethod = surface.Method(managerType, "TagFor", new[] { typeof(Bill_Production) });
                colonistCountForMethod = surface.Method(managerType, "ColonistCountFor", new[] { typeof(Bill_Production) });
                desiredTargetCountMethod = surface.Method(billUtilityType, "DesiredTargetCount", new[] { typeof(Bill_Production) });
                tagItemForMethod = surface.Method(tagType, "ItemFor", new[] { typeof(ThingDef) });
                itemCopyToMethod = surface.Method(itemType, "CopyTo", new[] { typeof(Bill_Production) });
                panelDrawMethod = surface.Method(panelType, "Draw",
                    new[] { typeof(Dialog_BillConfig), typeof(Listing_Standard), typeof(Bill_Production) });
                if (tagType != null)
                {
                    setTagForBillMethod = surface.Method(managerType, "SetTagForBill", new[] { typeof(Bill_Production), tagType });
                }

                ready = ClCompat.Ready && surface.Ready;
            }

            public static bool Ready
            {
                get { return ready; }
            }

            /// <summary>The panel the mod draws into the bill dialog's repeat-mode section, for its ring markers.</summary>
            public static MethodInfo PanelDraw
            {
                get { return ready ? panelDrawMethod : null; }
            }

            public static bool IsPerTag(Bill_Production bill)
            {
                if (!ready || bill == null)
                {
                    return false;
                }
                if (perTagMode == null)
                {
                    perTagMode = DefDatabase<BillRepeatModeDef>.GetNamedSilentFail(PerTagDefName);
                }
                return perTagMode != null && bill.repeatMode == perTagMode;
            }

            /// <summary>The tag the bill targets, null until one is picked.</summary>
            public static object TagFor(Bill_Production bill)
            {
                return ManagerLoaded() ? Guarded.Call(tagForMethod, null, "ClCompat.Bills.TagFor", bill) : null;
            }

            public static void SetTagForBill(Bill_Production bill, object tag)
            {
                if (ManagerLoaded())
                {
                    Guarded.Call(setTagForBillMethod, null, "ClCompat.Bills.SetTagForBill", bill, tag);
                }
            }

            /// <summary>Colonists on the bill's map whose loadout holds the bill's tag.</summary>
            public static int ColonistCountFor(Bill_Production bill)
            {
                return ManagerLoaded()
                    && Guarded.Call(colonistCountForMethod, null, "ClCompat.Bills.ColonistCountFor", bill) is int count
                    ? count
                    : 0;
            }

            /// <summary>The count the bill works toward: (colonists + extra copies) times the per-colonist count.</summary>
            public static int DesiredTargetCount(Bill_Production bill)
            {
                return ManagerLoaded()
                    && Guarded.Call(desiredTargetCountMethod, null, "ClCompat.Bills.DesiredTargetCount", bill) is int count
                    ? count
                    : 0;
            }

            /// <summary>The tag's item that can be made as <paramref name="def"/>, or null.</summary>
            public static object ItemFor(object tag, ThingDef def)
            {
                return tag == null || def == null ? null : Guarded.Call(tagItemForMethod, tag, "ClCompat.Bills.ItemFor", def);
            }

            /// <summary>The item's stuff, quality and hit point filters written onto the bill.</summary>
            public static void CopyItemTo(object item, Bill_Production bill)
            {
                if (item != null)
                {
                    Guarded.Call(itemCopyToMethod, item, "ClCompat.Bills.CopyItemTo", bill);
                }
            }
        }
    }
}
