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
    /// Available gear actions. Used as the dispatch discriminator so callers
    /// don't switch on localized labels.
    /// </summary>
    public enum GearAction
    {
        Drop,
        Consume,
        ViewInfo
    }

    /// <summary>
    /// Helper class for interactive gear management.
    /// Provides methods to extract gear items, determine available actions, and execute those actions.
    /// </summary>
    public static class InteractiveGearHelper
    {
        /// <summary>
        /// Returns the localized menu label for a gear action.
        /// </summary>
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

        /// <summary>
        /// Gear item wrapper with display information.
        /// </summary>
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
                var sb = new StringBuilder();
                sb.Append(thing.LabelCap.StripTags());

                // Add quality if applicable
                var qualityComp = thing.TryGetComp<CompQuality>();
                if (qualityComp != null)
                {
                    sb.Append($" ({qualityComp.Quality})");
                }

                // Add hit points if damaged
                if (thing.def.useHitPoints && thing.HitPoints < thing.MaxHitPoints)
                {
                    float healthPercent = (float)thing.HitPoints / thing.MaxHitPoints;
                    sb.Append($" ({healthPercent:P0} HP)");
                }

                // Add stack count if >1
                if (thing.stackCount > 1)
                {
                    sb.Append($" x{thing.stackCount}");
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Gets all equipment items (weapons and belt items) for a pawn.
        /// </summary>
        public static List<GearItem> GetEquipmentItems(Pawn pawn)
        {
            var items = new List<GearItem>();

            if (pawn?.equipment?.AllEquipmentListForReading == null)
                return items;

            // Add weapons
            foreach (var equipment in pawn.equipment.AllEquipmentListForReading)
            {
                items.Add(new GearItem(equipment, "Equipment"));
            }

            // Add belt items (utility belt layer apparel)
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

        /// <summary>
        /// Gets all apparel items (excluding belt layer) for a pawn.
        /// </summary>
        public static List<GearItem> GetApparelItems(Pawn pawn)
        {
            var items = new List<GearItem>();

            if (pawn?.apparel?.WornApparel == null)
                return items;

            foreach (var apparel in pawn.apparel.WornApparel)
            {
                // Skip belt layer items (they're shown in equipment)
                if (apparel.def.apparel.layers.Contains(ApparelLayerDefOf.Belt))
                    continue;

                items.Add(new GearItem(apparel, "Apparel"));
            }

            return items;
        }

        /// <summary>
        /// Gets all inventory items for a pawn.
        /// </summary>
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
        /// Gets all available actions for a gear item.
        /// Returns enum values; callers use GetActionLabel() to localize.
        /// </summary>
        public static List<GearAction> GetAvailableActions(GearItem item, Pawn pawn)
        {
            var actions = new List<GearAction>();

            // Drop action is almost always available
            if (CanDropItem(item, pawn))
            {
                actions.Add(GearAction.Drop);
            }

            // Consume action for food/drugs in inventory
            if (CanConsumeItem(item, pawn))
            {
                actions.Add(GearAction.Consume);
            }

            // Info action is always available
            actions.Add(GearAction.ViewInfo);

            return actions;
        }

        /// <summary>
        /// Checks if an item can be dropped.
        /// </summary>
        public static bool CanDropItem(GearItem item, Pawn pawn)
        {
            if (item == null || item.Thing == null || pawn == null)
                return false;

            // Check if pawn is controllable
            if (!pawn.IsColonistPlayerControlled && !pawn.IsSlaveOfColony)
                return false;

            // Dead pawns cannot drop items (corpse inspection)
            if (pawn.Dead)
                return false;

            // Check for quest locks (quest lodgers can't drop quest-related gear)
            if (item.Thing.def.destroyOnDrop)
                return false;

            // Check for locked apparel
            if (item.Thing is Apparel apparel)
            {
                if (!ApparelUtility.HasPartsToWear(pawn, apparel.def))
                    return false;

                // Check if locked by quest or ideology
                if (pawn.apparel != null && !pawn.apparel.IsLocked(apparel))
                    return true;
                else if (pawn.apparel != null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if an item can be consumed.
        /// </summary>
        public static bool CanConsumeItem(GearItem item, Pawn pawn)
        {
            if (item == null || item.Thing == null || pawn == null)
                return false;

            // Dead pawns cannot consume items
            if (pawn.Dead)
                return false;

            // Only inventory items can be consumed
            if (item.Category != "Inventory")
                return false;

            // Must be ingestible
            if (item.Thing.def.IsIngestible)
            {
                return FoodUtility.WillIngestFromInventoryNow(pawn, item.Thing);
            }

            return false;
        }

        /// <summary>
        /// Executes the drop action for an item.
        /// </summary>
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

                // Handle apparel
                if (thing is Apparel apparel && pawn.apparel != null)
                {
                    Job job = JobMaker.MakeJob(JobDefOf.RemoveApparel, apparel);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    TolkHelper.Speak("RimWorldAccess.Pawns.Gear.Removing".Loc(item.Label));
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    return true;
                }

                // Handle equipment
                if (thing is ThingWithComps equipment && pawn.equipment != null &&
                    pawn.equipment.AllEquipmentListForReading.Contains(equipment))
                {
                    Job job = JobMaker.MakeJob(JobDefOf.DropEquipment, equipment);
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    TolkHelper.Speak("RimWorldAccess.Pawns.Gear.Dropping".Loc(item.Label));
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    return true;
                }

                // Handle inventory items
                if (pawn.inventory?.innerContainer != null && pawn.inventory.innerContainer.Contains(thing))
                {
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

        /// <summary>
        /// Executes the consume action for an item.
        /// </summary>
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

                // Use RimWorld's built-in consume logic
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

        /// <summary>
        /// Displays detailed information about an item.
        /// </summary>
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

            // Get inspect string
            string inspectString = thing.GetInspectString();
            if (!string.IsNullOrEmpty(inspectString))
            {
                sb.AppendLine(inspectString);
                sb.AppendLine();
            }

            // Quality
            var qualityComp = thing.TryGetComp<CompQuality>();
            if (qualityComp != null)
            {
                sb.AppendLine($"Quality: {qualityComp.Quality}");
            }

            // Hit points
            if (thing.def.useHitPoints)
            {
                float healthPercent = (float)thing.HitPoints / thing.MaxHitPoints;
                sb.AppendLine($"Condition: {healthPercent:P0} ({thing.HitPoints} / {thing.MaxHitPoints} HP)");
            }

            // Material
            if (thing.Stuff != null)
            {
                sb.AppendLine($"Material: {thing.Stuff.LabelCap.ToString().StripTags()}");
            }

            // Market value
            sb.AppendLine($"Market Value: {thing.MarketValue:F0} silver");

            // Mass
            float mass = thing.GetStatValue(StatDefOf.Mass);
            sb.AppendLine($"Mass: {mass:F2} kg");

            // Description
            if (!string.IsNullOrEmpty(thing.def.description))
            {
                sb.AppendLine();
                sb.AppendLine("Description:");
                sb.AppendLine(thing.def.description);
            }

            TolkHelper.SpeakData(sb.ToString());
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Gets a summary of available actions for announcing.
        /// </summary>
        public static string GetActionsSummary(List<GearAction> actions)
        {
            if (actions == null || actions.Count == 0)
                return "RimWorldAccess.Pawns.Gear.NoActions".Translate();

            return string.Join(", ", actions.Select(GetActionLabel));
        }
    }
}
