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
    ///
    /// Localization note: each semantic case is its own key (see the
    /// RimWorldAccess.Abilities.Describe.* family). Noun words come from the vanilla
    /// one-word keys via <see cref="Tr"/>; the surrounding phrase, the indefinite
    /// article, and the list conjunction live in our keys so translators control
    /// per-language grammar.
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
            if (p.targetSpecificThing != null) return L("Specific", p.targetSpecificThing.LabelShort);
            if (p.onlyTargetDoors) return L("Door");
            if (p.onlyTargetCorpses) return L("Corpse");
            if (p.onlyTargetIncapacitatedPawns) return L("DownedPerson", person);
            if (p.onlyTargetAnimaTrees) return L("AnimaTree");
            if (p.onlyTargetDamagedThings) return L("DamagedThing");
            if (p.onlyTargetFlammables) return L("Flammable");
            if (p.onlyTargetColonists) return L("Colonist", colonist);
            if (p.onlyTargetPrisonersOfColony) return L("Prisoner", prisoner);
            if (p.onlyTargetColonistsOrPrisoners) return L("ColonistOrPrisoner", colonist, prisoner);
            if (p.onlyTargetColonistsOrPrisonersOrSlaves) return L("ColonistPrisonerSlave", colonist, prisoner, slave);

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

            if (p.canTargetBuildings) parts.Add(D("Building"));
            if (p.canTargetItems) parts.Add(D("Item"));
            if (p.canTargetFires) parts.Add(D("Fire"));
            if (p.canTargetPlants) parts.Add(D("Plant"));
            if (p.canTargetSelf) parts.Add(D("Self"));
            if (p.canTargetLocations) parts.Add(D("Location", location));

            if (parts.Count == 0) return "";
            return L("Frame", Join(parts));
        }

        private static string DescribePawnCategory(TargetingParameters p, string person, string animal, string mech)
        {
            // Sub-flags filter which races the params accept. All four default true →
            // just "person" (matches vanilla's user-facing language); otherwise enumerate.
            bool allDefault = p.canTargetHumans && p.canTargetAnimals && p.canTargetMechs && p.canTargetEntities;
            if (allDefault) return D("Person", person);

            var subs = new List<string>();
            if (p.canTargetHumans) subs.Add(D("Person", person));
            if (p.canTargetAnimals) subs.Add(D("Animal", animal));
            if (p.canTargetMechs) subs.Add(D("Mech", mech));
            if (p.canTargetEntities) subs.Add(D("Entity"));
            return subs.Count == 0 ? D("Person", person) : Join(subs);
        }

        private static string DescribeCorpseCategory(TargetingParameters p, string person, string animal, string mech)
        {
            // Inside the corpse branch vanilla also filters by canTargetMechs / Animals /
            // Humans against the InnerPawn's race. Default (all true) → just "a corpse".
            bool defaults = p.canTargetHumans && p.canTargetAnimals && p.canTargetMechs;
            if (defaults) return D("Corpse");

            var subs = new List<string>();
            if (p.canTargetHumans) subs.Add(D("PersonCorpse", person));
            if (p.canTargetAnimals) subs.Add(D("AnimalCorpse", animal));
            if (p.canTargetMechs) subs.Add(D("MechCorpse", mech));
            return subs.Count == 0 ? D("Corpse") : Join(subs);
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

        /// <summary>Resolves a full "Looking for ..." phrase key.</summary>
        private static string L(string suffix, params object[] args)
        {
            string key = "RimWorldAccess.Abilities.Describe." + suffix;
            return args.Length == 0 ? key.Translate().ToString() : key.Translate(NamedArgs(args)).ToString();
        }

        /// <summary>Resolves a target-descriptor fragment key (e.g. "a building").</summary>
        private static string D(string suffix, params object[] args)
        {
            string key = "RimWorldAccess.Abilities.Describe.Part." + suffix;
            return args.Length == 0 ? key.Translate().ToString() : key.Translate(NamedArgs(args)).ToString();
        }

        private static NamedArgument[] NamedArgs(object[] args)
        {
            var named = new NamedArgument[args.Length];
            for (int i = 0; i < args.Length; i++)
                named[i] = new NamedArgument(args[i], null);
            return named;
        }

        /// <summary>Returns true if the params will accept an empty cell (no Thing required).</summary>
        public static bool AcceptsLocations(TargetingParameters p) => p != null && p.canTargetLocations;

        private static string Join(List<string> parts)
        {
            if (parts.Count == 1) return parts[0];
            if (parts.Count == 2)
                return "RimWorldAccess.Abilities.Describe.JoinPair".Translate(parts[0], parts[1]).ToString();
            // 3+: "a, b, ..., or z" — the separator and the final conjunction are localized
            // so list grammar can differ per language.
            string sep = "RimWorldAccess.Abilities.Describe.JoinSeparator".Translate().ToString();
            string head = string.Join(sep, parts.GetRange(0, parts.Count - 1));
            return "RimWorldAccess.Abilities.Describe.JoinLast".Translate(head, parts[parts.Count - 1]).ToString();
        }
    }
}
