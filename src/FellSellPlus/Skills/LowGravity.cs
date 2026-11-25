using HarmonyLib;
using UnityEngine;
using static FellSellPlus.Skills.SkillRegistry;

namespace FellSellPlus.Skills;

// Fighter: Low Gravity, a passive skill:
// - higher jumps (jumpForce +30% / +50% / +73% by rank, about x1.7 / x2.25 / x3 height), legendary;
// - 30% / 40% / 50% less damage taken while in the air;
// - longer dash: x1.5 / x1.75 / x2 on the ground, +0.5 more in the air (dashDuration is scaled, not
//   dashSpeed, so the dash does not get fast enough to skip through walls);
// - gliding, the skill's main feature: hold Jump while falling and you fall 3 times slower.
// The controller's own values are cached once and put back whenever the skill is off.
// Works in the village as well, to help running around the town.
// The game has no fall damage, so landings need no extra handling.
[HarmonyPatch(typeof(FirstPersonController), "Update")]
internal static class LowGravityPatch
{
    // Jump height grows with the square of the force:
    // x1.3/1.5/1.73 force = about x1.7/2.25/3 height, enough for ledges without skipping the level.
    static readonly float[] JumpMul = { 1f, 1.3f, 1.5f, 1.73f };
    internal static readonly float[] DashGround = { 1f, 1.5f, 1.75f, 2f };
    internal const float DashAirExtra = 0.5f; // in the air the dash is this much longer still
    internal static readonly float[] AirArmor = { 0f, 0.30f, 0.40f, 0.50f };
    const float GlideFall = 3f;

    static System.IntPtr _owner;
    static float _gravity = -1f, _jump = -1f, _dash = -1f;
    static bool _glide;

    internal static float BaseDash(FirstPersonController fpc) { Cache(fpc); return _dash; }

    static void Cache(FirstPersonController fpc)
    {
        if (fpc.Pointer != _owner) { _owner = fpc.Pointer; _gravity = _jump = _dash = -1f; _glide = false; }
        if (_gravity < 0f) _gravity = fpc.gravityMultiplier;
        if (_jump < 0f) _jump = fpc.jumpForce;
        if (_dash < 0f) _dash = fpc.dashDuration;
    }

    static void Prefix(FirstPersonController __instance)
    {
        var fpc = __instance;
        Cache(fpc);
        int rank = System.Math.Clamp(RankAnywhere(FighterLowGravity), 0, 3); // works in the village too

        float jump = _jump * JumpMul[rank];
        if (!Mathf.Approximately(fpc.jumpForce, jump)) fpc.jumpForce = jump;
        if (!fpc.IsDashing && !Mathf.Approximately(fpc.dashDuration, _dash)) fpc.dashDuration = _dash;

        var input = fpc.playerInputHandler;
        bool glide = rank > 0 && input != null && !fpc.IsGrounded && input.IsJumpHolding && fpc.currentMovement.y <= 0f;
        if (glide != _glide)
        {
            _glide = glide;
            fpc.gravityMultiplier = glide ? _gravity / GlideFall : _gravity;
        }
    }
}

// The dash length is chosen when the dash starts.
[HarmonyPatch(typeof(FirstPersonController), "StartDash")]
internal static class LowGravityDashPatch
{
    static void Prefix(FirstPersonController __instance)
    {
        int r = System.Math.Clamp(RankAnywhere(FighterLowGravity), 0, 3);
        if (r <= 0) return;
        float baseDash = LowGravityPatch.BaseDash(__instance);
        float k = LowGravityPatch.DashGround[r] + (__instance.IsGrounded ? 0f : LowGravityPatch.DashAirExtra);
        __instance.dashDuration = baseDash * k;
    }
}

// Less damage while airborne.
[HarmonyPatch(typeof(PlayerStats), "TakeDamage", new[] { typeof(float), typeof(Transform), typeof(bool) })]
internal static class LowGravityAirArmorPatch
{
    static void Prefix(ref float amount)
    {
        int r = System.Math.Clamp(Rank(FighterLowGravity), 0, 3);
        if (amount <= 0f || r <= 0) return;
        var fpc = FirstPersonController.Instance;
        if (fpc != null && !fpc.IsGrounded) amount *= 1f - LowGravityPatch.AirArmor[r];
    }
}

// Free Stride: faster movement while the game says the player is not in combat (the same state that
// makes sprinting free). Works in the village too.
[HarmonyPatch(typeof(FirstPersonController), "CurrentSpeed", MethodType.Getter)]
internal static class FreeStridePatch
{
    // Balance: +30/60/100%; triple speed outran the level streaming and the camera.
    static readonly float[] Bonus = { 0f, 0.30f, 0.60f, 1.00f };
    static void Postfix(ref float __result)
    {
        int r = System.Math.Clamp(RankAnywhere(FighterFreeStride), 0, 3); // village too: never in combat there
        if (r <= 0) return;
        var ps = PlayerStats.Instance;
        if (ps == null || ps.IsInCombat) return;
        __result *= 1f + Bonus[r];
    }
}
