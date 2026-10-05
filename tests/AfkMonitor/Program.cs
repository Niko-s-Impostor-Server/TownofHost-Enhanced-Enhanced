using TOHE;
using UnityEngine;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
void Advance(double seconds)
{
    for (int step = 0; step < seconds * 2; step++)
    {
        Time.realtimeSinceStartup += 0.5f;
        AfkMonitor.Tick();
    }
}
void NewRound()
{
    Main.PlayerStates = new();
    foreach (var player in Main.Players) { player.Warnings = 0; player.Data.IsDead = false; player.Data.Disconnected = false; }
    AmongUsClient.Instance.Kicks.Clear();
    AfkMonitor.Tick();
}

AfkMonitor.SetupCustomOptions();
Check(!OptionItem.Items[61050].GetBool() && OptionItem.Items[61051].GetInt() == 180
    && OptionItem.Items[61052].GetValue() == 0, "AFK default must preserve existing behavior");
var host = new PlayerControl(0, 0) { AmOwner = true };
var target = new PlayerControl(1, 1);
var other = new PlayerControl(2, 2);
Main.Players = [host, target, other];
foreach (var player in Main.Players) AmongUsClient.Instance.Clients[player.OwnerId] = new() { Character = player };
AfkMonitor.Tick(); Advance(250);
Check(target.Warnings == 0 && AmongUsClient.Instance.Kicks.Count == 0, "Disabled monitor must have no consequences");
OptionItem.Items[61050].Value = 1;
OptionItem.Items[61051].Value = 30;
NewRound();
Advance(44);
Check(target.Warnings == 0, "Intro grace must precede the entire idle threshold");
Advance(2);
Check(target.Warnings == 1, "Crossing inactivity threshold must warn once");
Advance(60);
Check(target.Warnings == 1 && AmongUsClient.Instance.Kicks.Count == 0, "Warning mode must not spam or kick");
target.Position = new(0.1f, 0);
Advance(0.5);
Check(AfkMonitor.GetStatus(target).EndsWith("0s"), "Movement must clear the idle state");
Advance(29);
target.Tasks.CompletedTasksCount++;
Advance(1);
Check(target.Warnings == 1 && AfkMonitor.GetStatus(target).EndsWith("0s"), "Completed task must count as activity");
Advance(20);
AfkMonitor.RecordActivity(target);
Advance(20);
Check(target.Warnings == 1, "Host-observed chat/activity must reset the inactivity clock");

OptionItem.Items[61052].Value = 1;
NewRound(); Advance(46);
Check(AfkMonitor.IsShielded(target), "Shield mode must expose protection for a live AFK player");
AfkMonitor.RecordActivity(target);
Check(!AfkMonitor.IsShielded(target), "Activity must remove protection immediately");
Advance(32);
Check(AfkMonitor.IsShielded(target), "A new idle episode may protect again");
target.inVent = true; Advance(100);
Check(!AfkMonitor.IsShielded(target), "Venting must pause and clear AFK protection");
target.inVent = false; Advance(44);
Check(!AfkMonitor.IsShielded(target), "Leaving a paused transition must restore the grace period");
Advance(2);
Check(AfkMonitor.IsShielded(target), "Idle may resume only after grace plus threshold");
GameStates.IsInTask = false; Advance(200);
Check(!AfkMonitor.IsShielded(target), "Meetings must disable protection and timers");
GameStates.IsInTask = true; Advance(44);
Check(!AfkMonitor.IsShielded(target), "Meeting time must never count toward AFK");
Advance(2);
Check(AfkMonitor.IsShielded(target), "Post-meeting inactivity must requalify normally");

Check(AfkMonitor.SetExempt(target, true), "Host must be able to exempt a canonical player");
Advance(100);
Check(!AfkMonitor.IsShielded(target) && AfkMonitor.GetStatus(target).Contains("Exempt"), "Exemption must suppress consequences");
var replacement = new PlayerControl(1, 10) { Pointer = 999 };
Main.Players[1] = replacement;
AmongUsClient.Instance.Clients[10] = new() { Character = replacement };
Advance(1);
Check(!AfkMonitor.GetStatus(replacement).Contains("Exempt") && !AfkMonitor.IsShielded(replacement), "Reused player ID must not inherit exemption or idle state");
Check(!AfkMonitor.SetExempt(target, false), "Stale player handle must fail owner-pointer validation");
target = replacement;
Advance(46);
Check(AfkMonitor.IsShielded(target), "Replacement must accumulate its own inactivity");
OnGameJoinedPatch.Generation++;
Advance(1);
Check(!AfkMonitor.IsShielded(target), "New session generation must discard idle state");
Advance(46);
GameManager.Instance = new() { Pointer = 222 };
Advance(1);
Check(!AfkMonitor.IsShielded(target), "Game object replacement must discard idle state");

OptionItem.Items[61052].Value = 2;
NewRound(); Advance(46);
Check(AmongUsClient.Instance.Kicks.Count == 0 && target.Warnings == 1, "Kick mode must first warn and grant a recovery interval");
Advance(10);
AfkMonitor.RecordActivity(target);
Advance(10);
Check(!AmongUsClient.Instance.Kicks.Contains(target.OwnerId), "Activity during kick warning must cancel kick");
Advance(51);
Check(AmongUsClient.Instance.Kicks.Count(x => x == target.OwnerId) == 1
    && !AmongUsClient.Instance.Kicks.Contains(host.OwnerId), "Explicit kick mode must act once and never kick host");
Advance(50);
Check(AmongUsClient.Instance.Kicks.Count(x => x == target.OwnerId) == 1, "A sent kick must not repeat every poll");

NewRound();
Time.realtimeSinceStartup += 3600;
AfkMonitor.Tick();
Check(target.Warnings == 0 && AmongUsClient.Instance.Kicks.Count == 0, "Long host stall must not become an AFK penalty");
Time.timeScale = 0; Advance(100);
Time.timeScale = 1; Advance(44);
Check(target.Warnings == 0, "A paused game must not charge realtime toward inactivity");
GameStates.IsExilling = true; Advance(100);
GameStates.IsExilling = false; Advance(44);
Check(target.Warnings == 0, "Exile transition must restore grace rather than count paused time");
other.Data.IsDead = true; Advance(100);
Check(target.Warnings == 0, "Minimum alive player threshold must pause monitoring");
other.Data.IsDead = false; Advance(46);
Check(target.Warnings == 1, "Monitoring may restart when alive-player threshold is restored");
AmongUsClient.Instance.AmHost = false; Advance(100);
Check(!AfkMonitor.IsShielded(target) && !AfkMonitor.SetExempt(target, true), "Non-host must never grant exemptions or AFK protection");

Console.WriteLine($"PASS: {checks} linked-production AFK monitor state-transition checks.");
