using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace FellSellBows;

// A bow is a staff underneath: the game's staff code fires the shot (attack animation, projectile
// spawn, damage). The mod clones a vanilla staff ItemData and swaps the parts that make it a bow:
// the held model (the enemy archer's bow), the projectile (an arrow that flies straight and fast),
// no mana cost, its own name and stats.
internal static class Bows
{
    internal sealed class Def
    {
        public string Id, NameRu, NameEn;
        public ItemRarity Rarity;
        public float DamageMul, PriceMul, Scale;
        public Color Tint; // used only when the bow's own model (Art) is missing; Color.clear = no tint
        public string Art; // the bow's own model and drawn icon: bow_<Art>.bin, icon_<Art>.rgba (see BowArt)
        public float AttackSpeed = 1.3f;
        public ItemSubType DamageClass = ItemSubType.None;    // also counts as this weapon class
        public bool DamageLikeStrongest;                      // damage = strongest weapon of DamageClass and rarity
        public bool DamageLikeWeakestSword;                   // damage = weakest sword of the rarity
        public float CritChance;                              // crit chance (0..1), the item's own crit bonus
        public bool PierceShield;                             // arrows ignore an enemy's block
        public float StunChance;                              // the game's stun roll, as a share (0..1)
        public float DamageBonus, SpeedBonus;                 // shares, the item's own damage / attack speed bonus
        public float Weight = 3.2f;
    }

    internal static readonly Def[] Defs =
    {
        // Mad's line-up: three bows, one per rarity.
        // Green: hits like the strongest green mace, attack speed 0.8.
        new() { Id = "FSB_Bow_Hunter", NameRu = "Охотничий лук", NameEn = "Hunter's Bow", Rarity = ItemRarity.Uncommon, DamageMul = 1.3f, PriceMul = 1.6f, Scale = 1.1f, Tint = new Color(0.55f, 0.85f, 0.45f), Art = "hunter",
                AttackSpeed = 0.8f, DamageClass = ItemSubType.Hammer, DamageLikeStrongest = true,
                DamageBonus = 0.05f, SpeedBonus = 0.05f, CritChance = 0.07f, Weight = 3.2f },
        // Blue: light and fast, about the weakest blue sword, attack speed 1.3.
        new() { Id = "FSB_Bow_Swift",  NameRu = "Быстрый лук",   NameEn = "Swift Bow",    Rarity = ItemRarity.Rare,     DamageMul = 1.6f, PriceMul = 2.5f, Scale = 0.9f, Tint = new Color(0.45f, 0.65f, 1f), Art = "swift",
                AttackSpeed = 1.3f, DamageLikeWeakestSword = true,
                SpeedBonus = 0.08f, CritChance = 0.15f, Weight = 2.4f },
        // Purple: slow (0.2), hits like the strongest purple mace, stuns and goes through a block.
        new() { Id = "FSB_Bow_Heavy",  NameRu = "Тяжёлый лук",   NameEn = "Heavy Bow",    Rarity = ItemRarity.Epic,     DamageMul = 3.0f, PriceMul = 5.0f, Scale = 1.25f, Tint = new Color(0.55f, 0.25f, 0.85f), Art = "heavy",
                AttackSpeed = 0.2f, DamageClass = ItemSubType.Hammer, DamageLikeStrongest = true,
                DamageBonus = 0.10f, CritChance = 0.25f, StunChance = 0.40f, PierceShield = true, Weight = 5.6f },
    };

    // Bows from earlier builds that may sit in saves: they load as their replacement.
    internal static readonly Dictionary<string, string> Retired = new()
    {
        ["FSB_Bow_Long"] = "FSB_Bow_Hunter",
        ["FSB_Bow_Bone"] = "FSB_Bow_Swift",
        ["FSB_Bow_Blood"] = "FSB_Bow_Heavy",
        ["FSB_Bow_Cursed"] = "FSB_Bow_Heavy",
    };

    const float ArrowSpeedMul = 3f;       // relative to the base staff's projectile speed
    const float ArrowMinSpeed = 60f;
    const float HeldLength = 1.0f;        // metres, the bow as seen in the hand

