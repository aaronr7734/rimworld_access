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
                    ? (string)"RimWorldAccess.Building.Facility.Adjacent".Translate()
                    : (string)"RimWorldAccess.Building.Facility.WithinTiles".Translate(facilityProps.maxDistance.ToString("F0"));

                var statNames = facilityProps.statOffsets?
                    .Select(s => s.stat.LabelCap.ToString())
                    .ToList();

                if (statNames != null && statNames.Count > 0)
                {
                    parts.Add((string)"RimWorldAccess.Building.Facility.BoostsStats".Translate(string.Join(", ", statNames), distInfo));
                }
                else
                {
                    parts.Add((string)"RimWorldAccess.Building.Facility.LinksToBuildings".Translate(distInfo));
                }

                if (facilityProps.maxSimultaneous > 1)
                {
                    parts.Add((string)"RimWorldAccess.Building.Facility.MaxPerBuilding".Translate(facilityProps.maxSimultaneous));
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
                        : (string)"RimWorldAccess.Building.Facility.AndNMore".Translate(string.Join(", ", facilityNames.Take(3)), facilityNames.Count - 3);
                    parts.Add((string)"RimWorldAccess.Building.Facility.AcceptsFacilities".Translate(facilityList));
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
                sb.AppendLine("RimWorldAccess.Building.Facility.ProvidesBonuses".Translate());
                foreach (var statMod in props.statOffsets)
                {
                    sb.AppendLine("  " + (string)"RimWorldAccess.Building.Facility.StatOffset".Translate((string)statMod.stat.LabelCap, statMod.ValueToStringAsOffset));
                }
            }

            // Connected buildings
            var linked = facility.LinkedBuildings;
            if (linked != null && linked.Count > 0)
            {
                sb.AppendLine(linked.Count == 1
                    ? (string)"RimWorldAccess.Building.Facility.LinkedToOne".Translate(linked.Count)
                    : (string)"RimWorldAccess.Building.Facility.LinkedToMany".Translate(linked.Count));
                foreach (var linkedBuilding in linked)
                {
                    bool isActive = linkedBuilding.TryGetComp<CompAffectedByFacilities>()?.IsFacilityActive(facility.parent) ?? true;
                    string status = isActive ? "" : " (" + (string)"RimWorldAccess.Building.Facility.Inactive".Translate() + ")";
                    sb.AppendLine("  " + (string)linkedBuilding.LabelCap + status);
                }
            }
            else
            {
                sb.AppendLine("RimWorldAccess.Building.Facility.NotLinkedToBuildings".Translate());
            }

            if (props.showMaxSimultaneous && props.maxSimultaneous > 0)
            {
                sb.AppendLine("RimWorldAccess.Building.Facility.MaxConnectionsPerBuilding".Translate(props.maxSimultaneous));
            }
        }

        private static void BuildFacilityConsumerInfo(StringBuilder sb, CompAffectedByFacilities affected)
        {
            var linked = affected.LinkedFacilitiesListForReading;

            if (linked != null && linked.Count > 0)
            {
                sb.AppendLine(linked.Count == 1
                    ? (string)"RimWorldAccess.Building.Facility.BoostedByOne".Translate(linked.Count)
                    : (string)"RimWorldAccess.Building.Facility.BoostedByMany".Translate(linked.Count));
                foreach (var linkedFacility in linked)
                {
                    bool isActive = affected.IsFacilityActive(linkedFacility);
                    string status = isActive
                        ? (string)"RimWorldAccess.Building.Facility.Active".Translate()
                        : (string)"RimWorldAccess.Building.Facility.Inactive".Translate();
                    sb.AppendLine("  " + (string)linkedFacility.LabelCap + " (" + status + ")");
                }
            }
            else
            {
                sb.AppendLine("RimWorldAccess.Building.Facility.NoFacilitiesLinked".Translate());
            }

            // Show compatible facility types
            var props = affected.parent.def.GetCompProperties<CompProperties_AffectedByFacilities>();
            if (props?.linkableFacilities != null && props.linkableFacilities.Count > 0)
            {
                var facilityNames = props.linkableFacilities
                    .Select(f => f.LabelCap.ToString())
                    .Distinct()
                    .ToList();
                sb.AppendLine("RimWorldAccess.Building.Facility.CompatibleFacilities".Translate(string.Join(", ", facilityNames)));
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
                    Label = "RimWorldAccess.Building.Facility.ProvidesBonuses".Translate(),
                    IsHeader = true
                });
                foreach (var statMod in props.statOffsets)
                {
                    entries.Add(new FacilityInfoEntry
                    {
                        Label = (string)"RimWorldAccess.Building.Facility.StatOffset".Translate((string)statMod.stat.LabelCap, statMod.ValueToStringAsOffset)
                    });
                }
            }

            // Connection info
            var linked = facility.LinkedBuildings;
            if (linked != null && linked.Count > 0)
            {
                entries.Add(new FacilityInfoEntry
                {
                    Label = linked.Count == 1
                        ? (string)"RimWorldAccess.Building.Facility.LinkedToOne".Translate(linked.Count)
                        : (string)"RimWorldAccess.Building.Facility.LinkedToMany".Translate(linked.Count),
                    IsHeader = true
                });
                foreach (var linkedBuilding in linked)
                {
                    bool isActive = linkedBuilding.TryGetComp<CompAffectedByFacilities>()?.IsFacilityActive(facility.parent) ?? true;
                    string status = isActive ? "" : " (" + (string)"RimWorldAccess.Building.Facility.Inactive".Translate() + ")";
                    entries.Add(new FacilityInfoEntry
                    {
                        Label = (string)linkedBuilding.LabelCap + status
                    });
                }
            }
            else
            {
                entries.Add(new FacilityInfoEntry
                {
                    Label = "RimWorldAccess.Building.Facility.NotLinkedToBuildings".Translate()
                });
            }

            if (props.showMaxSimultaneous && props.maxSimultaneous > 0)
            {
                entries.Add(new FacilityInfoEntry
                {
                    Label = (string)"RimWorldAccess.Building.Facility.MaxConnectionsPerBuilding".Translate(props.maxSimultaneous)
                });
            }
        }

        private static void BuildFacilityConsumerEntries(List<FacilityInfoEntry> entries, CompAffectedByFacilities affected)
        {
            var linked = affected.LinkedFacilitiesListForReading;

            if (linked != null && linked.Count > 0)
            {
                entries.Add(new FacilityInfoEntry
                {
                    Label = linked.Count == 1
                        ? (string)"RimWorldAccess.Building.Facility.BoostedByOne".Translate(linked.Count)
                        : (string)"RimWorldAccess.Building.Facility.BoostedByMany".Translate(linked.Count),
                    IsHeader = true
                });
                foreach (var linkedFacility in linked)
                {
                    bool isActive = affected.IsFacilityActive(linkedFacility);
                    string status = isActive
                        ? (string)"RimWorldAccess.Building.Facility.Active".Translate()
                        : (string)"RimWorldAccess.Building.Facility.Inactive".Translate();
                    entries.Add(new FacilityInfoEntry
                    {
                        Label = (string)linkedFacility.LabelCap + " (" + status + ")"
                    });
                }
            }
            else
            {
                entries.Add(new FacilityInfoEntry
                {
                    Label = "RimWorldAccess.Building.Facility.NoFacilitiesLinked".Translate()
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
                    Label = (string)"RimWorldAccess.Building.Facility.CompatibleFacilities".Translate(string.Join(", ", facilityNames))
                });
            }
        }
    }
}
