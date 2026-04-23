using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Turns a <see cref="TargetingParameters"/> into a human-readable description like
    /// "Looking for an enemy" or "Looking for a building or location". Used by
    /// <see cref="GenericTargetingState"/> and others so blind users hear what kind of
    /// target the active session expects, rather than discovering it by trial and error.
    ///
    /// Walks the boolean flags in the order vanilla's TargetingParameters.CanTarget
    /// applies them (most-specific first) so the description matches what the game
    /// would actually accept.
    /// </summary>
    public static class TargetingParametersDescriber
    {
        public static string Describe(TargetingParameters p)
        {
            if (p == null) return "";

            // Single-shot exclusives — vanilla rejects everything else.
            if (p.targetSpecificThing != null) return $"Looking for {p.targetSpecificThing.LabelShort}";
            if (p.onlyTargetDoors) return "Looking for a door";
            if (p.onlyTargetCorpses) return "Looking for a corpse";
            if (p.onlyTargetIncapacitatedPawns) return "Looking for a downed pawn";
            if (p.onlyTargetAnimaTrees) return "Looking for an anima tree";
            if (p.onlyTargetDamagedThings) return "Looking for a damaged thing";
            if (p.onlyTargetFlammables) return "Looking for a flammable target";

            var parts = new List<string>();
            if (p.canTargetHumans) parts.Add("a human");
            else if (p.canTargetPawns)
            {
                if (p.canTargetAnimals && !p.canTargetMechs) parts.Add("a pawn or animal");
                else parts.Add("a pawn");
            }
            else if (p.canTargetAnimals) parts.Add("an animal");
            if (p.canTargetMechs && !p.canTargetPawns) parts.Add("a mech");
            if (p.canTargetEntities) parts.Add("an entity");
            if (p.canTargetBuildings) parts.Add("a building");
            if (p.canTargetItems) parts.Add("an item");
            if (p.canTargetFires) parts.Add("a fire");
            if (p.canTargetPlants) parts.Add("a plant");
            if (p.canTargetCorpses) parts.Add("a corpse");
            if (p.canTargetSelf) parts.Add("self");
            if (p.canTargetLocations) parts.Add("a map location");

            if (parts.Count == 0) return "";
            return $"Looking for {Join(parts)}";
        }

        /// <summary>Returns true if the params will accept an empty cell (no Thing required).</summary>
        public static bool AcceptsLocations(TargetingParameters p) => p != null && p.canTargetLocations;

        private static string Join(List<string> parts)
        {
            if (parts.Count == 1) return parts[0];
            if (parts.Count == 2) return $"{parts[0]} or {parts[1]}";
            return string.Join(", ", parts.GetRange(0, parts.Count - 1)) + ", or " + parts[parts.Count - 1];
        }
    }
}