    static readonly Dictionary<string, Def> ById = Defs.ToDictionary(d => d.Id);
    internal enum TextKind { Name, Desc, Action }
    static readonly Dictionary<IntPtr, (Def d, TextKind k)> Texts = new();
    internal static readonly List<ItemData> Made = new();
    static readonly HashSet<IntPtr> MadePtr = new();
    static GameObject _holder, _arrowTemplate;
    static readonly Dictionary<string, GameObject> BowPrefabs = new();
    static bool _bowMeshApplied, _arrowMeshApplied;

    internal static bool IsOurs(ItemData i) => i != null && MadePtr.Contains(i.Pointer);
    internal static Def DefOf(ItemData i) => i != null && i.name != null && ById.TryGetValue(i.name, out var d) ? d : null;
    static bool Ru
    {
        get
        {
            try { return LocalizationSettings.SelectedLocale?.Identifier.Code?.StartsWith("ru") == true; }
            catch { return false; }
        }
    }
    internal static string Name(Def d) => Ru ? d.NameRu : d.NameEn;
    internal static string ShootText => Ru ? "Выстрел" : "Shoot";
    internal static string Desc(Def d)
    {
        var parts = new List<string>();
        if (d.DamageBonus > 0f) parts.Add(Ru ? $"+{d.DamageBonus * 100:0}% к урону" : $"+{d.DamageBonus * 100:0}% damage");
        if (d.SpeedBonus > 0f) parts.Add(Ru ? $"+{d.SpeedBonus * 100:0}% к скорости" : $"+{d.SpeedBonus * 100:0}% speed");
        if (d.CritChance > 0f) parts.Add(Ru ? $"+{d.CritChance * 100:0}% к криту" : $"+{d.CritChance * 100:0}% crit chance");
        string head = Ru
            ? "Двуручный лук. Зажми, чтобы натянуть, отпусти, чтобы выстрелить."
            : "Two-handed bow. Hold to draw, release to shoot.";
        return parts.Count == 0 ? head : head + "\n" + string.Join(", ", parts);
    }
    internal static bool TryText(IntPtr p, out string s)
    {
        s = null;
        if (!Texts.TryGetValue(p, out var t)) return false;
        s = t.k switch { TextKind.Name => Name(t.d), TextKind.Desc => Desc(t.d), _ => ShootText };
        return true;
    }

    static ItemDatabaseSO Db => ItemDatabaseUtility._database;

    // Builds the bow items once (needs the vanilla database for the base staff) and keeps them
    // in the database and the name cache, so saves, shops and dev tools find them by name.
    internal static void Ensure()
    {
        var db = Db;
        if (db == null || db.allItems == null) return;
        if (Made.Count == 0) Build(db);
        if (Made.Count == 0) return;
        foreach (var it in Made)
        {
            bool has = false;
            for (int i = 0; i < db.allItems.Count; i++) if (db.allItems[i] != null && db.allItems[i].Pointer == it.Pointer) { has = true; break; }
            if (!has) db.allItems.Add(it);
            ItemDatabaseUtility.RegisterItemToCache(it);
        }
    }

