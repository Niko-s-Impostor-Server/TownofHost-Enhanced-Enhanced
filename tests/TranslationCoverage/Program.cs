using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

static class Program
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    private static string[] LoadResources()
    {
        var assembly = Assembly.GetExecutingAssembly();
        Check(assembly.GetManifestResourceNames().Contains("TOHE.Resources.Lang.Features.en_US.json"), "English feature JSON must be in main assembly");
        Check(assembly.GetManifestResourceNames().Contains("TOHE.Resources.Lang.Features.zh_CN.json"), "Chinese feature JSON must be in main assembly");
        var featureKeys = new HashSet<string>();
        foreach (var name in assembly.GetManifestResourceNames().Order())
        {
            using var stream = assembly.GetManifestResourceStream(name);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
            int language = int.Parse(values["LanguageID"]);
            values.Remove("LanguageID");
            Translator.MergeForTest(language, values);
            if (name.Contains("Features.en_US")) featureKeys.UnionWith(values.Keys);
        }
        return featureKeys.ToArray();
    }
    private static bool Valid(string value, string key) => !string.IsNullOrWhiteSpace(value) && !value.Contains("<INVALID:") && value != "*" + key;

    public static void Main()
    {
        var features = LoadResources();
        string[] repaired = ["NewHideMsg", "DisableDevicesInfo", "SyncButtonModeInfo", "SabotageTimeControlInfo", "RandomMapsModeInfo", "NoGameEndInfo", "Mad-", "ChiefOfPolice", "SpeedBooster", "NotAssigned", "CoinflipCommandInfo", "DetectiveVanillaBlurb", "DetectiveVanillaInfoLong", "ViperInfoLong"];
        foreach (var key in features.Concat(repaired).Distinct())
        {
            string en = Translator.GetString(key, SupportedLangs.English);
            string zh = Translator.GetString(key, SupportedLangs.SChinese);
            Check(Valid(en, key), key + ": English base text exists");
            Check(Valid(zh, key), key + ": Simplified Chinese base text exists");
            foreach (var language in Enum.GetValues<SupportedLangs>())
                Check(Valid(Translator.GetString(key, language), key), key + ": safe text/fallback for " + language);
            var enPlaceholders = Regex.Matches(en, @"\{[A-Za-z0-9]+\}").Select(m => m.Value).Order().ToArray();
            var zhPlaceholders = Regex.Matches(zh, @"\{[A-Za-z0-9]+\}").Select(m => m.Value).Order().ToArray();
            Check(enPlaceholders.SequenceEqual(zhPlaceholders), key + ": replacement placeholders match");
        }
        foreach (var language in new[] { SupportedLangs.English, SupportedLangs.SChinese })
        {
            Check(Translator.GetString("Mad-", language) == Translator.GetString("Madmate-", language), "exile prefix alias retains localized meaning");
            Check(Translator.GetString("CoinflipCommandInfo", language) == Translator.GetString("CoinFlipCommandInfo", language), "coinflip casing alias retains existing message");
            Check(Translator.GetString("DetectiveVanillaInfoLong", language) == Translator.GetString("DetectiveTOHEInfoLong", language), "native detective long text uses wrapper description");
            Check(Translator.GetString("ViperInfoLong", language) == Translator.GetString("ViperTOHEInfoLong", language), "native viper long text uses wrapper description");
        }
        foreach (var key in new[] { "Accept", "DetectiveBlurb", "ViperBlurb" })
        {
            Check(Translator.GetString(key, SupportedLangs.SChinese) == "native:" + key, "StringNames fallback: " + key);
            Check(Translator.GetString(key, vanilla: true) == "native:" + key, "explicit native GUI route: " + key);
        }
        Check(Translator.GetString("__UnknownTranslation", SupportedLangs.English) == "<INVALID:__UnknownTranslation>", "unknown mod key still visibly invalid");
        Check(Translator.GetString("__UnknownTranslation", SupportedLangs.English, showInvalid: false) == "__UnknownTranslation", "optional invalid marker suppression preserved");
        Check(Translator.GetString("__UnknownTranslation", vanilla: true).Contains("<INVALID:"), "unknown native GUI key remains invalid");
        Check(Translator.GetString("DetectiveVanillaBlurb", SupportedLangs.SChinese) != "native:DetectiveBlurb", "DetectiveVanilla alias intentionally uses available mod resource");
        var replacements = new Dictionary<string, string> { ["{state}"] = "Active", ["{seconds}"] = "30" };
        Check(Translator.GetString("AfkMonitor.Status", replacements).Contains("Active") && !Translator.GetString("AfkMonitor.Status", replacements).Contains("{seconds}"), "real replacement dictionary resolves AFK placeholders");
        Check(Logger.Errors == 0, "fallback does not enter translator exception logger");
        Console.WriteLine($"TranslationCoverage: {assertions} assertions passed; {features.Length} feature keys, {repaired.Length} repaired keys.");
    }
}
