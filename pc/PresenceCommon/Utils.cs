using PresenceCommon.Types;
using System;

namespace PresenceCommon
{
    public static class Utils
    {
        /// <summary>
        /// Builds a VitaActivity from the current Vita title.
        ///
        /// swapPresenceStyle = false (default):
        ///   Name    = null  → Discord shows registered app name ("PS Vita")
        ///   Details = game name
        ///   State   = custom state text
        ///
        /// swapPresenceStyle = true:
        ///   Name    = game name  → Discord shows game name as bold title
        ///   Details = "on PS Vita"
        ///   State   = null
        /// </summary>
        public static VitaActivity CreateDiscordPresence(
            Title title,
            long? timestampStart,
            string state            = "",
            string steamGridDbApiKey = null,
            string clientId          = null,
            bool swapPresenceStyle   = false,
            bool showTimer           = true)
        {
            string titleId   = title.Index == 0 ? "mainmenu" : title.TitleID;
            string titleName = title.Index == 0 ? "LiveArea"  : title.TitleName;

            var coverResult = CoverResolver.ResolveCoverImageUrl(
                titleId,
                titleName,
                "psv",
                steamGridDbApiKey,
                clientId);

            var activity = new VitaActivity
            {
                LargeImageKey  = coverResult.Item1,
                LargeImageText = titleName,
                TimestampStart = showTimer ? timestampStart : null
            };

            if (title.Index == 0)
            {
                // LiveArea — swap style doesn't apply
                activity.Name    = null;
                activity.Details = "In the LiveArea";
                activity.State   = state;
            }
            else if (swapPresenceStyle)
            {
                // Game name becomes the bold title; "on PS Vita" is the subtitle
                activity.Name    = titleName;
                activity.Details = "on PS Vita";
                activity.State   = null;
            }
            else
            {
                // Default: registered app name stays bold, game name is Details
                activity.Name    = null;
                activity.Details = titleName;
                activity.State   = state;
            }

            return activity;
        }

    }
}

