using System;
using System.Linq;
using System.Runtime.CompilerServices;
using FellSellCharacter;
using UnityEngine;

namespace FellSellPlus.Coop;

// The only place that names the Character mod's types. That mod is optional: nothing here may run,
// and no other class may hold a field of its types, unless Present is true. The runtime resolves
// FellSellCharacter.dll only when one of these methods is first compiled.
internal static class CharacterBridge
{
    static bool? _present;
    public static bool Present => _present ??= AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "FellSellCharacter");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static byte[] LocalLook() => LocalPlayer.Look?.ToBytes();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool LocalState(ref BodyState b)
    {
        if (!LocalPlayer.Sample(out var s)) return false;
        b.MoveX = s.Move.x; b.MoveY = s.Move.y; b.Pitch = s.Pitch; b.Bow = s.Bow;
        b.Grounded = s.Grounded; b.Block = s.Block; b.Crouch = s.Crouch;
        b.Action = (byte)LocalPlayer.LastAction; b.ActionSeq = (byte)LocalPlayer.ActionSeq;
        b.ActionDuration = LocalPlayer.LastActionDuration; b.ActionSkip = LocalPlayer.LastActionSkip;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static byte[] LocalHeld() => HeldItems.Local();

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ApplyHeld(object body, byte[] held) => ((RemoteBody)body).ApplyHeld(held);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static object Create(string name, byte[] look, out GameObject root)
    {
        var body = new RemoteBody(name, look);
        root = body.Root;
        return body;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ApplyLook(object body, byte[] look) => ((RemoteBody)body).ApplyLook(look);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Update(object body, in BodyState state, float dt) => ((RemoteBody)body).Update(state, dt);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Destroy(object body) => ((RemoteBody)body).Destroy();
}

// Another player's body: a Character-mod puppet wearing that player's look, animated from the state
// packets the way ThirdPerson animates the local hero.
internal sealed class RemoteBody
{
    readonly Puppet _puppet;
    readonly PuppetAnimator _anim;
    readonly PuppetPoser _poser = new();
    readonly PuppetHands _hands = new();
    int _seq = -1;

    public GameObject Root => _puppet.Root;

    public RemoteBody(string name, byte[] look)
    {
        _puppet = Puppet.Create(name);
        try { _puppet.Apply(Look.FromBytes(look)); }
        catch { _puppet.Destroy(); throw; }
        _anim = new PuppetAnimator(_puppet);
    }

    public void ApplyLook(byte[] look) => _puppet.Apply(Look.FromBytes(look));
    public void ApplyHeld(byte[] held) => _hands.Apply(_puppet, held);

    public void Update(in BodyState b, float dt)
    {
        if (!_puppet.Alive) return;
        var s = new PuppetState
        {
            Move = new Vector2(b.MoveX, b.MoveY), Grounded = b.Grounded, Pitch = b.Pitch,
            Block = b.Block, Bow = b.Bow, Crouch = b.Crouch,
        };
        // The counter in every packet makes a one-shot survive lost packets and play exactly once.
        // The first packet only sets the baseline: an old swing is not replayed on joining.
        if (_seq >= 0 && b.ActionSeq != _seq && b.Action != 0 && Enum.IsDefined(typeof(PuppetAction), (int)b.Action))
            _anim.Play((PuppetAction)b.Action, b.ActionDuration, b.ActionSkip);
        _seq = b.ActionSeq;
        _anim.Update(s, dt);
        if (!_anim.Ready) return;
        _poser.Apply(_puppet, s, dt);
        _hands.Pose(_puppet);
    }

    public void Destroy()
    {
        _hands.Reset();
        _anim.Dispose();
        _puppet.Destroy();
    }
}