    static void Build(ItemDatabaseSO db)
    {
        var all = db.allItems;
        var staffs = new List<ItemData>();
        for (int i = 0; i < all.Count; i++)
        {
            var it = all[i];
            if (it == null || it.upgradeLevel != 0 || it.staffProjectilePrefab == null) continue;
            if (it.subTypes == null || !it.subTypes.Contains(ItemSubType.Staff)) continue;
            staffs.Add(it);
        }
        var baseStaff = staffs.OrderBy(s => (int)s.rarity).ThenBy(s => s.damage).FirstOrDefault();
        if (baseStaff == null) { Plugin.L.Error("No vanilla staff found: bows not created."); return; }

        _holder = new GameObject("FSB_Templates");
        _holder.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(_holder);
        _holder.hideFlags = HideFlags.HideAndDontSave;

        _arrowTemplate = UnityEngine.Object.Instantiate(baseStaff.staffProjectilePrefab, _holder.transform);
        _arrowTemplate.name = "FSB_Arrow";
        float arrowSpeed = Mathf.Max(baseStaff.staffProjectileSpeed * ArrowSpeedMul, ArrowMinSpeed);

        foreach (var d in Defs)
        {
            var it = UnityEngine.Object.Instantiate(baseStaff);
            it.name = d.Id;
            it.hideFlags = HideFlags.DontUnloadUnusedAsset;
            it.rarity = d.Rarity;
            it.damage = d.DamageLikeStrongest
                ? StrongestOf(all, d.DamageClass, d.Rarity, Mathf.Max(1, Mathf.RoundToInt(baseStaff.damage * d.DamageMul)))
                : BowDamage(all, d.Rarity, Mathf.Max(1, Mathf.RoundToInt(baseStaff.damage * d.DamageMul)), d.DamageLikeWeakestSword ? 0 : 1);
            it.weaponAttackSpeed = d.AttackSpeed;
            it.weight = d.Weight;
            // The game's own item bonuses, shown in the tooltip like on vanilla weapons: fractions for
            // damage and speed, percent points for crit. Arrows get the crit; the attack speed bonus only
            // reaches the draw through PlayerStats (see BowDraw.AttackSpeedMultiplier).
            it.dungeonBonusAttackDamage = d.DamageBonus;
            it.dungeonAttackSpeed = d.SpeedBonus;
            it.dungeonBonusCritChance = d.CritChance * 100f;
            if (d.StunChance > 0f)
            {
                // Vanilla stun chances are percent points (up to 52.5) and last 1 s; match their scale.
                float maxStun = 0f, dur = 0f;
                for (int i = 0; i < all.Count; i++) { var w = all[i]; if (w != null && w.stunChance > maxStun) { maxStun = w.stunChance; dur = w.stunDuration; } }
                it.stunChance = maxStun > 1.001f ? d.StunChance * 100f : d.StunChance;
                if (it.stunDuration <= 0f) it.stunDuration = dur > 0f ? dur : 1f;
            }
            if (d.DamageClass != ItemSubType.None && it.subTypes != null && !it.subTypes.Contains(d.DamageClass)) it.subTypes.Add(d.DamageClass);
            it.manaCost = 0f;
            it.idealPrice = Mathf.Max(1, Mathf.RoundToInt(baseStaff.idealPrice * d.PriceMul));
            it.buyPrice = Mathf.Max(1, Mathf.RoundToInt(baseStaff.buyPrice * d.PriceMul));
            it.staffProjectilePrefab = _arrowTemplate;
            it.staffProjectileSpeed = arrowSpeed;
            it.baseItem = null;

            var prefab = UnityEngine.Object.Instantiate(baseStaff.prefab, _holder.transform);
            prefab.name = d.Id;
            BowPrefabs[d.Id] = prefab;
            it.prefab = prefab;
            var drawn = BowArt.Icon(d.Art, d.Id);
            if (drawn != null) it.icon = drawn;

            it.localizedName = new LocalizedString();
            it.localizedDescription = new LocalizedString();
            it.localizedAction = new LocalizedString();
            it.localizedAction2 = new LocalizedString();
            Texts[it.localizedName.Pointer] = (d, TextKind.Name);
            Texts[it.localizedDescription.Pointer] = (d, TextKind.Desc);
            Texts[it.localizedAction.Pointer] = (d, TextKind.Action);
            Texts[it.localizedAction2.Pointer] = (d, TextKind.Action);

            Made.Add(it);
            MadePtr.Add(it.Pointer);
        }
        Plugin.L.Msg($"Bows created: {string.Join(", ", Made.Select(m => $"{m.name} {m.rarity} dmg {m.damage}"))}; arrow speed {arrowSpeed} (staff {baseStaff.staffProjectileSpeed}).");
    }

