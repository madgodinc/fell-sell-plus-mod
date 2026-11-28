using UnityEngine;

namespace FellSellCharacter;

// What other mods (co-op) may ask about the local hero: the look he wears and what his body is doing
// this frame. It works in first person too, when no body is drawn locally.
public static class LocalPlayer
{
    public static Look Look => ThirdPerson.Look ??= Look.LoadSaved();

    // False while there is no hero to read (menus, loading).
    public static bool Sample(out PuppetState state) => ThirdPerson.Sample(FirstPersonController.Instance, out state);

    // One-shot actions (swings, casts, drinking): the counter goes up with every new one.
    public static int ActionSeq => ThirdPerson.ActionSeq;
    public static PuppetAction LastAction => ThirdPerson.LastAction;
    public static float LastActionDuration => ThirdPerson.LastDuration;
    public static float LastActionSkip => ThirdPerson.LastSkip;

    // Where the hero's soles are: the puppet's root goes here.
    public static Vector3 Feet(FirstPersonController fpc)
    {
        var cc = fpc.characterController;
        var pt = fpc.transform;
        return pt.position + Vector3.up * ((cc.center.y - cc.height * 0.5f) * pt.lossyScale.y);
    }
}
