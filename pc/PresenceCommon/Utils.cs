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
            bool isPstv = (title.Magic == 0xCAFECAFF);
            string deviceName = isPstv ? "PlayStation TV" : "PlayStation Vita";
            string onDevice   = isPstv ? "on PlayStation TV" : "on PS Vita";

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
                if (swapPresenceStyle)
                {
                    activity.Name    = "LiveArea";
                    activity.Details = onDevice;
                }
                else
                {
                    activity.Name    = deviceName;
                    activity.Details = "In the LiveArea";
                }
                activity.State = state;
            }
            else if (swapPresenceStyle)
            {
                // Game name becomes the bold title; "on PlayStation TV" or "on PS Vita" is the subtitle
                activity.Name    = titleName;
                activity.Details = onDevice;
                activity.State   = string.IsNullOrWhiteSpace(state) ? null : state;
            }
            else
            {
                // Default: "PlayStation TV" or "PlayStation Vita" is the bold title, game name is Details
                activity.Name    = deviceName;
                activity.Details = titleName;
                activity.State   = state;
            }

            return activity;
        }

    }
}