    // The weakest sword of the rarity, minus `less`. Rarities without a sword take the nearest lower
    // rarity's; no sword at all keeps the fallback.
    static int BowDamage(Il2CppSystem.Collections.Generic.List<ItemData> all, ItemRarity rarity, int fallback, int less = 1)
    {
        for (int r = (int)rarity; r >= 0; r--)
        {
            int min = int.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (it == null || it.upgradeLevel != 0 || (int)it.rarity != r || it.damage <= 0) continue;
                if (it.subTypes == null || !it.subTypes.Contains(ItemSubType.Sword)) continue;
                min = Math.Min(min, it.damage);
            }
            if (min != int.MaxValue) return Math.Max(1, min - less);
        }
        return fallback;
    }

    // The highest damage among weapons of this class and rarity (the heavy bow hits like the best mace).
    static int StrongestOf(Il2CppSystem.Collections.Generic.List<ItemData> all, ItemSubType cls, ItemRarity rarity, int fallback)
    {
        int best = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var it = all[i];
            if (it == null || it.upgradeLevel != 0 || it.rarity != rarity || it.subTypes == null || !it.subTypes.Contains(cls)) continue;
            best = Math.Max(best, it.damage);
        }
        return best > 0 ? best : fallback;
    }

    static Mesh FindMesh(string name)
    {
        foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>()) if (m != null && m.name == name) return m;
        return null;
    }

    // The game's bow mesh has a rigid string modelled in (and is not readable at runtime), so the held
    // bow uses a copy of it with the string cut out, shipped inside the mod; BowString draws the string.
    static Mesh _bowNoString;
    static Mesh BowWithoutString(Mesh original)
    {
        if (_bowNoString != null) return _bowNoString;
        try
        {
            using var st = typeof(Bows).Assembly.GetManifestResourceStream("FellSellBows.bow_nostring.bin");
            using var r = new System.IO.BinaryReader(st);
            var m = BowArt.ReadMesh(r, "FSB_Bow_NoString");
            m.bounds = original.bounds; // every placement is measured from the original's bounds
            _bowNoString = m;
            return m;
        }
        catch (Exception e)
        {
            Plugin.L.Warning("Bow mesh without string failed, the original is used: " + e.Message);
            return original;
        }
    }

    // A material that already draws this mesh somewhere (the archer's own bow / arrow).
    static Material FindMaterialFor(Mesh mesh)
    {
        foreach (var mf in Resources.FindObjectsOfTypeAll<MeshFilter>())
        {
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.Pointer != mesh.Pointer) continue;
            var r = mf.GetComponent<MeshRenderer>();
            if (r != null && r.sharedMaterial != null) return r.sharedMaterial;
        }
        return null;
    }

    internal static void UpgradeVisuals(string scene)
    {
        if (Made.Count == 0) return;
        if (!_bowMeshApplied)
        {
            var bow = FindMesh("SM_Prop_Bow_01");
            if (bow != null)
            {
                var mat = FindMaterialFor(bow);
                var convex = FindMesh("SM_Prop_Bow_01_Convex");
                foreach (var d in Defs) ApplyBowVisual(BowPrefabs[d.Id], d, bow, mat, convex);
                _bowMeshApplied = true;
                Plugin.L.Msg($"Bow models applied in scene {scene}.");
            }
        }
        if (!_arrowMeshApplied)
        {
            var arrow = FindMesh("SM_Arrow_01");
            if (arrow != null)
            {
                ApplyArrowVisual(_arrowTemplate, arrow, FindMaterialFor(arrow));
                _arrowMeshApplied = true;
            }
        }
    }

    // The staff prefab keeps its scripts (pickup, equip, colliders); only what is drawn changes.
    static void ApplyBowVisual(GameObject prefab, Def d, Mesh bow, Material mat, Mesh convex)
    {
        MeshFilter main = null;
        float best = -1;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            float s = mf.sharedMesh.bounds.size.sqrMagnitude;
            if (s > best) { best = s; main = mf; }
        }
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            if (main == null || r.gameObject.Pointer != main.gameObject.Pointer) r.enabled = false;
        foreach (var l in prefab.GetComponentsInChildren<Light>(true)) l.enabled = false;
        if (main == null) { Plugin.L.Warning($"{d.Id}: staff prefab has no mesh to replace."); return; }

        var model = BowArt.Model(d.Art);
        main.sharedMesh = model ?? BowWithoutString(bow);
        // The staff mesh hangs from its grip near the top; the bow is held at its middle:
        // centre the bow on the holder and size it to about a metre.
        main.transform.localScale = Vector3.one * (HeldLength / Mathf.Max(0.01f, bow.bounds.size.y)) * d.Scale;
        main.transform.localPosition = -(main.transform.localRotation * Vector3.Scale(bow.bounds.center, main.transform.localScale));
        var mr = main.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var m = new Material(mat ?? mr.sharedMaterial);
            if (model == null && d.Tint != Color.clear)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", d.Tint);
                if (m.HasProperty("_Color")) m.SetColor("_Color", d.Tint);
            }
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            mr.sharedMaterial = m;
            var item = Made.FirstOrDefault(i => i.name == d.Id);
            // The drawn icon was set when the item was built; without one, render the model.
            if (item != null && BowArt.Icon(d.Art, d.Id) == null)
            {
                var icon = IconRender.Render(model ?? bow, m, d.Id);
                if (icon != null) item.icon = icon;
            }
        }
        foreach (var mc in prefab.GetComponentsInChildren<MeshCollider>(true)) mc.sharedMesh = convex ?? bow;
    }

    static void ApplyArrowVisual(GameObject proj, Mesh arrow, Material mat)
    {
        foreach (var r in proj.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (var l in proj.GetComponentsInChildren<Light>(true)) l.enabled = false;
        var go = new GameObject("FSB_ArrowMesh");
        go.transform.SetParent(proj.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = arrow;
        var mr = go.AddComponent<MeshRenderer>();
        if (mat != null) mr.sharedMaterial = mat;
        // Point the mesh's long axis along +Z (the flight direction).
        var s = arrow.bounds.size;
        if (s.x >= s.y && s.x >= s.z) go.transform.localRotation = Quaternion.FromToRotation(Vector3.right, Vector3.forward);
        else if (s.y >= s.x && s.y >= s.z) go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
    }
}

// Database refreshes rebuild the name cache: re-add the bows each time.
[HarmonyPatch(typeof(ItemDatabaseUtility), nameof(ItemDatabaseUtility.RefreshCache))]
internal static class RefreshCachePatch
{
    static void Postfix()
    {
        try { Bows.Ensure(); } catch (Exception e) { Plugin.L.Error("Ensure failed: " + e); }
    }
}

// A save can be read before any scene hook ran: resolve bow names on demand.
[HarmonyPatch(typeof(ItemDatabaseUtility), nameof(ItemDatabaseUtility.GetItemByName))]
internal static class GetItemByNamePatch
{
    static void Postfix(string name, ref ItemData __result)
    {
        if (__result != null || name == null || !name.StartsWith("FSB_")) return;
        Bows.Ensure();
        if (Bows.Retired.TryGetValue(name, out var now)) name = now;
        var db = ItemDatabaseUtility._database;
        for (int i = 0; db?.allItems != null && i < db.allItems.Count; i++)
            if (db.allItems[i] != null && db.allItems[i].name == name) { __result = db.allItems[i]; return; }
    }
}

// Our items have no localization table entry; answer their texts directly.
[HarmonyPatch(typeof(LocalizedString), nameof(LocalizedString.GetLocalizedString), new Type[0])]
internal static class LocalizedTextPatch
{
    static bool Prefix(LocalizedString __instance, ref string __result)
    {
        if (__instance == null || !Bows.TryText(__instance.Pointer, out var s)) return true;
        __result = s;
        return false;
    }
}

[HarmonyPatch(typeof(ItemData), nameof(ItemData.GetDisplayName))]
internal static class DisplayNamePatch
{
    static void Postfix(ItemData __instance, ref string __result)
    {
        var d = Bows.DefOf(__instance);
        if (d != null) __result = Bows.Name(d);
    }
}

// Arrows fly straight: whatever the staff projectile does to its velocity each frame
// (drop, slowdown) is undone for our items, and the arrow points along its flight.
[HarmonyPatch(typeof(StaffProjectile), nameof(StaffProjectile.Update))]
internal static class ArrowFlightPatch
{
    static void Prefix(StaffProjectile __instance, out Vector3 __state)
    {
        __state = __instance._velocity;
    }

    static void Postfix(StaffProjectile __instance, Vector3 __state)
    {
        if (!Bows.IsOurs(__instance._item)) return;
        if (__state.sqrMagnitude < 0.01f) return;
        __instance._velocity = __state;
        __instance.transform.rotation = Quaternion.LookRotation(__state);
    }
}

[HarmonyPatch(typeof(StaffProjectile), nameof(StaffProjectile.Initialize))]
internal static class ArrowInitPatch
{
    const float ArrowHitVisualScale = 0.2f;
    const float ArrowHitRadius = 1.0f;
    static void Postfix(StaffProjectile __instance, ItemData item, Vector3 velocity)
    {
        if (!Bows.IsOurs(item)) return;
        if (__instance._rigidbody != null) __instance._rigidbody.useGravity = false;
        // A plain arrow: the staff's blast shrunk to a small hit. Damage still goes through the
        // projectile's own collision/explosion code, so the radius stays about a body width.
        __instance._explosionVisualScale *= ArrowHitVisualScale;
        __instance._explosionRadius = Mathf.Min(__instance._explosionRadius, ArrowHitRadius);
        __instance._explosionVolume *= 0.4f;
        if (velocity.sqrMagnitude > 0.01f) __instance.transform.rotation = Quaternion.LookRotation(velocity);
        ArrowHitScopePatch.Pulls[__instance.Pointer] = BowDraw.LastPull;
        if (ArrowHitScopePatch.Pulls.Count > 300) ArrowHitScopePatch.Pulls.Clear();
    }
}

// While a bow is held in one hand, the other hand does not attack.
internal static class TwoHanded
{
    internal static bool IsBow(GameObject held) => held != null && held.name != null && held.name.StartsWith("FSB_Bow");
}

[HarmonyPatch(typeof(PlayerCombat), nameof(PlayerCombat.ExecuteLeftHandAttack))]
internal static class LeftHandBlockPatch
{
    // With a bow in the left hand the game's left attack (the staff shot) runs only when the bow
    // looses an arrow (BowDraw.Fire); nothing else may trigger it.
    static bool Prefix()
    {
        var eq = PlayerEquipment.Instance;
        if (eq == null) return true;
        if (TwoHanded.IsBow(eq.currentLeftHandItem)) return BowDraw.Firing;
        return !TwoHanded.IsBow(eq.currentRightHandItem);
    }
}

[HarmonyPatch(typeof(PlayerCombat), nameof(PlayerCombat.ExecuteRightHandAttack))]
internal static class RightHandBlockPatch
{
    static bool Prefix()
    {
        var eq = PlayerEquipment.Instance;
        return eq == null || !TwoHanded.IsBow(eq.currentLeftHandItem) || TwoHanded.IsBow(eq.currentRightHandItem);
    }
}

// The bow is two-handed and always sits in the left hand: equipping it moves it there and clears
// the right hand; putting something into the right hand sends the bow back to the backpack.
internal static class BowHands
{
    static bool _busy;
    internal static bool IsBowItem(InventorySlot s) => s?.item != null && Bows.IsOurs(s.item);

    static float _nextCheck;
    internal static void Tick()
    {
        if (_busy || UnityEngine.Time.unscaledTime < _nextCheck) return;
        _nextCheck = UnityEngine.Time.unscaledTime + 0.5f;
        var inv = InventoryManager.Instance;
        if (inv == null || inv.leftHandSlot == null || inv.rightHandSlot == null) return;
        _busy = true;
        try
        {
            if (IsBowItem(inv.rightHandSlot))
            {
                if (inv.leftHandSlot.item != null) inv.UnequipItem(inv.leftHandSlot);
                inv.EquipToHand(inv.rightHandSlot, true);
            }
            else if (IsBowItem(inv.leftHandSlot) && inv.rightHandSlot.item != null) inv.UnequipItem(inv.rightHandSlot);
        }
        catch (Exception e) { Plugin.L.Warning("Two-handed bow check failed: " + e.Message); }
        finally { _busy = false; }
    }

    internal static void AfterEquip(bool wasBow)
    {
        if (_busy) return;
        var inv = InventoryManager.Instance;
        if (inv == null) return;
        _busy = true;
        try
        {
            if (IsBowItem(inv.leftHandSlot) && inv.rightHandSlot?.item != null)
            {
                if (wasBow) inv.UnequipItem(inv.rightHandSlot);
                else inv.UnequipItem(inv.leftHandSlot);
            }
        }
        catch (Exception e) { Plugin.L.Warning("Two-handed bow swap failed: " + e.Message); }
        finally { _busy = false; }
    }
}

[HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.EquipToHand))]
internal static class BowEquipToHandPatch
{
    static void Prefix(InventorySlot slotToEquip, ref bool toLeftHand, out bool __state)
    {
        __state = BowHands.IsBowItem(slotToEquip);
        if (__state) toLeftHand = true;
    }
    static void Postfix(bool __state) => BowHands.AfterEquip(__state);
}

[HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.EquipItem))]
internal static class BowEquipItemPatch
{
    static void Prefix(InventorySlot slotToEquip, ref bool forceLeftHand, out bool __state)
    {
        __state = BowHands.IsBowItem(slotToEquip);
        if (__state) forceLeftHand = true;
    }
    static void Postfix(bool __state) => BowHands.AfterEquip(__state);
}

// The action hint under the crosshair says "Cast spell" for staves; with a bow in hand it says "Shoot".
[HarmonyPatch(typeof(LocalizedString), nameof(LocalizedString.GetLocalizedString), new Type[0])]
internal static class ShootHintPatch
{
    static bool Prefix(LocalizedString __instance, ref string __result)
    {
        var hints = ContextualActionHintsUI.Instance;
        if (__instance == null || hints == null || hints._castLocalizedString == null || __instance.Pointer != hints._castLocalizedString.Pointer) return true;
        var eq = PlayerEquipment.Instance;
        if (eq == null || !(TwoHanded.IsBow(eq.currentLeftHandItem) || TwoHanded.IsBow(eq.currentRightHandItem))) return true;
        __result = Bows.ShootText;
        return false;
    }
}

// With a bow in hand both game attacks are skipped: BowDraw reads the left button itself.
[HarmonyPatch(typeof(PlayerCombat), nameof(PlayerCombat.HandlePrimaryAttack))]
internal static class BowPrimaryPatch
{
    static bool Prefix()
    {
        var eq = PlayerEquipment.Instance;
        return eq == null || !(TwoHanded.IsBow(eq.currentLeftHandItem) || TwoHanded.IsBow(eq.currentRightHandItem));
    }
}

