using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Available gear actions. The dispatch discriminator, so callers never switch on
    /// localized labels.
    /// </summary>
    public enum GearAction
    {
        Drop,
        Consume,
        ViewInfo
    }

    /// <summary>
    /// Extracts a pawn's gear items, determines which actions each allows, and executes them.
    /// </summary>
    public static class InteractiveGearHelper
    {
        /// <summary>Localized menu label for a gear action.</summary>
        public static string GetActionLabel(GearAction action)
        {
            switch (action)
            {
                case GearAction.Drop: return "RimWorldAccess.Pawns.Gear.Action.Drop".Translate();
                case GearAction.Consume: return "RimWorldAccess.Pawns.Gear.Action.Consume".Translate();
                case GearAction.ViewInfo: return "RimWorldAccess.Pawns.Gear.Action.ViewInfo".Translate();
                default: return action.ToString();
            }
        }

        /// <summary>Gear item wrapper carrying its display label and category.</summary>
        public class GearItem
        {
            public Thing Thing { get; set; }
            public string Label { get; set; }
            public string Category { get; set; } // "Equipment", "Apparel", or "Inventory"

            public GearItem(Thing thing, string category)
            {
                Thing = thing;
                Category = category;
                Label = GetItemLabel(thing);
            }

            private string GetItemLabel(Thing thing)
            {
                return ItemLabelHelper.LabelWithCondition(thing).StripTags();
            }
        }

        /// <summary>All equipment items (weapons plus belt-layer apparel) for a pawn.</summary>
        public static List<GearItem> GetEquipmentItems(Pawn pawn)
        {
            var items = new List<GearItem>();

            if (pawn?.equipment?.AllEquipmentListForReading == null)
                return items;

            foreach (var equipment in pawn.equipment.AllEquipmentListForReading)
            {
                items.Add(new GearItem(equipment, "Equipment"));
            }

            if (pawn.apparel?.WornApparel != null)
            {
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    if (apparel.def.apparel.layers.Contains(ApparelLayerDefOf.Belt))
                    {
                        items.Add(new GearItem(apparel, "Equipment"));
                    }
                }
            }

            return items;
        }

        /// <summary>All worn apparel for a pawn, excluding the belt layer.</summary>
        public static List<GearItem> GetApparelItems(Pawn pawn)
        {
            var items = new List<GearItem>();

            if (pawn?.apparel?.WornApparel == null)
                return items;

            foreach (var apparel in pawn.apparel.WornApparel)
            {
                if (apparel.def.apparel.layers.Contains(ApparelLayerDefOf.Belt))
                    continue;

                items.Add(new GearItem(apparel, "Apparel"));
            }

            return items;
        }

        /// <summary>ITab_Pawn_Gear's apparel order key, sorted descending; 0 for anything without body part groups.</summary>
        public static int ApparelListOrder(Thing thing)
        {
            List<BodyPartGroupDef> parts = thing.def.apparel?.bodyPartGroups;
            return parts != null && parts.Count > 0 ? parts[0].listOrder : 0;
        }

        /// <summary>All inventory items for a pawn.</summary>
        public static List<GearItem> GetInventoryItems(Pawn pawn)
        {
            var items = new List<GearItem>();

            if (pawn?.inventory?.innerContainer == null)
                return items;

            foreach (var thing in pawn.inventory.innerContainer)
            {
                items.Add(new GearItem(thing, "Inventory"));
            }

            return items;
        }

        /// <summary>
        /// Actions this item currently allows. Callers localize via <see cref="GetActionLabel"/>.
        /// </summary>
        public static List<GearAction> GetAvailableActions(GearItem item, Pawn pawn)
        {
            var actions = new List<GearAction>();

            if (CanDropItem(item, pawn))
            {
                actions.Add(GearAction.Drop);
            }

            if (CanConsumeItem(item, pawn))
            {
                actions.Add(GearAction.Consume);
            }

            actions.Add(GearAction.ViewInfo);

            return actions;
        }

        /// <summary>
        /// Whether the player can direct this pawn's gear at all, regardless of drop-button
        /// visibility.
        /// </summary>
        // MUTATION-C: no gated vanilla method exposes this predicate on its own — it's a
        // private ITab property. Hand-copied verbatim from ITab_Pawn_Gear.CanControl.
        private static bool CanControl(Pawn pawn)
        {
            if (pawn.Downed || pawn.InMentalState || pawn.CarriedBy != null)
                return false;
            if (pawn.Faction != Faction.OfPlayer && !pawn.IsPrisonerOfColony)
                return false;
            if (pawn.IsPrisonerOfColony && pawn.Spawned && !pawn.Map.mapPawns.AnyFreeColonistSpawned)
                return false;
            if (pawn.IsPrisonerOfColony && (PrisonBreakUtility.IsPrisonBreaking(pawn) || (pawn.CurJob != null && pawn.CurJob.exitMapOnArrival)))
                return false;
            return true;
        }

        /// <summary>Whether the pawn is a player-controlled colonist the player can direct.</summary>
        // MUTATION-C: same as CanControl above — private ITab property, hand-copied verbatim.
        private static bool CanControlColonist(Pawn pawn)
        {
            return CanControl(pawn) && pawn.IsColonistPlayerControlled;
        }

        /// <summary>Whether the item can be dropped.</summary>
        // MUTATION-C: mirrors ITab_Pawn_Gear.DrawThingRow's drop-button visibility gate
        // (decompiled line 202, "CanControl && (inventory || CanControlColonist ||
        // off-home-map spawned)") plus its disabled-state flag (lines 206-212: quest-lodger
        // via EquipmentUtility.QuestLodgerCanUnequip, pawn.kindDef.destroyGearOnDrop for
        // non-inventory items, and locked-apparel via pawn.apparel.IsLocked). The gate lives
        // inline in the draw call with no standalone Can*/AcceptanceReport method to invoke,
        // so it is hand-copied verbatim here.
        public static bool CanDropItem(GearItem item, Pawn pawn)
        {
            if (item == null || item.Thing == null || pawn == null)
                return false;

            bool inventory = item.Category == "Inventory";

            if (!CanControl(pawn))
                return false;
            if (!(inventory || CanControlColonist(pawn) || (pawn.Spawned && !pawn.Map.IsPlayerHome)))
                return false;

            Thing thing = item.Thing;

            bool questLodgerBlocks = false;
            if (pawn.IsQuestLodger())
            {
                questLodgerBlocks = inventory || !EquipmentUtility.QuestLodgerCanUnequip(thing, pawn);
            }
            bool destroyGearOnDropBlocks = !inventory && pawn.kindDef.destroyGearOnDrop;
            bool lockedApparelBlocks = thing is Apparel apparel && pawn.apparel != null && pawn.apparel.IsLocked(apparel);

            return !(questLodgerBlocks || destroyGearOnDropBlocks || lockedApparelBlocks);
        }

        /// <summary>Whether the item can be consumed from the pawn's inventory.</summary>
        // MUTATION-C: mirrors ITab_Pawn_Gear.DrawThingRow's consume-button gate (decompiled
        // line 244, "CanControlColonist"), replacing the drifted plain "not dead" check.
        public static bool CanConsumeItem(GearItem item, Pawn pawn)
        {
            if (item == null || item.Thing == null || pawn == null)
                return false;

            if (item.Category != "Inventory")
                return false;

            if (!CanControlColonist(pawn))
                return false;

            if (item.Thing.def.IsIngestible)
            {
                return FoodUtility.WillIngestFromInventoryNow(pawn, item.Thing);
            }

            return false;
        }

        /// <summary>Drops the item, returning whether the drop completed now.</summary>
        public static bool ExecuteDropAction(GearItem item, Pawn pawn)
        {
            try
            {
                if (!CanDropItem(item, pawn))
                {
                    TolkHelper.Speak("RimWorldAccess.Pawns.Gear.CannotDrop".Loc(item.Label), SpeechPriority.High);
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return false;
                }

                Thing thing = item.Thing;

                // Rides ITab_Pawn_Gear.DrawThingRow's drop-button wrapper: every drop is gated
                // through TryConfirmBandwidthLossFromDroppingThing so a mechanitor dropping
                // bandwidth-granting apparel gets vanilla's own confirmation first.
                Action performDrop = delegate
                {
                    if (PerformDropMutation(item, thing, pawn))
                    {
                        WindowlessInspectionState.RebuildTree();
                    }
                };

                if (!ModsConfig.BiotechActive || !MechanitorUtility.TryConfirmBandwidthLossFromDroppingThing(pawn, thing, performDrop))
                {
                    return PerformDropMutation(item, thing, pawn);
                }

                // Confirmation raised: performDrop runs only on accept, so report "not yet done"
                // rather than let the caller rebuild before the dialog resolves.
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error dropping item: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Gear.ErrorDropping".Loc(item.Label), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>
        /// Performs the drop (ordered job or inventory TryDrop) mirroring
        /// <c>ITab_Pawn_Gear.InterfaceDrop</c>, and announces the outcome. Runs either
        /// immediately or from the mechanitor confirmation's accept callback.
        /// </summary>
        private static bool PerformDropMutation(GearItem item, Thing thing, Pawn pawn)
        {
            try
            {
                if (thing is Apparel apparel && pawn.apparel != null && pawn.apparel.WornApparel.Contains(apparel))
                {
                    Job job = JobMaker.MakeJob(JobDefOf.RemoveApparel, apparel);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    TolkHelper.Speak("RimWorldAccess.Pawns.Gear.Removing".Loc(item.Label));
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    return true;
                }

                if (thing is ThingWithComps equipment && pawn.equipment != null &&
                    pawn.equipment.AllEquipmentListForReading.Contains(equipment))
                {
                    Job job = JobMaker.MakeJob(JobDefOf.DropEquipment, equipment);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    TolkHelper.Speak("RimWorldAccess.Pawns.Gear.Dropping".Loc(item.Label));
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    return true;
                }

                // destroyOnDrop is a per-item flag, distinct from the pawn-level
                // kindDef.destroyGearOnDrop already checked by CanDropItem.
                if (pawn.inventory?.innerContainer != null && pawn.inventory.innerContainer.Contains(thing))
                {
                    if (thing.def.destroyOnDrop)
                    {
                        TolkHelper.Speak("RimWorldAccess.Pawns.Gear.CannotDrop".Loc(item.Label), SpeechPriority.High);
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        return false;
                    }

                    Thing droppedThing;
                    if (pawn.inventory.innerContainer.TryDrop(thing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out droppedThing))
                    {
                        TolkHelper.Speak("RimWorldAccess.Pawns.Gear.Dropped".Loc(item.Label));
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        return true;
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Pawns.Gear.FailedToDrop".Loc(item.Label), SpeechPriority.High);
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        return false;
                    }
                }

                TolkHelper.Speak("RimWorldAccess.Pawns.Gear.CannotDrop".Loc(item.Label), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error dropping item: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Gear.ErrorDropping".Loc(item.Label), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>Has the pawn consume the item from inventory.</summary>
        public static bool ExecuteConsumeAction(GearItem item, Pawn pawn)
        {
            try
            {
                if (!CanConsumeItem(item, pawn))
                {
                    TolkHelper.Speak("RimWorldAccess.Pawns.Gear.CannotConsume".Loc(item.Label), SpeechPriority.High);
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return false;
                }

                Thing thing = item.Thing;

                FoodUtility.IngestFromInventoryNow(pawn, thing);
                TolkHelper.Speak("RimWorldAccess.Pawns.Gear.Consuming".Loc(item.Label));
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorldAccess] Error consuming item: {ex}");
                TolkHelper.Speak("RimWorldAccess.Pawns.Gear.ErrorConsuming".Loc(item.Label), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }
        }

        /// <summary>Speaks detailed information about an item.</summary>
        public static void ExecuteInfoAction(GearItem item)
        {
            if (item == null || item.Thing == null)
            {
                TolkHelper.Speak("RimWorldAccess.Pawns.Gear.NoItemInfo".Loc());
                return;
            }

            Thing thing = item.Thing;
            var sb = new StringBuilder();

            sb.AppendLine(thing.LabelCap.StripTags());
            sb.AppendLine();

            string inspectString = thing.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                sb.AppendLine(inspectString);
                sb.AppendLine();
            }

            var qualityComp = thing.TryGetComp<CompQuality>();
            if (qualityComp != null)
            {
                sb.AppendLine("RimWorldAccess.Pawns.Gear.Quality".Translate(qualityComp.Quality.GetLabel()));
            }

            if (thing.def.useHitPoints)
            {
                float healthPercent = (float)thing.HitPoints / thing.MaxHitPoints;
                sb.AppendLine("RimWorldAccess.Pawns.Gear.Condition".Translate(healthPercent.ToString("P0"), thing.HitPoints, thing.MaxHitPoints));
            }

            if (thing.Stuff != null)
            {
                sb.AppendLine("RimWorldAccess.Pawns.Gear.Material".Translate(thing.Stuff.LabelCap.ToString().StripTags()));
            }

            sb.AppendLine("RimWorldAccess.Pawns.Gear.MarketValue".Translate(thing.MarketValue.ToString("F0")));

            float mass = thing.GetStatValue(StatDefOf.Mass);
            sb.AppendLine("RimWorldAccess.Pawns.Gear.Mass".Translate(mass.ToString("F2")));

            if (!string.IsNullOrEmpty(thing.def.description))
            {
                sb.AppendLine();
                sb.AppendLine("RimWorldAccess.Pawns.Gear.DescriptionHeader".Translate());
                sb.AppendLine(thing.def.description);
            }

            TolkHelper.SpeakData(sb.ToString());
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>Comma-joined localized action labels, for announcing.</summary>
        public static string GetActionsSummary(List<GearAction> actions)
        {
            if (actions == null || actions.Count == 0)
                return "RimWorldAccess.Pawns.Gear.NoActions".Translate();

            return string.Join(", ", actions.Select(GetActionLabel));
        }
    }
}
