using System;
using System.IO;
using System.Linq;
using System.Text;
using MelonLoader.Utils;
using UnityEngine;

namespace FellSellCharacter;

// Developer test harness, off by default (hidden setting DevCommands). Lines written to
// UserData/FellSellCharacter/dev_cmd.txt run once; results are appended to dev_out.txt.
internal static class Dev
{
    static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellCharacter");
    static float _next;

    internal static void Tick()
    {
        if (!Settings.DevCommands.Value || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.3f;
        var file = Path.Combine(Dir, "dev_cmd.txt");
        if (!File.Exists(file)) return;
        string[] lines;
        try { lines = File.ReadAllLines(file); File.Delete(file); } catch { return; }
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            string result;
            try { result = Run(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)); }
            catch (Exception e) { result = "ERROR " + e; }
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] > {line}\n{result}");
        }
        Directory.CreateDirectory(Dir);
        File.AppendAllText(Path.Combine(Dir, "dev_out.txt"), sb.ToString());
    }

    static string Run(string[] a)
    {
        switch (a[0])
        {
            case "info":
                return $"scene {Plugin.Scene} tp {Settings.ThirdPerson.Value} preview {ThirdPerson.Preview} body {(ThirdPerson.Body != null && ThirdPerson.Body.Alive)} anim {ThirdPerson.Anim?.Ready} hooks late {ThirdPerson.LateHookAlive} render {ThirdPerson.RenderHookAlive}";
            case "yaw": // yaw <degrees> [pitch]
            {
                var fpc = FirstPersonController.Instance;
                fpc.transform.rotation = Quaternion.Euler(0f, float.Parse(a[1]), 0f);
                if (a.Length > 2) fpc.verticalRotation = float.Parse(a[2]);
                return "ok";
            }
            case "editor": // editor open | close
                if (a[1] == "open") Editor.Open(); else if (a[1] == "close") Editor.Close();
                return "open " + Editor.IsOpen;
            case "held": // held weapon|shield|bow x y z: how a held item is turned in the puppet's hand
            {
                var v = new Vector3(float.Parse(a[2]), float.Parse(a[3]), float.Parse(a[4]));
                if (a[1] == "weapon") ThirdPerson.TurnWeapon = v; else if (a[1] == "shield") ThirdPerson.TurnShield = v; else if (a[1] == "staff") ThirdPerson.TurnStaff = v; else ThirdPerson.TurnBow = v;
                ThirdPerson.RetuneHeld();
                return "ok";
            }
            case "cam": // cam <right> <up> <distance>
                Settings.ShoulderRight.Value = float.Parse(a[1]); Settings.ShoulderUp.Value = float.Parse(a[2]); Settings.Distance.Value = float.Parse(a[3]);
                return "ok";
            case "pose": // pose crouch|air thigh calf foot spine
            {
                var v = new Vector4(float.Parse(a[2]), float.Parse(a[3]), float.Parse(a[4]), float.Parse(a[5]));
                if (a[1] == "crouch") ThirdPerson.CrouchPose = v; else ThirdPerson.AirPose = v;
                return "ok";
            }
            case "view":
                Settings.ThirdPerson.Value = a[1] == "tp";
                return "third person " + Settings.ThirdPerson.Value;
            case "preview":
                ThirdPerson.Preview = a[1] == "on";
                if (a.Length > 4) { ThirdPerson.OrbitYaw = float.Parse(a[2]); ThirdPerson.OrbitPitch = float.Parse(a[3]); ThirdPerson.OrbitDist = float.Parse(a[4]); }
                return "preview " + ThirdPerson.Preview;
            case "play":
                ThirdPerson.Anim?.Play(Enum.Parse<PuppetAction>(a[1], true), a.Length > 2 ? float.Parse(a[2]) : 0f);
                return "ok";
            case "part": // part <slot number> <part name | none>
            {
                var look = ThirdPerson.Look ?? Look.LoadSaved();
                int slot = int.Parse(a[1]);
                if (a[2] == "none") look.Parts.Remove(slot); else look.Parts[slot] = a[2];
                ThirdPerson.ApplyLook(look);
                return "ok";
            }
            case "look": // look default | look <property> <value>
            {
                var look = a[1] == "default" ? Look.Default() : (ThirdPerson.Look ?? Look.LoadSaved());
                if (a[1] != "default")
                {
                    var prop = typeof(Look).GetProperty(a[1]);
                    if (prop == null) return "no such property";
                    string v = string.Join(" ", a.Skip(2));
                    prop.SetValue(look, prop.PropertyType == typeof(float) ? float.Parse(v) : v);
                }
                ThirdPerson.ApplyLook(look);
                return look.ToJson();
            }
            default: return "unknown command";
        }
    }
}
