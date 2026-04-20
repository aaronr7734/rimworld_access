using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public class FacilityInfoEntry
    {
        public string Label { get; set; }
        public bool IsHeader { get; set; }
    }

    public static class FacilityLinkHelper
    {
        /// <summary>
        /// Checks if a building has any facility-related comps.
        /// </summary>
        public static bool HasFacilityComps(Building building)
        {
            if (building == null) return false;
            return building.TryGetComp<CompFacility>() != null
                || building.TryGetComp<CompAffectedByFacilities>() != null;
        }

        /// <summary>
        /// Gets a brief placement description for facility-linked buildings.
        /// Reads CompProperties from the ThingDef (no building instance needed).
        /// </summary>
        public static string GetPlacementInfo(ThingDef def)
        {
            if (def?.comps == null) return null;

            var parts = new List<string>();

            // Check if this building IS a facility (provider)
            var facilityProps = def.GetCompProperties<CompProperties_Facility>();
            if (facilityProps != null)
            {
                string distInfo = facilityProps.mustBePlacedAdjacent
                    ? "adjacent"
                    : $"within {facilityProps.maxDistance:F0} tiles";

                var statNames = facilityProps.statOffsets?
                    .Select(s => s.stat.LabelCap.ToString())
                    .ToList();

                if (statNames != null && statNames.Count > 0)
                {
                    parts.Add($"Facility: boosts {string.Join(", ", statNames)}, {distInfo}");
                }
                else
                {
                    parts.Add($"Facility: links to buildings {distInfo}");
                }

                if (facilityProps.maxSimultaneous > 1)
                {
                    parts.Add($"max {facilityProps.maxSimultaneous} per building");
                }
            }

            // Check if this building RECEIVES facility bonuses (consumer)
            var affectedProps = def.GetCompProperties<CompProperties_AffectedByFacilities>();
            if (affectedProps?.linkableFacilities != null && affectedProps.linkableFacilities.Count > 0)
            {
                var facilityNames = affectedProps.linkableFacilities
                    .Select(f => f.LabelCap.ToString())
                    .Distinct()
                    .ToList();

                if (facilityNames.Count > 0)
                {
                    string facilityList = facilityNames.Count <= 3
                        ? string.Join(", ", facilityNames)
                        : $"{string.Join(", ", facilityNames.Take(3))} and {facilityNames.Count - 3} more";
                    parts.Add($"Accepts facilities: {facilityList}");
                }
            }

            return parts.Count > 0 ? string.Join(". ", parts) : null;
        }

        /// <summary>
        /// Gets detailed facility link information for an existing building.
        /// Used by the inspection system.
        /// </summary>
        public static string GetInspectionInfo(Building building)
        {
            if (building == null) return null;

            var sb = new StringBuilder();

            var facility = building.TryGetComp<CompFacility>();
            if (facility != null)
            {
                BuildFacilityProviderInfo(sb, facility);
            }

            var affected = building.TryGetComp<CompAffectedByFacilities>();
            if (affected != null)
            {
                BuildFacilityConsumerInfo(sb, affected);
            }

            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }

        /// <summary>
        /// Gets structured facility info as a list of entries for tree building.
        /// </summary>
        public static List<FacilityInfoEntry> GetFacilityEntries(Building building)
        {
            var entries = new List<FacilityInfoEntry>();
            if (building == null) return entries;

            var facility = building.TryGetComp<CompFacility>();
            if (facility != null)
            {
                BuildFacilityProviderEntries(entries, facility);
            }

            var affected = building.TryGetComp<CompAffectedByFacilities>();
            if (affected != null)
            {
                BuildFacilityConsumerEntries(entries, affected);
            }

            return entries;
        }

        private static void BuildFacilityProviderInfo(StringBuilder sb, CompFacility facility)
        {
            var props = facility.Props;

            // Stat offsets
            if (props.statOffsets != null && props.statOffsets.Count > 0)
            {
                sb.AppendLine("Provides bonuses:");
                foreach (var statMod in props.statOffsets)
                {
                    sb.AppendLine($"  {statMod.stat.LabelCap}: {statMod.ValueToStringAsOffset}");
                }
            }

            // Connected buildings
            var linked = facility.LinkedBuildings;
            if (linked != null && linked.Count > 0)
            {
                string buildingWord = linked.Count == 1 ? "building" : "buildings";
                sb.AppendLine($"Linked to {linked.Count} {buildingWord}:");
                foreach (var linkedBuilding in linked)
                {
                    bool isActive = linkedBuilding.TryGetComp<CompAffectedByFacilities>()?.IsFacilityActive(facility.parent) ?? true;
                    string status = isActive ? "" : " (inactive)";
                    sb.AppendLine($"  {linkedBuilding.LabelCap}{status}");
                }
            }
            else
            {
                sb.AppendLine("Not linked to any buildings.");
            }

            if (props.showMaxSimultaneous && props.maxSimultaneous > 0)
            {
                sb.AppendLine($"Max connections per building: {props.maxSimultaneous}");
            }
        }

        private static void BuildFacilityConsumerInfo(StringBuilder sb, CompAffectedByFacilities affected)
        {
            var linked = affected.LinkedFacilitiesListForReading;

            if (linked != null && linked.Count > 0)
            {
                string facilityWord = linked.Count == 1 ? "facility" : "facilities";
                sb.AppendLine($"Boosted by {linked.Count} {facilityWord}:");
                foreach (var linkedFacility in linked)
                {
                    bool isActive = affected.IsFacilityActive(linkedFacility);
                    string status = isActive ? "active" : "inactive";
                    sb.AppendLine($"  {linkedFacility.LabelCap} ({status})");
                }
            }
            else
            {
                sb.AppendLine("No facilities linked.");
            }

            // Show compatible facility types
            var props = affected.parent.def.GetCompProperties<CompProperties_AffectedByFacilities>();
            if (props?.linkableFacilities != null && props.linkableFacilities.Count > 0)
            {
                var facilityNames = props.linkableFacilities
                    .Select(f => f.LabelCap.ToString())
                    .Distinct()
                    .ToList();
                sb.AppendLine($"Compatible facilities: {string.Join(", ", facilityNames)}");
            }
        }

        private static void BuildFacilityProviderEntries(List<FacilityInfoEntry> entries, CompFacility facility)
        {
            var props = facility.Props;

            // Stat offsets
            if (props.statOffsets != null && props.statOffsets.Count > 0)
            {
                entries.Add(new FacilityInfoEntry
                {
                    Label = "Provides bonuses:",
                    IsHeader = true
                });
                foreach (var statMod in props.statOffsets)
                {
                    entries.Add(new FacilityInfoEntry
                    {
                        Label = $"{statMod.stat.LabelCap}: {statMod.ValueToStringAsOffset}"
                    });
                }
            }

            // Connection info
            var linked = facility.LinkedBuildings;
            if (linked != null && linked.Count > 0)
            {
                string buildingWord = linked.Count == 1 ? "building" : "buildings";
                entries.Add(new FacilityInfoEntry
                {
                    Label = $"Linked to {linked.Count} {buildingWord}:",
                    IsHeader = true
                });
                foreach (var linkedBuilding in linked)
                {
                    bool isActive = linkedBuilding.TryGetComp<CompAffectedByFacilities>()?.IsFacilityActive(facility.parent) ?? true;
                    string status = isActive ? "" : " (inactive)";
                    entries.Add(new FacilityInfoEntry
                    {
                        Label = $"{linkedBuilding.LabelCap}{status}"
                    });
                }
            }
            else
            {
                entries.Add(new FacilityInfoEntry
                {
                    Label = "Not linked to any buildings."
                });
            }

            if (props.showMaxSimultaneous && props.maxSimultaneous > 0)
            {
                entries.Add(new FacilityInfoEntry
                {
                    Label = $"Max connections per building: {props.maxSimultaneous}"
                });
            }
        }

        private static void BuildFacilityConsumerEntries(List<FacilityInfoEntry> entries, CompAffectedByFacilities affected)
        {
            var linked = affected.LinkedFacilitiesListForReading;

            if (linked != null && linked.Count > 0)
            {
                string facilityWord = linked.Count == 1 ? "facility" : "facilities";
                entries.Add(new FacilityInfoEntry
                {
                    Label = $"Boosted by {linked.Count} {facilityWord}:",
                    IsHeader = true
                });
                foreach (var linkedFacility in linked)
                {
                    bool isActive = affected.IsFacilityActive(linkedFacility);
                    string status = isActive ? "active" : "inactive";
                    entries.Add(new FacilityInfoEntry
                    {
                        Label = $"{linkedFacility.LabelCap} ({status})"
                    });
                }
            }
            else
            {
                entries.Add(new FacilityInfoEntry
                {
                    Label = "No facilities linked."
                });
            }

            // Compatible facility types
            var props = affected.parent.def.GetCompProperties<CompProperties_AffectedByFacilities>();
            if (props?.linkableFacilities != null && props.linkableFacilities.Count > 0)
            {
                var facilityNames = props.linkableFacilities
                    .Select(f => f.LabelCap.ToString())
                    .Distinct()
                    .ToList();
                entries.Add(new FacilityInfoEntry
                {
                    Label = $"Compatible facilities: {string.Join(", ", facilityNames)}"
                });
            }
        }
    }
}
