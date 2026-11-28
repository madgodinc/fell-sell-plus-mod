using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2CppEmeraldAI;
using UnityEngine;

namespace FellSellPlus.Coop;

// Voices of the host's enemies on a client (part of HostEnemies).
//
// Emerald AI plays an enemy's voice through its EmeraldSounds component: the attack, warning, idle,
// block, injured and footstep sounds each pick a random clip of the enemy's sound profile and play it
// on the enemy's own audio source. A copy on a client has that component switched off with the rest of its
// AI, so it stays silent. The host notes every such call on one of its enemies and sends it on; the
// client calls the same method on its copy, which then picks a clip of the same profile and plays it
// at the copy's place through the copy's own audio source (volume, distance and mixer as in a solo game).
//
// The death sound belongs to the copy's own death (HostEnemies.Die).
internal static class EnemySounds
{
    public const byte Msg = 13;
    const int PerPacket = 80; // 6 bytes each

    internal enum Kind : byte { Attack = 1, Warning, Idle, Block, Injured, Effect, Footstep }

    struct Note { public int Id; public Kind Kind; public byte Arg; }

    static readonly List<Note> _pending = new();
    internal static readonly int[] Sent = new int[8], Played = new int[8], Silent = new int[8];
    internal static string Last = "-";
    static bool _replaying;

    static void Heard(EmeraldSounds sounds, Kind kind, int arg = 0)
    {
        if (_replaying || !CoopSettings.IsHost || !HostEnemies.On || !CoopModule.HasPartner || sounds == null) return;
        try
        {
            int id = sounds.gameObject.GetInstanceID();
            if (!HostEnemies.IsTracked(id) || _pending.Count >= PerPacket) return;
            _pending.Add(new Note { Id = id, Kind = kind, Arg = (byte)Mathf.Clamp(arg, 0, 255) });
            Sent[(int)kind]++;
        }
        catch (Exception e) { CoopModule.Warn("enemy sound", e); }
    }

    [HarmonyPatch(typeof(EmeraldSounds), nameof(EmeraldSounds.PlayAttackSound))]
    internal static class AttackPatch { static void Prefix(EmeraldSounds __instance) => Heard(__instance, Kind.Attack); }
    [HarmonyPatch(typeof(EmeraldSounds), nameof(EmeraldSounds.PlayWarningSound))]
    internal static class WarningPatch { static void Prefix(EmeraldSounds __instance) => Heard(__instance, Kind.Warning); }
    [HarmonyPatch(typeof(EmeraldSounds), nameof(EmeraldSounds.PlayIdleSound))]
    internal static class IdlePatch { static void Prefix(EmeraldSounds __instance) => Heard(__instance, Kind.Idle); }
    [HarmonyPatch(typeof(EmeraldSounds), nameof(EmeraldSounds.PlayBlockSound))]
    internal static class BlockPatch { static void Prefix(EmeraldSounds __instance) => Heard(__instance, Kind.Block); }
    [HarmonyPatch(typeof(EmeraldSounds), nameof(EmeraldSounds.PlayInjuredSound))]
    internal static class InjuredPatch { static void Prefix(EmeraldSounds __instance) => Heard(__instance, Kind.Injured); }
    [HarmonyPatch(typeof(EmeraldSounds), nameof(EmeraldSounds.PlaySoundEffect))]
    internal static class EffectPatch { static void Prefix(EmeraldSounds __instance, int SoundEffectID) => Heard(__instance, Kind.Effect, SoundEffectID); }
    [HarmonyPatch(typeof(EmeraldSounds), nameof(EmeraldSounds.PlayFootstepSound))]
    internal static class FootstepPatch { static void Prefix(EmeraldSounds __instance) => Heard(__instance, Kind.Footstep); }

    public static void HostPackets(List<byte[]> into, int key)
    {
        if (_pending.Count == 0) return;
        if (key != 0)
        {
            var w = CoopModule.Begin(Msg, out var ms);
            w.Write(key); w.Write((byte)_pending.Count);
            foreach (var n in _pending) { w.Write(n.Id); w.Write((byte)n.Kind); w.Write(n.Arg); }
            into.Add(ms.ToArray());
        }
        _pending.Clear();
    }

    public static void Receive(BinaryReader r)
    {
        int key = r.ReadInt32(), count = r.ReadByte();
        if (!World.ClientShares || World.ClientKey != key) return;
        for (int i = 0; i < count; i++)
        {
            int id = r.ReadInt32(); var kind = (Kind)r.ReadByte(); int arg = r.ReadByte();
            if (!HostEnemies.Copy(id, out var go, out var sounds) || sounds == null) continue;
            Play(go, sounds, kind, arg);
        }
    }

    static void Play(GameObject go, EmeraldSounds sounds, Kind kind, int arg)
    {
        _replaying = true;
        try
        {
            switch (kind)
            {
                case Kind.Attack: sounds.PlayAttackSound(); break;
                case Kind.Warning: sounds.PlayWarningSound(); break;
                case Kind.Idle: sounds.PlayIdleSound(); break;
                case Kind.Block: sounds.PlayBlockSound(); break;
                case Kind.Injured: sounds.PlayInjuredSound(); break;
                case Kind.Effect: sounds.PlaySoundEffect(arg); break;
                case Kind.Footstep: sounds.PlayFootstepSound(null, 1f); break;
                default: return;
            }
            int k = (int)kind;
            Played[k]++;
            if (!Dev.DevCommands.Enabled.Value) return;
            // dev: is anything actually coming out of the copy's audio sources?
            bool any = false; AudioSource loud = null;
            foreach (var src in new[] { sounds.m_AudioSource, sounds.m_SecondaryAudioSource, sounds.m_EventAudioSource })
                if (src != null && src.isPlaying) { any = true; loud = src; break; }
            if (!any) Silent[k]++;
            var cam = Camera.main;
            Last = $"{kind} on {go.name}: playing {any}" + (loud != null ? $", clip {(loud.clip != null ? loud.clip.name : "one-shot")}, volume {loud.volume:0.00}, 3D {loud.spatialBlend:0.0}, max distance {loud.maxDistance:0}, mute {loud.mute}, mixer {(loud.outputAudioMixerGroup != null ? loud.outputAudioMixerGroup.name : "none")}" : "")
                + (cam != null ? $", {Vector3.Distance(cam.transform.position, go.transform.position):0.0} m away" : "");
        }
        catch (Exception e) { CoopModule.Warn("enemy sound replay", e); }
        finally { _replaying = false; }
    }

    internal static string Describe()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("sent");
        for (int i = 1; i < 8; i++) sb.Append($" {(Kind)i} {Sent[i]}");
        sb.Append(" | played");
        for (int i = 1; i < 8; i++) sb.Append($" {(Kind)i} {Played[i]} (silent {Silent[i]})");
        sb.Append(" | last: ").Append(Last);
        sb.Append($" | listener volume {AudioListener.volume:0.00} pause {AudioListener.pause}");
        return sb.ToString();
    }
}
