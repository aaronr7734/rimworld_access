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

        public enum PolicyAction { New, Rename, Copy, Delete, Edit }

        public class SubmenuOption
        {
            public string Label;
            public Action<Pawn> Apply;
            public Policy PolicyObject;
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
                    return GetPawnColumnHeaderTip("HostilityResponse", "Hostility response");
                case AssignColumnType.MedicalCare:
                    return GetPawnColumnHeaderTip("MedicalCare", "Medical care");
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
                    return GetOutfitValue(pawn);
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

        public static string GetColumnTooltip(Pawn pawn, int index)
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

        private static string GetOutfitValue(Pawn pawn)
        {
            if (pawn.IsQuestLodger())
                return "Unchangeable".Translate().Resolve();

            string label = pawn.outfits?.CurrentApparelPolicy?.label ?? "None".Translate().Resolve();
            if (pawn.outfits?.forcedHandler?.SomethingIsForced == true)
            {
                var forced = pawn.outfits.forcedHandler.ForcedApparel;
                label += ". " + "ForcedApparel".Translate() + ": " + string.Join(", ", forced.Select(a => a.LabelCap));
            }
            return label;
        }

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
                    Apply = pawn => pawn.outfits.CurrentApparelPolicy = p,
                    PolicyObject = p
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
                    Apply = pawn => pawn.foodRestriction.CurrentFoodPolicy = p,
                    PolicyObject = p
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
                    Apply = pawn => pawn.drugs.CurrentPolicy = p,
                    PolicyObject = p
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
                    Apply = pawn => pawn.reading.CurrentPolicy = p,
                    PolicyObject = p
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
            int colIndex, Pawn pawn, Action refreshCallback, Action editCallback, Policy policyOverride = null)
        {
            if (colIndex < 0 || colIndex >= activeColumns.Count)
                return null;

            switch (activeColumns[colIndex])
            {
                case AssignColumnType.Outfit:
                {
                    var db = Current.Game?.outfitDatabase;
                    if (db == null) return null;
                    var policy = policyOverride ?? pawn.outfits?.CurrentApparelPolicy;
                    List<FloatMenuOption> extras = null;
                    if (policyOverride == null && pawn.outfits?.forcedHandler?.SomethingIsForced == true)
                    {
                        extras = new List<FloatMenuOption>
                        {
                            new FloatMenuOption("ClearForcedApparel".Translate(), () =>
                            {
                                pawn.outfits.forcedHandler.Reset();
                                refreshCallback?.Invoke();
                                TolkHelper.Speak("ClearForcedApparel".Translate());
                            })
                        };
                    }
                    return BuildPolicyContextMenu(
                        policy,
                        () => db.MakeNewOutfit(),
                        p => db.TryDelete((ApparelPolicy)p),
                        db.DefaultOutfit(),
                        p => db.SetDefault((ApparelPolicy)p),
                        refreshCallback, editCallback, extras);
                }

                case AssignColumnType.FoodRestriction:
                {
                    var db = Current.Game?.foodRestrictionDatabase;
                    if (db == null) return null;
                    var policy = policyOverride ?? pawn.foodRestriction?.CurrentFoodPolicy;
                    return BuildPolicyContextMenu(
                        policy,
                        () => db.MakeNewFoodRestriction(),
                        p => db.TryDelete((FoodPolicy)p),
                        db.DefaultFoodRestriction(),
                        p => db.SetDefault((FoodPolicy)p),
                        refreshCallback, editCallback);
                }

                case AssignColumnType.DrugPolicy:
                {
                    var db = Current.Game?.drugPolicyDatabase;
                    if (db == null) return null;
                    var policy = policyOverride ?? pawn.drugs?.CurrentPolicy;
                    return BuildPolicyContextMenu(
                        policy,
                        () => db.MakeNewDrugPolicy(),
                        p => db.TryDelete((DrugPolicy)p),
                        db.DefaultDrugPolicy(),
                        p => db.SetDefault((DrugPolicy)p),
                        refreshCallback, editCallback);
                }

                case AssignColumnType.ReadingPolicy:
                {
                    var db = Current.Game?.readingPolicyDatabase;
                    if (db == null) return null;
                    var policy = policyOverride ?? pawn.reading?.CurrentPolicy;
                    return BuildPolicyContextMenu(
                        policy,
                        () => db.MakeNewReadingPolicy(),
                        p => db.TryDelete((ReadingPolicy)p),
                        db.DefaultReadingPolicy(),
                        p => db.SetDefault((ReadingPolicy)p),
                        refreshCallback, editCallback);
                }

                case AssignColumnType.MedicalCare:
                    return BuildMedicalCareContextMenu();
                default:
                    return null;
            }
        }

        private static List<FloatMenuOption> BuildPolicyContextMenu(
            Policy currentPolicy,
            Func<Policy> createNew,
            Func<Policy, AcceptanceReport> tryDelete,
            Policy defaultPolicy,
            Action<Policy> setDefault,
            Action refreshCallback,
            Action editCallback,
            List<FloatMenuOption> extraOptions = null)
        {
            var options = new List<FloatMenuOption>();

            // New
            options.Add(new FloatMenuOption($"{"NewPolicy".Translate()} (Alt+N)", () =>
            {
                var newPolicy = createNew();
                refreshCallback?.Invoke();
                TolkHelper.Speak($"Created: {newPolicy.label}");
            }));

            if (currentPolicy != null)
            {
                // Rename
                options.Add(new FloatMenuOption(
                    $"{"Rename".Translate()}: {currentPolicy.label} (Alt+R)", () =>
                    {
                        Find.WindowStack.Add(new Dialog_RenamePolicy(currentPolicy));
                    }));

                // Duplicate
                options.Add(new FloatMenuOption(
                    $"{"Copy".Translate()}: {currentPolicy.label} (Alt+C)", () =>
                    {
                        var newPolicy = createNew();
                        newPolicy.CopyFrom(currentPolicy);
                        refreshCallback?.Invoke();
                        TolkHelper.Speak($"Duplicated: {newPolicy.label}");
                    }));

                // Delete
                options.Add(new FloatMenuOption(
                    $"{"Delete".Translate()}: {currentPolicy.label} (Delete)", () =>
                    {
                        AcceptanceReport result = tryDelete(currentPolicy);
                        if (!result.Accepted)
                            TolkHelper.Speak(result.Reason);
                        else
                        {
                            refreshCallback?.Invoke();
                            TolkHelper.Speak("Policy deleted");
                        }
                    }));

                // Set as Default (only if not already default)
                if (defaultPolicy != currentPolicy)
                {
                    options.Add(new FloatMenuOption(
                        $"{"Default".Translate()}: {currentPolicy.label}", () =>
                        {
                            setDefault(currentPolicy);
                            TolkHelper.Speak($"{currentPolicy.label} set as default");
                        }));
                }
            }

            // Edit
            if (editCallback != null)
            {
                options.Add(new FloatMenuOption(
                    $"{"AssignTabEdit".Translate()}: {currentPolicy.label} (Alt+E)", () =>
                    {
                        editCallback.Invoke();
                    }));
            }

            // Extra options (e.g., Clear Forced Apparel for outfits)
            if (extraOptions != null)
            {
                options.AddRange(extraOptions);
            }

            return options;
        }

        // === Direct Policy Action Execution (keyboard shortcuts) ===

        public static bool ExecutePolicyAction(PolicyAction action, int colIndex, Pawn pawn,
            Action refreshCallback, Action editCallback, Policy policyOverride = null)
        {
            if (colIndex < 0 || colIndex >= activeColumns.Count)
                return false;

            switch (activeColumns[colIndex])
            {
                case AssignColumnType.Outfit:
                {
                    var db = Current.Game?.outfitDatabase;
                    if (db == null) return false;
                    var policy = policyOverride ?? pawn.outfits?.CurrentApparelPolicy;
                    return ExecuteAction(action, policy,
                        () => db.MakeNewOutfit(),
                        p => db.TryDelete((ApparelPolicy)p),
                        refreshCallback, editCallback);
                }
                case AssignColumnType.FoodRestriction:
                {
                    var db = Current.Game?.foodRestrictionDatabase;
                    if (db == null) return false;
                    var policy = policyOverride ?? pawn.foodRestriction?.CurrentFoodPolicy;
                    return ExecuteAction(action, policy,
                        () => db.MakeNewFoodRestriction(),
                        p => db.TryDelete((FoodPolicy)p),
                        refreshCallback, editCallback);
                }
                case AssignColumnType.DrugPolicy:
                {
                    var db = Current.Game?.drugPolicyDatabase;
                    if (db == null) return false;
                    var policy = policyOverride ?? pawn.drugs?.CurrentPolicy;
                    return ExecuteAction(action, policy,
                        () => db.MakeNewDrugPolicy(),
                        p => db.TryDelete((DrugPolicy)p),
                        refreshCallback, editCallback);
                }
                case AssignColumnType.ReadingPolicy:
                {
                    var db = Current.Game?.readingPolicyDatabase;
                    if (db == null) return false;
                    var policy = policyOverride ?? pawn.reading?.CurrentPolicy;
                    return ExecuteAction(action, policy,
                        () => db.MakeNewReadingPolicy(),
                        p => db.TryDelete((ReadingPolicy)p),
                        refreshCallback, editCallback);
                }
                default:
                    return false;
            }
        }

        private static bool ExecuteAction(PolicyAction action, Policy policy,
            Func<Policy> createNew, Func<Policy, AcceptanceReport> tryDelete,
            Action refreshCallback, Action editCallback)
        {
            switch (action)
            {
                case PolicyAction.New:
                    var newPolicy = createNew();
                    refreshCallback?.Invoke();
                    TolkHelper.Speak($"Created: {newPolicy.label}");
                    return true;

                case PolicyAction.Rename:
                    if (policy == null) return false;
                    Find.WindowStack.Add(new Dialog_RenamePolicy(policy));
                    return true;

                case PolicyAction.Copy:
                    if (policy == null) return false;
                    var copied = createNew();
                    copied.CopyFrom(policy);
                    refreshCallback?.Invoke();
                    TolkHelper.Speak($"Duplicated: {copied.label}");
                    return true;

                case PolicyAction.Delete:
                    if (policy == null) return false;
                    AcceptanceReport result = tryDelete(policy);
                    if (!result.Accepted)
                        TolkHelper.Speak(result.Reason);
                    else
                    {
                        refreshCallback?.Invoke();
                        TolkHelper.Speak("Policy deleted");
                    }
                    return true;

                case PolicyAction.Edit:
                    if (editCallback == null) return false;
                    editCallback.Invoke();
                    return true;

                default:
                    return false;
            }
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
