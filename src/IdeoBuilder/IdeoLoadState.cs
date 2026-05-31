using System.Collections.Generic;
using System.IO;
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
    /// Keyboard-accessible state for the saved-ideoligion picker (Dialog_IdeoList_Load).
    /// A flat list of saved ideoligion files: Up/Down to navigate, typeahead to search by
    /// file name, Enter to load (delegates to the dialog's DoFileInteraction so vanilla's
    /// version/meme-compatibility checks still run), Delete to delete (with confirmation),
    /// Escape to close.
    /// </summary>
    public static class IdeoLoadState
    {
        public static bool IsActive { get; private set; }

        /// <summary>
        /// True when a typeahead search is currently filtering the list. Escape should clear the
        /// search rather than close the dialog, so IdeoLoadPatch_OnCancel blocks vanilla's cancel
        /// only in this case and otherwise lets the game close the dialog normally.
        /// </summary>
        public static bool HasActiveSearch => typeahead.HasActiveSearch;

        private static Dialog_IdeoList_Load dialog;
        private static List<SaveFileInfo> files = new List<SaveFileInfo>();
        private static int selectedIndex;
        private static TypeaheadSearchHelper typeahead = new TypeaheadSearchHelper();

        private static readonly System.Reflection.FieldInfo FilesField =
            AccessTools.Field(typeof(Dialog_FileList), "files");
        private static readonly System.Reflection.MethodInfo DoFileInteractionMethod =
            AccessTools.Method(typeof(Dialog_IdeoList_Load), "DoFileInteraction");
        private static readonly System.Reflection.MethodInfo ReloadFilesMethod =
            AccessTools.Method(typeof(Dialog_IdeoList), "ReloadFiles");

        public static void EnsureOpen(Dialog_IdeoList_Load d)
        {
            // Reference equality alone is the right guard. After Close sets
            // IsActive=false, the window-stack's snapshot iteration still calls
            // DoWindowContents one more time on the just-removed dialog in the
            // SAME frame. Keying off IsActive there would re-announce on every
            // close. Compare the dialog reference (which we deliberately keep
            // through Close) so the teardown re-render is a no-op.
            if (System.Object.ReferenceEquals(dialog, d))
                return;
            dialog = d;
            IsActive = true;
            selectedIndex = 0;
            typeahead.ClearSearch();
            RebuildFromDialog();
            AnnounceOpening();
        }

        public static void Close()
        {
            IsActive = false;
            files.Clear();
            typeahead.ClearSearch();
            // Keep `dialog` set so EnsureOpen's reference check can detect the
            // post-close snapshot re-render (see EnsureOpen). The reference is
            // replaced naturally when a new load dialog opens.
        }

        private static void RebuildFromDialog()
        {
            files = (FilesField.GetValue(dialog) as List<SaveFileInfo>) ?? new List<SaveFileInfo>();
            if (selectedIndex >= files.Count)
                selectedIndex = System.Math.Max(0, files.Count - 1);
        }

        private static List<string> Labels() =>
            files.Select(f => Path.GetFileNameWithoutExtension(f.FileName)).ToList();

        #region Input

        public static bool HandleInput(Event ev)
        {
            if (ev.type != EventType.KeyDown) return false;

            KeyCode key = ev.keyCode;
            bool alt = KeyboardHelper.IsAltHeld;
            bool ctrl = ev.control;

            if (key == KeyCode.Escape && !alt && !ctrl)
            {
                if (typeahead.HasActiveSearch) { typeahead.ClearSearchAndAnnounce(); AnnounceCurrent(); return true; }
                dialog.Close(doCloseSound: false);
                return true;
            }

            if (files.Count == 0)
                return true;

            if (key == KeyCode.UpArrow) { Move(-1); return true; }
            if (key == KeyCode.DownArrow) { Move(1); return true; }
            if (key == KeyCode.Home)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches) selectedIndex = typeahead.GetFirstMatch();
                else { typeahead.ClearSearch(); selectedIndex = 0; }
                AnnounceCurrent();
                return true;
            }
            if (key == KeyCode.End)
            {
                if (typeahead.HasActiveSearch && !typeahead.HasNoMatches) selectedIndex = typeahead.GetLastMatch();
                else { typeahead.ClearSearch(); selectedIndex = files.Count - 1; }
                AnnounceCurrent();
                return true;
            }

            if (key == KeyCode.Return || key == KeyCode.KeypadEnter)
            {
                LoadSelected();
                return true;
            }

            if (key == KeyCode.Delete)
            {
                DeleteSelected();
                return true;
            }

            if (key == KeyCode.Backspace)
            {
                if (typeahead.HasActiveSearch && typeahead.ProcessBackspace(Labels(), out int ni))
                {
                    if (ni >= 0) selectedIndex = ni;
                    AnnounceCurrent();
                }
                return true;
            }

            char c = ev.character;
            if (!alt && !ctrl && c != '\0' && char.IsLetterOrDigit(c))
            {
                if (typeahead.ProcessCharacterInput(c, Labels(), out int ni))
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
            if (typeahead.HasActiveSearch && !typeahead.HasNoMatches)
                selectedIndex = delta > 0
                    ? typeahead.GetNextMatch(selectedIndex)
                    : typeahead.GetPreviousMatch(selectedIndex);
            else
                selectedIndex = delta > 0
                    ? MenuHelper.SelectNext(selectedIndex, files.Count)
                    : MenuHelper.SelectPrevious(selectedIndex, files.Count);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrent();
        }

        private static void LoadSelected()
        {
            if (selectedIndex < 0 || selectedIndex >= files.Count) return;
            string fileName = Path.GetFileNameWithoutExtension(files[selectedIndex].FileName);
            DoFileInteractionMethod.Invoke(dialog, new object[] { fileName });
        }

        private static void DeleteSelected()
        {
            if (selectedIndex < 0 || selectedIndex >= files.Count) return;
            var fileInfo = files[selectedIndex].FileInfo;
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "ConfirmDelete".Translate(fileInfo.Name),
                delegate
                {
                    fileInfo.Delete();
                    ReloadFilesMethod.Invoke(dialog, null);
                    RebuildFromDialog();
                    TolkHelper.Speak($"{fileInfo.Name}, removed");
                    AnnounceCurrent();
                },
                destructive: true));
        }

        #endregion

        #region Announcements

        private static void AnnounceOpening()
        {
            var sb = new StringBuilder();
            sb.Append("LoadGameButton".Translate());
            if (files.Count > 0)
            {
                sb.Append(". ").Append(files.Count);
                sb.Append(". ").Append(BuildCurrentText());
            }
            else
            {
                sb.Append(". ").Append("NoneLower".Translate());
            }
            TolkHelper.Speak(sb.ToString(), SpeechPriority.High);
        }

        private static void AnnounceCurrent()
        {
            string text = BuildCurrentText();
            if (!string.IsNullOrEmpty(text))
                TolkHelper.Speak(text);
        }

        private static string BuildCurrentText()
        {
            if (files.Count == 0) return "NoneLower".Translate();
            if (selectedIndex < 0 || selectedIndex >= files.Count) selectedIndex = 0;

            var file = files[selectedIndex];
            var sb = new StringBuilder();
            sb.Append(Path.GetFileNameWithoutExtension(file.FileName));
            if (!file.GameVersion.NullOrEmpty())
                sb.Append(". ").Append(file.GameVersion);

            string position = MenuHelper.FormatPosition(selectedIndex, files.Count);
            if (!string.IsNullOrEmpty(position))
                sb.Append(". ").Append(position);
            return sb.ToString();
        }

        #endregion
    }
}
