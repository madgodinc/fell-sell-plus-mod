using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MelonLoader.Utils;
using UnityEngine;

namespace FellSellPlus.Shop;

// Per-save shop choices (display slots, chosen checkout), keyed by world position because the
// game rebuilds furniture objects on every load. Stored in UserData, never in the game's saves.
internal static class ShopStore
{
    sealed class Data
    {
        public List<string> Display { get; set; } = new();
        public string Checkout { get; set; }
    }

    static Data _data = new();
    static int _slot = int.MinValue;
    static HashSet<string> _display = new();

    static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus");
    static string FileFor(int slot) => Path.Combine(Dir, $"shop_slot{slot}.json");

    public static string Key(Vector3 p) =>
        $"{Mathf.RoundToInt(p.x * 10)},{Mathf.RoundToInt(p.y * 10)},{Mathf.RoundToInt(p.z * 10)}";

    // Reloads when the active save slot changes.
    static void Sync()
    {
        int slot = Progression.GameQuery.CurrentSlot();
        if (slot == _slot) return;
        _slot = slot;
        _data = new Data();
        try
        {
            if (slot >= 0 && File.Exists(FileFor(slot)))
                _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FileFor(slot))) ?? new Data();
        }
        catch (Exception e) { Plugin.L.LogWarning($"Shop store load failed: {e.Message}"); }
        _display = new HashSet<string>(_data.Display);
    }

    static void Save()
    {
        if (_slot < 0) return;
        try
        {
            Directory.CreateDirectory(Dir);
            _data.Display = new List<string>(_display);
            File.WriteAllText(FileFor(_slot), JsonSerializer.Serialize(_data));
        }
        catch (Exception e) { Plugin.L.LogWarning($"Shop store save failed: {e.Message}"); }
    }

    public static bool IsDisplay(Vector3 p) { Sync(); return _display.Contains(Key(p)); }

    public static bool ToggleDisplay(Vector3 p)
    {
        Sync();
        var k = Key(p);
        bool now = !_display.Remove(k);
        if (now) _display.Add(k);
        Save();
        return now;
    }

    public static string Checkout { get { Sync(); return _data.Checkout; } }

    public static void SetCheckout(Vector3 p) { Sync(); _data.Checkout = Key(p); Save(); }

    public static void DeleteSlot(int slot)
    {
        try { if (File.Exists(FileFor(slot))) File.Delete(FileFor(slot)); } catch { }
        if (slot == _slot) { _slot = int.MinValue; }
    }
}
