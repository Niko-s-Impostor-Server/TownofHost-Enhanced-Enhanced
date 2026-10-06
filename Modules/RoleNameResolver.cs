using System;
using System.Text;
using System.Text.RegularExpressions;

namespace TOHE;

// Translation snapshots are replaced on reload, including names from removed .dat files.
internal sealed class RoleNameIndex
{
    private readonly Dictionary<int, Dictionary<string, HashSet<CustomRoles>>> names = [];

    internal static string Normalize(string name) => Regex.Replace(name ?? "", "<[^>]*>", "")
        .Normalize(NormalizationForm.FormKC).Trim().TrimStart('*')
        .Where(c => !char.IsWhiteSpace(c)).Aggregate(new StringBuilder(), (sb, c) => sb.Append(c))
        .ToString().ToLowerInvariant();

    internal void Add(int language, CustomRoles role, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("<INVALID:")) return;
        var key = Normalize(name);
        if (key.Length == 0) return;
        if (!names.TryGetValue(language, out var entries)) names[language] = entries = [];
        if (!entries.TryGetValue(key, out var roles)) entries[key] = roles = [];
        roles.Add(role);
    }

    internal CustomRoles[] Find(string name, int language, bool crossLanguage)
    {
        var key = Normalize(name);
        HashSet<CustomRoles> result = [];
        foreach (var entry in names)
            if ((crossLanguage || entry.Key == language) && entry.Value.TryGetValue(key, out var roles))
                result.UnionWith(roles);
        return result.OrderBy(role => (int)role).ToArray();
    }

    internal static bool TrySplitGuess(string text, out byte targetId, out string roleName)
    {
        var match = Regex.Match(text ?? "", @"^\s*((?>\d+))\s*(.+?)\s*$");
        roleName = match.Success ? match.Groups[2].Value : "";
        targetId = byte.MaxValue;
        return match.Success && byte.TryParse(match.Groups[1].Value, out targetId)
            && !string.IsNullOrWhiteSpace(roleName);
    }
}

internal static class RoleNameResolver
{
    private static RoleNameIndex index = new();
    internal static void Clear() => index = new();

    internal static void Rebuild(IEnumerable<KeyValuePair<string, Dictionary<int, string>>> builtIn,
        IEnumerable<KeyValuePair<string, Dictionary<int, string>>> translated)
    {
        var replacement = new RoleNameIndex();
        var roleKeys = CustomRolesHelper.AllRoles.Where(role => !role.IsVanilla()).ToDictionary(role => role.ToString());
        foreach (var source in new[] { builtIn, translated })
            foreach (var entry in source)
                if (roleKeys.TryGetValue(entry.Key, out var role))
                    foreach (var name in entry.Value) replacement.Add(name.Key, role, name.Value);
        // Match the existing display-language fallback when a translation is absent.
        foreach (var language in EnumHelper.GetAllValues<SupportedLangs>())
            foreach (var role in roleKeys.Values)
                replacement.Add((int)language, role, Translator.GetString(role.ToString(), language));
        index = replacement;
    }

    internal static CustomRoles[] Resolve(string name)
    {
        var language = Translator.GetCommandLanguage();
        var crossLanguage = Options.CrossLanguageGetRole?.GetBool() ?? false;
        var result = index.Find(name, (int)language, crossLanguage);
        // Keep established aliases without letting them override an exact translated name.
        return result.Length != 0 ? result : index.Find(ChatCommands.FixRoleNameInput(name?.Trim() ?? ""), (int)language, crossLanguage);
    }

    internal static CustomRoles[] Resolve(CustomRoles role)
    {
        if (!CustomRolesHelper.AllRoles.Contains(role) || role.IsVanilla()) return [];
        return Resolve(Translator.GetString(role.ToString())).Append(role).Distinct().OrderBy(candidate => (int)candidate).ToArray();
    }
}

internal static class GuessCandidateSelector
{
    internal static CustomRoles? Select(IEnumerable<CustomRoles> candidates,
        Func<CustomRoles, bool> allowed, Func<CustomRoles, bool> matches)
    {
        var legal = candidates.Where(allowed).ToArray();
        return legal.Length == 0 ? null : legal.FirstOrDefault(matches, legal[0]);
    }
}
