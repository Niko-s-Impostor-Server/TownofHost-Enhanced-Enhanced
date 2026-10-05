using System.Text.RegularExpressions;
using TOHE.Modules;
using TOHE.Patches;
using InnerNet;

var assertions = 0;
void Check(bool condition, string scenario)
{
    if (!condition) throw new Exception("FAIL: " + scenario);
    assertions++;
}
void Reject(Action action, string scenario)
{
    try { action(); }
    catch (InvalidOperationException) { assertions++; return; }
    throw new Exception("FAIL: " + scenario);
}

// Independently generated using Python uuid.uuid5(uuid.NAMESPACE_DNS, name).
const string version = "2026.1005.211.1";
const string expected = "d15f9333-446f-5203-aab4-4b09191bc94e";
Check(ModRegistration.CreateGuid(version).ToString("D") == expected, "fixed UUIDv5 vector");
Check(ModRegistration.CreateGuid("2026.1005.211.2").ToString("D") == "1f14ee03-780e-54a6-9308-c1aa5e633ff6", "different build version gets a different identity");
Check(ModRegistration.CreateGuid("2.1.1-au20260818").ToString("D") == "9e4f4a85-7cda-5f96-847e-ae35563176b3", "display version remains a distinct input");
Check(Convert.ToHexString(ModRegistration.CreateGuid(version).ToByteArray()).ToLowerInvariant() == "33935fd16f440352aab44b09191bc94e", "native .NET GUID wire byte order");

var mainSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Main.source.txt"));
var currentVersion = Regex.Match(mainSource, "public const string PluginVersion = \"([^\"]+)\";").Groups[1].Value;
Check(!string.IsNullOrEmpty(currentVersion), "production version is available");
Check(mainSource.Contains("ModRegistration.Register(PluginVersion)"), "load uses internal plugin version");
Check(mainSource.Contains("ModRegistration.Unregister();"), "unload releases the owned registration");
var unload = Regex.Match(mainSource, @"public override bool Unload\(\)\s*\{([^}]+)\}").Groups[1].Value;
Check(unload.IndexOf("Harmony.UnpatchSelf();", StringComparison.Ordinal) >= 0 &&
    unload.IndexOf("Harmony.UnpatchSelf();", StringComparison.Ordinal) < unload.IndexOf("ModRegistration.Unregister();", StringComparison.Ordinal) &&
    unload.Contains("return true;"), "successful unload removes patches before releasing registration");

foreach (var empty in new[] { null, "" })
{
    CurrentModRegistration.ModRegistrationGuidString = empty;
    Check(ModRegistration.Register(currentVersion) == ModRegistration.CreateGuid(currentVersion).ToString("D"), "registers an empty native field");
    ModRegistration.Unregister();
    Check(CurrentModRegistration.ModRegistrationGuidString == "", "unload clears the owned identity");
}

CurrentModRegistration.ModRegistrationGuidString = "{" + expected.ToUpperInvariant() + "}";
Check(ModRegistration.Register(version) == expected, "accepts the same GUID with a different valid formatting");
Check(ModRegistration.Register(version) == expected, "registration is idempotent");
ModRegistration.Unregister();

foreach (var foreign in new[] { "316e7f61-f150-4ac0-b2cd-7f3cc7225963", "malformed", " " })
{
    CurrentModRegistration.ModRegistrationGuidString = foreign;
    Reject(() => ModRegistration.Register(version), "rejects conflicting nonempty registration");
    Check(CurrentModRegistration.ModRegistrationGuidString == foreign, "failed registration preserves the foreign value");
    ModRegistration.Unregister();
    Check(CurrentModRegistration.ModRegistrationGuidString == foreign, "unload after failure preserves the foreign value");
}

CurrentModRegistration.ModRegistrationGuidString = "";
ModRegistration.Register(version);
const string replacement = "316e7f61-f150-4ac0-b2cd-7f3cc7225963";
CurrentModRegistration.ModRegistrationGuidString = replacement;
ModRegistration.Unregister();
Check(CurrentModRegistration.ModRegistrationGuidString == replacement, "unload does not clear another mod's replacement");
CurrentModRegistration.ModRegistrationGuidString = expected;
ModRegistration.Unregister();
Check(CurrentModRegistration.ModRegistrationGuidString == expected, "repeated unload does not retain ownership");

