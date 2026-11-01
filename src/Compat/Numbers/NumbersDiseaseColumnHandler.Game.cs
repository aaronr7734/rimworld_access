using System;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Numbers' disease race column: the immunity-minus-severity percent it draws for the most
    /// dangerous immunizable disease, plus the direction its corner arrow points, computed the
    /// way <c>PawnColumnWorker_DiseaseProgression.DoCell</c> computes it.
    /// </summary>
    internal sealed class NumbersDiseaseColumnHandler : PawnColumnHandler
    {
        private readonly MethodInfo findMostSevere;
        private readonly MethodInfo getTextFor;
        private readonly MethodInfo getTip;

        private NumbersDiseaseColumnHandler(MethodInfo findMostSevere, MethodInfo getTextFor, MethodInfo getTip)
        {
            this.findMostSevere = findMostSevere;
            this.getTextFor = getTextFor;
            this.getTip = getTip;
        }

        public static void Register()
        {
            var surface = new ReflectionSurface("NumbersDiseaseColumnHandler");
            Type workerType = surface.Type("Numbers.PawnColumnWorker_DiseaseProgression");
            MethodInfo findMostSevere = surface.Method(workerType, "FindMostSevereHediff", new[] { typeof(Pawn) });
            MethodInfo getTextFor = surface.Method(workerType, "GetTextFor", new[] { typeof(HediffWithComps) });
            MethodInfo getTip = surface.Method(workerType, "GetTip", new[] { typeof(Pawn), typeof(HediffWithComps) });
            if (surface.Ready)
            {
                PawnColumnHandlerRegistry.Register(workerType, new NumbersDiseaseColumnHandler(findMostSevere, getTextFor, getTip));
            }
        }

        private HediffWithComps MostSevere(PawnColumnDef def, Pawn pawn)
        {
            if (pawn.Dead || pawn.health == null || pawn.health.hediffSet == null || !pawn.health.hediffSet.HasImmunizableNotImmuneHediff())
            {
                return null;
            }
            return findMostSevere.Invoke(def.Worker, new object[] { pawn }) as HediffWithComps;
        }

        public override string CellText(PawnColumnDef def, Pawn pawn)
        {
            HediffWithComps hediff = MostSevere(def, pawn);
            if (hediff == null)
            {
                return "";
            }
            string margin = ((float)getTextFor.Invoke(def.Worker, new object[] { hediff })).ToStringPercent();
            return (ImmunityWinning(pawn, hediff)
                ? "RimWorldAccess.Compat.Numbers.DiseaseImproving"
                : "RimWorldAccess.Compat.Numbers.DiseaseWorsening").Loc(margin).ToString();
        }

        public override string CellTip(PawnColumnDef def, Pawn pawn)
        {
            HediffWithComps hediff = MostSevere(def, pawn);
            if (hediff == null)
            {
                return null;
            }
            string tip = getTip.Invoke(def.Worker, new object[] { pawn, hediff }) as string;
            return string.IsNullOrEmpty(tip) ? null : SpeechFlatten.ToSentences(tip.StripTags());
        }

        /// <summary>The worker's arrow test: the green up arrow unless severity gain outpaces immunity gain.</summary>
        private static bool ImmunityWinning(Pawn pawn, HediffWithComps hediff)
        {
            HediffComp_Immunizable immunizable = hediff.TryGetComp<HediffComp_Immunizable>();
            float fromImmunizable = immunizable != null ? immunizable.SeverityChangePerDay() : 0f;
            float fromTending = 0f;
            HediffComp_TendDuration tendDuration = hediff.TryGetComp<HediffComp_TendDuration>();
            if (tendDuration != null && tendDuration.IsTended)
            {
                fromTending = tendDuration.TProps.severityPerDayTended * tendDuration.tendQuality;
            }
            float immunityPerDay = 0f;
            ImmunityRecord record = pawn.health.immunity.GetImmunityRecord(hediff.def);
            if (record != null)
            {
                immunityPerDay = record.ImmunityChangePerTick(pawn, true, hediff) * GenDate.TicksPerDay;
            }
            return !(fromTending + fromImmunizable > immunityPerDay);
        }
    }
}
