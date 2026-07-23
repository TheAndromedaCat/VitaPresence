namespace PresenceCommon
{
    /// <summary>
    /// Represents a Discord Rich Presence activity to be sent via raw IPC.
    /// </summary>
    public class VitaActivity
    {
        /// <summary>
        /// Overrides the bold Discord application name shown in the presence card.
        /// Set to the game name when swap style is enabled.
        /// Leave null/empty to show the registered Discord app name (e.g. "PS Vita").
        /// </summary>
        public string Name;

        /// <summary>First line of text beneath the app name.</summary>
        public string Details;

        /// <summary>Second line of text beneath Details.</summary>
        public string State;

        /// <summary>Unix timestamp (seconds) for the elapsed timer. Null = no timer.</summary>
        public long? TimestampStart;

        /// <summary>Cover art URL or Discord asset key.</summary>
        public string LargeImageKey;

        /// <summary>Tooltip text shown on hover over the large image.</summary>
        public string LargeImageText;
    }
}
