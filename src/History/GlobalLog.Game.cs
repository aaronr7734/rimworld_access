using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Which vanilla log an entry was written to: the play log is the log tab's Social, the battle log its Combat.</summary>
    public enum GlobalLogKind
    {
        Social,
        Combat,
    }

    [Flags]
    public enum GlobalLogOwners
    {
        None = 0,
        Colonists = 1,
        ColonyAnimals = 2,
        Others = 4,
    }

    /// <summary>
    /// One global-log entry: the vanilla <see cref="LogEntry"/> while this session holds it, plus
    /// the rendered text, the pawns it concerns and its compact-view flag. Everything but the
    /// entry itself persists, so a reload needs no re-render and no deep-saved entry.
    ///
    /// Rendered from the POV of the entry's first concerned pawn (its initiator): vanilla
    /// interaction entries refuse any POV that is not one of their pawns, and the initiator's own
    /// log tab is the wording a sighted player reads. A discarded pawn is dropped from the record
    /// after its text is rendered, so the history never pins or renders against one.
    /// </summary>
    public sealed class GlobalLogRecord : IExposable
    {
        private static readonly Pawn[] NoPawns = new Pawn[0];

        public int LogId;
        public int TicksAbs;
        public GlobalLogKind Kind;
        public LogEntry Entry;

        private Pawn[] pawns = NoPawns;
        private string text;
        private GlobalLogOwners removedOwners;
        private bool compact = true;

        public GlobalLogRecord()
        {
        }

        internal GlobalLogRecord(LogEntry entry, GlobalLogKind kind)
        {
            Entry = entry;
            LogId = entry.LogID;
            TicksAbs = entry.Tick;
            Kind = kind;
            pawns = CollectPawns(entry);
            // Deterministic: every override seeds on the log id and construction-time fields.
            compact = entry.ShowInCompactView();
        }

        /// <summary>The distinct pawns the entry concerns, initiator first.</summary>
        public IReadOnlyList<Pawn> Pawns => pawns;

        public bool HasText => text != null;

        /// <summary>The entry rendered to nothing (a reference lost across a save), so the screen hides it.</summary>
        public bool Broken => text != null && text.Length == 0;

        public string Text
        {
            get
            {
                if (text == null && Entry != null)
                {
                    text = Render();
                    if (text.Length == 0)
                    {
                        GlobalLog.NotifyRenderFailed();
                    }
                }
                return text ?? "";
            }
        }

        /// <summary>Live from the pawns still held, plus the groups of any pawn discarded since.</summary>
        public GlobalLogOwners Owners
        {
            get
            {
                GlobalLogOwners owners = removedOwners;
                for (int i = 0; i < pawns.Length; i++)
                {
                    owners |= OwnerOf(pawns[i]);
                }
                return owners == GlobalLogOwners.None ? GlobalLogOwners.Others : owners;
            }
        }

        /// <summary>The log tab's compact-view gate; false only for the minor combat lines its "Show all" reveals.</summary>
        public bool ShowInCompactView => compact;

        internal bool Concerns(Pawn pawn)
        {
            return Array.IndexOf(pawns, pawn) >= 0;
        }

        internal void ForgetText()
        {
            text = null;
        }

        /// <summary>Renders while the pawn is intact, then lets go of it and of the entry vanilla is about to drop.</summary>
        internal void DropPawn(Pawn pawn)
        {
            _ = Text;
            removedOwners |= OwnerOf(pawn);
            var kept = new List<Pawn>(pawns.Length);
            for (int i = 0; i < pawns.Length; i++)
            {
                if (pawns[i] != pawn)
                {
                    kept.Add(pawns[i]);
                }
            }
            pawns = kept.ToArray();
            Entry = null;
        }

        private string Render()
        {
            try
            {
                return Entry.ToGameStringFromPOV(pawns.Length > 0 ? pawns[0] : null).StripTags();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("GlobalLogRecord.Render " + Entry.GetType().Name, ex);
                return "";
            }
        }

        private static GlobalLogOwners OwnerOf(Pawn pawn)
        {
            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
            {
                return GlobalLogOwners.Others;
            }
            return pawn.RaceProps.Humanlike ? GlobalLogOwners.Colonists : GlobalLogOwners.ColonyAnimals;
        }

        private static Pawn[] CollectPawns(LogEntry entry)
        {
            List<Pawn> found = null;
            foreach (Thing concern in entry.GetConcerns())
            {
                if (concern is Pawn pawn && (found == null || !found.Contains(pawn)))
                {
                    (found ?? (found = new List<Pawn>(2))).Add(pawn);
                }
            }
            return found == null ? NoPawns : found.ToArray();
        }

        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                _ = Text;
            }
            Scribe_Values.Look(ref LogId, "logId");
            Scribe_Values.Look(ref TicksAbs, "ticksAbs");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref text, "text");
            Scribe_Values.Look(ref removedOwners, "removedOwners");
            Scribe_Values.Look(ref compact, "compact", true);
            List<Pawn> pawnList = Scribe.mode == LoadSaveMode.Saving ? new List<Pawn>(pawns) : null;
            Scribe_Collections.Look(ref pawnList, "pawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
            {
                pawnList?.RemoveAll(p => p == null);
                pawns = pawnList != null && pawnList.Count > 0 ? pawnList.ToArray() : NoPawns;
            }
        }
    }

    /// <summary>
    /// The world-wide activity log behind the Global Log screen: every entry vanilla writes to its
    /// play and battle logs, kept past vanilla's own caps (150 play-log lines, 20 battles).
    ///
    /// Footprint: capture is a postfix appending a reference, with no text work. Rendering is
    /// spread over frames by <see cref="GameComponentUpdate"/> under a budget scaled to the
    /// frame time, so a slow machine renders fewer per frame rather than hitching, and the
    /// screen, a search, a pawn discard or a save rarely finds anything left to render.
    /// </summary>
    public sealed class GlobalLog : GameComponent
    {
        internal const int Capacity = 3000;
        private const int TrimSlack = 128;

        private List<GlobalLogRecord> records = new List<GlobalLogRecord>();
        private readonly HashSet<int> ids = new HashSet<int>();
        private readonly Dictionary<Pawn, int> concernCounts = new Dictionary<Pawn, int>();
        private readonly Stopwatch renderWatch = new Stopwatch();
        private int renderedCount;
        private string textLanguage;

        /// <summary>The entry each log's cursor last rested on, so reopening or switching back returns to it.</summary>
        internal int LastFocusedSocialLogId = -1;
        internal int LastFocusedCombatLogId = -1;

        /// <summary>Bumped on every change to any record set, so an open screen rebuilds only when something moved.</summary>
        public static int Version { get; private set; }

        public GlobalLog(Game game)
        {
            Version++;
        }

        internal static GlobalLog Instance => Current.Game?.GetComponent<GlobalLog>();

        /// <summary>Oldest first.</summary>
        internal IReadOnlyList<GlobalLogRecord> Records => records;

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                textLanguage = LanguageDatabase.activeLanguage?.folderName;
            }
            Scribe_Values.Look(ref textLanguage, "textLanguage");
            Scribe_Values.Look(ref LastFocusedSocialLogId, "lastFocusedSocialLogId", -1);
            Scribe_Values.Look(ref LastFocusedCombatLogId, "lastFocusedCombatLogId", -1);
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            if (records == null)
            {
                records = new List<GlobalLogRecord>();
            }
        }

        /// <summary>Relinks saved records to vanilla's loaded entries, then adopts every vanilla entry not yet held. Runs for new and loaded games alike.</summary>
        public override void FinalizeInit()
        {
            var vanilla = new Dictionary<int, KeyValuePair<LogEntry, GlobalLogKind>>();
            foreach (KeyValuePair<LogEntry, GlobalLogKind> pair in VanillaEntries())
            {
                vanilla[pair.Key.LogID] = pair;
            }
            // Text saved under another language re-renders wherever the entry still exists.
            bool languageChanged = textLanguage != null && textLanguage != LanguageDatabase.activeLanguage?.folderName;

            ids.Clear();
            records.RemoveAll(delegate (GlobalLogRecord r)
            {
                if (vanilla.TryGetValue(r.LogId, out KeyValuePair<LogEntry, GlobalLogKind> pair))
                {
                    r.Entry = pair.Key;
                    if (languageChanged)
                    {
                        r.ForgetText();
                    }
                }
                return (r.Entry == null && !r.HasText) || !ids.Add(r.LogId);
            });
            foreach (KeyValuePair<LogEntry, GlobalLogKind> pair in vanilla.Values)
            {
                if (ids.Add(pair.Key.LogID))
                {
                    records.Add(new GlobalLogRecord(pair.Key, pair.Value));
                }
            }
            records.Sort((a, b) => a.TicksAbs != b.TicksAbs ? a.TicksAbs.CompareTo(b.TicksAbs) : a.LogId.CompareTo(b.LogId));
            if (records.Count > Capacity)
            {
                RemoveOldest(records.Count - Capacity);
            }
            concernCounts.Clear();
            foreach (GlobalLogRecord record in records)
            {
                CountConcerns(record, 1);
            }
            renderedCount = 0;
            Version++;
        }

        public override void GameComponentUpdate()
        {
            if (renderedCount >= records.Count)
            {
                return;
            }
            double budgetMs = Mathf.Clamp(Time.unscaledDeltaTime * 50f, 0.5f, 3f);
            renderWatch.Restart();
            while (renderedCount < records.Count && renderWatch.Elapsed.TotalMilliseconds < budgetMs)
            {
                _ = records[renderedCount++].Text;
            }
        }

        internal static void Capture(LogEntry entry, GlobalLogKind kind)
        {
            GlobalLog log = Instance;
            if (log == null || entry == null || !log.ids.Add(entry.LogID))
            {
                return;
            }
            var record = new GlobalLogRecord(entry, kind);
            log.records.Add(record);
            log.CountConcerns(record, 1);
            if (log.records.Count > Capacity + TrimSlack)
            {
                log.RemoveOldest(log.records.Count - Capacity);
            }
            Version++;
        }

        internal static void NotifyRenderFailed()
        {
            Version++;
        }

        internal static void NotifyPawnDiscarded(Pawn pawn)
        {
            GlobalLog log = Instance;
            if (log == null || pawn == null || !log.concernCounts.ContainsKey(pawn))
            {
                return;
            }
            for (int i = log.records.Count - 1; i >= 0; i--)
            {
                GlobalLogRecord record = log.records[i];
                if (!record.Concerns(pawn))
                {
                    continue;
                }
                record.DropPawn(pawn);
                if (record.Broken)
                {
                    log.RemoveAt(i);
                }
            }
            log.concernCounts.Remove(pawn);
            Version++;
        }

        private void RemoveOldest(int count)
        {
            for (int i = 0; i < count; i++)
            {
                ids.Remove(records[i].LogId);
                CountConcerns(records[i], -1);
            }
            records.RemoveRange(0, count);
            renderedCount = Math.Max(0, renderedCount - count);
        }

        private void RemoveAt(int index)
        {
            ids.Remove(records[index].LogId);
            CountConcerns(records[index], -1);
            records.RemoveAt(index);
            if (index < renderedCount)
            {
                renderedCount--;
            }
        }

        private void CountConcerns(GlobalLogRecord record, int delta)
        {
            IReadOnlyList<Pawn> pawns = record.Pawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                concernCounts.TryGetValue(pawns[i], out int count);
                count += delta;
                if (count > 0)
                {
                    concernCounts[pawns[i]] = count;
                }
                else
                {
                    concernCounts.Remove(pawns[i]);
                }
            }
        }

        private static IEnumerable<KeyValuePair<LogEntry, GlobalLogKind>> VanillaEntries()
        {
            if (Find.PlayLog != null)
            {
                foreach (LogEntry entry in Find.PlayLog.AllEntries)
                {
                    yield return new KeyValuePair<LogEntry, GlobalLogKind>(entry, GlobalLogKind.Social);
                }
            }
            if (Find.BattleLog != null)
            {
                foreach (Battle battle in Find.BattleLog.Battles)
                {
                    foreach (LogEntry entry in battle.Entries)
                    {
                        yield return new KeyValuePair<LogEntry, GlobalLogKind>(entry, GlobalLogKind.Combat);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Add))]
    internal static class GlobalLogPlayLogAddPatch
    {
        private static void Postfix(LogEntry entry)
        {
            GlobalLog.Capture(entry, GlobalLogKind.Social);
        }
    }

    [HarmonyPatch(typeof(BattleLog), nameof(BattleLog.Add))]
    internal static class GlobalLogBattleLogAddPatch
    {
        private static void Postfix(LogEntry entry)
        {
            GlobalLog.Capture(entry, GlobalLogKind.Combat);
        }
    }

    /// <summary>Pawn.Discard notifies the play log first and the battle log right after, so a prefix here runs while every record's pawns are still intact.</summary>
    [HarmonyPatch(typeof(PlayLog), nameof(PlayLog.Notify_PawnDiscarded))]
    internal static class GlobalLogPawnDiscardedPatch
    {
        private static void Prefix(Pawn p)
        {
            GlobalLog.NotifyPawnDiscarded(p);
        }
    }
}
