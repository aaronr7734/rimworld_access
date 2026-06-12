using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public interface ILordJobDialogAdapter
    {
        Dialog_BeginLordJob Dialog { get; }

        string LocalizedDialogName { get; }
        string ClosingAnnouncement { get; }

        string DescriptionText { get; }
        string ExtraExplanationText { get; }
        string ExpectedQualitySentence { get; }
        string ExpectedDurationText { get; }
        string OutcomeDescriptionText { get; }

        IReadOnlyList<string> ComputeWarnings();

        IReadOnlyList<LordJobRoleView> BuildRoleList();
        IReadOnlyList<LordJobPawnView> BuildPawnList(LordJobRoleView role);

        AssignmentResult ToggleAssignment(LordJobRoleView role, LordJobPawnView pawn, out string failureReason);

        IReadOnlyList<QualityFactor> GetQualityFactors(out FloatRange qualityRange);
        IReadOnlyList<LordJobQualityRow> BuildExtraQualityRows();
        IReadOnlyList<LordJobOutcomeRow> BuildOutcomeChances(FloatRange qualityRange);

        IReadOnlyList<LordJobExtraToggle> BuildExtraToggles();
        bool ApplyExtraToggle(LordJobExtraToggle toggle);

        TargetInfo Target { get; }

        void Notify_AssignmentsChanged();
        bool TryStart(out IReadOnlyList<string> blockingReasons);
    }

    public sealed class LordJobRoleView
    {
        public enum Kind { Role, Spectators, ExtraToggle }

        public Kind Type;
        public string Label;
        public int AssignedCount;
        public int MaxCount;
        public int MinCount;
        public bool IsRequired;
        public bool IsLocked;
        public string ExtraInfoLine;
        public string Tooltip;
        public object AdapterTag;
    }

    public sealed class LordJobPawnView
    {
        public Pawn Pawn;
        public bool IsAssigned;
        public bool IsForced;
        public string SuitabilityLine;
        public string DisabledReason;
        public string Tooltip;
        public object AdapterTag;
    }

    public sealed class LordJobExtraToggle
    {
        public string Label;
        public bool Checked;
        public string Tooltip;
        public object AdapterTag;
    }

    public sealed class LordJobQualityRow
    {
        public string Label;
        public string Change;
        public float Quality;
        public bool IsPresent;
        public bool IsPositive = true;
        public bool IsUncertain;
        public bool IsInformational;
        public string Tooltip;
        public string Explanation;
    }

    public sealed class LordJobOutcomeRow
    {
        public string Label;
        public float Percentage;
        public string Tooltip;
    }

    public enum AssignmentResult
    {
        Assigned,
        Unassigned,
        BlockedForced,
        BlockedDisabled,
        Failed,
    }

    public abstract class LordJobDialogAdapterBase : ILordJobDialogAdapter
    {
        protected static readonly PropertyInfo DescriptionLabelProp =
            AccessTools.Property(typeof(Dialog_BeginLordJob), "DescriptionLabel");
        protected static readonly PropertyInfo ExtraExplanationLabelProp =
            AccessTools.Property(typeof(Dialog_BeginLordJob), "ExtraExplanationLabel");
        protected static readonly PropertyInfo HeaderLabelProp =
            AccessTools.Property(typeof(Dialog_BeginLordJob), "HeaderLabel");
        protected static readonly MethodInfo BlockingIssuesMethod =
            AccessTools.Method(typeof(Dialog_BeginLordJob), "BlockingIssues");
        protected static readonly MethodInfo PopulateQualityFactorsMethod =
            AccessTools.Method(typeof(Dialog_BeginLordJob), "PopulateQualityFactors");
        protected static readonly MethodInfo PopulateOutcomePossibilitiesMethod =
            AccessTools.Method(typeof(Dialog_BeginLordJob), "PopulateOutcomePossibilities");
        protected static readonly MethodInfo ExpectedDurationLabelMethod =
            AccessTools.Method(typeof(Dialog_BeginLordJob), "ExpectedDurationLabel");

        protected readonly Dialog_BeginLordJob dialog;

        protected LordJobDialogAdapterBase(Dialog_BeginLordJob dialog)
        {
            this.dialog = dialog;
        }

        public Dialog_BeginLordJob Dialog => dialog;

        public abstract string LocalizedDialogName { get; }
        public abstract string ClosingAnnouncement { get; }
        public abstract TargetInfo Target { get; }

        public virtual string DescriptionText
        {
            get
            {
                try
                {
                    return ResolveTaggedString(DescriptionLabelProp?.GetValue(dialog));
                }
                catch { return null; }
            }
        }

        public virtual string ExtraExplanationText
        {
            get
            {
                try
                {
                    string raw = ResolveTaggedString(ExtraExplanationLabelProp?.GetValue(dialog));
                    return string.IsNullOrEmpty(raw) ? null : SanitizeText(raw);
                }
                catch { return null; }
            }
        }

        public virtual string ExpectedQualitySentence
        {
            get
            {
                try
                {
                    var factors = GetQualityFactors(out var range);
                    if (factors == null) return null;
                    string label = ResolveExpectedQualityLabel();
                    return $"{label}: {FormatQualityRange(range)}.";
                }
                catch { return null; }
            }
        }

        public virtual string ExpectedDurationText
        {
            get
            {
                try
                {
                    if (ExpectedDurationLabelMethod == null) return null;
                    GetQualityFactors(out var range);
                    string raw = ResolveTaggedString(ExpectedDurationLabelMethod.Invoke(dialog, new object[] { range }));
                    return string.IsNullOrEmpty(raw) ? null : SanitizeText(raw);
                }
                catch { return null; }
            }
        }

        public virtual string OutcomeDescriptionText => null;

        public virtual IReadOnlyList<string> ComputeWarnings()
        {
            var list = new List<string>();
            try
            {
                var enumerable = BlockingIssuesMethod?.Invoke(dialog, null) as System.Collections.IEnumerable;
                if (enumerable != null)
                {
                    foreach (var item in enumerable)
                    {
                        string s = item?.ToString();
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                }
            }
            catch { /* fall through */ }
            AppendDialogSpecificWarnings(list);
            return list;
        }

        protected virtual void AppendDialogSpecificWarnings(List<string> warnings) { }

        public abstract IReadOnlyList<LordJobRoleView> BuildRoleList();
        public abstract IReadOnlyList<LordJobPawnView> BuildPawnList(LordJobRoleView role);
        public abstract AssignmentResult ToggleAssignment(LordJobRoleView role, LordJobPawnView pawn, out string failureReason);

        public virtual IReadOnlyList<QualityFactor> GetQualityFactors(out FloatRange qualityRange)
        {
            qualityRange = new FloatRange(0f, 0f);
            try
            {
                if (PopulateQualityFactorsMethod == null) return null;
                object[] args = new object[] { null };
                var factors = PopulateQualityFactorsMethod.Invoke(dialog, args) as List<QualityFactor>;
                qualityRange = (FloatRange)args[0];
                return factors;
            }
            catch { return null; }
        }

        public virtual IReadOnlyList<LordJobQualityRow> BuildExtraQualityRows()
        {
            return System.Array.Empty<LordJobQualityRow>();
        }

        public virtual IReadOnlyList<LordJobOutcomeRow> BuildOutcomeChances(FloatRange qualityRange)
        {
            var rows = new List<LordJobOutcomeRow>();
            try
            {
                if (PopulateOutcomePossibilitiesMethod == null) return rows;
                var outcomes = PopulateOutcomePossibilitiesMethod.Invoke(dialog, null) as List<ILordJobOutcomePossibility>;
                if (outcomes == null || outcomes.Count == 0) return rows;

                float weightSum = 0f;
                foreach (var o in outcomes) weightSum += o.Weight(qualityRange);
                if (weightSum <= 0f) return rows;

                foreach (var o in outcomes)
                {
                    float pct = o.Weight(qualityRange) / weightSum;
                    rows.Add(new LordJobOutcomeRow
                    {
                        Label = o.Label.Resolve(),
                        Percentage = pct,
                        Tooltip = ResolveTaggedString(o.ToolTip),
                    });
                }
            }
            catch { /* swallow */ }
            return rows;
        }

        public virtual IReadOnlyList<LordJobExtraToggle> BuildExtraToggles()
        {
            return System.Array.Empty<LordJobExtraToggle>();
        }

        public virtual bool ApplyExtraToggle(LordJobExtraToggle toggle) => false;

        public virtual void Notify_AssignmentsChanged() { }

        public virtual bool TryStart(out IReadOnlyList<string> blockingReasons)
        {
            var reasons = new List<string>();
            try
            {
                var enumerable = BlockingIssuesMethod?.Invoke(dialog, null) as System.Collections.IEnumerable;
                if (enumerable != null)
                {
                    foreach (var item in enumerable)
                    {
                        string s = item?.ToString();
                        if (!string.IsNullOrEmpty(s)) reasons.Add(s);
                    }
                }
            }
            catch { /* fall through */ }
            blockingReasons = reasons;
            return reasons.Count == 0;
        }

        protected virtual string ResolveExpectedQualityLabel()
        {
            try
            {
                var prop = AccessTools.Property(typeof(Dialog_BeginLordJob), "ExpectedQualityLabel");
                return ResolveTaggedString(prop?.GetValue(dialog))
                    ?? (string)"RimWorldAccess.Rituals.Quality.ExpectedQualityLabel".Translate();
            }
            catch { return "RimWorldAccess.Rituals.Quality.ExpectedQualityLabel".Translate(); }
        }

        protected static string ResolveTaggedString(object value)
        {
            if (value == null) return null;
            if (value is TaggedString ts) return ts.Resolve();
            return value.ToString();
        }

        protected static string FormatQualityRange(FloatRange range)
        {
            if (System.Math.Abs(range.min - range.max) < 0.01f)
            {
                return range.min.ToStringPercent("F0");
            }
            return (string)"RimWorldAccess.Rituals.Quality.RangeFormat".Translate(range.min.ToStringPercent("F0"), range.max.ToStringPercent("F0"));
        }

        protected static string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Game text uses newlines as semantic separators (Duration / Cooldown / Required offering
            // each on its own line). Replace each line break with ". " unless the previous character
            // already terminates the clause, so screen readers get a real pause between items.
            var sb = new System.Text.StringBuilder();
            string normalized = text.Replace("\r\n", "\n");
            foreach (var raw in normalized.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (sb.Length > 0)
                {
                    char last = sb[sb.Length - 1];
                    if (last != '.' && last != '!' && last != '?' && last != ':' && last != ';' && last != ',')
                        sb.Append('.');
                    sb.Append(' ');
                }
                sb.Append(line);
            }
            return sb.ToString();
        }
    }

    public static class LordJobAdapterFactory
    {
        public static bool TryCreate(Dialog_BeginLordJob dialog, out ILordJobDialogAdapter adapter)
        {
            adapter = null;
            if (dialog == null) return false;

            try
            {
                if (dialog is Dialog_BeginGravshipLaunch gravship)
                {
                    adapter = new GravshipLaunchAdapter(gravship);
                    return true;
                }
                if (dialog is Dialog_BeginPsychicRitual psychic)
                {
                    adapter = new PsychicRitualAdapter(psychic);
                    return true;
                }
                if (dialog is Dialog_BeginRitual ideology)
                {
                    adapter = new IdeologyRitualAdapter(ideology);
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[LordJobAdapterFactory] Failed to construct adapter for {dialog.GetType().Name}: {ex.Message}");
                adapter = null;
                return false;
            }
            return false;
        }
    }
}
