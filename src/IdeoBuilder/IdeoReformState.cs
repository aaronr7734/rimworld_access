using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Keyboard-accessible state for the in-game two-stage reform dialog (Dialog_ReformIdeo).
    ///
    /// Stage 1 (Memes &amp; styles): the "choose one change" stage. A short menu offers changing
    /// the structure meme, the normal memes, or the styles — but only one category may change
    /// per reform, so once a change is made the other categories are announced as locked.
    /// Reset clears the pending change; Alt+S advances to stage 2.
    ///
    /// Stage 2 (Precepts, narrative &amp; deities): free editing, presented as the same section
    /// menu as the Custom-creation hub (minus memes/styles, which belong to stage 1), operating
    /// on the reform's working copy. Alt+S confirms and applies; Escape returns to stage 1;
    /// Alt+R randomizes.
    /// </summary>
    public static class IdeoReformState
    {
        public static bool IsActive { get; private set; }

        /// <summary>
        /// Marker stored on the viewer's "Reform" tree node (see IdeologyHelper.BuildFluidSection).
        /// Activating that node closes the viewer and opens Dialog_ReformIdeo for this ideoligion.
        /// </summary>
        public sealed class ReformActionMarker
        {
            public Ideo Ideo;
        }

        private static Dialog_ReformIdeo dialog;
        private static Ideo newIdeo;
        private static Ideo originalIdeo;

        private static int selectedIndex;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        // Stage-2 sections (excludes memes/styles, which are stage-1 concerns).
        private static List<IdeoBuilderHelper.HubSection> sections = new List<IdeoBuilderHelper.HubSection>();

        #region Reflection

        private static readonly System.Reflection.FieldInfo NewIdeoField = AccessTools.Field(typeof(Dialog_ReformIdeo), "newIdeo");
        private static readonly System.Reflection.FieldInfo IdeoField = AccessTools.Field(typeof(Dialog_ReformIdeo), "ideo");
        private static readonly System.Reflection.FieldInfo StageField = AccessTools.Field(typeof(Dialog_ReformIdeo), "stage");
        private static readonly System.Reflection.MethodInfo RandomizeNewIdeoMethod = AccessTools.Method(typeof(Dialog_ReformIdeo), "RandomizeNewIdeo");
        private static readonly System.Reflection.MethodInfo ResetChangesMethod = AccessTools.Method(typeof(Dialog_ReformIdeo), "ResetAllChooseOneChanges");

        private static IdeoReformStage Stage
        {
            get => (IdeoReformStage)StageField.GetValue(dialog);
            set => StageField.SetValue(dialog, value);
        }

        private static bool StructureMemeChanged => dialog.StructureMemeChanged;
        private static bool NormalMemesChanged => dialog.NormalMemesChanged;
        private static bool StylesChanged => dialog.StylesChanged;
        private static bool AnyChooseOneChanges => dialog.AnyChooseOneChanges;

        #endregion

        #region Lifecycle

        public static void EnsureOpen(Dialog_ReformIdeo d)
        {
            if (IsActive && System.Object.ReferenceEquals(dialog, d))
                return;
            dialog = d;
            newIdeo = (Ideo)NewIdeoField.GetValue(d);
            originalIdeo = (Ideo)IdeoField.GetValue(d);
            IsActive = true;
            selectedIndex = 0;
            typeahead.ClearSearch();
            RebuildForStage();
            AnnounceStage();
        }

        public static void Close()
        {
            IsActive = false;
            dialog = null;
            newIdeo = null;
            originalIdeo = null;
            sections.Clear();
            typeahead.ClearSearch();
        }

        public static void RefreshSections()
        {
            if (!IsActive) return;
            RebuildForStage();
            AnnounceCurrent();
            // Proactively flag a freshly-created precept conflict so the player hears about it the
            // moment an edit causes it, not only when they try to confirm.
            AnnounceIncompatibilityIfAny();
        }

        private static void RebuildForStage()
        {
            selectedIndex = 0;
            if (Stage == IdeoReformStage.PreceptsNarrativeAndDeities)
            {
                // Stage 2 sections: everything except the meme / style facets (stage-1 only).
                sections = IdeoBuilderHelper.BuildSections(newIdeo)
                    .Where(s => s.Kind != IdeoBuilderHelper.SectionKind.StructureMeme
                             && s.Kind != IdeoBuilderHelper.SectionKind.NormalMemes
                             && s.Kind != IdeoBuilderHelper.SectionKind.Styles)
                    .ToList();
            }
            else
            {
                sections.Clear();
            }
        }

        #endregion

        #region Stage 1 actions

        private class Stage1Action
        {
            public string Label;
            public bool Enabled;
            public string DisabledReason;
            public System.Action Activate;
        }

        private static List<Stage1Action> BuildStage1Actions()
        {
            var list = new List<Stage1Action>();
            string oneChangeReason = "MessageFluidIdeoOneChangeAllowed".Translate();

            list.Add(new Stage1Action
            {
                Label = "ReformIdeoChangeStructure".Translate(),
                Enabled = !AnyChooseOneChanges || StructureMemeChanged,
                DisabledReason = oneChangeReason,
                Activate = () => Find.WindowStack.Add(new Dialog_ChooseMemes(newIdeo, MemeCategory.Structure, initialSelection: false, null, null, reformingIdeo: true)),
            });

            string normalLabel = originalIdeo.memes.Count(m => m.category == MemeCategory.Normal) <= 1
                ? "ReformIdeoAddMeme".Translate()
                : "ReformIdeoAddOrRemoveMeme".Translate();
            list.Add(new Stage1Action
            {
                Label = normalLabel,
                Enabled = !AnyChooseOneChanges || NormalMemesChanged,
                DisabledReason = oneChangeReason,
                Activate = () =>
                {
                    var preSelected = newIdeo.memes.Where(m => !originalIdeo.memes.Contains(m)).ToList();
                    originalIdeo.CopyTo(newIdeo);
                    Find.WindowStack.Add(new Dialog_ChooseMemes(newIdeo, MemeCategory.Normal, initialSelection: false, null, preSelected, reformingIdeo: true));
                },
            });

            list.Add(new Stage1Action
            {
                Label = "ReformIdeoChangeStyles".Translate(),
                Enabled = !AnyChooseOneChanges || StylesChanged,
                DisabledReason = oneChangeReason,
                Activate = () => IdeoSymbolEditState.OpenStylePicker(newIdeo),
            });

            if (AnyChooseOneChanges)
            {
                list.Add(new Stage1Action
                {
                    Label = "ReformIdeoResetChanges".Translate(),
                    Enabled = true,
                    Activate = () =>
                    {
                        ResetChangesMethod.Invoke(dialog, null);
                        SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                        RefreshSections();
                    },
                });
            }

            return list;
        }

        private static List<Stage1Action> stage1Cache = new List<Stage1Action>();

        #endregion

        #region Input

        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool alt = KeyboardHelper.IsAltHeld;
            bool ctrl = ev.control;

            // Alt+S — advance / confirm
            if (key == KeyCode.S && alt && !ctrl)
            {
                if (Stage == IdeoReformStage.MemesAndStyles)
                    GoToStage2();
                else
                    Confirm();
                return true;
            }

            // Alt+R — randomize (stage 2 only, matches vanilla)
            if (key == KeyCode.R && alt && !ctrl && Stage == IdeoReformStage.PreceptsNarrativeAndDeities)
            {
                RandomizeNewIdeoMethod.Invoke(dialog, null);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                RebuildForStage();
                AnnounceCurrent();
                AnnounceImpactAndWarnings();
                return true;
            }

            // Escape
            if (key == KeyCode.Escape && !alt && !ctrl)
            {
                if (typeahead.HasActiveSearch) { typeahead.ClearSearchAndAnnounce(); AnnounceCurrent(); return true; }
                if (Stage == IdeoReformStage.PreceptsNarrativeAndDeities)
                {
                    Stage = IdeoReformStage.MemesAndStyles;
                    RebuildForStage();
                    AnnounceStage();
                }
                else
                {
                    dialog.Close(doCloseSound: false);
                }
                return true;
            }

            int count = ItemCount();
            if (count == 0) return true;

            if (key == KeyCode.UpArrow) { Move(-1); return true; }
            if (key == KeyCode.DownArrow) { Move(1); return true; }
            if (key == KeyCode.Home) { selectedIndex = 0; typeahead.ClearSearch(); AnnounceCurrent(); return true; }
            if (key == KeyCode.End) { selectedIndex = count - 1; typeahead.ClearSearch(); AnnounceCurrent(); return true; }

            if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space)
            {
                Activate();
                return true;
            }

            if (key == KeyCode.Backspace)
            {
                if (typeahead.HasActiveSearch && typeahead.ProcessBackspace(ItemLabels(), out int ni))
                {
                    if (ni >= 0) selectedIndex = ni;
                    AnnounceCurrent();
                }
                return true;
            }

            // Typeahead
            char c = ev.character;
            if (!alt && !ctrl && c != '\0' && char.IsLetterOrDigit(c))
            {
                if (typeahead.ProcessCharacterInput(c, ItemLabels(), out int ni))
                {
                    selectedIndex = ni;
                    AnnounceCurrent();
                }
                else
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak($"No matches for '{typeahead.LastFailedSearch}'.");
                }
                return true;
            }

            return true;
        }

        private static void Move(int delta)
        {
            int count = ItemCount();
            selectedIndex = delta > 0
                ? MenuHelper.SelectNext(selectedIndex, count)
                : MenuHelper.SelectPrevious(selectedIndex, count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrent();
        }

        private static void Activate()
        {
            if (Stage == IdeoReformStage.MemesAndStyles)
            {
                stage1Cache = BuildStage1Actions();
                if (selectedIndex < 0 || selectedIndex >= stage1Cache.Count) return;
                var action = stage1Cache[selectedIndex];
                if (!action.Enabled)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak(action.DisabledReason ?? "Unavailable", SpeechPriority.High);
                    return;
                }
                action.Activate?.Invoke();
            }
            else
            {
                if (selectedIndex < 0 || selectedIndex >= sections.Count) return;
                IdeoBuilderSectionActions.Activate(newIdeo, sections[selectedIndex].Kind);
            }
        }

        private static void GoToStage2()
        {
            Stage = IdeoReformStage.PreceptsNarrativeAndDeities;
            RebuildForStage();
            AnnounceStage();
        }

        private static void Confirm()
        {
            var pair = newIdeo.FirstIncompatiblePreceptPair();
            if (pair != default(Pair<Precept, Precept>))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("MessageIdeoIncompatiblePrecepts".Translate(
                    pair.First.Label.Named("PRECEPT1"), pair.Second.Label.Named("PRECEPT2")).CapitalizeFirst(),
                    SpeechPriority.High);
                return;
            }

            var ideoLocal = originalIdeo;
            var newLocal = newIdeo;
            var dlg = dialog;
            IdeoDevelopmentUtility.ConfirmChangesToIdeo(ideoLocal, newLocal, delegate
            {
                IdeoDevelopmentUtility.ApplyChangesToIdeo(ideoLocal, newLocal);
                dlg.Close(doCloseSound: false);
            });
        }

        #endregion

        #region Items helpers

        private static int ItemCount()
        {
            if (Stage == IdeoReformStage.MemesAndStyles)
                return BuildStage1Actions().Count;
            return sections.Count;
        }

        private static List<string> ItemLabels()
        {
            if (Stage == IdeoReformStage.MemesAndStyles)
                return BuildStage1Actions().Select(a => a.Label).ToList();
            return sections.Select(s => s.Label).ToList();
        }

        #endregion

        #region Announcements

        private static void AnnounceStage()
        {
            var sb = new StringBuilder();
            sb.Append("ReformIdeoligion".Translate());
            sb.Append(". ").Append("ReformIdeoligionDesc".Translate());
            if (Stage == IdeoReformStage.MemesAndStyles)
            {
                sb.Append(". ").Append("ReformIdeoChooseOneChange".Translate());
            }
            else
            {
                sb.Append(". ").Append("ReformIdeoChangeAny".Translate());
                // On arriving at free-edit, state the current overall impact so the player hears
                // where their meme changes left it (parity with the builder hub).
                string impact = BuildImpactLine();
                if (!string.IsNullOrEmpty(impact))
                    sb.Append(". ").Append(impact);
            }
            sb.Append(". ").Append(BuildCurrentText());
            TolkHelper.Speak(sb.ToString(), SpeechPriority.High);
        }

        /// <summary>"Impact: N, label" for the working copy's normal memes, or "" if it has none.</summary>
        private static string BuildImpactLine()
        {
            if (newIdeo == null) return "";
            var normals = newIdeo.memes.Where(m => m.category == MemeCategory.Normal).ToList();
            if (normals.Count == 0) return "";
            int impact = IdeoBuilderHelper.ImpactOf(normals);
            return $"{"IdeoImpact".Translate()}: {impact}, {IdeoImpactUtility.OverallImpactLabel(impact)}";
        }

        /// <summary>Announces the working copy's overall impact plus any non-blocking precept warning.</summary>
        private static void AnnounceImpactAndWarnings()
        {
            if (newIdeo == null) return;
            var sb = new StringBuilder();
            string impact = BuildImpactLine();
            if (!string.IsNullOrEmpty(impact))
                sb.Append(impact);

            string warning = IdeoBuilderHelper.BuildPlayerWarning(newIdeo);
            if (!string.IsNullOrEmpty(warning))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(warning);
            }
            if (sb.Length > 0)
                TolkHelper.Speak(sb.ToString());

            AnnounceIncompatibilityIfAny();
        }

        /// <summary>Speaks a high-priority alert if the working copy now has an incompatible precept pair.</summary>
        private static void AnnounceIncompatibilityIfAny()
        {
            if (newIdeo == null) return;
            var pair = newIdeo.FirstIncompatiblePreceptPair();
            if (pair != default(Pair<Precept, Precept>))
            {
                TolkHelper.Speak("MessageIdeoIncompatiblePrecepts".Translate(
                    pair.First.Label.Named("PRECEPT1"), pair.Second.Label.Named("PRECEPT2")).CapitalizeFirst(),
                    SpeechPriority.High);
            }
        }

        private static void AnnounceCurrent()
        {
            string text = BuildCurrentText();
            if (!string.IsNullOrEmpty(text))
                TolkHelper.Speak(text);
        }

        private static string BuildCurrentText()
        {
            int count = ItemCount();
            if (count == 0) return "";
            if (selectedIndex < 0 || selectedIndex >= count) selectedIndex = 0;

            var sb = new StringBuilder();
            if (Stage == IdeoReformStage.MemesAndStyles)
            {
                var actions = BuildStage1Actions();
                var a = actions[selectedIndex];
                sb.Append(a.Label);
                if (!a.Enabled)
                    sb.Append(". ").Append("Disabled".Translate().ToString().CapitalizeFirst());
            }
            else
            {
                var s = sections[selectedIndex];
                sb.Append(s.Label);
                if (!string.IsNullOrEmpty(s.ValueSummary))
                    sb.Append(": ").Append(s.ValueSummary);
            }

            string position = MenuHelper.FormatPosition(selectedIndex, count);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);
            return sb.ToString();
        }

        #endregion
    }
}
