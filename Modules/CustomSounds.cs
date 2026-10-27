using Hazel;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace TOHE.Modules;

public static class CustomSoundsManager
{
    // These IDs are also the embedded resource names and the keys in the local settings file.
    internal static readonly string[] EffectIds =
    [
        "AWP", "Bet", "Bite", "Boom", "Clothe", "Congrats", "Curse", "Dove", "Eat",
        "FlashBang", "Gunfire", "Gunload", "Line", "MarioCoin", "MarioJump", "Oiiai",
        "Onichian", "Shield", "Teleport"
    ];

    private static readonly Dictionary<string, AudioClip> Clips = new(StringComparer.Ordinal);
    private static CustomSoundSettings settings;
    private static AudioSource soundSource;
    private static AudioSource previewSource;
    internal static CustomSoundSettings Settings => settings ??= CustomSoundSettings.Load(EffectIds);

    public static void RPCPlayCustomSound(this PlayerControl pc, string sound, bool force = false)
    {
        if (!force) if (!AmongUsClient.Instance.AmHost || !pc.IsModded()) return;
        if (pc == null || PlayerControl.LocalPlayer.PlayerId == pc.PlayerId)
        {
            Play(sound);
            return;
        }
        MessageWriter writer = CustomRpcTransport.Start(CustomRPC.PlayCustomSound, SendOption.Reliable, pc.GetClientId());
        writer.Write(sound);
        CustomRpcTransport.Finish(writer);
    }

    public static void RPCPlayCustomSoundAll(string sound)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        MessageWriter writer = CustomRpcTransport.Start(CustomRPC.PlayCustomSound, SendOption.Reliable, -1);
        writer.Write(sound);
        CustomRpcTransport.Finish(writer);
        Play(sound);
    }

    public static void ReceiveRPC(MessageReader reader) => Play(reader.ReadString());

    public static void Play(string sound) => Play(sound, false);

    internal static void Preview(string sound)
    {
        if (previewSource != null) previewSource.Stop();
        Play(sound, true);
    }

    internal static void StopPreview()
    {
        if (previewSource != null) previewSource.Stop();
    }

    internal static void StopAll()
    {
        if (soundSource != null) soundSource.Stop();
        StopPreview();
    }

    private static void Play(string sound, bool preview)
    {
        if (!Main.EnableCustomSoundEffect.Value || SoundManager.Instance == null) return;
        if (!Settings.Effects.TryGetValue(sound, out var effect) || (!preview && !effect.Enabled)) return;

        if (!Clips.TryGetValue(sound, out var clip) || clip == null)
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"TOHE.Resources.Sounds.{sound}.raw");
            if (stream == null)
            {
                Logger.Warn($"Sound resource missing: {sound}", "CustomSounds");
                return;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            if (bytes.Length == 0 || bytes.Length % 8 != 0)
            {
                Logger.Warn($"Invalid stereo PCM resource: {sound}", "CustomSounds");
                return;
            }

            var samples = new float[bytes.Length / sizeof(int)];
            for (var i = 0; i < samples.Length; i++)
                samples[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * sizeof(int), sizeof(int))) / 2147483648f;

            clip = AudioClip.Create(sound, samples.Length / 2, 2, 48000, false);
            if (!clip.SetData(new Il2CppStructArray<float>(samples), 0))
            {
                UnityEngine.Object.Destroy(clip);
                Logger.Warn($"Could not load sound resource: {sound}", "CustomSounds");
                return;
            }
            Clips[sound] = clip;
        }

        var source = preview ? previewSource : soundSource;
        if (source == null)
        {
            source = SoundManager.Instance.GetNamedAudioSource(preview ? "TOHE.CustomSoundPreview" : "TOHE.CustomSound", null);
            source.outputAudioMixerGroup = null;
            source.volume = 1f;
            if (preview) previewSource = source;
            else soundSource = source;
        }

        var gain = (Settings.MasterVolume / 100f) * (effect.Volume / 100f);
        source.PlayOneShot(clip, gain);
    }
}