[HarmonyPatch(typeof(PlayerCombat), nameof(PlayerCombat.HandleSecondaryAttack))]
internal static class BowSecondaryPatch
{
    static bool Prefix()
    {
        var eq = PlayerEquipment.Instance;
        return eq == null || !(TwoHanded.IsBow(eq.currentLeftHandItem) || TwoHanded.IsBow(eq.currentRightHandItem));
    }
}

// While one of our arrows resolves its hit (collision or blast), Current is its bow and CurrentPull
// how far the string was drawn for it.
[HarmonyPatch(typeof(StaffProjectile), nameof(StaffProjectile.HandleCollision))]
internal static class ArrowHitScopePatch
{
    internal static Bows.Def Current;
    internal static float CurrentPull = 1f;
    internal static readonly Dictionary<IntPtr, float> Pulls = new();
    internal static void Enter(StaffProjectile p)
    {
        Current = Bows.DefOf(p._item);
        CurrentPull = Current != null && Pulls.TryGetValue(p.Pointer, out var v) ? v : 1f;
    }
    static void Prefix(StaffProjectile __instance) => Enter(__instance);
    static void Postfix() => Current = null;
}

[HarmonyPatch(typeof(StaffProjectile), nameof(StaffProjectile.SpawnExplosion))]
internal static class ArrowBlastScopePatch
{
    static void Prefix(StaffProjectile __instance) => ArrowHitScopePatch.Enter(__instance);
    static void Postfix() => ArrowHitScopePatch.Current = null;
}

