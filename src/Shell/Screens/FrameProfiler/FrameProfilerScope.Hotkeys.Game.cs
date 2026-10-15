using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One rebindable chord per window control, so every control row speaks a hotkey and the
    /// chord presses the same captured control Enter would. Controls are identified by the
    /// window's own literals (the strip's labels are hard-coded English, never translated), and
    /// two-segment groups toggle to their other segment. "Reset to defaults" deliberately has no
    /// chord: it discards every profiler preference without a confirmation.
    /// </summary>
    internal sealed partial class FrameProfilerScope
    {
        private sealed class HotkeyTarget
        {
            public string ActionId;
            public Func<FrameProfilerScope, ProfilerControl, bool> Matches;
            /// <summary>For a two-segment group: press the segment that is not active.</summary>
            public bool Toggle;
            /// <summary>The scope already claims this chord with a handler of its own.</summary>
            public bool OwnClaim;
            /// <summary>What a bare-glyph button is spoken as: the action its click runs.</summary>
            public string SpokenKey;
            /// <summary>A frame stepper, whose effect is the frame it lands on.</summary>
            public bool StepsFrames;
        }

        private static readonly List<HotkeyTarget> hotkeyTargets = new List<HotkeyTarget>
        {
            Button("frameProfiler.settings", ProfilerZone.Title, ProfilerControlKind.Chip, "Settings"),
            Glyph(SharedMenuGrammar.Cancel, ProfilerZone.Title, "×", "RimWorldAccess.FrameProfiler.Control.Close", ownClaim: true),
            new HotkeyTarget { ActionId = "frameProfiler.pause", OwnClaim = true, Matches = (s, c) => c.Zone == ProfilerZone.Toolbar && c.Kind == ProfilerControlKind.Chip },
            FrameStep("frameProfiler.olderFrame", "◀", "RimWorldAccess.FrameProfiler.Control.OlderFrame"),
            FrameStep("frameProfiler.newerFrame", "▶", "RimWorldAccess.FrameProfiler.Control.NewerFrame"),
            FrameStep("frameProfiler.newestFrame", "▶▶", "RimWorldAccess.FrameProfiler.Control.NewestFrame"),
            Segments("frameProfiler.toggleSource", ProfilerZone.Toolbar, 0),
            Segments("frameProfiler.toggleHistogramMode", ProfilerZone.HistogramHeader, 0),
            Button("frameProfiler.clearFrames", ProfilerZone.HistogramHeader, ProfilerControlKind.Ghost, "Clear"),
            Segment("frameProfiler.cpuTab", 0),
            Segment("frameProfiler.allocTab", 1),
            Segment("frameProfiler.vramTab", 2),
            Segments("frameProfiler.toggleDisplay", ProfilerZone.TabStrip, 1),
            new HotkeyTarget { ActionId = "frameProfiler.threads", Matches = (s, c) => c.Kind == ProfilerControlKind.ThreadButton },
            new HotkeyTarget { ActionId = "frameProfiler.search", Matches = (s, c) => c.Kind == ProfilerControlKind.SearchField },
            new HotkeyTarget { ActionId = "frameProfiler.invert", Matches = (s, c) => c.Zone == ProfilerZone.TabStrip && c.Kind == ProfilerControlKind.Chip && c.Label.EndsWith("Invert") },
            Glyph("frameProfiler.expandAll", ProfilerZone.TabStrip, "+", "RimWorldAccess.FrameProfiler.Control.ExpandAll"),
            Glyph("frameProfiler.collapseAll", ProfilerZone.TabStrip, "−", "RimWorldAccess.FrameProfiler.Control.CollapseAll"),
            new HotkeyTarget { ActionId = "frameProfiler.copyView", OwnClaim = true, Matches = (s, c) => c.Zone == ProfilerZone.TabStrip && c.Kind == ProfilerControlKind.Ghost && c.Label == "Copy" },
            Button("frameProfiler.vramBaseline", ProfilerZone.TabStrip, ProfilerControlKind.Chip, "Baseline"),
            Button("frameProfiler.vramClearBaseline", ProfilerZone.TabStrip, ProfilerControlKind.Chip, "Clear Δ"),
            Button("frameProfiler.vramDeltaSort", ProfilerZone.TabStrip, ProfilerControlKind.Chip, "Δ sort"),
            Button("frameProfiler.vramExport", ProfilerZone.TabStrip, ProfilerControlKind.Chip, "Export"),
        };

        private static HotkeyTarget Button(string actionId, ProfilerZone zone, ProfilerControlKind kind, string label)
        {
            return new HotkeyTarget { ActionId = actionId, Matches = (s, c) => c.Zone == zone && c.Kind == kind && c.Label == label };
        }

        private static HotkeyTarget Glyph(string actionId, ProfilerZone zone, string glyph, string spokenKey, bool ownClaim = false)
        {
            HotkeyTarget target = Button(actionId, zone, ProfilerControlKind.Ghost, glyph);
            target.SpokenKey = spokenKey;
            target.OwnClaim = ownClaim;
            return target;
        }

        private static HotkeyTarget FrameStep(string actionId, string glyph, string spokenKey)
        {
            HotkeyTarget target = Glyph(actionId, ProfilerZone.Toolbar, glyph, spokenKey, ownClaim: true);
            target.StepsFrames = true;
            return target;
        }

        /// <summary>A two-segment group, by its order among the zone's segment groups.</summary>
        private static HotkeyTarget Segments(string actionId, ProfilerZone zone, int rank)
        {
            return new HotkeyTarget
            {
                ActionId = actionId,
                Toggle = true,
                Matches = (s, c) => c.Zone == zone && c.Kind == ProfilerControlKind.Segment && s.SegmentGroupRank(c) == rank,
            };
        }

        /// <summary>One segment of the tab strip's section group (CPU, Alloc, VRAM).</summary>
        private static HotkeyTarget Segment(string actionId, int index)
        {
            return new HotkeyTarget
            {
                ActionId = actionId,
                Matches = (s, c) => c.Zone == ProfilerZone.TabStrip && c.Kind == ProfilerControlKind.Segment
                    && s.SegmentGroupRank(c) == 0 && c.SegmentIndex == index,
            };
        }

        private Func<ProfilerControl, bool> pendingControl;

        private void ClaimHotkeys()
        {
            for (int i = 0; i < hotkeyTargets.Count; i++)
            {
                HotkeyTarget target = hotkeyTargets[i];
                if (!target.OwnClaim)
                {
                    Claim(target.ActionId, e => PressHotkey(target), when: NotEditing);
                }
            }
        }

        /// <summary>A segment group's position among the groups its zone drew this pass.</summary>
        private int SegmentGroupRank(ProfilerControl control)
        {
            int rank = 0;
            int seen = -1;
            IReadOnlyList<ProfilerControl> controls = FrameProfilerCapture.Published;
            for (int i = 0; i < controls.Count; i++)
            {
                ProfilerControl c = controls[i];
                if (c.Zone != control.Zone || c.Kind != ProfilerControlKind.Segment || c.SegmentGroup == seen)
                {
                    continue;
                }
                if (c.SegmentGroup == control.SegmentGroup)
                {
                    return rank;
                }
                seen = c.SegmentGroup;
                rank++;
            }
            return -1;
        }

        /// <summary>The hotkey entry a control row answers to, or null when it has none.</summary>
        private HotkeyTarget TargetFor(ProfilerControl control)
        {
            for (int i = 0; i < hotkeyTargets.Count; i++)
            {
                if (hotkeyTargets[i].Matches(this, control))
                {
                    return hotkeyTargets[i];
                }
            }
            return null;
        }

        private void PressHotkey(HotkeyTarget target)
        {
            IReadOnlyList<ProfilerControl> controls = FrameProfilerCapture.Published;
            ProfilerControl? found = null;
            for (int i = 0; i < controls.Count; i++)
            {
                ProfilerControl c = controls[i];
                if (target.Matches(this, c) && !(target.Toggle && c.Active))
                {
                    found = c;
                    break;
                }
            }
            if (!found.HasValue || !found.Value.Enabled)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            ProfilerControl control = found.Value;
            switch (control.Kind)
            {
                case ProfilerControlKind.ThreadButton:
                    OpenThreadPopup();
                    return;
                case ProfilerControlKind.SearchField:
                    MoveCursorTo(RegionKind.TableControls, row => row.IsControl && row.Control.Kind == ProfilerControlKind.SearchField);
                    ActivateCurrentRow();
                    return;
                case ProfilerControlKind.Segment:
                    if (control.Active)
                    {
                        AnnounceControl(control);
                        return;
                    }
                    break;
            }
            FrameProfilerCapture.ArmPress(control);
            ProfilerZone zone = control.Zone;
            int group = control.SegmentGroup;
            ProfilerControlKind kind = control.Kind;
            string label = control.Label;
            pendingControl = kind == ProfilerControlKind.Segment
                ? (Func<ProfilerControl, bool>)(c => c.Zone == zone && c.Kind == kind && c.SegmentGroup == group && c.Active)
                : (c => c.Zone == zone && c.Kind == kind && (c.Label == label || target.Matches(this, c)));
            Defer(Pending.Control);
        }

        /// <summary>Speaks one control's row as focus would, without moving the cursor onto it.</summary>
        private void AnnounceControl(ProfilerControl control)
        {
            ElementDescription d = DescribeRow(new Row { Control = control });
            TolkHelper.SpeakData(AnnouncementComposer.ComposeFocus(d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
        }

        private void AnnouncePendingControl()
        {
            Func<ProfilerControl, bool> match = pendingControl;
            pendingControl = null;
            if (match == null)
            {
                return;
            }
            IReadOnlyList<ProfilerControl> controls = FrameProfilerCapture.Published;
            for (int i = 0; i < controls.Count; i++)
            {
                if (match(controls[i]))
                {
                    AnnounceControl(controls[i]);
                    return;
                }
            }
        }

        private void MoveCursorTo(RegionKind kind, Func<Row, bool> match)
        {
            RefreshModel();
            int region = regions.IndexOf(kind);
            List<Row> rows = rowLists[kind];
            for (int i = 0; i < rows.Count && region >= 0; i++)
            {
                if (match(rows[i]))
                {
                    LandCursorAt(region, i, announce: false);
                    return;
                }
            }
        }

        private void ActivateCurrentRow()
        {
            ListModel region = Model.CurrentRegion;
            if (region != null && region.Index >= 0)
            {
                ActivateContentItem(Model.RegionIndex, region.Index);
            }
        }
    }
}
