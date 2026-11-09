using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Animal Traits System's Spare Traits column (a direct <see cref="PawnColumnWorker"/>
    /// subclass). Cell state reads through the worker's own ShouldSpare/ShouldCullFromSlaughter
    /// gates; the tip lists the matching trait labels. The column's only interaction is its
    /// header's configure float menu, so Enter serves that same menu on the cell — table-free,
    /// hence <see cref="ActivatesCellWithoutTable"/>.
    /// </summary>
    internal sealed class AnimalTraitsSpareColumnHandler : PawnColumnHandler
    {
        private readonly MethodInfo shouldSpare;
        private readonly MethodInfo shouldCull;
        private readonly MethodInfo getTraitDefNames;
        private readonly MethodInfo getCurrentComponent;
        private readonly MethodInfo getSpareTraits;
        private readonly MethodInfo getCullTraits;
        private readonly ConstructorInfo dialogCtor;

        private AnimalTraitsSpareColumnHandler(MethodInfo shouldSpare, MethodInfo shouldCull,
            MethodInfo getTraitDefNames, MethodInfo getCurrentComponent, MethodInfo getSpareTraits,
            MethodInfo getCullTraits, ConstructorInfo dialogCtor)
        {
            this.shouldSpare = shouldSpare;
            this.shouldCull = shouldCull;
            this.getTraitDefNames = getTraitDefNames;
            this.getCurrentComponent = getCurrentComponent;
            this.getSpareTraits = getSpareTraits;
            this.getCullTraits = getCullTraits;
            this.dialogCtor = dialogCtor;
        }

        /// <summary>No-ops (column stays on the honest fallback) when the mod's shape has drifted.</summary>
        public static void Register()
        {
            try
            {
                var surface = new ReflectionSurface("Animal Traits System spare column");
                Type workerType = surface.Type("AnimalTraitExtension.PawnColumnWorker_SpareTraits");
                Type componentType = surface.Type("AnimalTraitExtension.AnimalTraitsGameComponent");
                Type dialogType = surface.Type("AnimalTraitExtension.Dialog_SelectSpareTraits");

                MethodInfo spare = surface.Method(workerType, "ShouldSpareFromSlaughter", new[] { typeof(Pawn) });
                MethodInfo cull = surface.Method(workerType, "ShouldCullFromSlaughter", new[] { typeof(Pawn) });
                MethodInfo traitDefNames = surface.Method(workerType, "GetAnimalTraitDefNames", new[] { typeof(Pawn) });
                PropertyInfo current = surface.Property(componentType, "Current");
                MethodInfo spareTraits = surface.Method(componentType, "GetSpareTraits", Type.EmptyTypes);
                MethodInfo cullTraits = surface.Method(componentType, "GetCullTraits", Type.EmptyTypes);
                ConstructorInfo ctor = surface.Constructor(dialogType, new[] { typeof(bool) });
                if (!surface.Ready)
                {
                    return;
                }

                PawnColumnHandlerRegistry.Register(workerType, new AnimalTraitsSpareColumnHandler(
                    spare, cull, traitDefNames, current.GetGetMethod(), spareTraits, cullTraits, ctor));
            }
            catch (Exception ex)
            {
                ModLogger.Error("Animal Traits System spare column handler registration failed: " + ex.Message);
            }
        }

        /// <summary>The column def carries no label or headerTip (icon-only header).</summary>
        public override string HeaderLabel(PawnColumnDef def)
        {
            return "RimWorldAccess.Compat.AnimalTraits.SpareColumnName".Loc().ToString();
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            if (!DrawsCell(pawn))
            {
                return "";
            }
            if ((bool)shouldSpare.Invoke(null, new object[] { pawn }))
            {
                return "RimWorldAccess.Compat.AnimalTraits.Protected".Loc().ToString();
            }
            if ((bool)shouldCull.Invoke(null, new object[] { pawn }))
            {
                return "RimWorldAccess.Compat.AnimalTraits.MarkedForCull".Loc().ToString();
            }
            return "";
        }

        /// <summary>The trait labels behind the cell's state, mirroring the mod's own icon tooltip.</summary>
        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            if (!DrawsCell(pawn))
            {
                return null;
            }
            bool spared = (bool)shouldSpare.Invoke(null, new object[] { pawn });
            if (!spared && !(bool)shouldCull.Invoke(null, new object[] { pawn }))
            {
                return null;
            }
            object component = getCurrentComponent.Invoke(null, null);
            if (component == null)
            {
                return null;
            }
            var relevant = (spared ? getSpareTraits : getCullTraits).Invoke(component, null) as List<string>;
            var traitNames = getTraitDefNames.Invoke(def.Worker, new object[] { pawn }) as List<string>;
            if (relevant == null || traitNames == null)
            {
                return null;
            }
            var relevantSet = new HashSet<string>(relevant);
            var labels = new List<string>();
            foreach (string defName in traitNames)
            {
                if (!relevantSet.Contains(defName))
                {
                    continue;
                }
                HediffDef trait = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                labels.Add(trait != null && !trait.label.NullOrEmpty() ? trait.LabelCap.ToString() : defName);
            }
            return labels.Count > 0 ? string.Join(", ", labels) : null;
        }

        /// <summary>Mirrors the header's own float menu (PawnColumnWorker_SpareTraits.DoHeader), labels from the mod's own keys.</summary>
        public override PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(CompatText.ResolveOrFallback("AnimalTraits_ConfigureSpareTraits",
                    "RimWorldAccess.Compat.AnimalTraits.ConfigureSpare"), delegate
                {
                    Find.WindowStack.Add((Window)dialogCtor.Invoke(new object[] { false }));
                }),
                new FloatMenuOption(CompatText.ResolveOrFallback("AnimalTraits_ConfigureCullTraits",
                    "RimWorldAccess.Compat.AnimalTraits.ConfigureCull"), delegate
                {
                    Find.WindowStack.Add((Window)dialogCtor.Invoke(new object[] { true }));
                }),
            };
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return PawnColumnActivation.OpenedUI;
        }

        public override bool ActivatesCellWithoutTable(PawnColumnDef def)
        {
            return true;
        }

        /// <summary>The mod's own DoCell early-out: blank for anything but a player-faction animal.</summary>
        private static bool DrawsCell(Pawn pawn)
        {
            return pawn != null && pawn.RaceProps != null && pawn.RaceProps.Animal && pawn.Faction == Faction.OfPlayer;
        }
    }
}