[HarmonyPatch(typeof(Il2CppEmeraldAI.EmeraldHealth), nameof(Il2CppEmeraldAI.EmeraldHealth.Damage))]
internal static class ArrowDrawPenaltyPatch
{
    static void Prefix(ref int DamageAmount, ref bool CriticalHit)
    {
        if (ArrowHitScopePatch.Current == null || ArrowPierceShieldPatch.Again) return;
        // Mad: an early release is punished hard: damage falls with the square of the draw, and
        // only a full draw can crit (or stun, see ArrowStunPatch).
        float pull = ArrowHitScopePatch.CurrentPull;
        if (pull < 0.999f)
        {
            DamageAmount = Math.Max(1, Mathf.RoundToInt(DamageAmount * pull * pull));
            if (CriticalHit) { CriticalHit = false; DamageAmount = Math.Max(1, DamageAmount / 2); }
        }
    }
}

[HarmonyPatch(typeof(Il2CppEmeraldAI.EmeraldHealth), nameof(Il2CppEmeraldAI.EmeraldHealth.TriggerStun))]
internal static class ArrowStunPatch
{
    // No stun from an arrow loosed before full draw.
    static bool Prefix() => ArrowHitScopePatch.Current == null || ArrowHitScopePatch.CurrentPull >= 0.999f;
}

