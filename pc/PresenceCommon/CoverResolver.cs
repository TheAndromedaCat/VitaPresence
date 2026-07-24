using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PresenceCommon
{
    public static class CoverResolver
    {
        public const string DEFAULT_CLIENT_ID = "1354524447746293901";
        public const string DEFAULT_FALLBACK_KEY = "vita";

        // Cache for Discord Developer Application asset mappings (client_id -> { asset_name: asset_id })
        private static readonly ConcurrentDictionary<string, Dictionary<string, string>> DiscordAssetsCache =
            new ConcurrentDictionary<string, Dictionary<string, string>>();

        // Cache for resolved cover image results (cacheKey -> Tuple<urlOrKey, sourceDescription>)
        private static readonly ConcurrentDictionary<string, Tuple<string, string>> ResolvedCoversCache =
            new ConcurrentDictionary<string, Tuple<string, string>>();

        private static readonly JavaScriptSerializer JsonSerializer = new JavaScriptSerializer();

        /// <summary>
        /// Queries Discord Developer API to obtain the direct CDN image URL for a given asset key.
        /// </summary>
        public static string FetchDiscordAssetUrl(string clientId, string assetKey, string defaultFallbackKey = DEFAULT_FALLBACK_KEY)
        {
            string clientIdStr = string.IsNullOrWhiteSpace(clientId) ? DEFAULT_CLIENT_ID : clientId.Trim();
            string keyClean = string.IsNullOrWhiteSpace(assetKey) ? DEFAULT_FALLBACK_KEY : assetKey.Trim().ToLower();

            if (!DiscordAssetsCache.TryGetValue(clientIdStr, out var assetMap))
            {
                assetMap = new Dictionary<string, string>();
                try
                {
                    string url = $"https://discord.com/api/v9/oauth2/applications/{clientIdStr}/assets";
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.UserAgent = "VitaPresence/1.0";
                    request.Timeout = 6000;
                    request.Method = "GET";

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                            {
                                string json = reader.ReadToEnd();
                                object itemsObj = JsonSerializer.DeserializeObject(json);
                                if (itemsObj is IList items)
                                {
                                    foreach (var itemObj in items)
                                    {
                                        if (itemObj is Dictionary<string, object> dict &&
                                            dict.TryGetValue("name", out object nameObj) &&
                                            dict.TryGetValue("id", out object idObj))
                                        {
                                            string name = nameObj?.ToString().ToLower();
                                            string id = idObj?.ToString();
                                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
                                            {
                                                assetMap[name] = id;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback to empty map on error
                }
                DiscordAssetsCache[clientIdStr] = assetMap;
            }

            string targetKey = defaultFallbackKey.ToLower();
            if (assetMap.TryGetValue(keyClean, out string assetId))
            {
                return $"https://cdn.discordapp.com/app-assets/{clientIdStr}/{assetId}.png";
            }
            if (assetMap.TryGetValue(targetKey, out string fallbackId))
            {
                return $"https://cdn.discordapp.com/app-assets/{clientIdStr}/{fallbackId}.png";
            }

            return null;
        }

        /// <summary>
        /// Sanitizes human-readable game title for search queries (removes Vita edition suffixes, trademarks, etc.).
        /// </summary>
        private static string SanitizeGameTitle(string rawTitle)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return "";
            string cleaned = rawTitle;
            cleaned = Regex.Replace(cleaned, @"(?i):?\s*playstation\s*®?\s*vita\s*edition", "");
            cleaned = Regex.Replace(cleaned, @"(?i):?\s*vita\s*edition", "");
            cleaned = cleaned.Replace("®", "").Replace("™", "").Replace("©", "");
            return cleaned.Trim();
        }

        /// <summary>
        /// Helper to execute SteamGridDB grid fetch request.
        /// When squareOnly is true, prioritizes 1:1 aspect ratio square grids (1024x1024, 512x512).
        /// </summary>
        private static string FetchSteamGridDbGridUrl(string gameId, string steamGridDbApiKey, bool squareOnly)
        {
            try
            {
                string gridUrl = squareOnly
                    ? $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=1024x1024,512x512"
                    : $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}";

                HttpWebRequest gReq = (HttpWebRequest)WebRequest.Create(gridUrl);
                gReq.Headers["Authorization"] = $"Bearer {steamGridDbApiKey}";
                gReq.UserAgent = "VitaPresence/1.0";
                gReq.Timeout = 5000;
                gReq.Method = "GET";

                using (HttpWebResponse gRes = (HttpWebResponse)gReq.GetResponse())
                {
                    if (gRes.StatusCode == HttpStatusCode.OK)
                    {
                        using (StreamReader gReader = new StreamReader(gRes.GetResponseStream()))
                        {
                            string gJson = gReader.ReadToEnd();
                            var gData = JsonSerializer.Deserialize<Dictionary<string, object>>(gJson);
                            if (gData != null && gData.TryGetValue("data", out object gDataObj) && gDataObj is IList gList && gList.Count > 0)
                            {
                                foreach (var item in gList)
                                {
                                    if (item is Dictionary<string, object> grid && grid.TryGetValue("url", out object urlObj))
                                    {
                                        string urlStr = urlObj?.ToString();
                                        if (string.IsNullOrEmpty(urlStr)) continue;

                                        if (squareOnly)
                                        {
                                            int width = 0, height = 0;
                                            if (grid.TryGetValue("width", out object wObj)) int.TryParse(wObj?.ToString(), out width);
                                            if (grid.TryGetValue("height", out object hObj)) int.TryParse(hObj?.ToString(), out height);

                                            if (width > 0 && width == height)
                                            {
                                                return urlStr;
                                            }
                                        }
                                        else
                                        {
                                            return urlStr;
                                        }
                                    }
                                }

                                // Fallback for squareOnly if dimension query returned items
                                if (squareOnly && gList[0] is Dictionary<string, object> firstItem && firstItem.TryGetValue("url", out object fallbackUrl))
                                {
                                    return fallbackUrl?.ToString();
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Return null on failure
            }
            return null;
        }

        private static readonly Dictionary<string, string> VitaCodeAssetBypassMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool vitaCodesLoaded = false;
        private static readonly object vitaCodesLock = new object();

        public static void EnsureVitaCodesLoaded()
        {
            if (vitaCodesLoaded) return;
            lock (vitaCodesLock)
            {
                if (vitaCodesLoaded) return;

                // Built-in defaults matching vita-codes.txt
                VitaCodeAssetBypassMap["NPXS10007"] = "welc";
                VitaCodeAssetBypassMap["NPXS10002"] = "psstore";
                VitaCodeAssetBypassMap["NPXS10003"] = "www";
                VitaCodeAssetBypassMap["NPXS10026"] = "cman";
                VitaCodeAssetBypassMap["NPXS10006"] = "friend";
                VitaCodeAssetBypassMap["NPXS10072"] = "email";
                VitaCodeAssetBypassMap["NPXS10009"] = "music";
                VitaCodeAssetBypassMap["NPXS10013"] = "ps4";
                VitaCodeAssetBypassMap["NPXS10001"] = "party";
                VitaCodeAssetBypassMap["NPXS10000"] = "near";
                VitaCodeAssetBypassMap["NPXS10010"] = "videos";
                VitaCodeAssetBypassMap["NPXS10094"] = "parent";
                VitaCodeAssetBypassMap["NPXS10012"] = "ps4";
                VitaCodeAssetBypassMap["NPXS10091"] = "calendar";
                VitaCodeAssetBypassMap["NPXS10014"] = "messages";
                VitaCodeAssetBypassMap["NPXS10004"] = "photo";
                VitaCodeAssetBypassMap["NPXS10015"] = "settings";
                VitaCodeAssetBypassMap["NPXS10008"] = "trophy";
                VitaCodeAssetBypassMap["mainmenu"]   = "mainmenu";

                // Load runtime overrides from vita-codes.txt if present
                string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vita-codes.txt");
                if (File.Exists(localFile))
                {
                    try
                    {
                        foreach (var rawLine in File.ReadAllLines(localFile))
                        {
                            string line = rawLine?.Trim();
                            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith("//")) continue;

                            string[] parts = line.Split(new char[] { '-', ':', '=' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                string assetName = parts[0].Trim().ToLower();
                                string tId = parts[1].Trim().ToUpper();
                                if (!string.IsNullOrEmpty(assetName) && !string.IsNullOrEmpty(tId))
                                {
                                    VitaCodeAssetBypassMap[tId] = assetName;
                                }
                            }
                        }
                    }
                    catch { }
                }

                vitaCodesLoaded = true;
            }
        }

        /// <summary>
        /// Multi-tiered cover resolution pipeline according to COVER_IMAGES_FLOW.md.
        /// Returns a tuple: (image_url_or_key, source_description)
        /// </summary>
        public static Tuple<string, string> ResolveCoverImageUrl(
            string titleId,
            string gameTitle = null,
            string systemCode = "psv",
            string steamGridDbApiKey = null,
            string clientId = null)
        {
            EnsureVitaCodesLoaded();

            string clientIdStr = string.IsNullOrWhiteSpace(clientId) ? DEFAULT_CLIENT_ID : clientId.Trim();
            string cleanTitleId = titleId?.Trim() ?? "";
            string cleanGameTitle = gameTitle?.Trim() ?? "";
            string cleanApiKey = steamGridDbApiKey?.Trim() ?? "";

            string cacheKey = $"{cleanTitleId.ToUpper()}|{cleanGameTitle}|{systemCode}|{cleanApiKey}|{clientIdStr}";
            if (ResolvedCoversCache.TryGetValue(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            Tuple<string, string> result = null;

            // Tier 0: Direct Bypass for System Apps / Vita Codes (bypasses SteamGridDB & GameTDB)
            if (!string.IsNullOrWhiteSpace(cleanTitleId) && VitaCodeAssetBypassMap.TryGetValue(cleanTitleId, out string customAssetKey))
            {
                string discordUrl = FetchDiscordAssetUrl(clientIdStr, customAssetKey, DEFAULT_FALLBACK_KEY);
                if (!string.IsNullOrEmpty(discordUrl))
                {
                    result = Tuple.Create(discordUrl, $"Discord Asset ('{customAssetKey}')");
                    ResolvedCoversCache[cacheKey] = result;
                    return result;
                }
            }

            // Tier 1: SteamGridDB Title-Based Search
            if (!string.IsNullOrWhiteSpace(cleanApiKey) && !string.IsNullOrWhiteSpace(cleanGameTitle))
            {
                string[] titleCandidates = new string[] { cleanGameTitle, SanitizeGameTitle(cleanGameTitle) };
                foreach (string candidate in titleCandidates)
                {
                    if (result != null || string.IsNullOrWhiteSpace(candidate)) continue;
                    try
                    {
                        string searchUrl = $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(candidate)}";
                        HttpWebRequest sReq = (HttpWebRequest)WebRequest.Create(searchUrl);
                        sReq.Headers["Authorization"] = $"Bearer {cleanApiKey}";
                        sReq.UserAgent = "VitaPresence/1.0";
                        sReq.Timeout = 5000;
                        sReq.Method = "GET";

                        using (HttpWebResponse sRes = (HttpWebResponse)sReq.GetResponse())
                        {
                            if (sRes.StatusCode == HttpStatusCode.OK)
                            {
                                using (StreamReader reader = new StreamReader(sRes.GetResponseStream()))
                                {
                                    string json = reader.ReadToEnd();
                                    var sData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                                    if (sData != null && sData.TryGetValue("data", out object dataObj) && dataObj is IList dataList && dataList.Count > 0)
                                    {
                                        if (dataList[0] is Dictionary<string, object> firstGame && firstGame.TryGetValue("id", out object gameIdObj))
                                        {
                                            string gameId = gameIdObj.ToString();

                                            // Prioritize 1:1 square aspect ratio grids first (1024x1024, 512x512)
                                            string gridUrl = FetchSteamGridDbGridUrl(gameId, cleanApiKey, true);

                                            // Fallback to first available static image if no 1:1 grid is available
                                            if (string.IsNullOrEmpty(gridUrl))
                                            {
                                                gridUrl = FetchSteamGridDbGridUrl(gameId, cleanApiKey, false);
                                            }

                                            if (!string.IsNullOrEmpty(gridUrl))
                                            {
                                                result = Tuple.Create(gridUrl, "SteamGridDB");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore search errors, continue candidate loop or Tier 2
                    }
                }
            }

            // Tier 2: Title ID Cover Database (GameTDB)
            if (result == null && !string.IsNullOrWhiteSpace(cleanTitleId) && cleanTitleId.Length >= 3)
            {
                // Region extraction logic: check 4th char (index 3), then 3rd char (index 2)
                char regionChar = cleanTitleId.Length >= 4 ? char.ToUpper(cleanTitleId[3]) : char.ToUpper(cleanTitleId[2]);
                string primaryRegion = "US";
                switch (regionChar)
                {
                    case 'B':
                    case 'F':
                    case 'E':
                        primaryRegion = "EN";
                        break;
                    case 'U':
                    case 'H':
                    case 'A':
                        primaryRegion = "US";
                        break;
                    case 'G':
                    case 'C':
                    case 'J':
                        primaryRegion = "JA";
                        break;
                    case 'K':
                        primaryRegion = "KO";
                        break;
                    case 'D':
                        primaryRegion = "ZH";
                        break;
                }

                string[] regionsToTry = new string[] { primaryRegion, "US", "EN", "JA" };
                var triedRegions = new HashSet<string>();

                foreach (string regionCode in regionsToTry)
                {
                    if (result != null || triedRegions.Contains(regionCode)) continue;
                    triedRegions.Add(regionCode);

                    try
                    {
                        string gametdbUrl = $"https://art.gametdb.com/{systemCode.ToLower()}/cover/{regionCode}/{cleanTitleId.ToUpper()}.jpg";
                        HttpWebRequest headReq = (HttpWebRequest)WebRequest.Create(gametdbUrl);
                        headReq.UserAgent = "VitaPresence/1.0";
                        headReq.Timeout = 5000;
                        headReq.Method = "HEAD";

                        using (HttpWebResponse headRes = (HttpWebResponse)headReq.GetResponse())
                        {
                            if (headRes.StatusCode == HttpStatusCode.OK)
                            {
                                result = Tuple.Create(gametdbUrl, "GameTDB");
                            }
                        }
                    }
                    catch
                    {
                        // Try next region fallback
                    }
                }
            }

            // Tier 3: Discord Developer Application Asset Fallback
            if (result == null)
            {
                string assetKey = !string.IsNullOrWhiteSpace(cleanTitleId) ? cleanTitleId.ToLower() : DEFAULT_FALLBACK_KEY;
                string discordCdnUrl = FetchDiscordAssetUrl(clientIdStr, assetKey, DEFAULT_FALLBACK_KEY);
                if (!string.IsNullOrEmpty(discordCdnUrl))
                {
                    result = Tuple.Create(discordCdnUrl, "Discord App Asset");
                }
                else
                {
                    result = Tuple.Create(DEFAULT_FALLBACK_KEY, "Discord Developer Application ('vita')");
                }
            }

            ResolvedCoversCache[cacheKey] = result;
            return result;
        }
    }
}
