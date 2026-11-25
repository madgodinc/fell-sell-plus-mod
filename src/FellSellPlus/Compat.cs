using System;
using MelonLoader;

namespace FellSellPlus;

// Thin layer over MelonPreferences and MelonLogger so feature code keeps a small, stable API.
// Settings live in UserData/MelonPreferences.cfg, edited outside the game.

internal sealed class AcceptableValueRange<T> where T : IComparable<T>
{
    public readonly T Min, Max;
    public AcceptableValueRange(T min, T max) { Min = min; Max = max; }
}

internal sealed class ConfigDescription
{
    public readonly string Text;
    public readonly object Range;
    public ConfigDescription(string text, object range = null) { Text = text; Range = range; }
}

internal sealed class ConfigEntry<T>
{
    readonly MelonPreferences_Entry<T> _e;
    readonly Func<T, T> _clamp;
    public ConfigEntry(MelonPreferences_Entry<T> e, Func<T, T> clamp) { _e = e; _clamp = clamp; }
    public T Value { get => _clamp(_e.Value); set => _e.Value = value; }
}

internal sealed class ConfigFile
{
    // Every bound setting, exported to UserData/FellSellPlus/config_schema.json so the external
    // configurator knows types, defaults and ranges.
    internal readonly System.Collections.Generic.List<object> Schema = new();

    public void WriteSchema()
    {
        try
        {
            var dir = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "FellSellPlus");
            System.IO.Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                configFile = System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "MelonPreferences.cfg"),
                entries = Schema,
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "config_schema.json"), json);
        }
        catch (Exception e) { MelonLogger.Warning($"Config schema not written: {e.Message}"); }
    }

    public ConfigEntry<T> Bind<T>(string section, string key, T def, ConfigDescription desc)
    {
        object min = null, max = null;
        if (desc?.Range is AcceptableValueRange<int> ri0) { min = ri0.Min; max = ri0.Max; }
        else if (desc?.Range is AcceptableValueRange<float> rf0) { min = rf0.Min; max = rf0.Max; }
        Schema.Add(new { section = "FellSellPlus_" + section, key, type = typeof(T).Name, @default = def, min, max, description = desc?.Text });
        var cat = MelonPreferences.GetCategory("FellSellPlus_" + section)
                  ?? MelonPreferences.CreateCategory("FellSellPlus_" + section, "Fell & Sell Plus: " + section);
        var e = cat.GetEntry<T>(key) ?? cat.CreateEntry(key, def, key, desc?.Text);
        Func<T, T> clamp = v => v;
        if (desc?.Range is AcceptableValueRange<int> ri && typeof(T) == typeof(int))
            clamp = v => (T)(object)Math.Clamp((int)(object)v, ri.Min, ri.Max);
        else if (desc?.Range is AcceptableValueRange<float> rf && typeof(T) == typeof(float))
            clamp = v => (T)(object)Math.Clamp((float)(object)v, rf.Min, rf.Max);
        return new ConfigEntry<T>(e, clamp);
    }

    public ConfigEntry<T> Bind<T>(string section, string key, T def, string text) =>
        Bind(section, key, def, new ConfigDescription(text));
}

internal sealed class Log
{
    public void LogInfo(string m) => MelonLogger.Msg(m);
    public void LogWarning(string m) => MelonLogger.Warning(m);
    public void LogError(string m) => MelonLogger.Error(m);
}
