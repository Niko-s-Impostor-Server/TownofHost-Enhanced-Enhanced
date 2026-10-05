using AmongUs.Data;
using AmongUs.Data.Player;
using Assets.InnerNet;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using UnityEngine;

namespace TOHE;

// code credit https://github.com/Yumenopai/TownOfHost_Y
[HarmonyPatch]
public class ModNews
{
    public int Number;
    public int BeforeNumber;
    public string Title;
    public string SubTitle;
    public string ShortTitle;
    public string Text;
    public string Date;

    public Announcement ToAnnouncement()
    {
        var result = new Announcement
        {
            Number = Number,
            Title = Title,
            SubTitle = SubTitle,
            ShortTitle = ShortTitle,
            Text = Text,
            Language = (uint)DataManager.Settings.Language.CurrentLanguage,
            Date = Date,
            Id = "ModNews"
        };

        return result;
    }
    public static List<ModNews> AllModNews = [];
    private static SupportedLangs? loadedLanguage;
    public ModNews(int Number, string Title, string SubTitle, string ShortTitle, string Text, string Date)
    {
        this.Number = Number;
        this.Title = Title;
        this.SubTitle = SubTitle;
        this.ShortTitle = ShortTitle;
        this.Text = Text;
        this.Date = Date;
    }

    private static void LoadLocalNews()
    {
        var language = DataManager.Settings.Language.CurrentLanguage;
        if (loadedLanguage == language) return;
        AllModNews.Clear();
        loadedLanguage = language;

        var locale = language switch
        {
            SupportedLangs.German => "de_DE",
            SupportedLangs.Latam => "es_419",
            SupportedLangs.Spanish => "es_ES",
            SupportedLangs.Filipino => "fil_PH",
            SupportedLangs.French => "fr_FR",
            SupportedLangs.Italian => "it_IT",
            SupportedLangs.Japanese => "ja_JP",
            SupportedLangs.Korean => "ko_KR",
            SupportedLangs.Dutch => "nl_NL",
            SupportedLangs.Brazilian => "pt_BR",
            SupportedLangs.Portuguese => "pt_PT",
            SupportedLangs.Russian => "ru_RU",
            SupportedLangs.SChinese => "zh_CN",
            SupportedLangs.TChinese => "zh_TW",
            SupportedLangs.Irish => "ga_IE",
            _ => "en_US"
        };

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream($"TOHE.Resources.Announcements.modNews-{locale}.json")
                ?? assembly.GetManifestResourceStream("TOHE.Resources.Announcements.modNews-en_US.json");
            if (stream == null) return;
            using var reader = new StreamReader(stream);
            using var document = JsonDocument.Parse(reader.ReadToEnd());
            foreach (var news in document.RootElement.GetProperty("News").EnumerateArray())
            {
                var date = news.GetProperty("Date").GetString()?.Replace(" Uhr", string.Empty);
                if (!DateTimeOffset.TryParse(date, CultureInfo.GetCultureInfo(locale.Replace('_', '-')),
                    DateTimeStyles.None, out var parsedDate)) continue;
                AllModNews.Add(new ModNews(
                    int.Parse(news.GetProperty("Number").GetString(), CultureInfo.InvariantCulture),
                    news.GetProperty("Title").GetString(),
                    news.GetProperty("Subtitle").GetString(),
                    news.GetProperty("Short").GetString(),
                    string.Join("", news.GetProperty("Body").EnumerateArray().Select(line => line.GetString())),
                    parsedDate.ToString("O", CultureInfo.InvariantCulture)));
            }
        }
        catch (Exception ex)
        {
            AllModNews.Clear();
            Logger.Warn($"Could not load embedded announcements: {ex.GetType().Name}", "ModNews");
        }
    }

    private static DateTimeOffset AnnouncementDate(string date)
        => DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed : DateTimeOffset.MinValue;

    [HarmonyPatch(typeof(PlayerAnnouncementData), nameof(PlayerAnnouncementData.SetAnnouncements)), HarmonyPrefix]
    public static bool SetModAnnouncements_Prefix(PlayerAnnouncementData __instance, [HarmonyArgument(0)] ref Il2CppReferenceArray<Announcement> aRange)
    {
        LoadLocalNews();
        if (AllModNews.Count == 0) return true;

        List<Announcement> FinalAllNews = [];
        AllModNews.Do(n => FinalAllNews.Add(n.ToAnnouncement()));
        if (aRange != null)
        {
            foreach (var news in aRange)
            {
                if (!AllModNews.Any(x => x.Number == news.Number))
                    FinalAllNews.Add(news);
            }
        }
        FinalAllNews.Sort((a1, a2) => { return AnnouncementDate(a2.Date).CompareTo(AnnouncementDate(a1.Date)); });

        aRange = new(FinalAllNews.Count);
        for (int i = 0; i < FinalAllNews.Count; i++)
            aRange[i] = FinalAllNews[i];

        return true;
    }


    [HarmonyPatch(typeof(AnnouncementPanel), nameof(AnnouncementPanel.SetUp)), HarmonyPostfix]
    public static void SetUpPanel_Postfix(AnnouncementPanel __instance, [HarmonyArgument(0)] Announcement announcement)
    {
        if (announcement.Number < 100000) return;
        var obj = new GameObject("ModLabel");
        //obj.layer = -1;
        obj.transform.SetParent(__instance.transform);
        obj.transform.localPosition = new Vector3(-0.8f, 0.13f, 0.5f);
        obj.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
        var renderer = obj.AddComponent<SpriteRenderer>();
        renderer.sprite = Utils.LoadSprite($"TOHE.Resources.Images.CreditsButton.png", 250f);
        renderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
    }
}
