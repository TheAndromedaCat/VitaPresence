using DiscordRPC;
using PresenceCommon.Types;

namespace PresenceCommon
{
    public static class Utils
    {
        public static RichPresence CreateDiscordPresence(
            Title title,
            Timestamps time,
            string state = "",
            string steamGridDbApiKey = null,
            string clientId = null)
        {
            RichPresence presence = new RichPresence()
            {
                State = state
            };

            string titleId = title.Index == 0 ? "mainmenu" : title.TitleID;
            string titleName = title.Index == 0 ? "LiveArea" : title.TitleName;

            var coverResult = CoverResolver.ResolveCoverImageUrl(
                titleId,
                titleName,
                "psv",
                steamGridDbApiKey,
                clientId
            );

            Assets assets = new Assets
            {
                LargeImageText = title.Index == 0 ? "LiveArea" : title.TitleName,
                LargeImageKey = coverResult.Item1
            };

            if (title.Index == 0)
            {
                presence.Details = "In the LiveArea";
            }
            else
            {
                presence.Details = title.TitleName;
            }

            presence.Assets = assets;
            presence.Timestamps = time;

            return presence;
        }
    }
}
