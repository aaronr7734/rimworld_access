using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static class AssignMenuHelper
    {
        public enum AssignColumnType
        {
            Name,
            Ideo,
            Xenotype,
            HostilityResponse,
            MedicalCare,
            Outfit,
            FoodRestriction,
            DrugPolicy,
            ReadingPolicy,
            MedicineCarry
        }

        public class SubmenuOption
        {
            public string Label;
            public Action<Pawn> Apply;
        }

        private static List<AssignColumnType> activeColumns = new List<AssignColumnType>();

        public static List<AssignColumnType> ActiveColumns => activeColumns;

        // === Column Setup ===

        public static void BuildActiveColumns()
        {
            activeColumns.Clear();

            activeColumns.Add(AssignColumnType.Name);

            if (ModsConfig.IdeologyActive)
                activeColumns.Add(AssignColumnType.Ideo);

            if (ModsConfig.BiotechActive)
                activeColumns.Add(AssignColumnType.Xenotype);

            activeColumns.Add(AssignColumnType.HostilityResponse);
            activeColumns.Add(AssignColumnType.MedicalCare);
            activeColumns.Add(AssignColumnType.Outfit);
            activeColumns.Add(AssignColumnType.FoodRestriction);
            activeColumns.Add(AssignColumnType.DrugPolicy);

            if (Current.Game?.readingPolicyDatabase != null)
                activeColumns.Add(AssignColumnType.ReadingPolicy);

            if (Find.CurrentMap != null &&
                Find.CurrentMap.mapPawns.FreeColonists.Any(p => p.inventoryStock != null))
            {
                activeColumns.Add(AssignColumnType.MedicineCarry);
            }
        }

        // === TabularMenuHelper Delegates ===

        public static int GetColumnCount()
        {
            return activeColumns.Count;
        }

        public static string GetPawnLabel(Pawn pawn)
        {
            return pawn.LabelShort;
        }

        public static string GetColumnName(int index)
        {
            if (index < 0 || index >= activeColumns.Count)
                return "Unknown";

            switch (activeColumns[index])
            {
                case AssignColumnType.Name:
                    return GetPawnColumnLabel("Label", "Name");
                case AssignColumnType.Ideo:
                    return "Ideo".Translate().Resolve();
                case AssignColumnType.Xenotype:
                    return "Xenotype".Translate().Resolve();
                case AssignColumnType.HostilityResponse:
                    return GetPawnColumnHeaderTip("HostilityResponse", "HostilityResponse".Translate().Resolve());
                case AssignColumnType.MedicalCare:
                    return GetPawnColumnHeaderTip("MedicalCare", "MedicalCare".Translate().Resolve());
                case AssignColumnType.Outfit:
                    return GetPawnColumnLabel("Outfit", "ApparelPolicy".Translate().Resolve());
                case AssignColumnType.FoodRestriction:
                    return GetPawnColumnLabel("FoodRestriction", "FoodPolicy".Translate().Resolve());
                case AssignColumnType.DrugPolicy:
                    return GetPawnColumnLabel("DrugPolicy", "DrugPolicy".Translate().Resolve());
                case AssignColumnType.ReadingPolicy:
                    return GetPawnColumnLabel("Reading", "ReadingPolicy".Translate().Resolve());
                case AssignColumnType.MedicineCarry:
                    return GetPawnColumnLabel("Carry", "Carry");
                default:
                    return "Unknown";
            }
        }

        public static string GetColumnValue(Pawn pawn, int index)
        {
            if (index < 0 || index >= activeColumns.Count)
                return "Unknown";

            switch (activeColumns[index])
            {
                case AssignColumnType.Name:
                    return pawn.LabelShort;
                case AssignColumnType.Ideo:
                    return pawn.Ideo?.name ?? "None".Translate().Resolve();
                case AssignColumnType.Xenotype:
                    return pawn.genes?.XenotypeLabelCap ?? "None".Translate().Resolve();
                case AssignColumnType.HostilityResponse:
                    if (pawn.playerSettings == null) return "N/A";
                    return pawn.playerSettings.hostilityResponse.GetLabel();
                case AssignColumnType.MedicalCare:
                    if (pawn.playerSettings == null) return "N/A";
                    return pawn.playerSettings.medCare.GetLabel();
                case AssignColumnType.Outfit:
                    return pawn.outfits?.CurrentApparelPolicy?.label ?? "None".Translate().Resolve();
                case AssignColumnType.FoodRestriction:
                    return pawn.foodRestriction?.CurrentFoodPolicy?.label ?? "None".Translate().Resolve();
                case AssignColumnType.DrugPolicy:
                    return pawn.drugs?.CurrentPolicy?.label ?? "None".Translate().Resolve();
                case AssignColumnType.ReadingPolicy:
                    return pawn.reading?.CurrentPolicy?.label ?? "None".Translate().Resolve();
                case AssignColumnType.MedicineCarry:
                    return GetMedicineCarryValue(pawn);
                default:
                    return "Unknown";
            }
        }

        public static string GetColumnTooltip(int index)
        {
            if (index < 0 || index >= activeColumns.Count)
                return null;

            switch (activeColumns[index])
            {
                case AssignColumnType.HostilityResponse:
                    return "HostilityReponseTip".Translate().Resolve();
                case AssignColumnType.Outfit:
                    return "ApparelPolicyTip".Translate().Resolve();
                case AssignColumnType.FoodRestriction:
                    return "FoodPolicyTip".Translate().Resolve();
                case AssignColumnType.DrugPolicy:
                    return "DrugPolicyTip".Translate().Resolve();
                case AssignColumnType.ReadingPolicy:
                    return "ReadingPolicyTip".Translate().Resolve();
                default:
                    return null;
            }
        }

        // === Column Type Queries ===

        public static AssignColumnType GetColumnType(int index)
        {
            if (index < 0 || index >= activeColumns.Count)
                return AssignColumnType.Name;
            return activeColumns[index];
        }

        public static bool IsColumnInteractive(int index)
        {
            if (index < 0 || index >= activeColumns.Count)
                return false;

            switch (activeColumns[index])
            {
                case AssignColumnType.Ideo:
                case AssignColumnType.Xenotype:
                    return false;
                default:
                    return true;
            }
        }

        public static bool IsColumnPolicyType(int index)
        {
            if (index < 0 || index >= activeColumns.Count)
                return false;

            switch (activeColumns[index])
            {
                case AssignColumnType.Outfit:
                case AssignColumnType.FoodRestriction:
                case AssignColumnType.DrugPolicy:
                case AssignColumnType.ReadingPolicy:
                    return true;
                default:
                    return false;
            }
        }

        public static bool HasContextMenu(int index)
        {
            if (index < 0 || index >= activeColumns.Count)
                return false;

            switch (activeColumns[index])
            {
                case AssignColumnType.Outfit:
                case AssignColumnType.FoodRestriction:
                case AssignColumnType.DrugPolicy:
                case AssignColumnType.ReadingPolicy:
                case AssignColumnType.MedicalCare:
                    return true;
                default:
                    return false;
            }
        }

        // === PawnColumnDef Helpers ===

        private static string GetPawnColumnLabel(string defName, string fallback)
        {
            var def = DefDatabase<PawnColumnDef>.GetNamedSilentFail(defName);
            return def?.LabelCap.Resolve() ?? fallback;
        }

        private static string GetPawnColumnHeaderTip(string defName, string fallback)
        {
            var def = DefDatabase<PawnColumnDef>.GetNamedSilentFail(defName);
            if (def != null && !def.headerTip.NullOrEmpty())
                return def.headerTip;
            return fallback;
        }

        // === Value Helpers ===

        private static string GetMedicineCarryValue(Pawn pawn)
        {
            if (pawn.inventoryStock == null)
                return "N/A";

            var group = InventoryStockGroupDefOf.Medicine;
            if (group == null)
                return "N/A";

            int count = pawn.inventoryStock.GetDesiredCountForGroup(group);
            if (count == 0)
                return "None".Translate().Resolve();

            ThingDef medicine = pawn.inventoryStock.GetDesiredThingForGroup(group);
            return $"{medicine.LabelCap} x{count}";
        }

        // === Sorting ===

        public static List<Pawn> SortByColumn(List<Pawn> pawns, int columnIndex, bool descending)
        {
            if (columnIndex < 0 || columnIndex >= activeColumns.Count || pawns == null)
                return pawns;

            IEnumerable<Pawn> sorted;

            switch (activeColumns[columnIndex])
            {
                case AssignColumnType.Name:
                    sorted = pawns.OrderBy(p => p.LabelShort);
                    break;
                default:
                    sorted = pawns.OrderBy(p => GetColumnValue(p, columnIndex));
                    break;
            }

            if (descending)
                sorted = sorted.Reverse();

            return sorted.ToList();
        }

        // === Submenu Option Builders ===

        public static List<SubmenuOption> GetSubmenuOptions(int colIndex, Pawn pawn)
        {
            if (colIndex < 0 || colIndex >= activeColumns.Count)
                return new List<SubmenuOption>();

            switch (activeColumns[colIndex])
            {
                case AssignColumnType.HostilityResponse:
                    return BuildHostilityOptions(pawn);
                case AssignColumnType.MedicalCare:
                    return BuildMedicalCareOptions();
                case AssignColumnType.Outfit:
                    return BuildOutfitOptions();
                case AssignColumnType.FoodRestriction:
                    return BuildFoodOptions();
                case AssignColumnType.DrugPolicy:
                    return BuildDrugOptions();
                case AssignColumnType.ReadingPolicy:
                    return BuildReadingOptions();
                case AssignColumnType.MedicineCarry:
                    return BuildMedicineCarryOptions();
                default:
                    return new List<SubmenuOption>();
            }
        }

        public static int GetCurrentSubmenuIndex(int colIndex, Pawn pawn, List<SubmenuOption> options)
        {
            if (options == null || options.Count == 0)
                return 0;

            string currentValue = GetColumnValue(pawn, colIndex);
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Label == currentValue)
                    return i;
            }
            return 0;
        }

        private static List<SubmenuOption> BuildHostilityOptions(Pawn pawn)
        {
            var options = new List<SubmenuOption>();

            options.Add(new SubmenuOption
            {
                Label = HostilityResponseMode.Ignore.GetLabel(),
                Apply = p => p.playerSettings.hostilityResponse = HostilityResponseMode.Ignore
            });

            if (!pawn.WorkTagIsDisabled(WorkTags.Violent))
            {
                options.Add(new SubmenuOption
                {
                    Label = HostilityResponseMode.Attack.GetLabel(),
                    Apply = p => p.playerSettings.hostilityResponse = HostilityResponseMode.Attack
                });
            }

            options.Add(new SubmenuOption
            {
                Label = HostilityResponseMode.Flee.GetLabel(),
                Apply = p => p.playerSettings.hostilityResponse = HostilityResponseMode.Flee
            });

            return options;
        }

        private static List<SubmenuOption> BuildMedicalCareOptions()
        {
            var options = new List<SubmenuOption>();
            foreach (MedicalCareCategory cat in Enum.GetValues(typeof(MedicalCareCategory)))
            {
                var c = cat;
                options.Add(new SubmenuOption
                {
                    Label = c.GetLabel(),
                    Apply = p => p.playerSettings.medCare = c
                });
            }
            return options;
        }

        private static List<SubmenuOption> BuildOutfitOptions()
        {
            var options = new List<SubmenuOption>();
            if (Current.Game?.outfitDatabase == null)
                return options;

            foreach (var policy in Current.Game.outfitDatabase.AllOutfits)
            {
                var p = policy;
                options.Add(new SubmenuOption
                {
                    Label = p.label,
                    Apply = pawn => pawn.outfits.CurrentApparelPolicy = p
                });
            }
            return options;
        }

        private static List<SubmenuOption> BuildFoodOptions()
        {
            var options = new List<SubmenuOption>();
            if (Current.Game?.foodRestrictionDatabase == null)
                return options;

            foreach (var policy in Current.Game.foodRestrictionDatabase.AllFoodRestrictions)
            {
                var p = policy;
                options.Add(new SubmenuOption
                {
                    Label = p.label,
                    Apply = pawn => pawn.foodRestriction.CurrentFoodPolicy = p
                });
            }
            return options;
        }

        private static List<SubmenuOption> BuildDrugOptions()
        {
            var options = new List<SubmenuOption>();
            if (Current.Game?.drugPolicyDatabase == null)
                return options;

            foreach (var policy in Current.Game.drugPolicyDatabase.AllPolicies)
            {
                var p = policy;
                options.Add(new SubmenuOption
                {
                    Label = p.label,
                    Apply = pawn => pawn.drugs.CurrentPolicy = p
                });
            }
            return options;
        }

        private static List<SubmenuOption> BuildReadingOptions()
        {
            var options = new List<SubmenuOption>();
            if (Current.Game?.readingPolicyDatabase == null)
                return options;

            foreach (var policy in Current.Game.readingPolicyDatabase.AllReadingPolicies)
            {
                var p = policy;
                options.Add(new SubmenuOption
                {
                    Label = p.label,
                    Apply = pawn => pawn.reading.CurrentPolicy = p
                });
            }
            return options;
        }

        private static List<SubmenuOption> BuildMedicineCarryOptions()
        {
            var options = new List<SubmenuOption>();
            var group = InventoryStockGroupDefOf.Medicine;
            if (group == null)
                return options;

            // "None" option (carry 0)
            options.Add(new SubmenuOption
            {
                Label = "None".Translate().Resolve(),
                Apply = p =>
                {
                    if (p.inventoryStock != null)
                        p.inventoryStock.SetCountForGroup(group, 0);
                }
            });

            // Options for each medicine type and count (1 to max)
            foreach (var medicineDef in group.thingDefs)
            {
                for (int i = 1; i <= group.max; i++)
                {
                    var def = medicineDef;
                    var count = i;
                    options.Add(new SubmenuOption
                    {
                        Label = $"{def.LabelCap} x{count}",
                        Apply = p =>
                        {
                            if (p.inventoryStock != null)
                            {
                                p.inventoryStock.SetThingForGroup(group, def);
                                p.inventoryStock.SetCountForGroup(group, count);
                            }
                        }
                    });
                }
            }

            return options;
        }

        // === Context Menu Builders ===

        public static List<FloatMenuOption> GetContextMenuOptions(
            int colIndex, Pawn pawn, Action refreshCallback, Action editCallback)
        {
            if (colIndex < 0 || colIndex >= activeColumns.Count)
                return null;

            switch (activeColumns[colIndex])
            {
                case AssignColumnType.Outfit:
                    return BuildOutfitContextMenu(pawn, refreshCallback, editCallback);
                case AssignColumnType.FoodRestriction:
                    return BuildFoodContextMenu(pawn, refreshCallback, editCallback);
                case AssignColumnType.DrugPolicy:
                    return BuildDrugContextMenu(pawn, refreshCallback, editCallback);
                case AssignColumnType.ReadingPolicy:
                    return BuildReadingContextMenu(pawn, refreshCallback, editCallback);
                case AssignColumnType.MedicalCare:
                    return BuildMedicalCareContextMenu();
                default:
                    return null;
            }
        }

        private static List<FloatMenuOption> BuildOutfitContextMenu(
            Pawn pawn, Action refreshCallback, Action editCallback)
        {
            var options = new List<FloatMenuOption>();
            var db = Current.Game?.outfitDatabase;
            if (db == null) return null;

            var currentPolicy = pawn.outfits?.CurrentApparelPolicy;

            // New
            options.Add(new FloatMenuOption("NewPolicy".Translate(), () =>
            {
                var newPolicy = db.MakeNewOutfit();
                refreshCallback?.Invoke();
                TolkHelper.Speak($"Created: {newPolicy.label}");
            }));

            if (currentPolicy != null)
            {
                // Rename
                options.Add(new FloatMenuOption(
                    $"{"Rename".Translate()}: {currentPolicy.label}", () =>
                    {
                        Find.WindowStack.Add(new Dialog_RenamePolicy(currentPolicy));
                    }));

                // Duplicate
                options.Add(new FloatMenuOption(
                    $"{"Copy".Translate()}: {currentPolicy.label}", () =>
                    {
                        var newPolicy = db.MakeNewOutfit();
                        newPolicy.CopyFrom(currentPolicy);
                        refreshCallback?.Invoke();
                        TolkHelper.Speak($"Duplicated: {newPolicy.label}");
                    }));

                // Delete
                options.Add(new FloatMenuOption(
                    $"{"Delete".Translate()}: {currentPolicy.label}", () =>
                    {
                        AcceptanceReport result = db.TryDelete(currentPolicy);
                        if (!result.Accepted)
                            TolkHelper.Speak(result.Reason);
                        else
                        {
                            refreshCallback?.Invoke();
                            TolkHelper.Speak("Policy deleted");
                        }
                    }));

                // Set as Default (only if not already default)
                if (db.DefaultOutfit() != currentPolicy)
                {
                    options.Add(new FloatMenuOption(
                        $"{"Default".Translate()}: {currentPolicy.label}", () =>
                        {
                            db.SetDefault(currentPolicy);
                            TolkHelper.Speak($"{currentPolicy.label} set as default");
                        }));
                }
            }

            // Edit
            if (editCallback != null)
            {
                options.Add(new FloatMenuOption(
                    "AssignTabEdit".Translate(), () =>
                    {
                        editCallback.Invoke();
                    }));
            }

            // Clear Forced Apparel (outfit-specific, only if pawn has forced items)
            if (pawn.outfits?.forcedHandler?.SomethingIsForced == true)
            {
                options.Add(new FloatMenuOption(
                    "ClearForcedApparel".Translate(), () =>
                    {
                        pawn.outfits.forcedHandler.Reset();
                        refreshCallback?.Invoke();
                        TolkHelper.Speak("ClearForcedApparel".Translate());
                    }));
            }

            return options;
        }

        private static List<FloatMenuOption> BuildFoodContextMenu(
            Pawn pawn, Action refreshCallback, Action editCallback)
        {
            var options = new List<FloatMenuOption>();
            var db = Current.Game?.foodRestrictionDatabase;
            if (db == null) return null;

            var currentPolicy = pawn.foodRestriction?.CurrentFoodPolicy;

            // New
            options.Add(new FloatMenuOption("NewPolicy".Translate(), () =>
            {
                var newPolicy = db.MakeNewFoodRestriction();
                refreshCallback?.Invoke();
                TolkHelper.Speak($"Created: {newPolicy.label}");
            }));

            if (currentPolicy != null)
            {
                // Rename
                options.Add(new FloatMenuOption(
                    $"{"Rename".Translate()}: {currentPolicy.label}", () =>
                    {
                        Find.WindowStack.Add(new Dialog_RenamePolicy(currentPolicy));
                    }));

                // Duplicate
                options.Add(new FloatMenuOption(
                    $"{"Copy".Translate()}: {currentPolicy.label}", () =>
                    {
                        var newPolicy = db.MakeNewFoodRestriction();
                        newPolicy.CopyFrom(currentPolicy);
                        refreshCallback?.Invoke();
                        TolkHelper.Speak($"Duplicated: {newPolicy.label}");
                    }));

                // Delete
                options.Add(new FloatMenuOption(
                    $"{"Delete".Translate()}: {currentPolicy.label}", () =>
                    {
                        AcceptanceReport result = db.TryDelete(currentPolicy);
                        if (!result.Accepted)
                            TolkHelper.Speak(result.Reason);
                        else
                        {
                            refreshCallback?.Invoke();
                            TolkHelper.Speak("Policy deleted");
                        }
                    }));

                // Set as Default
                if (db.DefaultFoodRestriction() != currentPolicy)
                {
                    options.Add(new FloatMenuOption(
                        $"{"Default".Translate()}: {currentPolicy.label}", () =>
                        {
                            db.SetDefault(currentPolicy);
                            TolkHelper.Speak($"{currentPolicy.label} set as default");
                        }));
                }
            }

            // Edit
            if (editCallback != null)
            {
                options.Add(new FloatMenuOption(
                    "AssignTabEdit".Translate(), () =>
                    {
                        editCallback.Invoke();
                    }));
            }

            return options;
        }

        private static List<FloatMenuOption> BuildDrugContextMenu(
            Pawn pawn, Action refreshCallback, Action editCallback)
        {
            var options = new List<FloatMenuOption>();
            var db = Current.Game?.drugPolicyDatabase;
            if (db == null) return null;

            var currentPolicy = pawn.drugs?.CurrentPolicy;

            // New
            options.Add(new FloatMenuOption("NewPolicy".Translate(), () =>
            {
                var newPolicy = db.MakeNewDrugPolicy();
                refreshCallback?.Invoke();
                TolkHelper.Speak($"Created: {newPolicy.label}");
            }));

            if (currentPolicy != null)
            {
                // Rename
                options.Add(new FloatMenuOption(
                    $"{"Rename".Translate()}: {currentPolicy.label}", () =>
                    {
                        Find.WindowStack.Add(new Dialog_RenamePolicy(currentPolicy));
                    }));

                // Duplicate
                options.Add(new FloatMenuOption(
                    $"{"Copy".Translate()}: {currentPolicy.label}", () =>
                    {
                        var newPolicy = db.MakeNewDrugPolicy();
                        newPolicy.CopyFrom(currentPolicy);
                        refreshCallback?.Invoke();
                        TolkHelper.Speak($"Duplicated: {newPolicy.label}");
                    }));

                // Delete
                options.Add(new FloatMenuOption(
                    $"{"Delete".Translate()}: {currentPolicy.label}", () =>
                    {
                        AcceptanceReport result = db.TryDelete(currentPolicy);
                        if (!result.Accepted)
                            TolkHelper.Speak(result.Reason);
                        else
                        {
                            refreshCallback?.Invoke();
                            TolkHelper.Speak("Policy deleted");
                        }
                    }));

                // Set as Default
                if (db.DefaultDrugPolicy() != currentPolicy)
                {
                    options.Add(new FloatMenuOption(
                        $"{"Default".Translate()}: {currentPolicy.label}", () =>
                        {
                            db.SetDefault(currentPolicy);
                            TolkHelper.Speak($"{currentPolicy.label} set as default");
                        }));
                }
            }

            // Edit
            if (editCallback != null)
            {
                options.Add(new FloatMenuOption(
                    "AssignTabEdit".Translate(), () =>
                    {
                        editCallback.Invoke();
                    }));
            }

            return options;
        }

        private static List<FloatMenuOption> BuildReadingContextMenu(
            Pawn pawn, Action refreshCallback, Action editCallback)
        {
            var options = new List<FloatMenuOption>();
            var db = Current.Game?.readingPolicyDatabase;
            if (db == null) return null;

            var currentPolicy = pawn.reading?.CurrentPolicy;

            // New
            options.Add(new FloatMenuOption("NewPolicy".Translate(), () =>
            {
                var newPolicy = db.MakeNewReadingPolicy();
                refreshCallback?.Invoke();
                TolkHelper.Speak($"Created: {newPolicy.label}");
            }));

            if (currentPolicy != null)
            {
                // Rename
                options.Add(new FloatMenuOption(
                    $"{"Rename".Translate()}: {currentPolicy.label}", () =>
                    {
                        Find.WindowStack.Add(new Dialog_RenamePolicy(currentPolicy));
                    }));

                // Duplicate
                options.Add(new FloatMenuOption(
                    $"{"Copy".Translate()}: {currentPolicy.label}", () =>
                    {
                        var newPolicy = db.MakeNewReadingPolicy();
                        newPolicy.CopyFrom(currentPolicy);
                        refreshCallback?.Invoke();
                        TolkHelper.Speak($"Duplicated: {newPolicy.label}");
                    }));

                // Delete
                options.Add(new FloatMenuOption(
                    $"{"Delete".Translate()}: {currentPolicy.label}", () =>
                    {
                        AcceptanceReport result = db.TryDelete(currentPolicy);
                        if (!result.Accepted)
                            TolkHelper.Speak(result.Reason);
                        else
                        {
                            refreshCallback?.Invoke();
                            TolkHelper.Speak("Policy deleted");
                        }
                    }));

                // Set as Default
                if (db.DefaultReadingPolicy() != currentPolicy)
                {
                    options.Add(new FloatMenuOption(
                        $"{"Default".Translate()}: {currentPolicy.label}", () =>
                        {
                            db.SetDefault(currentPolicy);
                            TolkHelper.Speak($"{currentPolicy.label} set as default");
                        }));
                }
            }

            // Edit — opens vanilla dialog (no WindowlessReadingPolicyState exists)
            if (editCallback != null)
            {
                options.Add(new FloatMenuOption(
                    "AssignTabEdit".Translate(), () =>
                    {
                        editCallback.Invoke();
                    }));
            }

            return options;
        }

        private static List<FloatMenuOption> BuildMedicalCareContextMenu()
        {
            var options = new List<FloatMenuOption>();
            options.Add(new FloatMenuOption("ChangeDefaults".Translate(), () =>
            {
                Find.WindowStack.Add(new Dialog_MedicalDefaults());
            }));
            return options;
        }

        // === Paint Support ===

        public static void ApplyValueToPawn(Pawn sourcePawn, Pawn targetPawn, int colIndex)
        {
            if (colIndex < 0 || colIndex >= activeColumns.Count)
                return;

            switch (activeColumns[colIndex])
            {
                case AssignColumnType.HostilityResponse:
                    if (sourcePawn.playerSettings != null && targetPawn.playerSettings != null)
                        targetPawn.playerSettings.hostilityResponse = sourcePawn.playerSettings.hostilityResponse;
                    break;

                case AssignColumnType.MedicalCare:
                    if (sourcePawn.playerSettings != null && targetPawn.playerSettings != null)
                        targetPawn.playerSettings.medCare = sourcePawn.playerSettings.medCare;
                    break;

                case AssignColumnType.Outfit:
                    if (sourcePawn.outfits != null && targetPawn.outfits != null)
                        targetPawn.outfits.CurrentApparelPolicy = sourcePawn.outfits.CurrentApparelPolicy;
                    break;

                case AssignColumnType.FoodRestriction:
                    if (sourcePawn.foodRestriction != null && targetPawn.foodRestriction != null)
                        targetPawn.foodRestriction.CurrentFoodPolicy = sourcePawn.foodRestriction.CurrentFoodPolicy;
                    break;

                case AssignColumnType.DrugPolicy:
                    if (sourcePawn.drugs != null && targetPawn.drugs != null)
                        targetPawn.drugs.CurrentPolicy = sourcePawn.drugs.CurrentPolicy;
                    break;

                case AssignColumnType.ReadingPolicy:
                    if (sourcePawn.reading != null && targetPawn.reading != null)
                        targetPawn.reading.CurrentPolicy = sourcePawn.reading.CurrentPolicy;
                    break;

                case AssignColumnType.MedicineCarry:
                    if (sourcePawn.inventoryStock != null && targetPawn.inventoryStock != null)
                    {
                        var group = InventoryStockGroupDefOf.Medicine;
                        if (group != null)
                        {
                            int count = sourcePawn.inventoryStock.GetDesiredCountForGroup(group);
                            ThingDef thing = sourcePawn.inventoryStock.GetDesiredThingForGroup(group);
                            targetPawn.inventoryStock.SetThingForGroup(group, thing);
                            targetPawn.inventoryStock.SetCountForGroup(group, count);
                        }
                    }
                    break;
            }
        }

        public static bool CanPaintColumn(int colIndex)
        {
            if (colIndex < 0 || colIndex >= activeColumns.Count)
                return false;

            switch (activeColumns[colIndex])
            {
                case AssignColumnType.Name:
                case AssignColumnType.Ideo:
                case AssignColumnType.Xenotype:
                    return false;
                default:
                    return true;
            }
        }
    }
}
