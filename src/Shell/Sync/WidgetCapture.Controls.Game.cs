using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    public static partial class WidgetCapture
    {
        internal static void RecordAndMaybeToggleCheckbox(Rect rect, string label, ref bool checkOn, bool disabled)
        {
            if (!passOpen)
            {
                return;
            }
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            // Both channels honor the vanilla `disabled` gate: a gated post/match is
            // consumed without a flip (the armed caller reads ArmedGateBlocked).
            MaybeLiveActivateMatch(WidgetKind.Checkbox, label ?? "", index);
            if (!detachedPass && liveActivateFireIndex == index)
            {
                ClearPendingActivate();
                if (!disabled)
                {
                    checkOn = !checkOn;
                }
            }
            // Armed channel flips here so the row recorded below carries the NEW state;
            // the outcome announcer re-reads that folded row to speak it.
            if (MaybeArmMatch(WidgetKind.Checkbox, label ?? "", index))
            {
                if (disabled)
                {
                    armedGateBlocked = true;
                }
                else
                {
                    checkOn = !checkOn;
                    armedFired = true;
                    InjectedClickGuard.InFlight = true;
                }
            }
            gapLinePending = false;
            CapturedWidget row = new CapturedWidget { Kind = WidgetKind.Checkbox, Label = label ?? "", Rect = rect, ScreenRect = GuiSpace.ToScreen(rect), VisibleScreenRect = GuiSpace.VisibleScreenRect(rect), Clip = GuiSpace.CurrentClip(), Checked = checkOn, Disabled = disabled };
            sink.Add(row);
            DrawFocusRingIfFocused(index, rect);
        }

        internal static int RecordRadio(Rect rect, string label, bool chosen, bool disabled)
        {
            if (!passOpen)
            {
                return -1;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            MaybeArmMatch(WidgetKind.RadioButton, label ?? "", index);
            MaybeLiveActivateMatch(WidgetKind.RadioButton, label ?? "", index);
            sink.Add(new CapturedWidget { Kind = WidgetKind.RadioButton, Label = label ?? "", Rect = rect, ScreenRect = GuiSpace.ToScreen(rect), VisibleScreenRect = GuiSpace.VisibleScreenRect(rect), Clip = GuiSpace.CurrentClip(), Selected = chosen, Disabled = disabled });
            DrawFocusRingIfFocused(index, rect);
            return index;
        }

        /// <summary>
        /// Records the ONE synthetic FilterPanelHandoff row for a
        /// Verse.ThingFilterUI.DoThingFilterConfigWindow call, harvesting the call's own
        /// arguments onto <see cref="CapturedFilterPanel"/> rather than re-deriving them.
        /// Must run BEFORE <see cref="EnterFilterPanelSuppression"/> so this row lands in
        /// the real sink while everything the panel draws next does not. Label stays null:
        /// presentation supplies the localized wording.
        /// </summary>
        internal static int RecordFilterPanel(Rect rect, ThingFilter filter, ThingFilter parentFilter,
            IEnumerable<SpecialThingFilterDef> forceHiddenFilters, bool forceHideHitPointsConfig, bool forceHideQualityConfig)
        {
            if (!passOpen)
            {
                return -1;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            sink.Add(new CapturedWidget
            {
                Kind = WidgetKind.Label,
                Label = null,
                Rect = rect,
                ScreenRect = GuiSpace.ToScreen(rect),
                VisibleScreenRect = GuiSpace.VisibleScreenRect(rect),
                Clip = GuiSpace.CurrentClip(),
                Composite = CompositeMember.FilterPanelHandoff,
                Payload = new CapturedFilterPanel
                {
                    Filter = filter,
                    ParentFilter = parentFilter,
                    ForceHiddenFilters = forceHiddenFilters,
                    ForceHideHitPointsConfig = forceHideHitPointsConfig,
                    ForceHideQualityConfig = forceHideQualityConfig,
                },
            });
            DrawFocusRingIfFocused(index, rect);
            return index;
        }

        // Blank-button naming bracket: a drawer whose real click handler is ButtonText(rect, "")
        // (caption painted separately) captures as a nameless button; a bracket that knows the
        // draw call supplies the name a sighted player reads. Applied before the activation
        // matchers so descriptor matching sees the announced label. Capture-side rename only.
        private struct BlankButtonNaming
        {
            public string Name;
            public bool NeedsLateTipName;
            public bool LabelIsTextureName;
        }

        private static readonly List<BlankButtonNaming> blankButtonNames = new List<BlankButtonNaming>();

        public static void EnterBlankButtonName(string name)
        {
            blankButtonNames.Add(new BlankButtonNaming { Name = name ?? "" });
        }

        /// <summary>Blank-name bracket for ButtonImageWithBG's inner ButtonText(""): named from the texture, with ButtonImage's late-tip promotion.</summary>
        internal static void EnterImageWithBGName(Rect butRect, Texture2D tex)
        {
            string name = ImageButtonLabel(butRect, tex, null,
                out _, out bool needsLateTipName, out bool labelIsTextureName);
            blankButtonNames.Add(new BlankButtonNaming
            {
                Name = name ?? "",
                NeedsLateTipName = needsLateTipName,
                LabelIsTextureName = labelIsTextureName,
            });
        }

        public static void ExitBlankButtonName()
        {
            if (blankButtonNames.Count > 0)
            {
                blankButtonNames.RemoveAt(blankButtonNames.Count - 1);
            }
        }

        internal static int RecordButton(Rect rect, string label, bool disabled, bool closeX = false, bool nameFromTip = false,
            bool needsLateTipName = false, bool labelIsTextureName = false, bool mayOpenDropdown = false)
        {
            if (!passOpen)
            {
                return -1;
            }
            if (string.IsNullOrEmpty(label) && blankButtonNames.Count > 0)
            {
                BlankButtonNaming naming = blankButtonNames[blankButtonNames.Count - 1];
                label = naming.Name;
                needsLateTipName |= naming.NeedsLateTipName;
                labelIsTextureName |= naming.LabelIsTextureName;
            }
            gapLinePending = false;
            List<CapturedWidget> sink = CurrentSink;
            int index = sink.Count;
            MaybeArmMatch(WidgetKind.Button, label ?? "", index);
            MaybeLiveActivateMatch(WidgetKind.Button, label ?? "", index);
            CapturedWidget row = new CapturedWidget { Kind = WidgetKind.Button, Label = label ?? "", Rect = rect, ScreenRect = GuiSpace.ToScreen(rect), VisibleScreenRect = GuiSpace.VisibleScreenRect(rect), Clip = GuiSpace.CurrentClip(), RecordColor = GUI.color, Disabled = disabled, DropdownOpener = labeledButtonDepth > 0 || (mayOpenDropdown && DrawnByDropdown(rect, label)), CloseX = closeX, NameFromTip = nameFromTip, NeedsLateTipName = needsLateTipName, LabelIsTextureName = labelIsTextureName };
            if (intEntryDepth > 0)
            {
                // Draw order is the button's identity: vanilla draws -10x, -1x, +10x, +1x
                // (Widgets.cs:2189-2212), so big is ordinals 0 and 2.
                row.StepperButton = true;
                row.StepperPlus = stepperButtonOrdinal >= 2;
                row.StepperBig = stepperButtonOrdinal == 0 || stepperButtonOrdinal == 2;
                stepperButtonOrdinal++;
            }
            else if (intAdjusterDepth > 0)
            {
                // -N then +N (Listing_Standard.cs:398/:408); no big pair exists.
                row.StepperButton = true;
                row.StepperPlus = stepperButtonOrdinal == 1;
                stepperButtonOrdinal++;
            }
            if (listingTreeExpanderDepth > 0)
            {
                // This ButtonImage is Listing_Tree.OpenCloseWidget's only draw and its click
                // already calls node.SetOpen (Verse/Listing_Tree.cs:70-81), so the row stays
                // the live injectable expander and only gains tree structure.
                row.Composite = CompositeMember.TreeRowExpander;
                row.TreeLevel = listingTreeIndentLevel;
                row.TreeOpen = listingTreeExpanderOpen;
            }
            else if (hyperlinkDepth > 0)
            {
                // Widgets.HyperlinkWithIcon draws its caption via ButtonText with
                // active: false (Verse/Widgets.cs:1153) — paint, not a gate; the live click
                // target is the ButtonInvisible over the same rect. The real gate is a HIDDEN
                // link, whose ActivateHyperlink is a no-op (Verse/Dialog_InfoCard.cs:246-251).
                row.Composite = CompositeMember.HyperlinkLabel;
                row.Disabled = hyperlinkHidden;
            }
            else if (selectableDefDepth > 0)
            {
                row.Composite = CompositeMember.SelectableDefDelete;
            }
            else if (toggleableIconDepth > 0)
            {
                // Verse.WidgetRow.ToggleableIcon's only click target; its click already flips
                // the caller's ref bool and plays the tick (Verse/WidgetRow.cs:191-202), so
                // the row stays live and injectable. Checked state is stamped by
                // ExitToggleableIcon, the first point that knows the POST-click value.
                row.Composite = CompositeMember.ToggleableIcon;
                row.IconTexName = toggleableIconTexName;
                // Retroactively un-heading: RecordLabel's gap-line heuristic cannot tell a
                // real section break from a plain visual rule at the caption's own record
                // time, so a caption drawn by WidgetRow.Label right after a GapLine gets
                // wrongly stamped as a heading and absorbed as section context instead of
                // naming its toggles. A real heading is never immediately followed by a
                // same-row control, and WidgetRow lays every member of one row at the same
                // curY, so an exact Rect.y match is "same WidgetRow line" — no fuzzy
                // adjacency test. Harmless when the heading stamp was correct.
                if (index > 0)
                {
                    CapturedWidget prev = sink[index - 1];
                    if (prev.Kind == WidgetKind.Label && prev.HeadingFromGapLineOnly && !prev.LabelPairFolded
                        && prev.Rect.y == rect.y)
                    {
                        prev.Heading = false;
                        prev.HeadingFromGapLineOnly = false;
                    }
                }
            }
            sink.Add(row);
            DrawFocusRingIfFocused(index, rect);
            return index;
        }

        /// <summary>
        /// Forces a true click-result when this row's index has a pending live activation or
        /// an armed match. <paramref name="gateBlocked"/> carries the target widget's own
        /// gate; a gated fire is refused, exactly as vanilla refuses the mouse click.
        /// </summary>
        internal static void MaybeForceActivate(int index, ref bool result, bool gateBlocked = false)
        {
            if (index < 0 || !passOpen)
            {
                return;
            }
            if (detachedPass)
            {
                if (armedActive && !armedFired && armedFireIndex == index)
                {
                    if (gateBlocked)
                    {
                        armedGateBlocked = true;
                    }
                    else
                    {
                        result = true;
                        armedFired = true;
                        InjectedClickGuard.InFlight = true;
                    }
                }
                return;
            }
            if (liveActivateFireIndex == index)
            {
                // Consume the post even when gated — leaving it would fire on an unrelated
                // later pass — but never force a gated widget's result.
                ClearPendingActivate();
                if (!gateBlocked)
                {
                    result = true;
                    InjectedClickGuard.InFlight = true;
                }
            }
        }

        /// <summary>
        /// DraggableResult twin of <see cref="MaybeForceActivate"/>: forces a
        /// <c>Pressed</c> result so the vanilla call site's own <c>== Pressed</c> branch runs
        /// its handler unmodified (mutation vehicle A). <paramref name="gateBlocked"/>
        /// carries the target's own gate; a gated fire is refused.
        /// </summary>
        internal static void MaybeForceDraggablePressed(int index, ref Widgets.DraggableResult result, bool gateBlocked = false)
        {
            if (index < 0 || !passOpen)
            {
                return;
            }
            if (detachedPass)
            {
                if (armedActive && !armedFired && armedFireIndex == index)
                {
                    if (gateBlocked)
                    {
                        armedGateBlocked = true;
                    }
                    else
                    {
                        result = Widgets.DraggableResult.Pressed;
                        armedFired = true;
                        InjectedClickGuard.InFlight = true;
                    }
                }
                return;
            }
            if (liveActivateFireIndex == index)
            {
                ClearPendingActivate();
                if (!gateBlocked)
                {
                    result = Widgets.DraggableResult.Pressed;
                    InjectedClickGuard.InFlight = true;
                }
            }
        }

        /// <summary>
        /// Accessible name for an icon-only button, in tiers: a composite bracket's known
        /// name, else the tooltip vanilla attaches to the button, else a tooltip a caller
        /// registered separately over the same rect, else the localized close-button phrase
        /// when the texture is the shared close-X chrome (texture identity, never string
        /// matching), else the texture's asset name.
        /// <paramref name="nameFromTip"/> reports that the geometric tier supplied the name,
        /// so the folding path does not speak that tooltip twice.
        /// </summary>
        /// <param name="needsLateTipName">
        /// Every tier failed and the raw texture name is standing in; the folding path should
        /// retry the tooltip lookup once the pass's whole tip index is in.
        /// </param>
        internal static string ImageButtonLabel(Rect butRect, Texture2D tex, string tooltip, out bool nameFromTip,
            out bool needsLateTipName, out bool labelIsTextureName)
        {
            return ImageButtonLabelCore(butRect, tex, tooltip, out nameFromTip, out needsLateTipName, out labelIsTextureName);
        }

        /// <summary>
        /// Same naming order WITHOUT the geometric tooltip tier, for callers that only want
        /// the name a texture yields. No rect, no tip query.
        /// </summary>
        internal static string ImageButtonLabel(Texture2D tex, string tooltip)
        {
            return ImageButtonLabelCore(null, tex, tooltip, out bool _, out bool _, out bool _);
        }

        private static string ImageButtonLabelCore(Rect? butRect, Texture2D tex, string tooltip, out bool nameFromTip,
            out bool needsLateTipName, out bool labelIsTextureName)
        {
            nameFromTip = false;
            needsLateTipName = false;
            labelIsTextureName = false;
            // The passOpen gates are not redundant: bracket depths track vanilla's call stack
            // whether or not anything is recording, and this runs on EVERY icon button the
            // game draws, so the lookups below must stay dormant outside a capture pass.
            if (passOpen && infoCardDepth > 0)
            {
                return "RimWorldAccess.UI.GenericWindow.InfoCardButton".Translate();
            }
            if (passOpen && toggleableIconDepth > 0 && !string.IsNullOrEmpty(toggleableIconTip))
            {
                // Verse.WidgetRow.ToggleableIcon registers this as the control's TipRegion but
                // passes ButtonImage no tooltip (Verse/WidgetRow.cs:178-183); without the
                // bracket the row would be named after a texture asset.
                return toggleableIconTip;
            }
            if (passOpen && selectableDefDepth > 0 && tex != null && tex == TexButton.Delete
                && !string.IsNullOrEmpty(selectableDefName))
            {
                // A bare "Delete" says what the icon does but never which row, which a
                // sighted player reads off its position.
                return "RimWorldAccess.UI.GenericWindow.DeleteNamed".Translate(selectableDefName).ToString();
            }
            if (!string.IsNullOrEmpty(tooltip))
            {
                return tooltip;
            }
            if (butRect.HasValue)
            {
                string registeredTip = RegisteredTipName(butRect.Value);
                if (!string.IsNullOrEmpty(registeredTip))
                {
                    nameFromTip = true;
                    return registeredTip;
                }
            }
            if (tex != null && (tex == TexButton.CloseXSmall || tex == TexButton.CloseXBig))
            {
                return "CloseButton".Translate();
            }
            if (tex != null && tex == TexButton.Delete)
            {
                return "Delete".Translate();
            }
            // The designator rotation pair (Verse/DesignatorUtility.cs:88/:99): the key
            // binding it duplicates is the game's localized name; the texture name is
            // "RotLeft".
            if (tex != null && tex == TexUI.RotLeftTex && KeyBindingDefOf.Designator_RotateLeft != null)
            {
                return KeyBindingDefOf.Designator_RotateLeft.LabelCap;
            }
            if (tex != null && tex == TexUI.RotRightTex && KeyBindingDefOf.Designator_RotateRight != null)
            {
                return KeyBindingDefOf.Designator_RotateRight.LabelCap;
            }
            if (tex != null)
            {
                // Storyteller portrait buttons carry no tooltip and no caption, so the def
                // behind the texture is the only name available.
                string storytellerName = StorytellerPortraitName(tex);
                if (storytellerName != null)
                {
                    return storytellerName;
                }
                // Nothing named the button synchronously. A caller that registers its
                // TipRegion AFTER this draw cannot be seen from here, so flag the row for the
                // fold-time retry; the texture name below stays the answer if that retry also
                // comes up empty. Gated like RegisteredTipName's tier — the DETACHED channel
                // is the folding path's own.
                needsLateTipName = butRect.HasValue && passOpen && TooltipCapture.DetachedPassOpen;
                // Channel-agnostic twin: an ARMED reader has its own tip index and does this
                // promotion itself, so it must know the fallback fired whichever channel is open.
                labelIsTextureName = true;
                // Well-known vanilla glyphs (mods reuse these textures too) get a localized
                // label; anything else is humanized rather than spoken as a raw filename.
                string glyphSuffix = TextureButtonLabels.TryGetGlyphKeySuffix(tex.name);
                if (glyphSuffix != null)
                {
                    return ("RimWorldAccess.Shell.Glyph." + glyphSuffix).Translate();
                }
                return TextureButtonLabels.Humanize(tex.name);
            }
            return "";
        }

        /// <summary>
        /// The name carried by a tooltip a caller registered over <paramref name="butRect"/>
        /// itself rather than handing it to the ButtonImage overload. Resolved in SCREEN
        /// space with clip-context equality, because this string becomes the control's NAME
        /// and a tip stolen across coordinate spaces would be unrecoverably wrong.
        /// Gated on the DETACHED channel, the capture-folding path's own; a scope reading the
        /// ARMED channel presents these tips itself, so naming from them would speak one
        /// tooltip twice. Only registrations made earlier in the same pass are visible.
        /// </summary>
        private static string RegisteredTipName(Rect butRect)
        {
            if (!passOpen || !TooltipCapture.DetachedPassOpen)
            {
                return null;
            }
            string tip = TooltipCapture.TryResolveDetachedAtScreen(GuiSpace.ToScreen(butRect), GuiSpace.CurrentClip());
            return ShortTipName(tip);
        }

        private static Dictionary<Texture2D, string> storytellerPortraitNames;

        /// <summary>
        /// The label of the storyteller whose tiny portrait <paramref name="tex"/> is, or
        /// null. Built once: defs are immutable after load and a texture is unique to its def.
        /// </summary>
        private static string StorytellerPortraitName(Texture2D tex)
        {
            if (storytellerPortraitNames == null)
            {
                var names = new Dictionary<Texture2D, string>();
                foreach (StorytellerDef def in DefDatabase<StorytellerDef>.AllDefsListForReading)
                {
                    if (def.portraitTinyTex != null && !names.ContainsKey(def.portraitTinyTex))
                    {
                        names.Add(def.portraitTinyTex, def.LabelCap);
                    }
                }
                storytellerPortraitNames = names;
            }
            return storytellerPortraitNames.TryGetValue(tex, out string name) ? name : null;
        }

        /// <summary>
        /// A tooltip reduced to a control NAME: its first line, and its first sentence when
        /// that line is long enough to be prose. Not lossy for the reader — what this drops
        /// is still spoken as the row's tooltip, which is suppressed only when the name came
        /// out identical. Shared with the folding path's late retry so a row named from a
        /// tooltip reads the same whether resolved at draw time or after the pass.
        /// </summary>
        internal static string ShortTipName(string tip)
        {
            if (string.IsNullOrEmpty(tip))
            {
                return null;
            }
            string text = tip.Trim();
            int lineBreak = text.IndexOfAny(new char[] { '\n', '\r' });
            if (lineBreak >= 0)
            {
                text = text.Substring(0, lineBreak).TrimEnd();
            }
            if (text.Length > MaxTipNameLength)
            {
                int sentenceEnd = text.IndexOfAny(new char[] { '.', '!', '?' });
                if (sentenceEnd > 0 && sentenceEnd + 1 <= MaxTipNameLength)
                {
                    text = text.Substring(0, sentenceEnd + 1);
                }
            }
            return text.Length > 0 ? text : null;
        }

    }
}
