namespace Il2CppSystem { public class Object { } }
namespace Il2CppInterop.Runtime.InteropTypes.Arrays
{
    public class Il2CppReferenceArray<T>(int length) { public int Length = length; }
}
public sealed class TranslationController
{
    public static bool InstanceExists = true;
    public static TranslationController Instance => DestroyableSingleton<TranslationController>.Instance;
    public Language currentLanguage = new();
    public string GetString(StringNames name) => "native:" + name;
    public string GetString(StringNames name, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object> args) => GetString(name);
}
public sealed class Language { public SupportedLangs languageID = SupportedLangs.English; }
public static class DestroyableSingleton<T> where T : new() { public static T Instance = new(); }
public sealed class ConfigValue { public bool Value; }
public static class Main { public static ConfigValue ForceOwnLanguage = new(); }
public static class EnumHelper
{
    public static T[] GetAllValues<T>() where T : struct, Enum => Enum.GetValues<T>();
}
public static class Logger
{
    public static int Errors;
    public static void Fatal(string message, string tag) => Errors++;
    public static void Error(string message, string tag) => Errors++;
}
