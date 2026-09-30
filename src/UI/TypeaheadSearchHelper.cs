using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Typeahead search state for a menu. Each menu owns its own instance.
    /// </summary>
    public class TypeaheadSearchHelper
    {
        private const float AUTO_RESET_SECONDS = 3.0f;

        /// <summary>
        /// When true the auto-reset timeout is ignored and the buffer persists until cleared. Set
        /// while an IME search session is open, where composing one character can outlast the
        /// timeout. Global, since every typeahead consumer shares this helper.
        /// </summary>
        public static bool SuppressAutoReset { get; set; }

        /// <summary>
        /// Admit mid-word matches, ranked below every word-prefix tier. Set by scopes whose row
        /// list is itself a substring filter's output, so the matcher reaches every drawn row.
        /// </summary>
        public bool SubstringFallback;

        public bool DocumentOrder;

        private string searchBuffer = "";
        private string lastFailedSearch = "";  // Stores the search that had no matches (for announcement)
        private float lastInputTime = 0f;
        private List<int> matchingIndices = new List<int>();
        private int currentMatchIndex = 0;

        /// <summary>True when the search buffer is not empty.</summary>
        public bool HasActiveSearch => !string.IsNullOrEmpty(searchBuffer);

        /// <summary>True when a search is active and nothing matches it.</summary>
        public bool HasNoMatches => !string.IsNullOrEmpty(searchBuffer) && matchingIndices.Count == 0;

        public string SearchBuffer => searchBuffer;

        /// <summary>The last search string that matched nothing, kept for announcement after auto-clear.</summary>
        public string LastFailedSearch => lastFailedSearch;

        public int MatchCount => matchingIndices.Count;

        /// <summary>1-based position within the matches, or 0 when there are none.</summary>
        public int CurrentMatchPosition => matchingIndices.Count > 0 ? currentMatchIndex + 1 : 0;

        public List<int> MatchingIndices => matchingIndices;

        /// <summary>
        /// Appends a typed character to the search and reports the index to navigate to, or -1
        /// when nothing matches (in which case the search auto-clears).
        /// </summary>
        public bool ProcessCharacterInput(char c, List<string> labels, out int newIndex)
        {
            return ProcessCharacterInput(c, labels, null, out newIndex);
        }

        /// <summary>
        /// As above, with per-candidate ranking metadata parallel to <paramref name="labels"/>.
        /// </summary>
        public bool ProcessCharacterInput(char c, List<string> labels,
            IReadOnlyList<Shell.TypeaheadCandidate> candidates, out int newIndex)
        {
            newIndex = -1;

            float currentTime = Time.realtimeSinceStartup;

            // Timeout is checked before the buffer grows, and skipped during an IME session where
            // slow composition between commits must not wipe the in-progress query.
            if (!SuppressAutoReset && HasActiveSearch && currentTime - lastInputTime > AUTO_RESET_SECONDS)
            {
                ClearSearch();
            }

            lastInputTime = currentTime;
            searchBuffer += c;

            FindMatches(labels, candidates);

            if (matchingIndices.Count > 0)
            {
                currentMatchIndex = 0;
                newIndex = matchingIndices[0];
                return true;
            }

            lastFailedSearch = searchBuffer;
            ClearSearch();
            return false;
        }

        /// <summary>
        /// Drops the last search character, reporting the index to navigate to or -1 when the
        /// search ends up cleared. Returns false when no search was active.
        /// </summary>
        public bool ProcessBackspace(List<string> labels, out int newIndex)
        {
            return ProcessBackspace(labels, null, out newIndex);
        }

        /// <summary>As above, with candidate-based ranking.</summary>
        public bool ProcessBackspace(List<string> labels,
            IReadOnlyList<Shell.TypeaheadCandidate> candidates, out int newIndex)
        {
            newIndex = -1;

            if (!HasActiveSearch)
            {
                return false;
            }

            searchBuffer = searchBuffer.Substring(0, searchBuffer.Length - 1);
            lastInputTime = Time.realtimeSinceStartup;

            if (string.IsNullOrEmpty(searchBuffer))
            {
                ClearSearch();
                return true;
            }

            FindMatches(labels, candidates);

            if (matchingIndices.Count > 0)
            {
                currentMatchIndex = 0;
                newIndex = matchingIndices[0];
            }

            return true;
        }

        public void ClearSearch()
        {
            searchBuffer = "";
            matchingIndices.Clear();
            currentMatchIndex = 0;
        }

        /// <summary>
        /// Clears the search and announces it. Returns false when no search was active.
        /// </summary>
        public bool ClearSearchAndAnnounce()
        {
            if (!HasActiveSearch)
                return false;

            ClearSearch();
            TolkHelper.Speak("RimWorldAccess.Search.Cleared".Loc());
            return true;
        }

        /// <summary>
        /// Announces the no-matches message for the last failed search. Only formats the
        /// announcement; the caller decides whether the input failed.
        /// </summary>
        public void SpeakNoMatches(SpeechPriority priority = SpeechPriority.Normal)
        {
            TolkHelper.Speak("RimWorldAccess.Search.NoMatches".Loc(lastFailedSearch), priority);
        }

        /// <summary>
        /// The match-position suffix for the current match, empty when no search is active. Its
        /// leading separator makes it safe to append straight onto a label.
        /// </summary>
        public string BuildSearchContextSuffix()
        {
            if (!HasActiveSearch || matchingIndices.Count == 0) return "";
            return "RimWorldAccess.Search.ContextSuffix".Translate(CurrentMatchPosition, MatchCount, searchBuffer).ToString();
        }

        /// <summary>The label with the match-position suffix appended when a search is active.</summary>
        public string BuildItemAnnouncement(string itemLabel)
        {
            return (itemLabel ?? "") + BuildSearchContextSuffix();
        }

        /// <summary>
        /// Speaks the item announcement, or the no-matches announcement when nothing matched.
        /// </summary>
        public void SpeakItemAnnouncement(string itemLabel, SpeechPriority priority = SpeechPriority.Normal)
        {
            if (HasNoMatches || (HasActiveSearch && matchingIndices.Count == 0))
            {
                SpeakNoMatches(priority);
                return;
            }
            TolkHelper.SpeakData(BuildItemAnnouncement(itemLabel), priority);
        }

        /// <summary>
        /// Delegates to <see cref="Shell.TypeaheadMatcher"/>, the one matching algorithm shared
        /// with the shell's TypeaheadModel.
        /// </summary>
        private void FindMatches(List<string> labels, IReadOnlyList<Shell.TypeaheadCandidate> candidates)
        {
            matchingIndices.Clear();
            matchingIndices.AddRange(
                Shell.TypeaheadMatcher.FindMatches(searchBuffer, labels, candidates, SubstringFallback, DocumentOrder));
        }

        /// <summary>The next match after the given index, wrapping; -1 when there are none.</summary>
        public int GetNextMatch(int currentIndex)
        {
            if (matchingIndices.Count == 0)
            {
                return -1;
            }

            int pos = matchingIndices.IndexOf(currentIndex);

            if (pos >= 0)
            {
                currentMatchIndex = (pos + 1) % matchingIndices.Count;
            }
            else
            {
                currentMatchIndex = 0;
                for (int i = 0; i < matchingIndices.Count; i++)
                {
                    if (matchingIndices[i] > currentIndex)
                    {
                        currentMatchIndex = i;
                        break;
                    }
                }
            }

            return matchingIndices[currentMatchIndex];
        }

        /// <summary>The previous match before the given index, wrapping; -1 when there are none.</summary>
        public int GetPreviousMatch(int currentIndex)
        {
            if (matchingIndices.Count == 0)
            {
                return -1;
            }

            // Find current position in matches
            int pos = matchingIndices.IndexOf(currentIndex);

            if (pos >= 0)
            {
                currentMatchIndex = (pos - 1 + matchingIndices.Count) % matchingIndices.Count;
            }
            else
            {
                currentMatchIndex = matchingIndices.Count - 1;
                for (int i = matchingIndices.Count - 1; i >= 0; i--)
                {
                    if (matchingIndices[i] < currentIndex)
                    {
                        currentMatchIndex = i;
                        break;
                    }
                }
            }

            return matchingIndices[currentMatchIndex];
        }

        /// <summary>The first match, updating the tracked position; -1 when there are none.</summary>
        public int GetFirstMatch()
        {
            if (matchingIndices.Count == 0)
            {
                return -1;
            }
            currentMatchIndex = 0;
            return matchingIndices[0];
        }

        /// <summary>The last match, updating the tracked position; -1 when there are none.</summary>
        public int GetLastMatch()
        {
            if (matchingIndices.Count == 0)
            {
                return -1;
            }
            currentMatchIndex = matchingIndices.Count - 1;
            return matchingIndices[currentMatchIndex];
        }

    }
}
