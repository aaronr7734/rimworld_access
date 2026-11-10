using System;
using System.Collections.Generic;
using System.Linq;
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
    /// One subclass per concrete type, each with its own surface. GizmoOnGUI never returns
    /// Clicked and no subclass overrides ProcessInput, so the slider session and the centre-button
    /// option are the only ways to change them, matching the three mouse buttons.
    /// </summary>
    internal abstract class ModifyNumberGizmoHandler : GizmoHandlerBase
    {
        // Display ceiling for the mod's unbounded int setters (SetRentalFee, SetPrice,
        // SetEmptyThreshold all clamp to [0, int.MaxValue]).
        protected const float UnboundedMoneyCeiling = 100000f;

        private static readonly MethodInfo typedReader =
            typeof(ModifyNumberGizmoHandler).GetMethod(nameof(TypedReader), BindingFlags.NonPublic | BindingFlags.Static);

        protected readonly ReflectionSurface surface;
        protected readonly MethodInfo toFromToString;
        private readonly Type elementType;
        private readonly MethodInfo titleGetter;
        private readonly MethodInfo buttonUp;
        private readonly MethodInfo buttonDown;
        private readonly MethodInfo buttonCenter;
        private readonly FieldInfo selection;
        private bool ready;

        protected ModifyNumberGizmoHandler(Type gizmoType, string kind)
        {
            surface = new ReflectionSurface("ModifyNumberGizmoHandler." + kind);
            surface.Supplied(gizmoType.FullName, gizmoType);
            titleGetter = surface.Property(gizmoType, "Title")?.GetGetMethod(true);
            buttonUp = surface.Method(gizmoType, "ButtonUp", Type.EmptyTypes);
            buttonDown = surface.Method(gizmoType, "ButtonDown", Type.EmptyTypes);
            buttonCenter = surface.Method(gizmoType, "ButtonCenter", Type.EmptyTypes);
            selection = surface.Field(gizmoType, "selection");
            toFromToString = surface.Method(gizmoType, "ToFromToString");
            elementType = selection?.FieldType.GetElementType();
        }

        /// <summary>Called last by each subclass constructor; true when every member resolved.</summary>
        protected bool Seal()
        {
            ready = surface.Ready && elementType != null;
            return ready;
        }

        /// <summary>A Func&lt;T, TValue&gt; over the gizmo's element type, for ToFromToString.</summary>
        protected object ElementReader<TValue>(Func<object, TValue> read)
        {
            return typedReader.MakeGenericMethod(elementType, typeof(TValue)).Invoke(null, new object[] { read });
        }

        private static Func<T, TValue> TypedReader<T, TValue>(Func<object, TValue> read)
        {
            return element => read(element);
        }

        protected abstract string Status(Gizmo gizmo);

        protected abstract GizmoSliderAdapter Slider(Gizmo gizmo, Array selected);

        protected abstract string CenterLabel { get; }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = ready ? Guarded.Get<string>(titleGetter, gizmo, "ModifyNumberGizmoHandler title") : null;
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
                status = Status(gizmo);
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
                adapter = Slider(gizmo, selected);
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
        protected GizmoSliderAdapter MakeAdapter(Gizmo gizmo, string adapterTitle, float value, float max, float step, Func<float, string> describe)
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
            options.Add(new FloatMenuOption(CenterLabel, () => Guarded.Call(buttonCenter, gizmo, "ModifyNumberGizmoHandler ButtonCenter")));
            return true;
        }

        protected Array Selection(Gizmo gizmo)
        {
            return ready ? selection.GetValue(gizmo) as Array : null;
        }

        protected static string Money(int amount)
        {
            return ((float)amount).ToStringMoney("F0");
        }
    }

    internal sealed class GuestBedGizmoHandler : ModifyNumberGizmoHandler
    {
        private readonly PropertyInfo rentalFee;
        private readonly FieldInfo feeStep;
        private readonly PropertyInfo bedStats;
        private readonly FieldInfo attractiveness;
        private readonly MethodInfo updateRoyaltyStats;
        private readonly FieldInfo metRoyalTitles;
        private readonly FieldInfo textNextTitleReq;
        private readonly PropertyInfo allTitles;
        private readonly PropertyInfo distinctFactions;
        private readonly MethodInfo intFromTo;
        private readonly object feeReader;
        private readonly object attractivenessReader;

        internal GuestBedGizmoHandler(Type gizmoType)
            : base(gizmoType, "GuestBed")
        {
            Type bedType = surface.Type("Hospitality.Building_GuestBed");
            Type statsType = surface.Type("Hospitality.Building_GuestBed+BedStats");
            rentalFee = surface.Property(bedType, "RentalFee");
            feeStep = surface.Field(bedType, "FeeStep");
            bedStats = surface.Property(bedType, "Stats");
            updateRoyaltyStats = surface.Method(bedType, "UpdateRoyaltyStats", Type.EmptyTypes);
            attractiveness = surface.Field(statsType, "attractiveness");
            metRoyalTitles = surface.Field(statsType, "metRoyalTitles");
            textNextTitleReq = surface.Field(statsType, "textNextTitleReq");
            Type guestUtilityType = surface.Type("Hospitality.Utilities.GuestUtility");
            allTitles = surface.Property(guestUtilityType, "AllTitles");
            distinctFactions = surface.Property(guestUtilityType, "DistinctFactions");
            if (Seal())
            {
                intFromTo = toFromToString.MakeGenericMethod(typeof(int));
                feeReader = ElementReader(b => (int)rentalFee.GetValue(b, null));
                attractivenessReader = ElementReader(b => (int)attractiveness.GetValue(bedStats.GetValue(b, null)));
            }
        }

        protected override string CenterLabel => "RimWorldAccess.Compat.Hospitality.ResetRentalFee".Translate();

        protected override string Status(Gizmo gizmo)
        {
            return "RimWorldAccess.Compat.Hospitality.BedFeeStatus".Translate(FeeText(gizmo), AttractivenessText(gizmo));
        }

        protected override GizmoSliderAdapter Slider(Gizmo gizmo, Array selected)
        {
            return MakeAdapter(gizmo, "RimWorldAccess.Compat.Hospitality.BedRentalFeeTitle".Translate(),
                (int)rentalFee.GetValue(selected.GetValue(0), null), UnboundedMoneyCeiling, Convert.ToSingle(feeStep.GetValue(null)),
                v => selected.Length == 1 ? Money((int)v) : FeeText(gizmo));
        }

        // The gizmo's own ToFromToString fed live values, so a multi-selection reads "min - max"
        // in the mod's own format after every step, not the snapshot its constructor took.
        private string FeeText(Gizmo gizmo)
        {
            return (string)intFromTo.Invoke(gizmo, new[] { feeReader, (Func<int, string>)Money });
        }

        private string AttractivenessText(Gizmo gizmo)
        {
            return (string)intFromTo.Invoke(gizmo, new[] { attractivenessReader, (Func<int, string>)(i => i.ToString()) });
        }

        /// <summary>The royal-title box Gizmo_GuestBed.DrawTooltipBox opens on hover, for a single bed like the mod's own.</summary>
        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            Array selected = Selection(gizmo);
            if (!ModsConfig.RoyaltyActive || selected == null || selected.Length != 1)
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

                var met = metRoyalTitles.GetValue(stats) as RoyalTitleDef[] ?? new RoyalTitleDef[0];
                List<string> titles = BedTitleEntries(met);
                if (titles.Count == 0)
                {
                    return false;
                }
                var parts = new List<string>
                {
                    CompatText.ModText("BedSatisfiedTitles"),
                    titles.ToCommaList(),
                    CompatText.Flatten(textNextTitleReq.GetValue(stats)?.ToString()),
                };
                description = CompatText.JoinSentences(parts);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("ModifyNumberGizmoHandler royal titles", ex);
                return false;
            }
        }

        /// <summary>
        /// Every title BedStatsDrawer.DrawTitles lists, in its order: all royal titles by seniority that
        /// some faction in play grants, the unsatisfied ones dimmed. Its per-title faction icons are
        /// named only when the granting factions differ between titles; one shared set says nothing.
        /// </summary>
        private List<string> BedTitleEntries(RoyalTitleDef[] met)
        {
            // AllTitles' getter initializes the cache DistinctFactions is filled from.
            allTitles.GetValue(null, null);
            var factions = distinctFactions.GetValue(null, null) as Faction[] ?? new Faction[0];
            var rows = new List<(RoyalTitleDef title, List<Faction> grantedBy)>();
            foreach (RoyalTitleDef title in DefDatabase<RoyalTitleDef>.AllDefsListForReading.OrderBy(t => t.seniority))
            {
                var grantedBy = factions
                    .Where(f => f?.def?.RoyalTitlesAllInSeniorityOrderForReading != null
                        && f.def.RoyalTitlesAllInSeniorityOrderForReading.Contains(title))
                    .ToList();
                if (grantedBy.Count > 0)
                {
                    rows.Add((title, grantedBy));
                }
            }
            bool nameFactions = rows.Select(r => string.Join("|", r.grantedBy.Select(f => f.GetUniqueLoadID()))).Distinct().Count() > 1;

            var entries = new List<string>(rows.Count);
            foreach (var (title, grantedBy) in rows)
            {
                string entry = title.GetLabelCapForBothGenders();
                if (nameFactions)
                {
                    entry = "RimWorldAccess.Compat.Hospitality.RoyalTitleFactions".Translate(entry, grantedBy.Select(f => f.Name).ToCommaList()).ToString();
                }
                if (!met.Contains(title))
                {
                    entry = "RimWorldAccess.Compat.Hospitality.RoyalTitleUnmet".Translate(entry).ToString();
                }
                entries.Add(entry);
            }
            return entries;
        }
    }

    /// <summary>Gizmo_VendingMachine and Gizmo_VendingMachineContent, both over a CompVendingMachine.</summary>
    internal abstract class VendingGizmoHandler : ModifyNumberGizmoHandler
    {
        private readonly FieldInfo vendingMachine;
        private readonly PropertyInfo vendingProperties;
        private readonly FieldInfo priceSteps;
        protected readonly PropertyInfo currentPrice;
        protected readonly PropertyInfo currentEmptyThreshold;
        protected readonly PropertyInfo totalSold;

        protected VendingGizmoHandler(Type gizmoType, string kind)
            : base(gizmoType, kind)
        {
            Type compType = surface.Type("Hospitality.CompVendingMachine");
            Type propsType = surface.Type("Hospitality.CompProperties_VendingMachine");
            vendingMachine = surface.Field(gizmoType, "vendingMachine");
            currentPrice = surface.Property(compType, "CurrentPrice");
            currentEmptyThreshold = surface.Property(compType, "CurrentEmptyThreshold");
            totalSold = surface.Property(compType, "TotalSold");
            vendingProperties = surface.Property(compType, "Properties");
            priceSteps = surface.Field(propsType, "priceSteps");
            Seal();
        }

        protected int VendingInt(Gizmo gizmo, PropertyInfo property)
        {
            return (int)property.GetValue(vendingMachine.GetValue(gizmo), null);
        }

        protected GizmoSliderAdapter MoneyAdapter(Gizmo gizmo, string adapterTitle, PropertyInfo property)
        {
            object props = vendingProperties.GetValue(vendingMachine.GetValue(gizmo), null);
            return MakeAdapter(gizmo, adapterTitle, VendingInt(gizmo, property), UnboundedMoneyCeiling,
                Convert.ToSingle(priceSteps.GetValue(props)), v => Money((int)v));
        }
    }

    internal sealed class VendingPriceGizmoHandler : VendingGizmoHandler
    {
        internal VendingPriceGizmoHandler(Type gizmoType)
            : base(gizmoType, "VendingPrice")
        {
        }

        protected override string CenterLabel => "RimWorldAccess.Compat.Hospitality.SetAutoPrice".Translate();

        protected override string Status(Gizmo gizmo)
        {
            return "RimWorldAccess.Compat.Hospitality.VendingPriceStatus".Translate(Money(VendingInt(gizmo, currentPrice)));
        }

        protected override GizmoSliderAdapter Slider(Gizmo gizmo, Array selected)
        {
            return MoneyAdapter(gizmo, "RimWorldAccess.Compat.Hospitality.VendingPriceTitle".Translate(), currentPrice);
        }
    }

    internal sealed class VendingThresholdGizmoHandler : VendingGizmoHandler
    {
        internal VendingThresholdGizmoHandler(Type gizmoType)
            : base(gizmoType, "VendingThreshold")
        {
        }

        protected override string CenterLabel => "RimWorldAccess.Compat.Hospitality.SetThresholdAuto".Translate();

        protected override string Status(Gizmo gizmo)
        {
            return "RimWorldAccess.Compat.Hospitality.VendingContentStatus".Translate(
                Money(VendingInt(gizmo, currentEmptyThreshold)), Money(VendingInt(gizmo, totalSold)));
        }

        protected override GizmoSliderAdapter Slider(Gizmo gizmo, Array selected)
        {
            return MoneyAdapter(gizmo, "RimWorldAccess.Compat.Hospitality.VendingThresholdTitle".Translate(), currentEmptyThreshold);
        }
    }

    internal sealed class RegisterRadiusGizmoHandler : ModifyNumberGizmoHandler
    {
        private readonly PropertyInfo radius;
        private readonly FieldInfo radiusStep;
        private readonly FieldInfo infiniteRadius;
        private readonly MethodInfo includeRegionString;
        private readonly object radiusReader;
        private readonly Delegate radiusFormat;

        internal RegisterRadiusGizmoHandler(Type gizmoType)
            : base(gizmoType, "Radius")
        {
            Type registerType = surface.Type("CashRegister.Building_CashRegister");
            radius = surface.Property(registerType, "Radius");
            radiusStep = surface.Field(registerType, "RadiusStep");
            infiniteRadius = surface.Field(registerType, "InfiniteRadius");
            MethodInfo radiusString = surface.Method(gizmoType, "GetRadiusString", new[] { typeof(object) });
            includeRegionString = surface.Method(gizmoType, "GetIncludeRegionString", Type.EmptyTypes);
            if (Seal())
            {
                radiusReader = ElementReader(r => radius.GetValue(r, null));
                radiusFormat = Delegate.CreateDelegate(typeof(Func<object, string>), radiusString);
            }
        }

        protected override string CenterLabel => "RimWorldAccess.Compat.CashRegister.ToggleIncludeRoom".Translate();

        protected override string Status(Gizmo gizmo)
        {
            return "RimWorldAccess.Compat.CashRegister.RadiusStatus".Translate(
                RadiusText(gizmo), includeRegionString.Invoke(gizmo, null).ToString());
        }

        protected override GizmoSliderAdapter Slider(Gizmo gizmo, Array selected)
        {
            // Building_CashRegister.ChangeRadius clamps to [0, InfiniteRadius - 1].
            return MakeAdapter(gizmo, "RimWorldAccess.Compat.CashRegister.RadiusTitle".Translate(),
                (float)radius.GetValue(selected.GetValue(0), null), Convert.ToSingle(infiniteRadius.GetValue(null)) - 1f,
                Convert.ToSingle(radiusStep.GetValue(null)), v => RadiusText(gizmo));
        }

        // CashRegister's copy is non-generic over object, formatted by its own GetRadiusString,
        // so 45 reads "45" and only 46 or more reads "Infinite".
        private string RadiusText(Gizmo gizmo)
        {
            return (string)toFromToString.Invoke(gizmo, new[] { radiusReader, radiusFormat });
        }
    }
}
