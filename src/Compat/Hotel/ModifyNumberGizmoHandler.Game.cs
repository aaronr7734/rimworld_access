using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// The Gizmo_ModifyNumber&lt;T&gt; family: a bare Gizmo with a Title, ButtonUp/ButtonDown/
    /// ButtonCenter and a T[] selection, duplicated verbatim into Hospitality (guest bed, vending
    /// price, vending threshold) and CashRegister (radius) with no shared base we can reference.
    /// One instance per concrete type, each with its own surface. GizmoOnGUI never returns
    /// Clicked and no subclass overrides ProcessInput, so the slider session and the centre-button
    /// option are the only ways to change them, matching the three mouse buttons.
    /// </summary>
    internal sealed class ModifyNumberGizmoHandler : GizmoHandlerBase
    {
        internal enum Kind
        {
            GuestBed,
            VendingPrice,
            VendingThreshold,
            Radius,
        }

        // Display ceiling for the mod's unbounded int setters (SetRentalFee, SetPrice,
        // SetEmptyThreshold all clamp to [0, int.MaxValue]).
        private const float UnboundedMoneyCeiling = 100000f;

        private static readonly MethodInfo typedReader =
            typeof(ModifyNumberGizmoHandler).GetMethod(nameof(TypedReader), BindingFlags.NonPublic | BindingFlags.Static);

        private readonly Kind kind;
        private readonly bool ready;
        private readonly Type elementType;

        private readonly PropertyInfo title;
        private readonly MethodInfo buttonUp;
        private readonly MethodInfo buttonDown;
        private readonly MethodInfo buttonCenter;
        private readonly FieldInfo selection;
        private readonly MethodInfo toFromToString;

        private readonly PropertyInfo rentalFee;
        private readonly FieldInfo feeStep;
        private readonly PropertyInfo bedStats;
        private readonly FieldInfo attractiveness;
        private readonly MethodInfo updateRoyaltyStats;
        private readonly FieldInfo metRoyalTitles;
        private readonly FieldInfo textNextTitleReq;

        private readonly FieldInfo vendingMachine;
        private readonly PropertyInfo currentPrice;
        private readonly PropertyInfo currentEmptyThreshold;
        private readonly PropertyInfo totalSold;
        private readonly PropertyInfo vendingProperties;
        private readonly FieldInfo priceSteps;

        private readonly PropertyInfo radius;
        private readonly FieldInfo radiusStep;
        private readonly FieldInfo infiniteRadius;
        private readonly MethodInfo radiusString;
        private readonly MethodInfo includeRegionString;

        internal ModifyNumberGizmoHandler(Type gizmoType, Kind kind)
        {
            this.kind = kind;
            var surface = new ReflectionSurface("ModifyNumberGizmoHandler." + kind);
            surface.Supplied(gizmoType.FullName, gizmoType);
            title = surface.Property(gizmoType, "Title");
            buttonUp = surface.Method(gizmoType, "ButtonUp", Type.EmptyTypes);
            buttonDown = surface.Method(gizmoType, "ButtonDown", Type.EmptyTypes);
            buttonCenter = surface.Method(gizmoType, "ButtonCenter", Type.EmptyTypes);
            selection = surface.Field(gizmoType, "selection");
            toFromToString = surface.Method(gizmoType, "ToFromToString");
            elementType = selection?.FieldType.GetElementType();

            switch (kind)
            {
                case Kind.GuestBed:
                    Type bedType = surface.Type("Hospitality.Building_GuestBed");
                    Type statsType = surface.Type("Hospitality.Building_GuestBed+BedStats");
                    rentalFee = surface.Property(bedType, "RentalFee");
                    feeStep = surface.Field(bedType, "FeeStep");
                    bedStats = surface.Property(bedType, "Stats");
                    updateRoyaltyStats = surface.Method(bedType, "UpdateRoyaltyStats", Type.EmptyTypes);
                    attractiveness = surface.Field(statsType, "attractiveness");
                    metRoyalTitles = surface.Field(statsType, "metRoyalTitles");
                    textNextTitleReq = surface.Field(statsType, "textNextTitleReq");
                    break;
                case Kind.VendingPrice:
                case Kind.VendingThreshold:
                    Type compType = surface.Type("Hospitality.CompVendingMachine");
                    Type propsType = surface.Type("Hospitality.CompProperties_VendingMachine");
                    vendingMachine = surface.Field(gizmoType, "vendingMachine");
                    currentPrice = surface.Property(compType, "CurrentPrice");
                    currentEmptyThreshold = surface.Property(compType, "CurrentEmptyThreshold");
                    totalSold = surface.Property(compType, "TotalSold");
                    vendingProperties = surface.Property(compType, "Properties");
                    priceSteps = surface.Field(propsType, "priceSteps");
                    break;
                case Kind.Radius:
                    Type registerType = surface.Type("CashRegister.Building_CashRegister");
                    radius = surface.Property(registerType, "Radius");
                    radiusStep = surface.Field(registerType, "RadiusStep");
                    infiniteRadius = surface.Field(registerType, "InfiniteRadius");
                    radiusString = surface.Method(gizmoType, "GetRadiusString", new[] { typeof(object) });
                    includeRegionString = surface.Method(gizmoType, "GetIncludeRegionString", Type.EmptyTypes);
                    break;
            }
            ready = surface.Ready && elementType != null;
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = ready ? Guarded.Get<string>(title.GetGetMethod(true), gizmo, "ModifyNumberGizmoHandler title") : null;
            return !string.IsNullOrEmpty(label);
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!ready)
            {
                return false;
            }
            try
            {
                switch (kind)
                {
                    case Kind.GuestBed:
                        status = "RimWorldAccess.Compat.Hospitality.BedFeeStatus".Translate(FeeText(gizmo), AttractivenessText(gizmo));
                        break;
                    case Kind.VendingPrice:
                        status = "RimWorldAccess.Compat.Hospitality.VendingPriceStatus".Translate(Money(VendingInt(gizmo, currentPrice)));
                        break;
                    case Kind.VendingThreshold:
                        status = "RimWorldAccess.Compat.Hospitality.VendingContentStatus".Translate(
                            Money(VendingInt(gizmo, currentEmptyThreshold)), Money(VendingInt(gizmo, totalSold)));
                        break;
                    case Kind.Radius:
                        status = "RimWorldAccess.Compat.CashRegister.RadiusStatus".Translate(
                            RadiusText(gizmo), includeRegionString.Invoke(gizmo, null).ToString());
                        break;
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("ModifyNumberGizmoHandler status", ex);
                return false;
            }
        }

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            adapter = null;
            if (!ready || !(selection.GetValue(gizmo) is Array selected) || selected.Length == 0)
            {
                return false;
            }
            try
            {
                object first = selected.GetValue(0);
                switch (kind)
                {
                    case Kind.GuestBed:
                        adapter = MakeAdapter(gizmo, "RimWorldAccess.Compat.Hospitality.BedRentalFeeTitle".Translate(),
                            (int)rentalFee.GetValue(first, null), UnboundedMoneyCeiling, Convert.ToSingle(feeStep.GetValue(null)),
                            v => selected.Length == 1 ? Money((int)v) : FeeText(gizmo));
                        break;
                    case Kind.VendingPrice:
                        adapter = MakeAdapter(gizmo, "RimWorldAccess.Compat.Hospitality.VendingPriceTitle".Translate(),
                            VendingInt(gizmo, currentPrice), UnboundedMoneyCeiling, VendingStep(gizmo), v => Money((int)v));
                        break;
                    case Kind.VendingThreshold:
                        adapter = MakeAdapter(gizmo, "RimWorldAccess.Compat.Hospitality.VendingThresholdTitle".Translate(),
                            VendingInt(gizmo, currentEmptyThreshold), UnboundedMoneyCeiling, VendingStep(gizmo), v => Money((int)v));
                        break;
                    case Kind.Radius:
                        // Building_CashRegister.ChangeRadius clamps to [0, InfiniteRadius - 1].
                        adapter = MakeAdapter(gizmo, "RimWorldAccess.Compat.CashRegister.RadiusTitle".Translate(),
                            (float)radius.GetValue(first, null), Convert.ToSingle(infiniteRadius.GetValue(null)) - 1f,
                            Convert.ToSingle(radiusStep.GetValue(null)), v => RadiusText(gizmo));
                        break;
                }
                return adapter != null;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("ModifyNumberGizmoHandler slider", ex);
                return false;
            }
        }

        /// <summary>
        /// Every adjustment steps the gizmo once through its own ButtonUp/ButtonDown, which applies
        /// to the whole selection exactly as one click does; the session always asks for exactly
        /// one step away from the value the adapter was built with.
        /// </summary>
        private GizmoSliderAdapter MakeAdapter(Gizmo gizmo, string adapterTitle, float value, float max, float step, Func<float, string> describe)
        {
            return new GizmoSliderAdapter
            {
                Title = adapterTitle,
                Value = value,
                Min = 0f,
                Max = max,
                Step = step,
                DescribeValue = describe,
                Write = newValue =>
                {
                    if (newValue > value)
                    {
                        Guarded.Call(buttonUp, gizmo, "ModifyNumberGizmoHandler ButtonUp");
                    }
                    else if (newValue < value)
                    {
                        Guarded.Call(buttonDown, gizmo, "ModifyNumberGizmoHandler ButtonDown");
                    }
                },
            };
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (!ready)
            {
                return false;
            }
            string label;
            switch (kind)
            {
                case Kind.GuestBed:
                    label = "RimWorldAccess.Compat.Hospitality.ResetRentalFee".Translate();
                    break;
                case Kind.VendingPrice:
                    label = "RimWorldAccess.Compat.Hospitality.SetAutoPrice".Translate();
                    break;
                case Kind.VendingThreshold:
                    label = "RimWorldAccess.Compat.Hospitality.SetThresholdAuto".Translate();
                    break;
                default:
                    label = "RimWorldAccess.Compat.CashRegister.ToggleIncludeRoom".Translate();
                    break;
            }
            options.Add(new FloatMenuOption(label, () => Guarded.Call(buttonCenter, gizmo, "ModifyNumberGizmoHandler ButtonCenter")));
            return true;
        }

        /// <summary>The royal-title box Gizmo_GuestBed.DrawTooltipBox opens on hover, for a single bed like the mod's own.</summary>
        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            if (!ready || kind != Kind.GuestBed || !ModsConfig.RoyaltyActive
                || !(selection.GetValue(gizmo) is Array selected) || selected.Length != 1)
            {
                return false;
            }
            try
            {
                object bed = selected.GetValue(0);
                updateRoyaltyStats.Invoke(bed, null);
                object stats = bedStats.GetValue(bed, null);
                if (stats == null)
                {
                    return false;
                }

                var titleLabels = new List<string>();
                if (metRoyalTitles.GetValue(stats) is RoyalTitleDef[] met)
                {
                    foreach (RoyalTitleDef royalTitle in met)
                    {
                        if (royalTitle != null)
                        {
                            titleLabels.Add(royalTitle.GetLabelCapForBothGenders());
                        }
                    }
                }

                var parts = new List<string>
                {
                    titleLabels.Count > 0
                        ? "RimWorldAccess.Compat.Hospitality.RoyalTitlesSatisfied".Translate(titleLabels.ToCommaList()).ToString()
                        : "RimWorldAccess.Compat.Hospitality.NoRoyalTitlesSatisfied".Translate().ToString(),
                };
                string nextTitleReq = textNextTitleReq.GetValue(stats)?.ToString();
                if (!string.IsNullOrEmpty(nextTitleReq))
                {
                    parts.Add(nextTitleReq);
                }
                description = CompatText.JoinSentences(parts);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("ModifyNumberGizmoHandler royal titles", ex);
                return false;
            }
        }

        private int VendingInt(Gizmo gizmo, PropertyInfo property)
        {
            return (int)property.GetValue(vendingMachine.GetValue(gizmo), null);
        }

        private float VendingStep(Gizmo gizmo)
        {
            object props = vendingProperties.GetValue(vendingMachine.GetValue(gizmo), null);
            return Convert.ToSingle(priceSteps.GetValue(props));
        }

        private static string Money(int amount)
        {
            return ((float)amount).ToStringMoney("F0");
        }

        // The gizmo's own ToFromToString fed live values, so a multi-selection reads "min - max"
        // in the mod's own format after every step, not the snapshot its constructor took.
        private string FeeText(Gizmo gizmo)
        {
            return FromTo(gizmo, b => (int)rentalFee.GetValue(b, null), Money);
        }

        private string AttractivenessText(Gizmo gizmo)
        {
            return FromTo(gizmo, b => (int)attractiveness.GetValue(bedStats.GetValue(b, null)), i => i.ToString());
        }

        private string FromTo(Gizmo gizmo, Func<object, int> read, Func<int, string> format)
        {
            object typedRead = typedReader.MakeGenericMethod(elementType, typeof(int)).Invoke(null, new object[] { read });
            return (string)toFromToString.MakeGenericMethod(typeof(int)).Invoke(gizmo, new[] { typedRead, format });
        }

        // CashRegister's copy is non-generic over object, formatted by its own GetRadiusString,
        // so 45 reads "45" and only 46 or more reads "Infinite".
        private string RadiusText(Gizmo gizmo)
        {
            Func<object, object> read = r => radius.GetValue(r, null);
            object typedRead = typedReader.MakeGenericMethod(elementType, typeof(object)).Invoke(null, new object[] { read });
            Delegate format = Delegate.CreateDelegate(typeof(Func<object, string>), radiusString);
            return (string)toFromToString.Invoke(gizmo, new[] { typedRead, format });
        }

        private static Func<T, TValue> TypedReader<T, TValue>(Func<object, TValue> read)
        {
            return element => read(element);
        }
    }
}
