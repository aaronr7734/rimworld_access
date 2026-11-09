using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Exact-type override for the mod's Traits text column: cell text stays the worker's own
    /// GetTextFor, but the tip is recomposed from the trait hediff defs because the mod's GetTip
    /// truncates descriptions to 97 chars and carries visual formatting. Unregistered, the type
    /// chain falls back to <see cref="TextColumnHandler"/>.
    /// </summary>
    internal sealed class AnimalTraitsTraitColumnHandler : PawnColumnHandler
    {
        private static readonly MethodInfo getTextFor = AccessTools.Method(typeof(PawnColumnWorker_Text), "GetTextFor");

        private readonly MethodInfo getAnimalTraits;

        private AnimalTraitsTraitColumnHandler(MethodInfo getAnimalTraits)
        {
            this.getAnimalTraits = getAnimalTraits;
        }

        public static void Register()
        {
            try
            {
                var surface = new ReflectionSurface("Animal Traits System traits column");
                Type workerType = surface.Type("AnimalTraitExtension.PawnColumnWorker_AnimalTraits");
                MethodInfo animalTraits = surface.Method(workerType, "GetAnimalTraits", new[] { typeof(Pawn) });
                if (!surface.Ready)
                {
                    return;
                }
                PawnColumnHandlerRegistry.Register(workerType, new AnimalTraitsTraitColumnHandler(animalTraits));
            }
            catch (Exception ex)
            {
                ModLogger.Error("Animal Traits System traits column handler registration failed: " + ex.Message);
            }
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            string text = (string)getTextFor.Invoke(def.Worker, new object[] { pawn });
            return text != null ? text.StripTags() : "";
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            var traits = getAnimalTraits.Invoke(def.Worker, new object[] { pawn }) as List<Hediff>;
            if (traits == null || traits.Count == 0)
            {
                return null;
            }
            var entries = new List<string>();
            foreach (Hediff hediff in traits)
            {
                HediffDef trait = hediff.def;
                string label = !trait.label.NullOrEmpty() ? trait.LabelCap.ToString() : trait.defName;
                string polarity = (trait.isBad
                    ? "RimWorldAccess.Compat.AnimalTraits.Negative"
                    : "RimWorldAccess.Compat.AnimalTraits.Positive").Loc().ToString();
                string description = trait.description.NullOrEmpty() ? "" : trait.description.StripTags().Trim();
                entries.Add("RimWorldAccess.Compat.AnimalTraits.TraitTipEntry".Loc(label, polarity, description).ToString().Trim());
            }
            return entries.Count > 0 ? string.Join(" ", entries) : null;
        }

        /// <summary>Text-column behavior: a widget can hide behind the text, so extras stay visible.</summary>
        public override bool SuppressCapturedExtras(PawnColumnDef def)
        {
            return false;
        }
    }
}
