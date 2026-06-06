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

            // Vanilla one-word noun keys we lean on. CanTranslate() guards each so a
            // missing/DLC-gated key falls back to a safe English default rather than
            // emitting the raw key name. Verified against the Core/Royalty/Ideology/
            // Biotech/Anomaly Keyed XML at lookup time:
            //   "Person"     → Misc_Gameplay.xml (Core)
            //   "Location"   → Misc_Gameplay.xml (Core)
            //   "Colonist"   → Misc.xml          (Core)
            //   "Prisoner"   → Misc_Gameplay.xml (Core)
            //   "Slave"      → ITabs.xml         (Core)
            //   "Animal"     → ITabs.xml         (Ideology)
            //   "Mechanoid"  → ScenParts.xml     (Biotech)
            string person   = Tr("Person",    "person");
            string colonist = Tr("Colonist",  "colonist");
            string prisoner = Tr("Prisoner",  "prisoner");
            string slave    = Tr("Slave",     "slave");
            string animal   = Tr("Animal",    "animal");
            string mech     = Tr("Mechanoid", "mech");
            string location = Tr("Location",  "location");

            // Single-shot exclusives — vanilla rejects everything else when these are set.
            if (p.targetSpecificThing != null) return $"Looking for {p.targetSpecificThing.LabelShort}";
            if (p.onlyTargetDoors) return $"Looking for a door";
            if (p.onlyTargetCorpses) return $"Looking for a corpse";
            if (p.onlyTargetIncapacitatedPawns) return $"Looking for a downed {person}";
            if (p.onlyTargetAnimaTrees) return $"Looking for an anima tree";
            if (p.onlyTargetDamagedThings) return $"Looking for a damaged thing";
            if (p.onlyTargetFlammables) return $"Looking for a flammable target";
            if (p.onlyTargetColonists) return $"Looking for a {colonist}";
            if (p.onlyTargetPrisonersOfColony) return $"Looking for a {prisoner}";
            if (p.onlyTargetColonistsOrPrisoners) return $"Looking for a {colonist} or {prisoner}";
            if (p.onlyTargetColonistsOrPrisonersOrSlaves) return $"Looking for a {colonist}, {prisoner}, or {slave}";

            // Mirror vanilla TargetingParameters.CanTarget so we only mention what the
            // params actually accept. The pawn sub-flags (canTargetHumans/Mechs/Entities/
            // Animals) only matter when a parent flag (canTargetPawns or canTargetCorpses)
            // is true; iterating them independently produced bogus descriptions like
            // "Looking for a human, a mech, an entity, or a corpse" for the resurrector
            // serum, which actually has canTargetPawns=false and canTargetCorpses=true.
            var parts = new List<string>();

            if (p.canTargetPawns)
            {
                parts.Add(DescribePawnCategory(p, person, animal, mech));
            }

            if (p.canTargetCorpses)
            {
                parts.Add(DescribeCorpseCategory(p, person, animal, mech));
            }

            if (p.canTargetBuildings) parts.Add("a building");
            if (p.canTargetItems) parts.Add("an item");
            if (p.canTargetFires) parts.Add("a fire");
            if (p.canTargetPlants) parts.Add("a plant");
            if (p.canTargetSelf) parts.Add("self");
            if (p.canTargetLocations) parts.Add($"a {location}");

            if (parts.Count == 0) return "";
            return $"Looking for {Join(parts)}";
        }

        private static string DescribePawnCategory(TargetingParameters p, string person, string animal, string mech)
        {
            // Sub-flags filter which races the params accept. All four default true →
            // just "person" (matches vanilla's user-facing language); otherwise enumerate.
            bool allDefault = p.canTargetHumans && p.canTargetAnimals && p.canTargetMechs && p.canTargetEntities;
            if (allDefault) return $"a {person}";

            var subs = new List<string>();
            if (p.canTargetHumans) subs.Add($"a {person}");
            if (p.canTargetAnimals) subs.Add($"an {animal}");
            if (p.canTargetMechs) subs.Add($"a {mech}");
            if (p.canTargetEntities) subs.Add("an entity");
            return subs.Count == 0 ? $"a {person}" : Join(subs);
        }

        private static string DescribeCorpseCategory(TargetingParameters p, string person, string animal, string mech)
        {
            // Inside the corpse branch vanilla also filters by canTargetMechs / Animals /
            // Humans against the InnerPawn's race. Default (all true) → just "a corpse".
            bool defaults = p.canTargetHumans && p.canTargetAnimals && p.canTargetMechs;
            if (defaults) return "a corpse";

            var subs = new List<string>();
            if (p.canTargetHumans) subs.Add($"a {person}'s corpse");
            if (p.canTargetAnimals) subs.Add($"an {animal} corpse");
            if (p.canTargetMechs) subs.Add($"a {mech} corpse");
            return subs.Count == 0 ? "a corpse" : Join(subs);
        }

        /// <summary>
        /// Returns the localized string for <paramref name="key"/>, or
        /// <paramref name="fallback"/> if the key is not present in the loaded language
        /// (for DLC-gated keys like "Animal" / "Mechanoid" or for languages that haven't
        /// been updated yet). Never emits the raw key name to the user.
        /// </summary>
        private static string Tr(string key, string fallback)
        {
            return key.CanTranslate() ? key.Translate().ToString() : fallback;
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
