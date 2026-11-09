using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using static RimWorldAccess.Shell.CompatText;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Hunting tab's detail rows: the mod's two columns as two regions, each section of each
    /// column as a run of rows in the order the mod draws them.
    ///
    /// Options carries the left column: the target-resource pair, the whole threshold section
    /// (summary, count, the two threshold-scope toggles the trigger itself draws, synchronize,
    /// distance, reachability, and the meat shortcuts that only exist while the job targets meat),
    /// the unforbid-corpses pair, and the hunting-grounds area strip with its invert toggle. Animals
    /// carries the right column: the group shortcuts, the refresh and padlock icons, and one row per
    /// animal kind.
    ///
    /// Conditional rows appear and vanish with their own conditions exactly as the sighted rows do --
    /// the meat shortcuts with the target resource, the "also unforbid disallowed animals" child with
    /// its parent, the twisted-meat shortcut with Anomaly, each animal shortcut only where the mod's
    /// own visibility rule draws it. Nothing is presented that the mod is not drawing.
    ///
    /// DEFERRED: the magnifier beside the threshold label, which float-menus the job's current hunt
    /// designations and jumps the camera to one (Trigger_Threshold.cs:508-563). It is a navigation
    /// aid rather than a job setting, and it needs the manager window to close itself the way the
    /// mod's own options do; recorded for a later slice.
    /// </summary>
    internal sealed class CmrHuntingDetails : ICmrJobDetailsProvider
    {
        public bool Handles(object tab)
        {
            return CmrCompat.Hunting.HandlesTab(tab);
        }

        public List<CmrDetailRegion> Build(object tab, object job)
        {
            var regions = new List<CmrDetailRegion>();
            if (job == null || !CmrCompat.Hunting.Ready)
            {
                return regions;
            }
            regions.Add(BuildOptions(job));
            regions.Add(BuildAnimals(job));
            return regions;
        }

        // ------------------------------------------------------------------
        // Options: the left column.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildOptions(object job)
        {
            var region = new CmrDetailRegion("RimWorldAccess.Cmr.OptionsRegion".Translate());
            AddTargetResource(region, job);
            AddThreshold(region, job);
            AddUnforbidCorpses(region, job);
            AddHuntingGrounds(region, job);
            return region;
        }

        /// <summary>
        /// The mod's radio pair, in the enum's own declaration order (Leather, then Meat -- the order
        /// <c>Enum.GetValues</c> hands its draw loop, ManagerTab_Hunting.cs:437-455). Activating the
        /// current choice does nothing, matching the widget's empty "off" delegate.
        /// </summary>
        private static void AddTargetResource(CmrDetailRegion region, object job)
        {
            string section = ModText("ColonyManagerRedux.Hunting.TargetResource");
            foreach (object value in CmrCompat.Hunting.TargetResourceValues())
            {
                string keyBase = "ColonyManagerRedux.Hunting.TargetResource."
                    + CmrCompat.Hunting.TargetResourceName(value);
                object choice = value;
                region.Rows.Add(new CmrDetailRow
                {
                    Label = ModText(keyBase),
                    Tooltip = ModTip(keyBase + ".Tip"),
                    Role = ElementRole.RadioButton,
                    SectionTitle = section,
                    Selected = () => IsCurrentResource(job, choice),
                    Activate = delegate
                    {
                        if (!IsCurrentResource(job, choice))
                        {
                            CmrCompat.Hunting.SetTargetResource(job, choice);
                        }
                    },
                });
            }
        }

        private static bool IsCurrentResource(object job, object value)
        {
            object current = CmrCompat.Hunting.TargetResource(job);
            return current != null && current.Equals(value);
        }

        private static void AddThreshold(CmrDetailRegion region, object job)
        {
            string section = CmrTabRows.AddThresholdRows(region, job, "Hunting",
                "RimWorldAccess.Cmr.Threshold.TargetCountRow",
                () => ThresholdText(job, "ColonyManagerRedux.Hunting.TargetCount"),
                () => Flatten(ThresholdText(job, "ColonyManagerRedux.Hunting.TargetCountTooltip")),
                () => CmrCompat.Hunting.OpenThresholdDetails(job),
                () => CmrCompat.Hunting.TargetLabel(job),
                () => CmrCompat.Hunting.SyncFilterAndAllowed(job),
                value => CmrCompat.Hunting.SetSyncFilterAndAllowed(job, value),
                // Hunting draws path-based distance before reachability; the other three threshold
                // tabs draw the reverse.
                reachBeforePath: false);

            if (!CmrCompat.Hunting.TargetsMeat(job))
            {
                return;
            }

            // Human meat is the one tri-state here: the filter holds a def per humanlike race, so the
            // mod reads "all of them" and "none of them" separately and one click fills or empties the
            // whole set (ManagerTab_Hunting.cs:519-528).
            region.Rows.Add(new CmrDetailRow
            {
                Label = ModText("ColonyManagerRedux.Hunting.AllowHumanMeat"),
                Tooltip = ModTip("ColonyManagerRedux.Hunting.AllowHumanMeat.Tip"),
                Role = ElementRole.Checkbox,
                SectionTitle = section,
                Check = delegate
                {
                    if (CmrCompat.Hunting.AllowsAllHumanLikeMeat(job))
                    {
                        return CheckState.Checked;
                    }
                    return CmrCompat.Hunting.AllowsNoHumanLikeMeat(job)
                        ? CheckState.Unchecked
                        : CheckState.PartiallyChecked;
                },
                Activate = () => CmrCompat.Hunting.SetHumanLikeMeat(job,
                    !CmrCompat.Hunting.AllowsAllHumanLikeMeat(job)),
            });

            region.Rows.Add(CmrTabRows.Toggle(section,
                "ColonyManagerRedux.Hunting.AllowInsectMeat",
                () => CmrCompat.Hunting.AllowsInsectMeat(job),
                value => CmrCompat.Hunting.SetInsectMeat(job, value)));

            if (ModsConfig.AnomalyActive && CmrCompat.Hunting.TwistedMeatExists)
            {
                region.Rows.Add(CmrTabRows.Toggle(section,
                    "ColonyManagerRedux.Hunting.AllowTwistedMeat",
                    () => CmrCompat.Hunting.AllowsTwistedMeat(job),
                    value => CmrCompat.Hunting.SetTwistedMeat(job, value)));
            }
        }

        /// <summary>The mod draws this pair in a section of its own, with no heading of any kind.</summary>
        private static void AddUnforbidCorpses(CmrDetailRegion region, object job)
        {
            region.Rows.Add(CmrTabRows.Toggle(null,
                "ColonyManagerRedux.Hunting.UnforbidCorpses",
                () => CmrCompat.Hunting.UnforbidCorpses(job),
                value => CmrCompat.Hunting.SetUnforbidCorpses(job, value)));
            if (CmrCompat.Hunting.UnforbidCorpses(job))
            {
                region.Rows.Add(CmrTabRows.Toggle(null,
                    "ColonyManagerRedux.Hunting.UnforbidAllCorpses",
                    () => CmrCompat.Hunting.UnforbidAllCorpses(job),
                    value => CmrCompat.Hunting.SetUnforbidAllCorpses(job, value)));
                if (CmrCompat.Hunting.UnforbidAllCorpses(job))
                {
                    region.Rows.Add(CmrTabRows.Toggle(null,
                        "ColonyManagerRedux.Hunting.UnforbidHumanCorpses",
                        () => CmrCompat.Hunting.UnforbidHumanCorpses(job),
                        value => CmrCompat.Hunting.SetUnforbidHumanCorpses(job, value)));
                }
            }
        }

        /// <summary>
        /// The area strip and its invert toggle. The strip's cells share one heading and one value, so
        /// the row wears that heading as its label and carries no section title of its own -- the
        /// section crossing is already audible in the label.
        /// </summary>
        private static void AddHuntingGrounds(CmrDetailRegion region, object job)
        {
            region.Rows.Add(CmrTabRows.AreaRow(
                ModText("ColonyManagerRedux.Hunting.AreaRestriction"),
                () => CmrCompat.Hunting.HuntingGrounds(job),
                area => CmrCompat.Hunting.SetHuntingGrounds(job, area),
                () => CmrCompat.JobBase.MapOf(job)));
            region.Rows.Add(CmrTabRows.Toggle(null,
                "ColonyManagerRedux.InvertArea",
                () => CmrCompat.Hunting.InvertHuntingGrounds(job),
                value => CmrCompat.Hunting.SetInvertHuntingGrounds(job, value)));
        }

        // ------------------------------------------------------------------
        // Animals: the right column.
        // ------------------------------------------------------------------

        private static CmrDetailRegion BuildAnimals(object job)
        {
            var region = new CmrDetailRegion(ModText("ColonyManagerRedux.Hunting.Animals"));
            List<PawnKindDef> all = CmrCompat.Hunting.AllAnimals(job);

            CmrTabRows.AddShortcutRows(region, all, CmrCompat.Shortcuts.Hunting(CmrCompat.JobTab(job)),
                kind => CmrCompat.Hunting.IsAnimalAllowed(job, kind),
                (kind, allow) => CmrCompat.Hunting.SetAnimalAllowed(job, kind, allow));

            CmrTabRows.AddRefreshAndLock(region,
                "RimWorldAccess.Cmr.Hunting.RefreshAnimals", "RimWorldAccess.Cmr.Hunting.AnimalsRefreshed",
                () => CmrCompat.Hunting.RefreshAllAnimals(job),
                "RimWorldAccess.Cmr.Hunting.LockAnimalsToMap",
                () => CmrCompat.Hunting.AnimalsLockedToMap(job),
                value => CmrCompat.Hunting.SetAnimalsLockedToMap(job, value));

            foreach (PawnKindDef animal in all)
            {
                PawnKindDef kind = animal;
                region.Rows.Add(new CmrDetailRow
                {
                    Label = kind.LabelCap,
                    Role = ElementRole.Checkbox,
                    Check = () => CmrCompat.Hunting.IsAnimalAllowed(job, kind)
                        ? CheckState.Checked
                        : CheckState.Unchecked,
                    Activate = () => CmrCompat.Hunting.SetAnimalAllowed(job, kind,
                        !CmrCompat.Hunting.IsAnimalAllowed(job, kind)),
                    Tooltip = () => AnimalTail(job, kind),
                    InfoCardDef = kind.race,
                });
            }
            return region;
        }

        /// <summary>
        /// What an animal row shows beyond its name: the mod's own hover text (description, expected
        /// yield of the job's target resource, aggressiveness), then the two icons the mod draws on
        /// the row. The claw icon has no hover text of its own, so it gets our own wording; the
        /// venerated icon has the mod's, including its own some/all distinction.
        /// </summary>
        private static string AnimalTail(object job, PawnKindDef animal)
        {
            var parts = new List<string>();
            parts.Add(Flatten(CmrCompat.Hunting.AnimalTooltip(job, animal)));
            if (animal.RaceProps.manhunterOnDamageChance >= 0.1f)
            {
                parts.Add("RimWorldAccess.Cmr.Hunting.RevengeRisk".Translate());
            }
            string venerated = VeneratedFragment(job, animal);
            if (venerated != null)
            {
                parts.Add(venerated);
            }
            return CompatText.JoinSentences(parts);
        }

        /// <summary>
        /// Mirrors the venerated icon's own condition and tooltip (ManagerTab_Hunting.cs:196-221):
        /// drawn when at least one spawned free colonist venerates the race, worded by whether that
        /// is all of them or only some.
        /// </summary>
        private static string VeneratedFragment(object job, PawnKindDef animal)
        {
            if (!ModsConfig.IdeologyActive || animal.race == null)
            {
                return null;
            }
            Map map = CmrCompat.JobBase.MapOf(job);
            if (map == null)
            {
                return null;
            }
            bool anyVenerated = false;
            bool allVenerated = true;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                bool venerated = pawn.Ideo != null && pawn.Ideo.IsVeneratedAnimal(animal.race);
                anyVenerated |= venerated;
                allVenerated &= venerated;
            }
            if (!anyVenerated)
            {
                return null;
            }
            string share = ModText(allVenerated
                ? "ColonyManagerRedux.Misc.All"
                : "ColonyManagerRedux.Misc.Some");
            return Flatten(ModArgs("ColonyManagerRedux.Hunting.VeneratedAnimal.Tip", share));
        }

        // ------------------------------------------------------------------
        // Threshold count and area helpers.
        // ------------------------------------------------------------------

        /// <summary>
        /// The threshold label and its tooltip, composed from exactly the four values the mod's own
        /// section passes to the same two keys (ManagerTab_Hunting.cs:465-489): what is in storage now,
        /// what the corpses on the map will yield, what the outstanding designations will yield, and
        /// the trigger's own operator-and-target label.
        /// </summary>
        private static string ThresholdText(object job, string key)
        {
            return ModArgs(key,
                CmrCompat.Hunting.CurrentCount(job),
                CmrCompat.Hunting.YieldInCorpses(job),
                CmrCompat.Hunting.YieldInDesignations(job),
                CmrCompat.Hunting.TargetLabel(job));
        }
    }
}
