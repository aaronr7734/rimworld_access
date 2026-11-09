using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One focusable control of a Colony Manager Redux job's detail pane, named and stated the way
    /// the sighted control is.
    /// State reads are delegates, never captured values: a row outlives the mutation its own Enter
    /// performs, so a captured bool would speak the state from before the press. The tooltip is a
    /// delegate too — the mod composes some of them expensively, and only the focused row should pay.
    /// </summary>
    internal sealed class CmrDetailRow
    {
        public string Label = "";

        /// <summary>The mod's own tooltip for this control, flattened; null where it ships no hover text.</summary>
        public Func<string> Tooltip;

        public ElementRole Role = ElementRole.None;

        public Func<CheckState> Check;

        /// <summary>
        /// Checked-state write for rows whose sighted checkbox is paintable: Shift+Down/Up carry this
        /// row's state onto the neighbour, mirroring the sighted drag-paint.
        /// </summary>
        public Action<bool> SetChecked;

        public Func<bool> Selected;

        public bool Disabled;

        public Func<string> Value;

        /// <summary>Whether an expandable row is open; activating it toggles and echoes the new word.</summary>
        public Func<bool> Expanded;

        public Action Activate;

        public Action<CmrShortcutClick> ShortcutClick;

        /// <summary>Left/Right, paired with <see cref="CanAdjust"/>. Null for a row with no value axis.</summary>
        public Action<int> Adjust;

        public Func<int, bool> CanAdjust;

        /// <summary>Ctrl+Up/Down, paired with <see cref="CanReorder"/>: reorders within the mod's own list.</summary>
        public Action<int> Reorder;

        /// <summary>Whether the row can still move in this direction (-1/+1); boundary rows refuse silently.</summary>
        public Func<int, bool> CanReorder;

        /// <summary>Silently mirrors the visual state the mod applies on hover or selection; never announces.</summary>
        public Action OnSettle;

        public Def InfoCardDef;

        /// <summary>Set when Enter opens numeric entry instead of activating; <see cref="Activate"/> stays null then.</summary>
        public CmrNumericSpec Numeric;

        /// <summary>Set when Enter opens text entry instead of activating (the export file-name field); <see cref="Activate"/> stays null then.</summary>
        public CmrTextSpec Text;

        /// <summary>
        /// Set when Enter opens a picker instead of activating; <see cref="Activate"/> stays null.
        /// The scope presents the choices and speaks the result, so a provider never touches float menus.
        /// </summary>
        public Func<List<CmrPickerChoice>> Choices;

        /// <summary>Localized line spoken after <see cref="Activate"/> on a row with no state to echo.</summary>
        public string Confirmation;

        /// <summary>True when <see cref="Activate"/> opens one of the mod's own windows, which announces itself.</summary>
        public bool OpensWindow;

        /// <summary>
        /// The mod's own heading for this row's section, spoken once when the cursor crosses into it
        /// so a flat row list keeps the grouping a sighted player reads off the headings.
        /// </summary>
        public string SectionTitle;

        /// <summary>
        /// The LIVE label of the window text button this row fully models, composed from the state the
        /// mod composes it from, so the Buttons region can drop that button rather than present the
        /// control twice. Null for a row that shadows no captured button.
        /// </summary>
        public string CapturedTwin;
    }

    internal sealed class CmrPickerChoice
    {
        public readonly string Label;
        public readonly Action Choose;

        public CmrPickerChoice(string label, Action choose)
        {
            Label = label ?? "";
            Choose = choose;
        }
    }

    /// <summary>
    /// Numeric entry for a value row. Min/Max are snapshots while Current/Apply are delegates — safe
    /// only because <see cref="CmrRowEditor"/> re-resolves the spec from the live row at apply time.
    /// Never cache a spec across a refresh.
    /// </summary>
    internal sealed class CmrNumericSpec
    {
        public int Min;
        public int Max;
        public Func<int> Current;
        public Action<int> Apply;
    }

    internal sealed class CmrTextSpec
    {
        public Func<string> Current;
        public Action<string> Apply;
    }

    /// <summary>
    /// The two Enter behaviours a Colony Manager value row can carry, shared by every scope here:
    /// typing an exact number into one of the mod's count controls, and picking a value out of one of
    /// its single-select strips. The numeric half is stateful — one live
    /// <see cref="TextFieldEditSession"/> per owning scope, so the scope can cancel it when it pops.
    /// </summary>
    internal sealed class CmrRowEditor
    {
        // No length cap: the mod's controls bound these counts by VALUE, not by characters typed.
        private static readonly TextFieldSpec CountSpec = new TextFieldSpec(
            labelKey: null,
            minLength: 0,
            allowedChars: new Regex("^[0-9]*$"));

        // The mod keeps a typed name only while GenText.IsValidFilename accepts it; the shared
        // validator's mustBeFilename runs that same gate at commit.
        private static readonly TextFieldSpec FilenameSpec = new TextFieldSpec(
            labelKey: null,
            minLength: 1,
            mustBeFilename: true);

        private readonly TextFieldEditSession session = new TextFieldEditSession();

        private Func<CmrNumericSpec> resolve;
        private Func<CmrTextSpec> resolveText;
        private Action onChanged;
        private Action onExit;

        /// <summary>
        /// Opens numeric entry for a count row. The spec is RE-RESOLVED at apply time: rows are
        /// rebuilt on every refresh, so a captured spec would write through a row that is already gone.
        /// </summary>
        public void BeginCountEdit(string label, Func<CmrNumericSpec> resolve, Action onChanged,
            Action onExit)
        {
            CmrNumericSpec spec = resolve == null ? null : resolve();
            if (spec == null)
            {
                return;
            }
            this.resolve = resolve;
            resolveText = null;
            this.onChanged = onChanged;
            this.onExit = onExit;
            session.EnterEdit(spec.Current().ToString(), CountSpec, label, Apply, Exit,
                announcePrompt: true);
        }

        /// <summary>
        /// Opens text entry for a name row, with <see cref="BeginCountEdit"/>'s re-resolve-at-apply
        /// contract. A name the vanilla filename check rejects never commits.
        /// </summary>
        public void BeginTextEdit(string label, Func<CmrTextSpec> resolve, Action onChanged,
            Action onExit)
        {
            CmrTextSpec spec = resolve == null ? null : resolve();
            if (spec == null)
            {
                return;
            }
            resolveText = resolve;
            this.resolve = null;
            this.onChanged = onChanged;
            this.onExit = onExit;
            session.EnterEdit(spec.Current() ?? "", FilenameSpec, label, ApplyText, Exit,
                announcePrompt: true);
        }

        public void CancelIfActive()
        {
            session.CancelIfActive();
        }

        /// <summary>
        /// Writes the typed count, once on Enter-confirm and never on Escape, clamped to the range the
        /// mod's own control spans and written through the mod's own setter.
        /// </summary>
        private void Apply(string value)
        {
            CmrNumericSpec spec = resolve == null ? null : resolve();
            int typed;
            if (spec == null || !int.TryParse(value, out typed))
            {
                return;
            }
            spec.Apply(Mathf.Clamp(typed, spec.Min, spec.Max));
            if (onChanged != null)
            {
                onChanged();
            }
        }

        private void ApplyText(string value)
        {
            CmrTextSpec spec = resolveText == null ? null : resolveText();
            if (spec == null)
            {
                return;
            }
            spec.Apply(value);
            if (onChanged != null)
            {
                onChanged();
            }
        }

        private void Exit()
        {
            if (onExit != null)
            {
                onExit();
            }
        }

        /// <summary>
        /// The keyboard form of one of the mod's single-select strips: its cells as a menu opening on
        /// the choice the row holds. Presented through <see cref="WindowlessFloatMenuState"/>, not a
        /// real FloatMenu, which fades itself out when opened away from the mouse. False when the strip
        /// has no cells, so the caller can re-announce rather than leave the press silent.
        /// </summary>
        public static bool OpenPicker(List<CmrPickerChoice> choices, string currentValue, string title,
            Action onChosen)
        {
            if (choices == null || choices.Count == 0)
            {
                return false;
            }
            int startIndex = 0;
            var options = new List<FloatMenuOption>(choices.Count);
            for (int i = 0; i < choices.Count; i++)
            {
                CmrPickerChoice choice = choices[i];
                if (currentValue != null && choice.Label == currentValue)
                {
                    startIndex = i;
                }
                options.Add(new FloatMenuOption(choice.Label, delegate
                {
                    if (choice.Choose != null)
                    {
                        choice.Choose();
                    }
                    if (onChosen != null)
                    {
                        onChosen();
                    }
                }));
            }
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex,
                announceSelection: false, titleText: title);
            return true;
        }
    }

    /// <summary>
    /// One region of a job's detail pane: the mod's own column as a flat row list, or — when
    /// <see cref="Table"/> is set — one of its embedded pawn tables as a real table region.
    /// </summary>
    internal sealed class CmrDetailRegion
    {
        public readonly string Name;
        public readonly List<CmrDetailRow> Rows = new List<CmrDetailRow>();

        /// <summary>Set when this region IS a pawn table; <see cref="Rows"/> stays empty then.</summary>
        public CmrDetailPawnTable Table;

        public CmrDetailRegion(string name)
        {
            Name = name ?? "";
        }
    }

    /// <summary>
    /// Builds the detail regions for one manager tab's selected job, one implementation per tab shape.
    /// A provider never names the mod's internal tab type in source; it asks its own compat surface.
    /// </summary>
    internal interface ICmrJobDetailsProvider
    {
        bool Handles(object tab);

        List<CmrDetailRegion> Build(object tab, object job);
    }

    /// <summary>
    /// The provider registry. A tab with no provider grows no detail regions, so the manager window
    /// keeps its tab and job lists and nothing pretends the pane is modelled.
    /// </summary>
    internal static class CmrJobDetails
    {
        private static readonly ICmrJobDetailsProvider[] providers =
        {
            new CmrHuntingDetails(),
            new CmrForestryDetails(),
            new CmrForagingDetails(),
            new CmrMiningDetails(),
            new CmrLivestockDetails(),
            new CmrProductionDetails(),
            new CmrOverviewDetails(),
            new CmrLogsDetails(),
            new CmrImportExportDetails(),
            new CmrPowerDetails(),
        };

        // A null job reaches the provider: a tab can carry job-independent regions.
        public static List<CmrDetailRegion> Build(object tab, object job)
        {
            if (tab != null)
            {
                for (int i = 0; i < providers.Length; i++)
                {
                    if (providers[i].Handles(tab))
                    {
                        return providers[i].Build(tab, job);
                    }
                }
            }
            return new List<CmrDetailRegion>();
        }
    }
}
