using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for Vehicle Framework's Vehicles.ITab_Vehicle_Health — the tab
    /// showing a vehicle's production date and per-category stat readouts alongside a components
    /// table (per-part health, efficiency, armor ratings, hazard indicators). Entirely read-only.
    /// Structure is read straight from VehicleStatHandler.components and
    /// VehicleDef.StatCategoryDefs, mirroring VehicleTabHelper_Health.DrawHealthPanel's own draw
    /// order.
    ///
    /// DEVIATION: armor ratings are always listed, even though vanilla hides them behind the "more
    /// info" toggle. That toggle is a purely visual space-saver.
    ///
    /// DEVIATION: a base-value line replaces vanilla's color gradient on the stat value (the
    /// gradient encodes the current/base ratio), and only when the two differ.
    ///
    /// DEVIATION: the efficiency-affected-stats clause inlines VF's own VF_EfficiencyEffector
    /// hover tooltip content rather than requiring a separate hover.
    ///
    /// VF-owned strings are baked into RimWorldAccess.Compat.Vf.* keys because Workshop-mod Keyed
    /// XML is invisible to check_l10n_keys.py. Vanilla Core keys are used literally.
    ///
    /// Deliberately NOT covered: the JobSettings tab enum value (VF's own DrawJobSettings is
    /// commented out), the mouse-hover HighlightedComponent map overlay (a sighted-only hitbox
    /// highlight with no informational content beyond the row itself), and the row-click
    /// selectedComponent toggle (a pure visual highlight that mutates nothing).
    /// </summary>
    internal sealed class VfHealthTabAdapter : InspectNodeAdapter
    {
        private readonly Type vehiclePawnType;

        private readonly Type vehicleStatHandlerType;
        private readonly FieldInfo componentsField;

        private readonly Type vehicleComponentType;
        private readonly FieldInfo propsField;
        private readonly PropertyInfo healthProperty;
        private readonly PropertyInfo maxHealthProperty;
        private readonly PropertyInfo healthPercentProperty;
        private readonly PropertyInfo efficiencyProperty;
        private readonly MethodInfo armorRatingMethod;
        private readonly FieldInfo indicatorField;

        private readonly Type vehicleComponentPropertiesType;
        private readonly FieldInfo propsLabelField;
        private readonly FieldInfo categoriesField;

        private readonly Type vehicleDefType;
        private readonly MethodInfo statCategoryDefsMethod;

        private readonly Type vehicleStatDefType;
        private readonly PropertyInfo workerProperty;
        private readonly FieldInfo operationTypeField;

        private readonly Type vehicleStatWorkerType;
        private readonly MethodInfo statValueFormattedMethod;
        private readonly MethodInfo getValueMethod;
        private readonly MethodInfo getBaseValueMethod;
        private readonly MethodInfo tipSignalMethod;
        private readonly MethodInfo valueToStringMethod;

        private readonly bool ready;

        public override bool Ready => ready;

        public VfHealthTabAdapter()
        {
            var surface = new ReflectionSurface("VfHealthTabAdapter");

            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            vehicleStatHandlerType = surface.Type("Vehicles.VehicleStatHandler");
            vehicleComponentType = surface.Type("Vehicles.VehicleComponent");
            vehicleComponentPropertiesType = surface.Type("Vehicles.VehicleComponentProperties");
            vehicleDefType = surface.Type("Vehicles.VehicleDef");
            vehicleStatDefType = surface.Type("Vehicles.VehicleStatDef");
            vehicleStatWorkerType = surface.Type("Vehicles.VehicleStatWorker");

            componentsField = surface.Field(vehicleStatHandlerType, "components");

            propsField = surface.Field(vehicleComponentType, "props");
            healthProperty = surface.Property(vehicleComponentType, "Health");
            maxHealthProperty = surface.Property(vehicleComponentType, "MaxHealth");
            healthPercentProperty = surface.Property(vehicleComponentType, "HealthPercent");
            efficiencyProperty = surface.Property(vehicleComponentType, "Efficiency");
            armorRatingMethod = surface.Method(vehicleComponentType, "ArmorRating");
            indicatorField = surface.Field(vehicleComponentType, "indicator");

            propsLabelField = surface.Field(vehicleComponentPropertiesType, "label");
            categoriesField = surface.Field(vehicleComponentPropertiesType, "categories");

            statCategoryDefsMethod = surface.Method(vehicleDefType, "StatCategoryDefs", Type.EmptyTypes);

            workerProperty = surface.Property(vehicleStatDefType, "Worker");
            operationTypeField = surface.Field(vehicleStatDefType, "operationType");

            statValueFormattedMethod = vehiclePawnType != null
                ? surface.Method(vehicleStatWorkerType, "StatValueFormatted", new[] { vehiclePawnType })
                : null;
            getValueMethod = vehiclePawnType != null
                ? surface.Method(vehicleStatWorkerType, "GetValue", new[] { vehiclePawnType })
                : null;
            getBaseValueMethod = vehicleDefType != null
                ? surface.Method(vehicleStatWorkerType, "GetBaseValue", new[] { vehicleDefType })
                : null;
            tipSignalMethod = vehiclePawnType != null
                ? surface.Method(vehicleStatWorkerType, "TipSignal", new[] { vehiclePawnType })
                : null;
            valueToStringMethod = surface.Method(vehicleStatWorkerType, "ValueToString",
                new[] { typeof(float), typeof(bool), typeof(ToStringNumberSense) });

            ready = surface.Ready && VfVehiclePawn.Ready;
        }

        // Stable English dispatch token (l10n-exempt: never displayed raw —
        // DisplayName/CategoryDisplayName below render VF's own tab label).
        public override string CategoryKey => "VF Health";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        // The tab's own labelKey, the word a sighted player reads on the tab strip.
        private static string TabLabel => CompatText.ResolveOrFallback("VF_TabComponents", "RimWorldAccess.Compat.Vf.HealthTab");

        public override string DisplayName(InspectTabBase tab) => TabLabel;

        public override string CategoryDisplayName(object obj) => TabLabel;

        public override bool CanExpand(object obj)
        {
            return ready && obj is Pawn p && vehiclePawnType.IsInstanceOfType(p);
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;
            if (!(obj is Pawn vehicle) || !vehiclePawnType.IsInstanceOfType(vehicle))
                return;

            try
            {
                BuildAllSections(categoryItem, vehicle);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfHealthTabAdapter.BuildChildren failed: {ex.Message}");
            }
        }

        // ---- Tree body ----

        private void BuildAllSections(InspectionTreeItem categoryItem, Pawn vehicle)
        {
            object statHandler = VfVehiclePawn.StatHandler(vehicle);
            IList components = statHandler != null ? componentsField.GetValue(statHandler) as IList : null;

            InspectNodeFactory.Section(categoryItem, "HealthOverview".Translate(), vehicle,
                secItem => BuildOverviewChildren(secItem, vehicle));

            InspectNodeFactory.Section(categoryItem, "RimWorldAccess.Compat.Vf.HealthComponentsSection".Translate(), vehicle,
                secItem => BuildComponentsChildren(secItem, components));
        }

        private void BuildOverviewChildren(InspectionTreeItem secItem, Pawn vehicle)
        {
            string dateReadout =
                $"{Find.ActiveLanguageWorker.OrdinalNumber(vehicle.ageTracker.BirthDayOfSeasonZeroBased + 1)} {vehicle.ageTracker.BirthQuadrum.Label()}, {vehicle.ageTracker.BirthYear}";
            InspectNodeFactory.DetailLine(secItem, "RimWorldAccess.Compat.Vf.HealthProducedLine".Translate(dateReadout));

            (GenTicks.TicksAbs - vehicle.ageTracker.BirthAbsTicks).TicksToPeriod(out int years, out int quadrums, out int days, out _);
            InspectNodeFactory.DetailLine(secItem, "AgeChronological".Translate(years, quadrums, days));

            IEnumerable statDefs = statCategoryDefsMethod.Invoke(vehicle.def, null) as IEnumerable;
            if (statDefs == null)
                return;

            var seen = new HashSet<object>();
            foreach (object statDefObj in statDefs)
            {
                if (!seen.Add(statDefObj))
                    continue;

                BuildStatRow(secItem, vehicle, statDefObj);
            }
        }

        private void BuildStatRow(InspectionTreeItem secItem, Pawn vehicle, object statDefObj)
        {
            var def = (Def)statDefObj;
            object operationTypeValue = operationTypeField.GetValue(statDefObj);

            if (Convert.ToInt32(operationTypeValue) != 0)
            {
                object worker = workerProperty.GetValue(statDefObj);
                string formattedValue = statValueFormattedMethod.Invoke(worker, new object[] { vehicle }) as string ?? "";
                string label = "RimWorldAccess.Compat.Vf.HealthStatLine".Translate(def.LabelCap, formattedValue);

                InspectNodeFactory.Section(secItem, label, statDefObj,
                    statSecItem => BuildStatChildren(statSecItem, vehicle, worker));
            }
            else
            {
                InspectNodeFactory.DetailLine(secItem, def.LabelCap);
            }
        }

        private void BuildStatChildren(InspectionTreeItem statSecItem, Pawn vehicle, object worker)
        {
            string tip = tipSignalMethod.Invoke(worker, new object[] { vehicle }) as string;
            if (!string.IsNullOrEmpty(tip))
                InspectNodeFactory.DetailLine(statSecItem, tip);

            float value = (float)getValueMethod.Invoke(worker, new object[] { vehicle });
            float baseValue = (float)getBaseValueMethod.Invoke(worker, new object[] { vehicle.def });

            if (!Mathf.Approximately(value, baseValue))
            {
                string baseFormatted = valueToStringMethod.Invoke(worker, new object[] { baseValue, true, ToStringNumberSense.Absolute }) as string ?? "";
                InspectNodeFactory.DetailLine(statSecItem, "RimWorldAccess.Compat.Vf.HealthStatBaseLine".Translate(baseFormatted));
            }
        }

        private void BuildComponentsChildren(InspectionTreeItem secItem, IList components)
        {
            if (components == null)
                return;

            foreach (object component in components)
            {
                BuildComponentSection(secItem, component);
            }
        }

        private void BuildComponentSection(InspectionTreeItem secItem, object component)
        {
            object props = propsField.GetValue(component);
            string propsLabel = propsLabelField.GetValue(props) as string ?? "";
            float healthPercent = (float)healthPercentProperty.GetValue(component);
            string label = "RimWorldAccess.Compat.Vf.HealthComponentSection".Translate(propsLabel, healthPercent.ToStringPercent());

            InspectNodeFactory.Section(secItem, label, component,
                compSecItem => BuildComponentChildren(compSecItem, component, props));
        }

        private void BuildComponentChildren(InspectionTreeItem compSecItem, object component, object props)
        {
            float health = (float)healthProperty.GetValue(component);
            float maxHealth = (float)maxHealthProperty.GetValue(component);
            InspectNodeFactory.DetailLine(compSecItem,
                "RimWorldAccess.Compat.Vf.HealthComponentHealthLine".Translate(health.ToString("F0"), maxHealth.ToString("F0")));

            IList categories = categoriesField.GetValue(props) as IList;
            if (categories != null && categories.Count > 0)
            {
                float efficiency = (float)efficiencyProperty.GetValue(component);
                var categoryLabels = new List<string>();
                foreach (object category in categories)
                {
                    if (category is Def categoryDef)
                        categoryLabels.Add(categoryDef.LabelCap);
                }
                InspectNodeFactory.DetailLine(compSecItem,
                    "RimWorldAccess.Compat.Vf.HealthComponentEfficiencyLine".Translate(efficiency.ToStringPercent(), string.Join(", ", categoryLabels)));
            }
            else
            {
                InspectNodeFactory.DetailLine(compSecItem, "RimWorldAccess.Compat.Vf.HealthComponentEfficiencyNone".Translate());
            }

            foreach (DamageArmorCategoryDef armorCategoryDef in DefDatabase<DamageArmorCategoryDef>.AllDefsListForReading)
            {
                object[] args = { armorCategoryDef, null };
                float armorRating = (float)armorRatingMethod.Invoke(component, args);
                float upgraded = (float)args[1];
                string armorLabel = armorRating.ToStringByStyle(armorCategoryDef.armorRatingStat.toStringStyle);

                if (Mathf.Approximately(upgraded, 0))
                {
                    InspectNodeFactory.DetailLine(compSecItem,
                        "RimWorldAccess.Compat.Vf.HealthComponentArmorLine".Translate(armorCategoryDef.armorRatingStat.LabelCap, armorLabel));
                }
                else
                {
                    string baseArmorLabel = (armorRating - upgraded).ToStringByStyle(armorCategoryDef.armorRatingStat.toStringStyle);
                    InspectNodeFactory.DetailLine(compSecItem,
                        "RimWorldAccess.Compat.Vf.HealthComponentArmorUpgradedLine".Translate(armorCategoryDef.armorRatingStat.LabelCap, armorLabel, baseArmorLabel));
                }
            }

            if (indicatorField.GetValue(component) is Def indicatorDef)
                InspectNodeFactory.DetailLine(compSecItem, "RimWorldAccess.Compat.Vf.HealthComponentIndicatorLine".Translate(indicatorDef.LabelCap));
        }
    }
}
