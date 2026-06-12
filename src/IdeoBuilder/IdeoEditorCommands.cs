using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Editor-level commands shared by every ideoligion section editor — the worldgen builder hub
    /// (<see cref="IdeoBuilderHubState"/>) and the in-game Archonexus reform editor
    /// (<see cref="IdeoSectionEditorState"/>). Keeping randomize / save-to-file / ritual-sound preview
    /// in one place is what guarantees the two editors behave identically; each host supplies its own
    /// "refresh after the command" callback since only the host knows how to re-announce its list.
    ///
    /// All commands operate on whichever <see cref="Ideo"/> is passed in.
    /// </summary>
    public static class IdeoEditorCommands
    {
        private static readonly TextInputController saveController = new TextInputController();
        private static Sustainer ritualPreviewSustainer;

        /// <summary>
        /// Replaces the ENTIRE ideoligion (memes, name, description, precepts, the lot) via the same
        /// foundation re-init vanilla's "Randomize all" button uses. Caller is responsible for the
        /// confirmation prompt and for refreshing/announcing afterwards. Returns false if the action
        /// was blocked (tutorial) or the ideo was null.
        /// </summary>
        public static bool RandomizeAll(Ideo ideo)
        {
            if (ideo == null) return false;
            if (!TutorSystem.AllowAction("ConfiguringIdeo")) return false;
            var parms = new IdeoGenerationParms(
                IdeoUIUtility.FactionForRandomization(ideo),
                forceNoExpansionIdeo: false,
                null, null, null,
                classicExtra: false,
                forceNoWeaponPreference: false,
                ideo.Fluid);
            ideo.foundation.Init(parms);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            return true;
        }

        /// <summary>
        /// Opens the editor context menu (the ']' key): save to file, randomize all, preview ritual
        /// sound. Save and the ritual preview are keyboard-only nowhere else, so they live here;
        /// Randomize is surfaced with its Alt+R shortcut as a tooltip for discoverability (matching
        /// the character-creation menu). The randomize action is supplied by the host so it can run
        /// its own confirm + refresh.
        /// </summary>
        public static void OpenContextMenu(Ideo ideo, Action onRandomizeAll = null)
        {
            if (ideo == null) return;

            var options = new List<FloatMenuOption>
            {
                // Save has no keyboard shortcut (Alt+S is "continue") — it lives only in this menu.
                new FloatMenuOption("Save".Translate() + " " + "StatsReport_Ideoligion".Translate().ToString().ToLower(),
                    () => SaveIdeoligion(ideo)),
            };

            if (onRandomizeAll != null)
                options.Add(WithTip(new FloatMenuOption("RandomizeAll".Translate(), onRandomizeAll), "Alt+R"));

            if (ideo.SoundOngoingRitual != null)
            {
                bool playing = ritualPreviewSustainer != null && !ritualPreviewSustainer.Ended;
                options.Add(new FloatMenuOption(
                    (playing ? "Stop" : "Preview") + " ritual sound", () => ToggleRitualPreview(ideo)));
            }

            // Each action announces its own result, so suppress the generic "{label} selected" echo.
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false);
        }

        private static FloatMenuOption WithTip(FloatMenuOption opt, string tip)
        {
            opt.tooltip = new TipSignal(tip);
            return opt;
        }

        public static void SaveIdeoligion(Ideo ideo)
        {
            if (ideo == null) return;
            saveController.Begin(ideo.name ?? "", TextFieldSpec.Unrestricted("Name"),
                text =>
                {
                    string fileName = GenFile.SanitizedFileName(text.Trim());
                    if (string.IsNullOrEmpty(fileName))
                    {
                        TolkHelper.Speak("NeedAName".Loc(), SpeechPriority.High);
                        return;
                    }
                    string absPath = GenFilePaths.AbsPathForIdeo(fileName);
                    LongEventHandler.QueueLongEvent(
                        () => GameDataSaveLoader.SaveIdeo(ideo, absPath),
                        "SavingLongEvent", doAsynchronously: false, null);
                    TolkHelper.Speak("SavedAs".Loc(fileName), SpeechPriority.High);
                },
                // This field is a save filename, not an editable value — it announces "Saved as X"
                // itself, so suppress the generic "Name set to X" commit announcement.
                announceOnCommit: false);
        }

        public static void ToggleRitualPreview(Ideo ideo)
        {
            if (ritualPreviewSustainer != null && !ritualPreviewSustainer.Ended)
            {
                StopRitualPreview();
                TolkHelper.Speak("RimWorldAccess.Ideology.RitualSound.Stopped".Loc((string)"RitualAmbienceSound".Translate()));
                return;
            }
            var sound = ideo?.SoundOngoingRitual;
            if (sound == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            // Force on-camera playback so the sustainer is actually audible; MaintainRitualPreview
            // then ducks the game music (in a running game).
            var info = SoundInfo.OnCamera(MaintenanceType.PerFrame);
            info.forcedPlayOnCamera = true;
            info.testPlay = true;
            ritualPreviewSustainer = sound.TrySpawnSustainer(info);
            TolkHelper.Speak("RimWorldAccess.Ideology.RitualSound.Playing".Loc((string)"RitualAmbienceSound".Translate()));
        }

        /// <summary>
        /// Keeps the ritual-sound preview alive; call every frame from the host patch. Wrapped so a
        /// sound-system failure can never propagate and stall the host's input handling.
        /// </summary>
        public static void MaintainRitualPreview()
        {
            if (ritualPreviewSustainer == null) return;
            try
            {
                if (ritualPreviewSustainer.Ended)
                {
                    ritualPreviewSustainer = null;
                    return;
                }
                ritualPreviewSustainer.Maintain();
                // ForceSilenceFor lives only on MusicManagerPlay, and Find.MusicManagerPlay casts
                // Current.Root to Root_Play — which throws pre-game (the main-menu builder runs in a
                // Root_Entry). Only duck the music when actually in a running game.
                if (Current.ProgramState == ProgramState.Playing)
                    Find.MusicManagerPlay?.ForceSilenceFor(0.1f);
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimWorld Access] Ritual sound preview stopped after an error: {ex.Message}");
                StopRitualPreview();
            }
        }

        public static void StopRitualPreview()
        {
            if (ritualPreviewSustainer != null)
            {
                if (!ritualPreviewSustainer.Ended) ritualPreviewSustainer.End();
                ritualPreviewSustainer = null;
            }
        }
    }
}
