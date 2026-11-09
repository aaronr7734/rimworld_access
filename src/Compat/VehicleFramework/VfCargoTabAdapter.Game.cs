using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Vehicle Framework's Vehicles.ITab_Vehicle_Cargo (a vehicle's
    /// cargo hold, with drop actions and pending-load rows) and its base
    /// Vehicles.ITab_Airdrop_Container (the plain storage tab VF puts on airdrop containers: same
    /// item list, no header, no dropping). One adapter instance serves both tab types. Every
    /// reflected member used here is declared on ITab_Airdrop_Container, the cargo tab's base
    /// type, so that type is treated as required rather than optional.
    ///
    /// Mutations ride vehicle A: <see cref="OnDropThing"/> and <see cref="OnDropAll"/> invoke
    /// InterfaceDrop/InterfaceDropAll on the game's shared ITab instance, so VF's own drop logic
    /// (the TryDropOutsideVehicle gate, plus the CargoRemoved vehicle event the cargo tab's
    /// override fires) runs unmodified. Tree BUILDING never depends on the live tab instance or the
    /// game's current selection: <see cref="ResolveInventory"/> mirrors
    /// ITab_Airdrop_Container.Inventory's body directly from the inspected object, since the tab's
    /// own property reads SelThing.
    ///
    /// DEVIATION: drop outcomes are announced — vanilla communicates the result purely visually.
    ///
    /// DEVIATION: the pending-load "not yet loaded" clause on CargoPendingRow replaces vanilla's
    /// red row color for cargo queued to load but not yet in the vehicle's inventory.
    ///
    /// VF-owned strings go into RimWorldAccess.Compat.Vf.* keys because Workshop-mod Keyed XML is
    /// invisible to check_l10n_keys.py. Vanilla CORE keys are used literally; the drop-all label is
    /// baked because vanilla's "EjectAll" key ships with Biotech, not Core, and would read as a raw
    /// key for players without that DLC.
    /// </summary>
    internal sealed class VfCargoTabAdapter : InspectNodeAdapter
    {
        private readonly Type vehiclePawnType;
        private readonly Type cargoTabType;
        private readonly Type airdropTabType;

        private readonly PropertyInfo allowDroppingProperty;
        private readonly MethodInfo interfaceDropMethod;
        private readonly MethodInfo interfaceDropAllMethod;

        private readonly MethodInfo getStatValueMethod;
        private readonly object cargoCapacityDef;

        private readonly bool ready;

        public override bool Ready => ready;

        public VfCargoTabAdapter()
        {
            var surface = new ReflectionSurface("VfCargoTabAdapter");

            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            cargoTabType = surface.Type("Vehicles.ITab_Vehicle_Cargo");
            airdropTabType = surface.Type("Vehicles.ITab_Airdrop_Container");
            Type vehicleStatDefType = surface.Type("Vehicles.VehicleStatDef");
            Type vehicleStatDefOfType = surface.Type("Vehicles.VehicleStatDefOf");

            // "Inventory" is only ever consumed by mirroring its body from the inspected object
            // (see ResolveInventory), but it is still validated so a VF rename surfaces.
            surface.Property(airdropTabType, "Inventory");

            allowDroppingProperty = surface.Property(airdropTabType, "AllowDropping");
            interfaceDropMethod = surface.Method(airdropTabType, "InterfaceDrop", new[] { typeof(Thing) });
            interfaceDropAllMethod = surface.Method(airdropTabType, "InterfaceDropAll", Type.EmptyTypes);

            getStatValueMethod = vehicleStatDefType != null
                ? surface.Method(vehiclePawnType, "GetStatValue", new[] { vehicleStatDefType })
                : null;

            cargoCapacityDef = surface.Field(vehicleStatDefOfType, "CargoCapacity")?.GetValue(null);

            ready = surface.Ready && VfVehiclePawn.Ready && cargoCapacityDef != null;
        }

        // Stable English dispatch token (l10n-exempt: never displayed raw —
        // DisplayName/CategoryDisplayName below render the tab's own label).
        public override string CategoryKey => "VF Cargo";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        // Each tab's own labelKey: VF's cargo tab for vehicles, vanilla's "TabStorage" for the
        // airdrop container.
        private static string CargoTabLabel => CompatText.ResolveOrFallback("VF_TabCargo", "RimWorldAccess.Compat.Vf.CargoTab");

        public override string DisplayName(InspectTabBase tab)
        {
            return cargoTabType.IsInstanceOfType(tab) ? CargoTabLabel : "TabStorage".Translate().Resolve();
        }

        public override string CategoryDisplayName(object obj)
        {
            return vehiclePawnType.IsInstanceOfType(obj) ? CargoTabLabel : "TabStorage".Translate().Resolve();
        }

        public override bool CanExpand(object obj)
        {
            // Mirrors ITab_Airdrop_Container.IsVisible ("Inventory != null").
            return ready && ResolveInventory(obj) != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;

            try
            {
                BuildAllSections(categoryItem, obj, mode);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCargoTabAdapter.BuildChildren failed: {ex.Message}");
            }
        }

        // ---- Resolution helpers ----

        /// <summary>
        /// Mirrors ITab_Airdrop_Container.Inventory's body from the inspected object rather than
        /// reading the tab's property, which resolves off SelThing and would tie tree building to
        /// the game's current selection.
        /// </summary>
        private ThingOwner ResolveInventory(object obj)
        {
            if (obj is Pawn pawn)
                return pawn.inventory.innerContainer;
            if (obj is IThingHolder holder)
                return holder.GetDirectlyHeldThings();
            return null;
        }

        /// <summary>Used only inside the activation handlers, where the object is selected.</summary>
        private InspectTabBase ResolveTab(object obj)
        {
            Type tabType = vehiclePawnType.IsInstanceOfType(obj) ? cargoTabType : airdropTabType;
            return InspectTabManager.GetSharedInstance(tabType);
        }

        /// <summary>
        /// Clears and rebuilds the whole category after a drop — the mass and market-value
        /// header lines, the drop-all row's presence and the item list all move together,
        /// so a full rebuild is simplest and correct. Routed through the framework so the
        /// extender and parity-capture passes survive the rebuild.
        /// </summary>
        private void RebuildCategory(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            InspectionTreeBuilder.RebuildAdapterCategory(categoryItem, obj, mode, this, ResolveTab(obj));
        }

        // ---- Tree body ----

        private void BuildAllSections(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            ThingOwner inv = ResolveInventory(obj);
            bool isVehicle = vehiclePawnType.IsInstanceOfType(obj);
            Thing thing = obj as Thing;

            if (isVehicle)
                BuildHeaderLines(categoryItem, (Pawn)obj, inv);

            if (isVehicle && inv != null && inv.Any && thing != null && thing.Spawned && mode != InspectionMode.ReadOnly)
            {
                InspectionTreeItem dropAllItem = InspectNodeFactory.ActionRow(categoryItem,
                    "RimWorldAccess.Compat.Vf.CargoDropAllAction".Translate(), obj, null);
                dropAllItem.OnActivate = () => OnDropAll(categoryItem, obj, mode);
            }

            bool anyItemRow = BuildItemRows(categoryItem, obj, inv, isVehicle, thing, mode);
            bool anyPendingRow = isVehicle && BuildPendingRows(categoryItem, (Pawn)obj);

            if (!anyItemRow && !anyPendingRow)
                InspectNodeFactory.DetailLine(categoryItem, "RimWorldAccess.Compat.Vf.CargoEmpty".Translate());
        }

        private void BuildHeaderLines(InspectionTreeItem categoryItem, Pawn vehicle, ThingOwner inv)
        {
            float mass = MassUtility.GearAndInventoryMass(vehicle);
            float capacity = (float)getStatValueMethod.Invoke(vehicle, new[] { cargoCapacityDef });
            InspectNodeFactory.DetailLine(categoryItem,
                "MassCarried".Translate(mass.ToString("0.##"), capacity.ToString("0.##")));

            float marketValue = 0f;
            if (inv != null)
            {
                foreach (Thing t in inv)
                    marketValue += t.MarketValue * t.stackCount;
            }
            InspectNodeFactory.DetailLine(categoryItem,
                "RimWorldAccess.Compat.Vf.CargoMarketValueLine".Translate(
                    GenText.CapitalizeFirst(StatDefOf.MarketValue.label, StatDefOf.MarketValue), marketValue.ToString("F0")));
        }

        private bool BuildItemRows(InspectionTreeItem categoryItem, object obj, ThingOwner inv, bool isVehicle, Thing vehicleThing, InspectionMode mode)
        {
            if (inv == null || inv.Count == 0)
                return false;

            var workingList = new List<Thing>(inv);
            foreach (Thing t in workingList)
            {
                InspectNodeFactory.Section(categoryItem, t.LabelCap, t, secItem =>
                    BuildItemChildren(categoryItem, secItem, obj, isVehicle, vehicleThing, t, mode));
            }
            return true;
        }

        private void BuildItemChildren(InspectionTreeItem categoryItem, InspectionTreeItem secItem, object obj, bool isVehicle, Thing vehicleThing, Thing t, InspectionMode mode)
        {
            InspectNodeFactory.DetailLine(secItem,
                "RimWorldAccess.Compat.Vf.CargoMassLine".Translate((t.GetStatValue(StatDefOf.Mass) * t.stackCount).ToStringMass()));

            if (t.def.useHitPoints)
                InspectNodeFactory.DetailLine(secItem,
                    "RimWorldAccess.Compat.Vf.CargoHitPointsLine".Translate(t.HitPoints, t.MaxHitPoints));

            InspectNodeFactory.DetailLines(secItem, t.DescriptionDetailed);

            if (isVehicle && vehicleThing != null && vehicleThing.Spawned && mode != InspectionMode.ReadOnly)
            {
                InspectionTreeItem dropItem = InspectNodeFactory.ActionRow(secItem, "DropThing".Translate(), t, null);
                dropItem.OnActivate = () => OnDropThing(categoryItem, obj, t, mode);
            }
        }

        private bool BuildPendingRows(InspectionTreeItem categoryItem, Pawn vehicle)
        {
            List<TransferableOneWay> cargoToLoad = VfVehiclePawn.CargoToLoad(vehicle);
            if (cargoToLoad == null)
                return false;

            bool any = false;
            foreach (TransferableOneWay transferable in cargoToLoad)
            {
                if (transferable.AnyThing == null || transferable.CountToTransfer <= 0)
                    continue;
                if (vehicle.inventory.innerContainer.Contains(transferable.AnyThing))
                    continue;

                any = true;
                Thing anyThing = transferable.AnyThing;
                int count = transferable.CountToTransfer;
                InspectNodeFactory.Section(categoryItem,
                    "RimWorldAccess.Compat.Vf.CargoPendingRow".Translate(anyThing.LabelCapNoCount, count),
                    transferable,
                    secItem => BuildPendingChildren(secItem, anyThing));
            }
            return any;
        }

        private void BuildPendingChildren(InspectionTreeItem secItem, Thing t)
        {
            InspectNodeFactory.DetailLine(secItem,
                "RimWorldAccess.Compat.Vf.CargoMassLine".Translate((t.GetStatValue(StatDefOf.Mass) * t.stackCount).ToStringMass()));

            if (t.def.useHitPoints)
                InspectNodeFactory.DetailLine(secItem,
                    "RimWorldAccess.Compat.Vf.CargoHitPointsLine".Translate(t.HitPoints, t.MaxHitPoints));

            InspectNodeFactory.DetailLines(secItem, t.DescriptionDetailed);
        }

        private void OnDropThing(InspectionTreeItem categoryItem, object obj, Thing thing, InspectionMode mode)
        {
            try
            {
                InspectTabBase tab = ResolveTab(obj);
                bool allowDropping = (bool)allowDroppingProperty.GetValue(tab);
                Thing vehicleThing = obj as Thing;
                ThingOwner inv = ResolveInventory(obj);

                if (!allowDropping || vehicleThing == null || !vehicleThing.Spawned || inv == null || !inv.Contains(thing))
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.CargoDropFailed".Loc(thing.LabelShort));
                    return;
                }

                // The tab's own SelThing comes from the game's selector, so obj must be selected
                // before InterfaceDrop can see it.
                if (Find.Selector.SingleSelectedThing != obj)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(obj, playSound: false, forceDesignatorDeselect: false);
                }

                SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                bool result = (bool)interfaceDropMethod.Invoke(tab, new object[] { thing });

                RebuildCategory(categoryItem, obj, mode);

                TolkHelper.Speak(result
                    ? "RimWorldAccess.Compat.Vf.CargoDropped".Loc(thing.LabelShort)
                    : "RimWorldAccess.Compat.Vf.CargoDropFailed".Loc(thing.LabelShort));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCargoTabAdapter drop action failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.CargoDropFailed".Loc(thing.LabelShort));
            }
        }

        private void OnDropAll(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            try
            {
                InspectTabBase tab = ResolveTab(obj);
                bool allowDropping = (bool)allowDroppingProperty.GetValue(tab);
                Thing vehicleThing = obj as Thing;
                ThingOwner inv = ResolveInventory(obj);

                if (!allowDropping || vehicleThing == null || !vehicleThing.Spawned || inv == null || !inv.Any)
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.CargoDropAllFailed".Loc());
                    return;
                }

                if (Find.Selector.SingleSelectedThing != obj)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(obj, playSound: false, forceDesignatorDeselect: false);
                }

                // The drawn header button plays Tick_High on drop-all.
                SoundDefOf.Tick_High.PlayOneShotOnCamera(null);
                bool result = (bool)interfaceDropAllMethod.Invoke(tab, null);

                RebuildCategory(categoryItem, obj, mode);

                TolkHelper.Speak(result
                    ? "RimWorldAccess.Compat.Vf.CargoDroppedAll".Loc()
                    : "RimWorldAccess.Compat.Vf.CargoDropAllFailed".Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCargoTabAdapter drop-all action failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.CargoDropAllFailed".Loc());
            }
        }
    }
}
