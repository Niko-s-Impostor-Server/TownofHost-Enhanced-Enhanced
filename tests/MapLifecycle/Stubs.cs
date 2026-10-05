using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public sealed class Transform { public object localPosition; }
    public sealed class GameObject
    {
        public ShipStatus Component;
        public bool Active;
        public T GetComponent<T>() => (T)(object)Component;
        public void SetActive(bool value) => Active = value;
    }
}
namespace Il2CppSystem.Collections
{
    public interface IEnumerator { bool MoveNext(); object Current { get; } }
}
namespace BepInEx.Unity.IL2CPP.Utils.Collections
{
    public static class Extensions
    {
        public sealed class Wrapped(System.Collections.IEnumerator source) : Il2CppSystem.Collections.IEnumerator
        {
            public bool MoveNext() => source.MoveNext();
            public object Current => source.Current;
        }
        public static Il2CppSystem.Collections.IEnumerator WrapToIl2Cpp(this System.Collections.IEnumerator source) => new Wrapped(source);
    }
}
namespace InnerNet { public enum SpawnFlags { None } }

public static class Trace { public static readonly List<string> Events = []; }
public sealed class LoadOperation
{
    public bool Completed;
    public UnityEngine.GameObject Result = new() { Component = new ShipStatus() };
}
public struct AsyncHandle
{
    public LoadOperation Operation;
    public UnityEngine.GameObject Result => Operation.Completed ? Operation.Result : throw new Exception("Result read before async completion");
}
public sealed class ShipPrefab(int index)
{
    public int Requests;
    public AsyncHandle InstantiateAsync(object parent, bool active)
    {
        Requests++;
        Trace.Events.Add("load:" + index);
        return new() { Operation = new() };
    }
}
public class ShipStatus
{
    public static ShipStatus Instance;
    public static bool operator !(ShipStatus value) => value == null;
    public T Cast<T>() => (T)(object)this;
}
public sealed class AirshipStatus : ShipStatus { public UnityEngine.GameObject outOfOrderPlat = new(); }
public sealed class AmongUsClient
{
    public bool AmHost = true;
    public ShipPrefab[] ShipPrefabs = [new(0), new(1), new(2), new(3), new(4)];
    public AsyncHandle ShipLoadingAsyncHandle;
    public int Spawns;
    public void Spawn(ShipStatus ship, int owner, InnerNet.SpawnFlags flags)
    {
        if (!ReferenceEquals(ship, ShipStatus.Instance) || owner != -2 || flags != InnerNet.SpawnFlags.None)
            throw new Exception("Unexpected native ship spawn contract");
        Trace.Events.Add("spawn"); Spawns++;
    }
}
public static class OnGameJoinedPatch
{
    public static uint Generation;
    public static bool IsCurrentSession(uint generation) => generation == Generation;
}
public static class GameStates { public static bool IsModHost = true, IsHideNSeek = true, IsNormalGame, IsFreePlay; }
public sealed class GameOptions { public byte MapId = 3; }
public sealed class GameOptionsManager
{
    public static GameOptionsManager Instance = new();
    public GameOptions CurrentGameOptions = new();
}
public sealed class Flag { public bool Value; public bool GetBool() => Value; }
public static class Options { public static Flag RandomMapsMode = new(), DisableAirshipMovingPlatform = new(); }
public static class Main { public static GameOptions NormalOptions = new(), HideNSeekOptions = new(); }
public static class CreateOptionsPickerPatch { public static bool SetDleks; }
public enum MapNames { Skeld, Dleks = 3 }
public sealed class MapIconByName { public MapNames Name; public object MapIcon, MapImage, NameImage; }
public sealed class GameStartManager
{
    public static int Constructions;
    public GameStartManager() => Constructions++;
    public List<MapIconByName> AllMapIcons = [new() { Name = MapNames.Skeld, MapIcon = new(), MapImage = new(), NameImage = new() }];
    public int MapUpdates;
    public void UpdateMapImage(MapNames name) => MapUpdates++;
}
public static class Utils { public static object LoadSprite(string resource, float scale) => resource; }
public class HarmonyPatch : Attribute
{
    public HarmonyPatch(Type type) { }
    public HarmonyPatch(string name, params Type[] arguments) { }
}
public class HarmonyPrefix : Attribute { }
public sealed class PlayerControl { }
public sealed class MovingPlatformBehaviour
{
    public UnityEngine.Transform transform = new();
    public object DisabledPosition = new();
    public int Cleans;
    public bool Dirty = true;
    public void MarkClean() { Dirty = false; Cleans++; }
    public void Start() { }
    public void SetTarget() { }
    public void SetSide() { }
    public void Use(PlayerControl player) { }
}
