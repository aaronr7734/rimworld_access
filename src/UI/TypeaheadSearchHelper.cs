using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Helper class for typeahead search functionality in menus.
    /// Each menu creates its own instance to manage search state.
    /// </summary>
    public class TypeaheadSearchHelper
    {
        // Configuration
        private const float AUTO_RESET_SECONDS = 3.0f;

        // Word separators for prefix matching
        private static readonly char[] WordSeparators = { ' ', '-', '_', '(', ')', '[', ']', '/', '\\', '.', ',' };

        // State
        private string searchBuffer = "";
        private string lastFailedSearch = "";  // Stores the search that had no matches (for announcement)
        private float lastInputTime = 0f;
        private List<int> matchingIndices = new List<int>();
        private int currentMatchIndex = 0;

        /// <summary>
        /// Returns true if there is an active search (buffer not empty).
        /// </summary>
        public bool HasActiveSearch => !string.IsNullOrEmpty(searchBuffer);

        /// <summary>
        /// Returns true if there is an active search with no matches.
        /// </summary>
        public bool HasNoMatches => !string.IsNullOrEmpty(searchBuffer) && matchingIndices.Count == 0;

        /// <summary>
        /// Gets the current search string.
        /// </summary>
        public string SearchBuffer => searchBuffer;

        /// <summary>
        /// Gets the last search string that had no matches (for announcement after auto-clear).
        /// </summary>
        public string LastFailedSearch => lastFailedSearch;

        /// <summary>
        /// Gets the number of current matches.
        /// </summary>
        public int MatchCount => matchingIndices.Count;

        /// <summary>
        /// Gets the 1-based position within matches for announcements.
        /// </summary>
        public int CurrentMatchPosition => matchingIndices.Count > 0 ? currentMatchIndex + 1 : 0;

        /// <summary>
        /// Gets the list of matching indices.
        /// </summary>
        public List<int> MatchingIndices => matchingIndices;

        /// <summary>
        /// Processes a character input for typeahead search.
        /// </summary>
        /// <param name="c">The character typed</param>
        /// <param name="labels">List of item labels to search</param>
        /// <param name="newIndex">Output: the index to navigate to, or -1 if no matches</param>
        /// <returns>True if input was processed successfully with matches, false if no matches found</returns>
        public bool ProcessCharacterInput(char c, List<string> labels, out int newIndex)
        {
            newIndex = -1;

            float currentTime = Time.realtimeSinceStartup;

            // Check timeout FIRST with current time
            if (HasActiveSearch && currentTime - lastInputTime > AUTO_RESET_SECONDS)
            {
                ClearSearch();
            }

            // Update time immediately
            lastInputTime = currentTime;
            searchBuffer += c;

            // Find all matching items using two-pass approach:
            // Pass 1: Match against names only (ignore parenthetical content like descriptions)
            // Pass 2: If no matches, try matching full labels including parenthetical content
            FindMatches(labels);

            // If matches found, set newIndex to first match
            if (matchingIndices.Count > 0)
            {
                currentMatchIndex = 0;
                newIndex = matchingIndices[0];
                return true;
            }

            // No matches - store the failed search for announcement, then auto-clear
            lastFailedSearch = searchBuffer;
            ClearSearch();
            return false;
        }

        /// <summary>
        /// Processes backspace to remove the last character from search buffer.
        /// </summary>
        /// <param name="labels">List of item labels to search</param>
        /// <param name="newIndex">Output: the index to navigate to, or -1 if search cleared</param>
        /// <returns>True if backspace was handled</returns>
        public bool ProcessBackspace(List<string> labels, out int newIndex)
        {
            newIndex = -1;

            if (!HasActiveSearch)
            {
                return false;
            }

            // Remove last character
            searchBuffer = searchBuffer.Substring(0, searchBuffer.Length - 1);
            lastInputTime = Time.realtimeSinceStartup;

            // If buffer is now empty, clear search entirely
            if (string.IsNullOrEmpty(searchBuffer))
            {
                ClearSearch();
                return true;
            }

            // Re-filter matches using two-pass approach
            FindMatches(labels);

            // Update current match index and return first match
            if (matchingIndices.Count > 0)
            {
                currentMatchIndex = 0;
                newIndex = matchingIndices[0];
            }

            return true;
        }

        /// <summary>
        /// Clears the search state entirely.
        /// </summary>
        public void ClearSearch()
        {
            searchBuffer = "";
            matchingIndices.Clear();
            currentMatchIndex = 0;
        }

        /// <summary>
        /// Clears the search and announces "Search cleared" via TolkHelper.
        /// Returns true if there was an active search to clear, false otherwise.
        /// Use this when the user explicitly clears the search (e.g., pressing Escape).
        /// </summary>
        public bool ClearSearchAndAnnounce()
        {
            if (!HasActiveSearch)
                return false;

            ClearSearch();
            TolkHelper.Speak("Search cleared");
            return true;
        }

        /// <summary>
        /// Finds matching items using a two-pass approach:
        /// Pass 1: Match against names only (ignoring parenthetical content like descriptions)
        /// Pass 2: If no matches, try matching full labels including parenthetical content
        /// This prioritizes exact name matches while still allowing description searches as fallback.
        /// </summary>
        private void FindMatches(List<string> labels)
        {
            matchingIndices.Clear();

            // Pass 1: Try matching without parenthetical content (names only)
            for (int i = 0; i < labels.Count; i++)
            {
                if (MatchesWordPrefix(searchBuffer, labels[i], ignoreParenthetical: true))
                {
                    matchingIndices.Add(i);
                }
            }

            // If we found matches in pass 1, we're done
            if (matchingIndices.Count > 0)
                return;

            // Pass 2: No matches found - try matching full labels including descriptions
            for (int i = 0; i < labels.Count; i++)
            {
                if (MatchesWordPrefix(searchBuffer, labels[i], ignoreParenthetical: false))
                {
                    matchingIndices.Add(i);
                }
            }
        }

        /// <summary>
        /// Gets the next match after the current index, wrapping around if needed.
        /// </summary>
        /// <param name="currentIndex">The current selected index</param>
        /// <returns>The next matching index, or -1 if no matches</returns>
        public int GetNextMatch(int currentIndex)
        {
            if (matchingIndices.Count == 0)
            {
                return -1;
            }

            // Find current position in matches
            int pos = matchingIndices.IndexOf(currentIndex);

            if (pos >= 0)
            {
                // Move to next match, wrapping around
                currentMatchIndex = (pos + 1) % matchingIndices.Count;
            }
            else
            {
                // Current index not in matches, find next match after current index
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

        /// <summary>
        /// Gets the previous match before the current index, wrapping around if needed.
        /// </summary>
        /// <param name="currentIndex">The current selected index</param>
        /// <returns>The previous matching index, or -1 if no matches</returns>
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
                // Move to previous match, wrapping around
                currentMatchIndex = (pos - 1 + matchingIndices.Count) % matchingIndices.Count;
            }
            else
            {
                // Current index not in matches, find previous match before current index
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

        /// <summary>
        /// Checks if a search string matches any word prefix in a label.
        /// Case-insensitive matching.
        /// </summary>
        /// <param name="search">The search string</param>
        /// <param name="label">The label to match against</param>
        /// <param name="ignoreParenthetical">If true, strips content in parentheses before matching</param>
        /// <returns>True if the search matches a word prefix or the label start</returns>
        public static bool MatchesWordPrefix(string search, string label, bool ignoreParenthetical = false)
        {
            if (string.IsNullOrEmpty(search) || string.IsNullOrEmpty(label))
            {
                return false;
            }

            string searchLower = search.ToLowerInvariant();
            string labelLower = label.ToLowerInvariant().Trim();

            // Strip parenthetical content if requested
            if (ignoreParenthetical)
            {
                labelLower = StripParentheticalContent(labelLower);
            }

            // Check if entire label starts with search
            if (labelLower.StartsWith(searchLower))
            {
                return true;
            }

            // Split by word separators and check each word
            string[] words = labelLower.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
            foreach (string word in words)
            {
                if (word.StartsWith(searchLower))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Strips content inside parentheses from a string.
        /// Example: "Sleeping spot (description here)" -> "Sleeping spot"
        /// </summary>
        private static string StripParentheticalContent(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Remove all content between parentheses (including nested)
            var result = new System.Text.StringBuilder();
            int depth = 0;

            foreach (char c in text)
            {
                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth > 0) depth--;
                }
                else if (depth == 0)
                {
                    result.Append(c);
                }
            }

            return result.ToString().Trim();
        }
    }
}
