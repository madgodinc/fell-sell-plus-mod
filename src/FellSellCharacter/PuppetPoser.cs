using UnityEngine;

namespace FellSellCharacter;

// Touches on top of the clips for one puppet: bent legs for crouch and jump (the game has no human
// clip for either) and a lean with the view pitch. One instance per puppet, so several bodies
// (the local hero and co-op partners) keep their own blend state.
public sealed class PuppetPoser
{
    float _crouch, _air;
    static readonly string[] Sides = { "_l", "_r" };

    public void Apply(Puppet body, in PuppetState state, float dt)
    {
        if (body == null || !body.Alive) return;
        var root = body.Root.transform;
        Vector3 axis = root.right;
        _crouch = Mathf.MoveTowards(_crouch, state.Crouch && state.Grounded ? 1f : 0f, dt * 6f);
        _air = Mathf.MoveTowards(_air, state.Grounded ? 0f : 1f, dt * (state.Grounded ? 8f : 5f));
        if (_crouch > 0.001f || _air > 0.001f)
        {
            var footL = body.Bone("foot_l"); var footR = body.Bone("foot_r");
            float before = footL != null && footR != null ? Mathf.Min(footL.position.y, footR.position.y) : 0f;
            Vector4 p = ThirdPerson.CrouchPose * _crouch + ThirdPerson.AirPose * _air;
            foreach (var side in Sides)
            {
                Lean(body, "thigh" + side, p.x, axis);
                Lean(body, "calf" + side, p.y, axis);
                Lean(body, "foot" + side, p.z, axis);
            }
            Lean(body, "spine_01", p.w, axis);
            // Crouching keeps the feet where they stood: the whole body comes down instead.
            if (_crouch > 0.001f && footL != null && footR != null)
                root.position += Vector3.up * ((before - Mathf.Min(footL.position.y, footR.position.y)) * (1f - _air));
        }
        // The body leans with the view pitch, so swings and shots go where the player looks.
        float pitch = Mathf.Clamp(state.Pitch, -70f, 70f);
        if (Mathf.Abs(pitch) < 0.5f) return;
        Lean(body, "spine_02", pitch * 0.22f, axis);
        Lean(body, "spine_03", pitch * 0.22f, axis);
        Lean(body, "neck_01", pitch * 0.2f, axis);
    }

    static void Lean(Puppet body, string bone, float degrees, Vector3 axis)
    {
        var t = body.Bone(bone);
        if (t != null) t.rotation = Quaternion.AngleAxis(degrees, axis) * t.rotation;
    }
}
