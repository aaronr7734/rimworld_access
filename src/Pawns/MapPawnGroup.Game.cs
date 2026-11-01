using System;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Who a pawn on the map is to the colony, in the order overview lists rank them.</summary>
    internal enum MapPawnGroup
    {
        Colonist,
        ColonyMech,
        ColonyAnimal,
        Slave,
        Prisoner,
        Visitor,
        Wild,
        Hostile,
    }

    /// <summary>The overview filter checkboxes, persisted as flags.</summary>
    [Flags]
    public enum MapPawnGroupFilter
    {
        None = 0,
        Colonists = 1,
        ColonyAnimals = 2,
        ColonyMechs = 4,
        PrisonersAndSlaves = 8,
        Visitors = 16,
        Hostiles = 32,
        All = Colonists | ColonyAnimals | ColonyMechs | PrisonersAndSlaves | Visitors | Hostiles,
    }

    internal static class MapPawnGroups
    {
        /// <summary>
        /// Hostility first, so prison breakers and rebelling slaves count as hostile; hosted guests
        /// before the faction test, since prisoners and guests keep a hostile home faction. Faction
        /// hostility also catches dormant mechs and entities that HostileTo skips.
        /// </summary>
        internal static MapPawnGroup Of(Pawn pawn)
        {
            Faction player = Faction.OfPlayer;
            if (pawn.HostileTo(player))
            {
                return MapPawnGroup.Hostile;
            }
            if (pawn.IsPrisonerOfColony)
            {
                return MapPawnGroup.Prisoner;
            }
            if (pawn.IsSlaveOfColony)
            {
                return MapPawnGroup.Slave;
            }
            if (pawn.HostFaction == player)
            {
                return MapPawnGroup.Visitor;
            }
            if (pawn.Faction == player)
            {
                if (pawn.RaceProps.Humanlike)
                {
                    return MapPawnGroup.Colonist;
                }
                return pawn.IsColonyMech ? MapPawnGroup.ColonyMech : MapPawnGroup.ColonyAnimal;
            }
            if (pawn.Faction?.HostileTo(player) == true)
            {
                return MapPawnGroup.Hostile;
            }
            return pawn.Faction == null ? MapPawnGroup.Wild : MapPawnGroup.Visitor;
        }

        /// <summary>The checkbox that shows a group; the factionless share Visitors.</summary>
        internal static MapPawnGroupFilter FilterOf(MapPawnGroup group)
        {
            switch (group)
            {
                case MapPawnGroup.Colonist:
                    return MapPawnGroupFilter.Colonists;
                case MapPawnGroup.ColonyMech:
                    return MapPawnGroupFilter.ColonyMechs;
                case MapPawnGroup.ColonyAnimal:
                    return MapPawnGroupFilter.ColonyAnimals;
                case MapPawnGroup.Slave:
                case MapPawnGroup.Prisoner:
                    return MapPawnGroupFilter.PrisonersAndSlaves;
                case MapPawnGroup.Hostile:
                    return MapPawnGroupFilter.Hostiles;
                default:
                    return MapPawnGroupFilter.Visitors;
            }
        }

        internal static bool Shown(MapPawnGroup group, MapPawnGroupFilter shown)
        {
            return (shown & FilterOf(group)) != 0;
        }

        /// <summary>"Rook, colonist".</summary>
        internal static string NameWithGroup(string name, MapPawnGroup group)
        {
            switch (group)
            {
                case MapPawnGroup.Colonist:
                    return "RimWorldAccess.PawnGroup.Colonist".Translate(name);
                case MapPawnGroup.ColonyMech:
                    return "RimWorldAccess.PawnGroup.ColonyMech".Translate(name);
                case MapPawnGroup.ColonyAnimal:
                    return "RimWorldAccess.PawnGroup.ColonyAnimal".Translate(name);
                case MapPawnGroup.Slave:
                    return "RimWorldAccess.PawnGroup.Slave".Translate(name);
                case MapPawnGroup.Prisoner:
                    return "RimWorldAccess.PawnGroup.Prisoner".Translate(name);
                case MapPawnGroup.Visitor:
                    return "RimWorldAccess.PawnGroup.Visitor".Translate(name);
                case MapPawnGroup.Wild:
                    return "RimWorldAccess.PawnGroup.Wild".Translate(name);
                default:
                    return "RimWorldAccess.PawnGroup.Hostile".Translate(name);
            }
        }
    }
}
