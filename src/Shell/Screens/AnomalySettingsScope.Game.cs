using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Anomaly Settings dialog as ONE content region over
    /// <see cref="AnomalySettingsDialogState"/>'s settings rows, in the same DifficultySetting shape
    /// — same <see cref="DifficultySettingAdapter"/> announcements, same Left/Right adjust, same
    /// combo-box picker on Enter — that <see cref="StorytellerScopeBase"/>'s CustomDifficulty region
    /// runs on. Window-attached, floating above <see cref="StorytellerScreenScope"/> through
    /// ordinary stack order. The state's PostOpen/PostClose lifecycle, including the wasAcceptClose
    /// accept-vs-discard announcement and the ReturnToStorytellerMode handoff, lives in
    /// AnomalySettingsDialogPatch.
    ///
    /// MODAL: unclaimed keys stop at this scope and die at the dispatcher's native modal swallow via
    /// FocusStack.AnyLiveModal; the state is not a ShellGuards.MenuOwnsInput member, the transition
    /// term alone carrying it.
    ///
    /// <see cref="CaptureWindowButtons"/> is false even though the dialog draws two
    /// <c>Widgets.ButtonText</c> calls: both must run <see cref="AnomalySettingsDialogState"/>'s own
    /// vehicles rather than vanilla's inline handlers, because Accept there also sets the
    /// <c>wasAcceptClose</c> flag the PostClose patch reads to say "saved" instead of "closed" and
    /// to hand focus back to the storyteller row. Declaring them keeps their real labels and their
    /// memorized chords on the rows.
    ///
    /// The dialog never overrides OnCancelKeyPressed/OnAcceptKeyPressed and its Open clears
    /// closeOnAccept/closeOnCancel, so window-level blocking is this scope's ownership instead:
    /// <see cref="OwnsCancel"/> is overridden back to unconditional true, since the chassis owns
    /// cancel only during a typeahead search, and OwnsAccept keeps the chassis default. That makes
    /// the Window router twins block the dialog's vanilla handlers whenever this scope is top, even
    /// in the pre-PostOpen instants where the flags are still vanilla's defaults. Page-level
    /// blocking is the stamps in this scope's Cancel/Activate handlers: the deferred re-test that
    /// walks down to the page hits the Page router twins' stamp check, and DoBottomButtons' polls
    /// hit the same stamp guard.
    ///
    /// Focus ring: the playstyle row rings the current playstyle's own radio button, and each slider
    /// row rings the caption label vanilla draws above the slider. The mapping lives on
    /// <see cref="AnomalySettingsDialogState.CurrentListingFocus"/>, beside the row data it reads.
    /// </summary>
    public sealed class AnomalySettingsScope : ScreenScope, IListingRingClient
    {
        private readonly Dialog_AnomalySettings dialog;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public AnomalySettingsScope(Dialog_AnomalySettings dialog)
        {
            this.dialog = dialog;

            Claim("anomalySettings.accept", delegate { AnomalySettingsDialogState.AcceptSettings(); });
            Claim("anomalySettings.setStandardPlaystyle", delegate { OpenStandardPlaystylePicker(); });
            // Plain Left/Right ride the base's horizontal claims; these two are the Shift twins
            // that scale a slider's step.
            Claim("anomalySettings.decreaseValueLarge", delegate { AdjustCurrent(-1, large: true); });
            Claim("anomalySettings.increaseValueLarge", delegate { AdjustCurrent(1, large: true); });
            // Escape discards and closes: the state's Open clears closeOnCancel, so vanilla would
            // do nothing. The base's typeahead Escape claim is registered ahead of this one.
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "anomaly-settings"; }
        }

        /// <summary>Unconditional, because the chassis's search-only default would leave cancel uncovered.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Both text buttons are declared instead, so Accept keeps its wasAcceptClose handoff.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Vanilla's own label for the row that opens this dialog.</summary>
        protected override string ContentRegionName(int region)
        {
            return "AnomalySettings".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return AnomalySettingsDialogState.RowCount;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return AnomalySettingsDialogState.DescribeRow(index);
        }

        /// <summary>
        /// Enter TOGGLES the focused row and never closes the dialog; accept is Alt+S. The playstyle
        /// combo box opens its picker and re-reads the row once a pick lands. The base's Activate
        /// claim has already stamped the accept frame, so the same Enter cannot reach the storyteller
        /// page's deferred Accept re-test beneath the dialog.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            AnomalySettingsDialogState.ToggleRow(index, () => AnnounceRow(index));
        }

        /// <summary>Every row answers Left/Right; a combo box has no adjust of its own, but re-reading it keeps the keypress from being silent.</summary>
        protected override bool CanAdjustContentItem(int region, int index)
        {
            return AnomalySettingsDialogState.RowAt(index) != null;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            AdjustRow(index, direction, large: false);
        }

        protected override bool TryGetSliderBox(int region, int index, out float value, out bool high)
        {
            value = 0f;
            high = false;
            if (!(AnomalySettingsDialogState.RowAt(index) is DifficultySliderSetting slider))
            {
                return false;
            }
            value = slider.WidgetValue;
            return true;
        }

        /// <summary>Alt+S and Alt+R as real rows, each teaching its chord; both run the state's vehicles.</summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Accept".Translate().ToString(),
                    AnomalySettingsDialogState.AcceptSettings, "anomalySettings.accept"));
                actions.Add(new ScreenAction("SetToStandardPlaystyle".Translate().ToString() + "...",
                    OpenStandardPlaystylePicker, "anomalySettings.setStandardPlaystyle"));
                return actions;
            }
        }

        /// <summary>Shift+Enter accepts from anywhere on the screen.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "anomalySettings.accept"; }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "AnomalySettings".Translate().ToString();
        }

        public override void OnPush()
        {
            base.OnPush();
            ScreenScopeDrawPatch.RegisterListingClient(this);
        }

        public override void OnPop()
        {
            ScreenScopeDrawPatch.UnregisterListingClient(this);
            base.OnPop();
        }

        Window IListingRingClient.ListingRingWindow
        {
            get { return dialog; }
        }

        ListingRingFocus IListingRingClient.CurrentListingFocus()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || Model.RegionIndex != 0)
            {
                return ListingRingFocus.None;
            }
            return AnomalySettingsDialogState.CurrentListingFocus(region.Index);
        }

        private void OpenStandardPlaystylePicker()
        {
            AnomalySettingsDialogState.OpenStandardPlaystylePicker(AnnounceCurrentItem);
        }

        /// <summary>The Shift twins' entry point: resolve the cursor as the base's adjust path does, then share <see cref="AdjustRow"/>.</summary>
        private void AdjustCurrent(int direction, bool large)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || Model.RegionIndex != 0)
            {
                return;
            }
            AdjustRow(region.Index, direction, large);
        }

        private void AdjustRow(int index, int direction, bool large)
        {
            if (AnomalySettingsDialogState.RowAt(index) is AnomalyPlaystyleSetting)
            {
                AnnounceCurrentItem();
                return;
            }
            AnomalySettingsDialogState.AdjustRow(index, direction, large);
        }

        /// <summary>The state-change announcement for one row, after a mutation it performed itself.</summary>
        private void AnnounceRow(int index)
        {
            RefreshModel();
            ElementDescription d = AnomalySettingsDialogState.DescribeRow(index);
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private static void OnCancel(KeyEventSnapshot e)
        {
            // Stamp before closing: the dialog leaves the stack mid-frame, and the same Escape's
            // deferred re-test would otherwise reach the page's cancel chain and Back poll.
            ShellFrameStamps.MarkCancelConsumed();
            AnomalySettingsDialogState.HandleCancel();
        }
    }

    /// <summary>
    /// The tokens the anomaly dialog's label rows are identified by, shared between the manifests
    /// below and <see cref="AnomalySettingsDialogState"/>, which maps a focused model row onto one.
    /// Every site is SYNTHETIC: vanilla concatenates the live value and the frequency word onto the
    /// translated caption, so the literal nearest the call is a bare formatting fragment rather than
    /// the translation key. The playstyle radios carry the <c>AnomalyPlaystyleDef</c> instead.
    /// </summary>
    internal static class AnomalyRowKeys
    {
        internal const string PlaystyleHeading = "Dialog_AnomalySettings.DrawPlaystyles#0";

        internal const string ExtraSettingsHeading = "Dialog_AnomalySettings.DrawExtraSettings#0";

        internal const string ThreatsInactive = "Dialog_AnomalySettings.DrawExtraSettings#1";

        internal const string ThreatsActive = "Dialog_AnomalySettings.DrawExtraSettings#2";

        internal const string ThreatsOverride = "Dialog_AnomalySettings.DrawExtraSettings#3";

        internal const string StudyEfficiency = "Dialog_AnomalySettings.DrawExtraSettings#4";
    }

    /// <summary>
    /// The row calls the anomaly dialog makes, resolved by exact signature: the TaggedString Label
    /// overload, every caption here being a TaggedString concatenation, and the 7-parameter
    /// RadioButton core the playstyle loop binds directly.
    /// </summary>
    internal static class AnomalyRowCalls
    {
        internal static readonly MethodInfo Label =
            AccessTools.Method(typeof(Listing_Standard), "Label",
                new[] { typeof(TaggedString), typeof(float), typeof(string) });

        internal static readonly MethodInfo RadioButton =
            AccessTools.Method(typeof(Listing_Standard), "RadioButton",
                new[] { typeof(string), typeof(bool), typeof(float), typeof(float), typeof(string), typeof(float?), typeof(bool) });
    }

    /// <summary>The two rows of <c>DrawPlaystyles</c> in IL order: the section heading, then each playstyle's radio marked with its own def.</summary>
    [HarmonyPatch(typeof(Dialog_AnomalySettings), "DrawPlaystyles")]
    internal static class AnomalyPlaystyleRowMarkerPatch
    {
        private static readonly MethodInfo PlaystyleCurrent =
            AccessTools.Method(typeof(IEnumerator<AnomalyPlaystyleDef>), "get_Current");

        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { AnomalyRowCalls.Label, AnomalyRowCalls.RadioButton },
            Sites = new[]
            {
                new MarkerSite
                {
                    RowCall = AnomalyRowCalls.Label,
                    Payload = MarkerPayload.SyntheticKey,
                    ExpectedKey = AnomalyRowKeys.PlaystyleHeading,
                },
                new MarkerSite
                {
                    RowCall = AnomalyRowCalls.RadioButton,
                    Payload = MarkerPayload.LoopLocal,
                    LocalSource = PlaystyleCurrent,
                },
            },
        };

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }

    /// <summary>
    /// The five label rows of <c>DrawExtraSettings</c>, in IL order. The <c>listing.Slider</c> calls
    /// beneath each caption are plain <c>Slider</c>, not <c>SliderLabeled</c>, so they carry no outer
    /// tap and are not sites here; the ring lands on the caption row instead. Only one of the
    /// inactive/active pair, the override row, and the study row draws on any pass, and an unexecuted
    /// branch emits no marker.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_AnomalySettings), "DrawExtraSettings")]
    internal static class AnomalyExtraSettingsRowMarkerPatch
    {
        private static readonly MarkerPlan Plan = new MarkerPlan
        {
            RowCallTargets = new[] { AnomalyRowCalls.Label },
            Sites = new[]
            {
                Site(AnomalyRowKeys.ExtraSettingsHeading),
                Site(AnomalyRowKeys.ThreatsInactive),
                Site(AnomalyRowKeys.ThreatsActive),
                Site(AnomalyRowKeys.ThreatsOverride),
                Site(AnomalyRowKeys.StudyEfficiency),
            },
        };

        private static MarkerSite Site(string key)
        {
            return new MarkerSite
            {
                RowCall = AnomalyRowCalls.Label,
                Payload = MarkerPayload.SyntheticKey,
                ExpectedKey = key,
            };
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return ListingRowMarkerInjector.Inject(instructions, __originalMethod, Plan);
        }
    }
}