// Mad: the heavy bow's arrows go through shields. The game decides a block inside
// EmeraldHealth.Damage, against an attacker standing in front of the shield (it never asks
// EmeraldCombat.IsBlocking, which the 1.0.0 hook sat on, and the enemy's blocking flags are still
// down when the blow arrives). So a piercing arrow that a shield stopped lands again as a blow
// with no attacker, which the block does not judge.
[HarmonyPatch(typeof(Il2CppEmeraldAI.EmeraldHealth), nameof(Il2CppEmeraldAI.EmeraldHealth.Damage))]
internal static class ArrowPierceShieldPatch
{
    internal static bool Again; // the second landing is running: no draw penalty and no third try

    static void Prefix(Il2CppEmeraldAI.EmeraldHealth __instance, out int __state) => __state = __instance.CurrentHealth;

    static void Postfix(Il2CppEmeraldAI.EmeraldHealth __instance, int DamageAmount, int RagdollForce, bool CriticalHit, int __state)
    {
        if (Again || !(BowsCoop.ForcePierce || BowsCoop.PiercingNow)) return;
        if (DamageAmount <= 0 || __state <= 0 || __instance.CurrentHealth != __state || __instance.Immortal) return;
        var animator = __instance.GetComponent<Il2CppEmeraldAI.EmeraldSystem>()?.AIAnimator;
        if (animator == null || !animator.GetBool("Blocking")) return;
        Again = true;
        try { __instance.Damage(DamageAmount, null, RagdollForce, CriticalHit); }
        finally { Again = false; }
        BowsCoop.Pierced++;
    }
}

// For the co-op mod (found by name, no reference either way): a partner's arrow lands on the host's
// enemy, so the host has to be told that it goes through shields.
public static class BowsCoop
{
    // A shield-piercing arrow of this game is resolving its hit right now.
    public static bool PiercingNow => ArrowHitScopePatch.Current != null && ArrowHitScopePatch.Current.PierceShield;
    // Held on by the co-op host while it applies a partner's piercing arrow.
    public static bool ForcePierce;
    // How many blocks arrows have gone through (for the log).
    public static int Pierced;
}

// Arrows are the staff's projectile underneath, so the game would scale them by the prayers'
// magical damage bonus. A bow is physical: while an arrow is fired (the staff attack spawns it a
// frame or two late, hence JustFired) or resolves its hit, the magical multiplier answers with the
// physical one.
[HarmonyPatch(typeof(PrayersManager), nameof(PrayersManager.GetMagicalDamageMultiplier))]
internal static class ArrowPhysicalDamagePatch
{
    static bool Prefix(PrayersManager __instance, ref float __result)
    {
        if (!BowDraw.Firing && !BowDraw.JustFired && ArrowHitScopePatch.Current == null) return true;
        __result = __instance.GetPhysicalDamageMultiplier();
        return false;
    }
}
