using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// A single "What's New" announcement: a version and the pointer to its localized message.
    /// The message text itself lives in Keyed XML at RimWorldAccess.WhatsNew.Message.&lt;version&gt;,
    /// with dots replaced by underscores (e.g. "2.0.0" -> RimWorldAccess.WhatsNew.Message.2_0_0).
    /// </summary>
    public class Announcement
    {
        public string Version { get; }

        /// <summary>Read-tracking token; defaults to Version. A distinct value re-issues an announcement whose Version installs already recorded.</summary>
        public string ReadKey { get; }

        private readonly string messageId;

        public Announcement(string version, string readKey = null, string messageId = null)
        {
            Version = version;
            ReadKey = readKey ?? version;
            this.messageId = messageId;
        }

        /// <summary>The Keyed message key for this announcement's body text.</summary>
        public string MessageKey => "RimWorldAccess.WhatsNew.Message." + (messageId ?? Version.Replace('.', '_'));
    }

    /// <summary>
    /// The ordered record of What's New announcements, OLDEST FIRST. Each entry must have an authored
    /// message in every language's Keyed XML (RimWorldAccess.WhatsNew.Message.&lt;version&gt;).
    ///
    /// Oldest-first so a player who is behind reads what they missed in chronological order and ends
    /// on the newest announcement. The reader opens on the oldest unread entry and "Jump to next
    /// announcement" walks forward in time.
    ///
    /// To publish a new announcement when cutting a release: bump the version everywhere (see
    /// <see cref="RimWorldAccessVersion"/>), add the message to RimWorldAccess_WhatsNew.xml (and
    /// translate it in the other language folders), and APPEND an <see cref="Announcement"/> to the
    /// end of this list (the newest entry is always last).
    ///
    /// An announcement is "unread" for a player when its version is not in
    /// <see cref="RimWorldAccessSettings.ReadAnnouncementVersions"/>. Unread announcements drive the
    /// on-update popup and the "jump to next announcement" navigation.
    /// </summary>
    public static class WhatsNewCatalog
    {
        public static readonly IReadOnlyList<Announcement> Announcements = new List<Announcement>
        {
            // OLDEST FIRST — newest is always last. Append a new entry when cutting a release.
            // Distinct read key: the beta recorded a placeholder under "2.0.0", so this first real
            // announcement uses an unrecorded token to still surface on those installs.
            new Announcement("2.0.0", readKey: "2.0.0-launch"),
            // Shipped without a version bump: the label is display-only and messageId names its key.
            new Announcement("2.0 RC 2", readKey: "2.0-rc2", messageId: "2_0_rc2"),
            new Announcement("2.0 RC 3", readKey: "2.0-rc3", messageId: "2_0_rc3"),
            new Announcement("2.0 RC 4", readKey: "2.0-rc4", messageId: "2_0_rc4"),
        };
    }
}