CurrentModRegistration.ModRegistrationGuidString = "";
ModRegistration.Register(version);
foreach (var networkMode in new[] { NetworkModes.LocalGame, NetworkModes.FreePlay })
{
    var client = new InnerNetClient { NetworkMode = networkMode };
    LocalHostModRegistrationPatch.Prefix(client, out var suspension);
    Check(suspension == expected && CurrentModRegistration.ModRegistrationGuidString == "", "local host uses native tag 0 path");
    Check(LocalHostModRegistrationPatch.Finalizer(suspension, null) == null, "successful local host finalizer preserves success");
    Check(CurrentModRegistration.ModRegistrationGuidString == expected, "successful local host restores registration");
    LocalHostModRegistrationPatch.Prefix(client, out suspension);
    var failure = new InvalidOperationException("native host failure");
    Check(ReferenceEquals(LocalHostModRegistrationPatch.Finalizer(suspension, failure), failure), "local host finalizer preserves native exception");
    Check(CurrentModRegistration.ModRegistrationGuidString == expected, "failed local host restores registration");
}

LocalHostModRegistrationPatch.Prefix(new InnerNetClient { NetworkMode = NetworkModes.OnlineGame }, out var onlineState);
Check(onlineState == null && CurrentModRegistration.ModRegistrationGuidString == expected, "online host retains MCI identity and tag 25 path");
LocalHostModRegistrationPatch.Finalizer(onlineState, null);
Check(CurrentModRegistration.ModRegistrationGuidString == expected, "online finalizer preserves registration");

foreach (var foreign in new[] { replacement, "invalid", "" })
{
    CurrentModRegistration.ModRegistrationGuidString = foreign;
    LocalHostModRegistrationPatch.Prefix(new InnerNetClient { NetworkMode = NetworkModes.LocalGame }, out var suspension);
    Check(suspension == null && CurrentModRegistration.ModRegistrationGuidString == foreign, "local host does not suspend foreign or empty identity");
    LocalHostModRegistrationPatch.Finalizer(suspension, null);
    Check(CurrentModRegistration.ModRegistrationGuidString == foreign, "local host does not restore over foreign identity");
}

CurrentModRegistration.ModRegistrationGuidString = expected;
LocalHostModRegistrationPatch.Prefix(new InnerNetClient { NetworkMode = NetworkModes.LocalGame }, out var outerState);
LocalHostModRegistrationPatch.Prefix(new InnerNetClient { NetworkMode = NetworkModes.LocalGame }, out var nestedState);
LocalHostModRegistrationPatch.Finalizer(nestedState, null);
Check(CurrentModRegistration.ModRegistrationGuidString == "", "nested host completion keeps outer suspension");
LocalHostModRegistrationPatch.Finalizer(outerState, null);
Check(CurrentModRegistration.ModRegistrationGuidString == expected, "outer host restores once");

LocalHostModRegistrationPatch.Prefix(new InnerNetClient { NetworkMode = NetworkModes.LocalGame }, out var replacedState);
CurrentModRegistration.ModRegistrationGuidString = replacement;
LocalHostModRegistrationPatch.Finalizer(replacedState, null);
Check(CurrentModRegistration.ModRegistrationGuidString == replacement, "replacement while hosting remains untouched");
CurrentModRegistration.ModRegistrationGuidString = expected;
LocalHostModRegistrationPatch.Prefix(new InnerNetClient { NetworkMode = NetworkModes.FreePlay }, out var unloadedState);
ModRegistration.Unregister();
LocalHostModRegistrationPatch.Finalizer(unloadedState, null);
Check(CurrentModRegistration.ModRegistrationGuidString == "", "unload while hosting revokes restoration ownership");

Console.WriteLine($"PASS: {assertions} mod registration assertions; current {currentVersion} => {ModRegistration.CreateGuid(currentVersion):D}");
