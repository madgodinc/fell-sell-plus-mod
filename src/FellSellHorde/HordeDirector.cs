using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppEmeraldAI;
using UnityEngine;
using UnityEngine.AI;

namespace FellSellHorde;

// Runs the horde floor: endless waves started at the altar pillar in the arena, bosses on their
// schedule, gifts between waves and the bank chest. Only the authority (solo player or co-op host)
// changes state; everyone enforces the side-room lock for their own body and draws the UI from the
// latest snapshot.
internal static class HordeDirector
{
    internal static HordeSnapshot State;
    static bool _setup;
    static Bounds _arena;
    static Vector3 _arenaCenter, _sideCenter;
    static bool _sideOpen;
    static GameObject _pillar;
    static readonly List<(EmeraldHealth h, EmeraldSystem s, float aggroAt, int aggroLeft, int tier)> Alive = new();
    static float _nextSpawn, _phaseEnd, _nextPublish;
    static readonly List<(PrayersStatue s, List<(Light l, float i)> lights)> Statues = new();
    static readonly List<GameObject> Chests = new();
    internal static string Toast; static float _toastUntil;
    static IHordeNet _subscribedTo;
    static int _seenBoss = -1, _seenGrandma = -1;
    static float _nextBusy;
    static HordePhase _pubPhase; static int _pubWave;

    static IHordeNet Net => HordeNet.Current;

    internal static void Reset()
    {
        _setup = false;
        _setupSince = -1f;
        HordeHud.Show(false);
        _byBiome = null;
        _special.Clear();
        HordeBosses.Reset();
        HordeBalance.Reset();
        HordeBalance.KindOf.Clear();
        HordeBuild.Reset();
        HordeProgress.Reset();
        State = default;
        Alive.Clear();
        Statues.Clear();
        Chests.Clear();
        _bank = null;
        _bankReplaceAt = 0f;
        _pillar = null;
        _sideOpen = false;
        _gate = null;
        _bossGates.Clear();
        _seenBoss = _seenGrandma = -1;
    }

    internal static void Tick()
    {
        if (!HordeGen.Active) return;
        var g = CustomDungeonGenerator.Instance;
        if (g == null) return;
        if (HordeGen.Generating)
        {
            // Restore only after the generator really ran: right after Begin its flag is still
            // false, and restoring then hands the whole floor back to the vanilla settings.
            if (g.isGenerating) { HordeGen.SawGenerating = true; return; }
            if (!HordeGen.SawGenerating && Time.time - HordeGen.BeganAt < 30f) return;
            HordeGen.Restore(g);
        }
        if (!_setup) { if (!Setup(g)) return; }

        EnforceLock();
        MoveGates();
        HordeProgress.Tick();
        HordeHud.Show(true);
        HordeHud.Tick(State, HordeProgress.Level, HordeProgress.Xp, HordeProgress.Threshold(HordeProgress.Level), HordeProgress.ChestPoints);
        if (_bankReplaceAt > 0f && Time.time >= _bankReplaceAt) { _bankReplaceAt = 0f; SpawnBank(g); }
        if (Net.IsAuthority) RunAuthority(g);
        else if (Time.unscaledTime >= _nextBusy) { _nextBusy = Time.unscaledTime + 0.3f; Net.ReportBusy(HordeProgress.Busy); }
    }

    // ---------- setup ----------

    static float _setupSince = -1f;

    static bool Setup(CustomDungeonGenerator g)
    {
        var me = Net.Players().FirstOrDefault();
        if (me == null) return false;
        if (!ReferenceEquals(_subscribedTo, Net)) { _subscribedTo = Net; Net.Command += OnCommand; Net.Snapshot += OnSnapshot; }

        // Let the game finish placing the player, then replace the floor with the horde rooms.
        if (_setupSince < 0f) _setupSince = Time.time;
        if (Time.time - _setupSince < 1.5f) return false;
        var built = HordeBuild.Build(g);
        if (built == HordeBuild.Step.Wait) return false;
        if (built == HordeBuild.Step.Failed) { HordeGen.Active = false; return false; }
        var arena = HordeBuild.Arena;
        _arena = RendererBounds(arena);

        // The statues (only those of the boss room: the cleared floor's are still being destroyed).
        Statues.Clear();
        foreach (var s in UnityEngine.Object.FindObjectsOfType<PrayersStatue>())
        {
            if (!s.transform.IsChildOf(arena.transform)) continue;
            var lights = new List<(Light, float)>();
            foreach (var l in s.GetComponentsInChildren<Light>(true)) lights.Add((l, l.intensity));
            Statues.Add((s, lights));
        }
        if (Statues.Count == 0) { Plugin.L.Error("Horde: no statues in the boss room; horde stopped."); HordeGen.Active = false; return false; }
        // The statues only lend their prayer pools to level-ups; praying at them directly is off.
        int iLayer = InteractionLayer();
        foreach (var (st, _) in Statues)
            foreach (var col in st.GetComponentsInChildren<Collider>(true))
                if (col.gameObject.layer == iLayer) col.gameObject.layer = 0;

        // Hall geometry along the axis from the hall centre to the statues: the entrance at the low
        // end, the statue room past the arch (about 7 m in front of the statues).
        var statueAvg = Vector3.zero;
        foreach (var (s, _) in Statues) statueAvg += s.transform.position;
        statueAvg /= Statues.Count;
        _floorY = statueAvg.y;
        _statueY = statueAvg.y;
        var c0 = new Vector3(_arena.center.x, _floorY, _arena.center.z);
        _axis = Vector3.ProjectOnPlane(statueAvg - c0, Vector3.up).normalized;
        _perp = Vector3.Cross(Vector3.up, _axis);
        _origin = c0;
        float minP = float.MaxValue, maxP = float.MinValue, maxW = 0f;
        foreach (var corner in Corners(_arena))
        {
            var d = corner - c0;
            minP = Mathf.Min(minP, Vector3.Dot(d, _axis)); maxP = Mathf.Max(maxP, Vector3.Dot(d, _axis));
            maxW = Mathf.Max(maxW, Mathf.Abs(Vector3.Dot(d, _perp)));
        }
        _gateP = Vector3.Dot(statueAvg - c0, _axis) - GateBeforeStatues;
        // The hall floor is lower than the statue room: take the most common navmesh height
        // in front of the arch.
        var heights = new Dictionary<int, int>();
        for (float a = minP; a < _gateP; a += 1f)
            for (float x = -6f; x <= 6f; x += 3f)
                if (NavMesh.SamplePosition(At(a, x), out var ph, 3f, NavMesh.AllAreas))
                {
                    int k = Mathf.RoundToInt(ph.position.y * 2f);
                    heights[k] = heights.TryGetValue(k, out var n) ? n + 1 : 1;
                }
        if (heights.Count > 0) _floorY = heights.OrderByDescending(kv => kv.Value).First().Key / 2f;
        _origin = new Vector3(c0.x, _floorY, c0.z);
        _entryP = FindEntry(minP);
        _halfWidth = HallHalfWidth(maxW);
        _hallEndP = HallSpan(minP).end;
        _arenaCenter = OnNav(At((_entryP + _hallEndP) * 0.5f, 0f));
        Plugin.L.Msg($"Horde: hall floor at {_floorY:0.0} (statues {_statueY:0.0}), {_hallEndP - _entryP:0} m long, half width {_halfWidth:0}, altar at {_arenaCenter}.");
        if (_hallEndP - _entryP < 15f) Plugin.L.Warning("Horde: the hall came out shorter than 15 m, its floor was not found where expected; enemies may fail to spawn.");
        _sideCenter = OnNav(statueAvg - _axis * 3f);
        _bossGates.Clear();
        foreach (var bg in arena.GetComponentsInChildren<BossGate>(true))
        {
            try { bg.EnsureTargetPositionsCalculated(); _bossGates.Add(bg); } catch (Exception e) { Plugin.L.Warning("Boss gate: " + e.Message); }
        }
        if (_bossGates.Count == 0) _gate = MakeGate(arena.transform); // fallback: an invisible wall


        // No vanilla chests on a horde floor: the bank chest is the only one.
        foreach (var c in UnityEngine.Object.FindObjectsOfType<DungeonChest>()) UnityEngine.Object.Destroy(c.gameObject);
        foreach (var c in UnityEngine.Object.FindObjectsOfType<BossChest>()) UnityEngine.Object.Destroy(c.gameObject);

        // The boss room's own scripts (door trigger, boss spawner) stay out of the horde.
        foreach (var t in arena.GetComponentsInChildren<BossDoorTrigger>(true)) t.gameObject.SetActive(false);

        _pillar = MakePillar(_arenaCenter); // the player starts in the start room and walks in
        SpawnBank(g);
        _sideOpen = true;
        SetSideOpen(false);
        State = new HordeSnapshot { Phase = HordePhase.Ready, Seed = HordeSettings.Seed.Value };
        _setup = true;
        return true;
    }

    // ---------- hall geometry ----------

    const float GateBeforeStatues = 7f;
    static float _statueY;
    static float _floorY, _gateP, _entryP, _hallEndP, _halfWidth;
    static Vector3 _axis, _perp, _origin;
    static GameObject _gate;
    static readonly List<BossGate> _bossGates = new();

    static Vector3 At(float along, float across) => _origin + _axis * along + _perp * across;
    static float AlongOf(Vector3 p) => Vector3.Dot(p - _origin, _axis);

    static IEnumerable<Vector3> Corners(Bounds b)
    {
        for (int i = 0; i < 4; i++)
            yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, b.center.y, (i & 2) == 0 ? b.min.z : b.max.z);
    }

    static Vector3 OnNav(Vector3 p)
    {
        p.y = _floorY;
        return NavMesh.SamplePosition(p, out var hit, 6f, NavMesh.AllAreas) ? hit.position : p;
    }

    static bool OnHallFloor(Vector3 p) => Mathf.Abs(p.y - _floorY) < 1.2f;

    // Walk from the gate back toward the entrance while there is hall floor somewhere across the
    // hall (the centre line has rubble and the boss dais on it); short gaps are allowed.
    static float FindEntry(float minP) => HallSpan(minP).entry;

    // Hall floor span along the axis: from the gate walk back to the first hall floor (steps up to
    // the arch are higher), then on to the entrance while floor continues (short gaps allowed).
    static (float entry, float end) HallSpan(float minP)
    {
        float end = float.NaN, entry = float.NaN; int misses = 0;
        for (float a = _gateP; a > minP; a -= 1f)
        {
            bool floor = false;
            foreach (var x in new[] { 0f, -2f, 2f, -4f, 4f, -6f, 6f })
                if (NavMesh.SamplePosition(At(a, x), out var hit, 1f, NavMesh.AllAreas) && OnHallFloor(hit.position)) { floor = true; break; }
            if (float.IsNaN(end)) { if (floor) end = entry = a; continue; }
            if (floor) { entry = a; misses = 0; }
            else if (++misses > 5) break;
        }
        if (float.IsNaN(end)) return (_gateP - 10f, _gateP);
        return (entry, end);
    }

    // Widest stretch of hall floor across the centre of the hall.
    static float HallHalfWidth(float maxW)
    {
        var span = HallSpan(-1000f);
        float mid = (span.entry + span.end) * 0.5f, w = 2f;
        for (float x = 1f; x < maxW; x += 1f)
        {
            bool l = NavMesh.SamplePosition(At(mid, -x), out var h1, 1f, NavMesh.AllAreas) && OnHallFloor(h1.position);
            bool r = NavMesh.SamplePosition(At(mid, x), out var h2, 1f, NavMesh.AllAreas) && OnHallFloor(h2.position);
            if (!l && !r) break;
            w = x;
        }
        return w;
    }

    // An invisible wall across the arch: closed during waves, opened for the break.
    static GameObject MakeGate(Transform parent)
    {
        var go = new GameObject("Horde_StatueRoomGate");
        go.transform.SetParent(parent, true);
        go.transform.position = At(_gateP, 0f) + Vector3.up * 4f;
        go.transform.rotation = Quaternion.LookRotation(_axis);
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(_halfWidth * 2f + 30f, 14f, 0.6f);
        return go;
    }

    // Fallback for the side room: the generated room object that holds the first prayer statue.
    static GameObject StatueRoom(CustomDungeonGenerator g)
    {
        var st = UnityEngine.Object.FindObjectOfType<PrayersStatue>();
        if (st == null) return null;
        var t = st.transform;
        var root = g.dungeonRoot != null ? g.dungeonRoot.transform : null;
        while (t.parent != null && (root == null || t.parent.Pointer != root.Pointer)) t = t.parent;
        return t.gameObject;
    }

    static GameObject FindRoomObject(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return null;
        GameObject found = null;
        foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
            if (t.name == prefabName + "(Clone)") { found = t.gameObject; break; }
        return found;
    }

    static Bounds RendererBounds(GameObject root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(root.transform.position, new Vector3(20, 8, 20));
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static void MovePlayer(Transform me, Vector3 at)
    {
        var cc = me.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        me.position = at + Vector3.up * 1.2f;
        if (cc != null) cc.enabled = true;
    }

    static Vector3 Floor(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return p;
    }

    static GameObject MakePillar(Vector3 at)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Horde_ContinuePillar";
        go.transform.position = at + Vector3.up * 0.6f;
        go.transform.localScale = new Vector3(0.7f, 0.6f, 0.7f);
        var r = go.GetComponent<MeshRenderer>();
        var src = Statues.Count > 0 ? Statues[0].s.GetComponentInChildren<MeshRenderer>() : null;
        if (src != null && src.sharedMaterial != null) r.sharedMaterial = src.sharedMaterial;
        var lightGo = new GameObject("glow");
        lightGo.transform.SetParent(go.transform, false);
        lightGo.transform.localPosition = Vector3.up * 2f;
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.range = 6f; l.intensity = 2f; l.color = new Color(1f, 0.55f, 0.25f);
        go.layer = InteractionLayer();
        if (HordeStatuePlacer.Registered) go.AddComponent<HordePillar>();
        return go;
    }

    // ---------- authority ----------

    static void OnCommand(HordeCommand c)
    {
        if (!Net.IsAuthority || c != HordeCommand.Continue) return;
        if (State.Phase != HordePhase.Ready && State.Phase != HordePhase.Break) return;
        if (Net.PartnerBusy)
        {
            ShowToast(Ru ? "Напарник ещё выбирает дар" : "Your partner is still choosing a gift");
            return;
        }
        SetSideOpen(false);
        StartWave(State.Wave + 1);
    }

    // ---------- client ----------

    // A client mirrors the run from the host's snapshots: banners, the side room, and its own gifts
    // and bank chest (every player rolls his own).
    static void OnSnapshot(HordeSnapshot s)
    {
        if (Net.IsAuthority) return;
        var old = State;
        State = s;
        if (!_setup) return;
        // What happened before this player stood in the arena is not paid out to him.
        if (_seenBoss < 0) { _seenBoss = s.BossKills; _seenGrandma = s.GrandmaKills; old = s; }
        SetSideOpen(s.Phase == HordePhase.Break);
        for (; _seenBoss < s.BossKills; _seenBoss++) HordeProgress.AddBankItem(s.Wave);
        for (; _seenGrandma < s.GrandmaKills; _seenGrandma++) HordeProgress.AddBankItem(s.Wave, true);
        bool newWave = s.Phase == HordePhase.Wave && (old.Phase != HordePhase.Wave || old.Wave != s.Wave);
        if (old.Phase == HordePhase.Wave && (s.Phase != HordePhase.Wave || s.Wave != old.Wave))
        {
            HordeProgress.OnWaveCleared(old.Wave);
            if (s.Phase == HordePhase.Break)
                HordeHud.Announce(Ru ? "ПЕРЕДЫШКА" : "RESPITE", Ru ? "решётка поднята · портал открыт · алтарь ждёт" : "the gate is up · the portal is open · the altar waits", "clear");
            else if (!newWave)
                HordeHud.Announce(Ru ? "ВОЛНА ПРОЙДЕНА" : "WAVE CLEARED", Ru ? $"следующая через {HordeSettings.IntermissionSeconds.Value:0} с" : $"next in {HordeSettings.IntermissionSeconds.Value:0} s", "clear");
        }
        if (newWave) AnnounceWave(s.Wave, s.LeftToSpawn + s.Alive, s.Bosses);
    }

    static void AnnounceWave(int wave, int count, int bosses) =>
        HordeHud.Announce(Ru ? $"ВОЛНА {wave}" : $"WAVE {wave}",
            bosses == 0 ? (Ru ? $"врагов: {count}" : $"enemies: {count}")
            : bosses == 1 ? (Ru ? "с ней идёт босс" : "a boss walks with it")
            : (Ru ? $"с ней идут боссы: {bosses}" : $"{bosses} bosses walk with it"), "wave");

    // For the co-op mod's test commands.
    internal static string Describe() =>
        $"active {HordeGen.Active} setup {_setup} authority {Net.IsAuthority} players {Net.PlayerCount} phase {State.Phase} wave {State.Wave} alive {State.Alive} left {State.LeftToSpawn} bossKills {State.BossKills} "
        + $"level {HordeProgress.Level} busy {HordeProgress.Busy} partnerBusy {Net.PartnerBusy} bank {HordeProgress.BankItems.Count} sideOpen {_sideOpen}"
        + (_pillar != null ? $" pillar {_pillar.transform.position}" : "") + (_bank != null ? $" chest {_bank.transform.position}" : "");

    static void StartWave(int wave)
    {
        int n = Net.PlayerCount;
        // Mad: start with one enemy and add one per wave up to wave 10; after that the difficulty's step.
        int count = wave <= 10 ? wave : 10 + HordeSettings.EnemiesPerWave.Value * (wave - 10);
        count = Math.Min(count, MaxWave); // pressure comes from stronger enemies, never from more than 50
        count = Mathf.RoundToInt(count * (1f + 0.5f * (n - 1)));
        State.Phase = HordePhase.Wave;
        State.Wave = wave;
        _special.Clear();
        var bosses = HordeBosses.ForWave(_gen, wave);
        float share = 1f / (1f + 0.25f * Math.Max(0, bosses.Count - 1)); // two: 0.8 each, three: 0.67
        foreach (var b in bosses)
        {
            _special.Enqueue((b.prefab, b.grandma ? HordeBalance.Kind.Grandma : HordeBalance.Kind.Boss, share));
            // From wave 20 every boss brings its own retinue (the adds its boss room summons).
            if (wave >= 20 && b.retinue.Count > 0)
                for (int i = 0; i < 2 + (wave - 20) / 10; i++)
                    _special.Enqueue((b.retinue[Rng.Next(b.retinue.Count)], HordeBalance.Kind.Normal, 1f));
        }
        // Mad: a boss wave is the boss's fight. Before wave 20 the boss comes alone; later its
        // retinue plus half the usual wave.
        if (bosses.Count > 0) count = wave < 20 ? 0 : count / 2;
        count = Math.Min(MaxWave, count + _special.Count);
        State.LeftToSpawn = count;
        State.Bosses = bosses.Count;
        _nextSpawn = Time.time + FirstSpawnDelay;
        AnnounceWave(wave, count, bosses.Count);
        Plugin.L.Msg($"Horde: wave {wave}, {count} enemies.");
    }

    static void RunAuthority(CustomDungeonGenerator g)
    {
        Prune();
        switch (State.Phase)
        {
            case HordePhase.Wave:
                if (State.LeftToSpawn > 0 && Alive.Count < Math.Min(MaxWave, HordeSettings.MaxAlive.Value) && Time.time >= _nextSpawn)
                {
                    if (SpawnOne(g)) State.LeftToSpawn--;
                    _nextSpawn = Time.time + HordeSettings.SpawnInterval.Value;
                }
                if (State.LeftToSpawn == 0 && Alive.Count == 0)
                {
                    HordeProgress.OnWaveCleared(State.Wave);
                    if (State.Wave % Math.Max(1, HordeSettings.WavesPerBlock.Value) == 0) BeginBreak(g);
                    else
                    {
                        State.Phase = HordePhase.Intermission; _phaseEnd = Time.time + HordeSettings.IntermissionSeconds.Value;
                        HordeHud.Announce(Ru ? "ВОЛНА ПРОЙДЕНА" : "WAVE CLEARED", Ru ? $"следующая через {HordeSettings.IntermissionSeconds.Value:0} с" : $"next in {HordeSettings.IntermissionSeconds.Value:0} s", "clear");
                    }
                }
                break;
            case HordePhase.Intermission:
                // A gift being revealed or chosen holds the next wave back.
                if (HordeProgress.Busy || Net.PartnerBusy) _phaseEnd = Mathf.Max(_phaseEnd, Time.time + 1f);
                if (Time.time >= _phaseEnd) StartWave(State.Wave + 1);
                break;
        }
        RetryAggro();
        State.Alive = Alive.Count;
        // A change of phase or wave goes out at once: the clients' gifts and banners hang on it.
        if (Time.unscaledTime >= _nextPublish || State.Phase != _pubPhase || State.Wave != _pubWave)
        {
            _nextPublish = Time.unscaledTime + 0.25f; _pubPhase = State.Phase; _pubWave = State.Wave;
            Net.Publish(State);
        }
    }

    static void BeginBreak(CustomDungeonGenerator g)
    {
        State.Phase = HordePhase.Break;
        SetSideOpen(true);
        HordeHud.Announce(Ru ? "ПЕРЕДЫШКА" : "RESPITE", Ru ? "решётка поднята · портал открыт · алтарь ждёт" : "the gate is up · the portal is open · the altar waits", "clear");
        Plugin.L.Msg($"Horde: block cleared at wave {State.Wave}.");
    }

    static void Prune()
    {
        for (int i = Alive.Count - 1; i >= 0; i--)
        {
            var h = Alive[i].h;
            if (h == null || h.WasCollected || h.gameObject == null || !h.gameObject.activeInHierarchy) { Alive.RemoveAt(i); continue; }
            if (h.CurrentHealth <= 0)
            {
                int tier = Alive[i].tier;
                if (tier >= 60) State.GrandmaKills++; else if (tier >= 30) State.BossKills++;
                HordeProgress.OnKill(tier); Alive.RemoveAt(i);
            }
        }
    }

    // ---------- spawning ----------

    // Every biome's enemies, biomes in the dungeon's order, kinds sorted by the floor they first appear on.
    static List<List<(GameObject prefab, int tier)>> _byBiome;

    static List<List<(GameObject prefab, int tier)>> Roster(CustomDungeonGenerator g)
    {
        if (_byBiome != null) return _byBiome;
        _byBiome = new List<List<(GameObject, int)>>();
        var seen = new HashSet<IntPtr>();
        var biomes = new List<DungeonBiome>();
        var maps = g.biomeFloorMappings;
        var ordered = new List<(int floor, DungeonBiome b)>();
        for (int i = 0; maps != null && i < maps.Count; i++) if (maps[i]?.biome != null) ordered.Add((maps[i].minFloor, maps[i].biome));
        foreach (var (_, b) in ordered.OrderBy(x => x.floor)) if (!biomes.Any(x => x.Pointer == b.Pointer)) biomes.Add(b);
        foreach (var bi in biomes)
        {
            var list = new List<(GameObject, int)>();
            var groups = bi.enemyLevelGroups;
            for (int i = 0; groups != null && i < groups.Count; i++)
            {
                var gr = groups[i];
                var ps = gr?.GetActivePrefabs();
                for (int j = 0; ps != null && j < ps.Count; j++)
                    if (ps[j] != null && seen.Add(ps[j].Pointer)) list.Add((ps[j], gr.minLevel));
            }
            if (list.Count > 0) _byBiome.Add(list.OrderBy(x => x.Item2).ToList());
        }
        return _byBiome;
    }

    // Enemies follow the boss phases (Mad): only the current biome's enemies, the next biome's
    // starting the wave after its first boss fight (one new kind per wave) while the previous
    // biome's fade out over ten waves; once the boss pool opens, every enemy of every biome mixes.
    static (GameObject prefab, int tier) PickEnemy(CustomDungeonGenerator g, int wave)
    {
        var all = Roster(g);
        if (all.Count == 0) return default;
        int phase = HordeBosses.BiomePhase(g, wave);
        int every = Math.Max(1, HordeSettings.BossEvery.Value);
        var weights = new float[all.Count];
        var unlocked = new int[all.Count];
        if (phase < 0)
        {
            for (int b = 0; b < all.Count; b++) { weights[b] = 1f; unlocked[b] = all[b].Count; }
        }
        else
        {
            for (int b = 0; b < all.Count; b++)
            {
                int start = b == 0 ? 1 : 10 + every * HordeBosses.FightsPerBiome * b + 1;
                if (b == phase)
                {
                    weights[b] = b == 0 ? 1f : Mathf.Lerp(0.25f, 1f, Mathf.Clamp01((wave - start) / 6f));
                    unlocked[b] = Mathf.Clamp(wave - start + 1, 1, all[b].Count);
                }
                else if (b == phase - 1)
                {
                    int nextStart = 10 + every * HordeBosses.FightsPerBiome * phase + 1;
                    weights[b] = Mathf.Lerp(1f, 0f, Mathf.Clamp01((wave - nextStart + 1) / 10f));
                    unlocked[b] = all[b].Count;
                }
            }
        }
        float total = weights.Sum();
        if (total <= 0f) { weights[0] = 1f; unlocked[0] = Math.Max(1, unlocked[0]); total = 1f; }
        float r = (float)Rng.NextDouble() * total;
        int pick = 0;
        for (int b = 0; b < all.Count; b++) { r -= weights[b]; if (r <= 0f && weights[b] > 0f) { pick = b; break; } }
        var list = all[pick];
        int u = Math.Max(1, unlocked[pick]);
        int i = Math.Min(u - 1, (int)Math.Floor(Math.Sqrt(Rng.NextDouble()) * u));
        return list[i];
    }

    static readonly System.Random Rng = new();
    const float SpawnRingMin = 3f, SpawnRingMax = 7f;
    const float FirstSpawnDelay = 0.5f; // after Start/Continue and after the pause between waves
    const int MaxWave = 50;           // hard cap on a wave's size, bosses and retinue included

    static bool SpawnOne(CustomDungeonGenerator g)
    {
        var pick = PickEnemy(g, State.Wave);
        if (pick.prefab == null) { Plugin.L.Warning("Horde: no enemy prefabs in the dungeon biomes."); State.LeftToSpawn = 0; return false; }
        var players = Net.Players().ToList();
        // Around the centre pillar (Mad: enemies come out right by it), on the hall floor,
        // in front of the statue-room arch, not on top of a player.
        Vector3 pos = default; bool found = false;
        float minD = HordeSettings.MinSpawnDistance.Value;
        for (int attempt = 0; attempt < 24 && !found; attempt++)
        {
            float ang = (float)(Rng.NextDouble() * Math.PI * 2);
            float r = SpawnRingMin + (float)Rng.NextDouble() * (SpawnRingMax - SpawnRingMin);
            var p = _arenaCenter + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
            if (!NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) continue;
            if (!OnHallFloor(hit.position) || AlongOf(hit.position) > _gateP - 1f) continue;
            if (players.Any(t => (t.position - hit.position).sqrMagnitude < minD * minD)) continue;
            pos = hit.position; found = true;
        }
        if (!found) return false;

        var kind = HordeBalance.Kind.Normal;
        float hpShare = 1f;
        bool special = false;
        if (_special.Count > 0)
        {
            var sp = _special.Dequeue();
            kind = sp.kind; hpShare = sp.share; special = true;
            pick = (sp.prefab, kind == HordeBalance.Kind.Normal ? pick.tier : 30);
        }
        bool boss = kind == HordeBalance.Kind.Boss || kind == HordeBalance.Kind.Grandma;
        var prefab = pick.prefab;
        var target = Nearest(players, pos);
        var rot = target != null ? Quaternion.LookRotation(Vector3.ProjectOnPlane(target.position - pos, Vector3.up)) : Quaternion.identity;
        var go = UnityEngine.Object.Instantiate(prefab, pos + Vector3.up * g.enemySpawnYOffset, rot);
        go.name = prefab.name;

        var h = go.GetComponent<EmeraldHealth>();
        var s = go.GetComponent<EmeraldSystem>();
        if (h != null)
        {
            float mul = HordeBalance.HealthMul(State.Wave) * hpShare;
            mul *= 1f + 0.5f * (Net.PlayerCount - 1);
            int tier = pick.tier;
            if (!boss && !special && MakeElite(go)) { tier += 6; kind = HordeBalance.Kind.Elite; }
            int hp = Mathf.Max(1, Mathf.RoundToInt(h.StartingHealth * mul));
            hp = HordeBalance.AdaptHealth(hp, kind, State.Wave);
            h.StartingHealth = hp;
            h.CurrentHealth = hp;
            HordeBalance.KindOf[go.Pointer] = kind;
            if (kind == HordeBalance.Kind.Grandma) tier = 60;
            if (boss)
            {
                Plugin.L.Msg($"Horde: boss {prefab.name} with {h.StartingHealth} HP.");
                string bn = go.GetComponent<EmeraldUI>()?.AIName;
                if (string.IsNullOrWhiteSpace(bn)) bn = prefab.name.Replace("Boss", "").Trim();
                HordeHud.Announce(Ru ? "БОСС" : "BOSS", bn.ToUpperInvariant(), "boss");
            }
            Alive.Add((h, s, Time.time + 0.6f, 2, tier));
        }
        else
        {
            Plugin.L.Warning($"Horde: enemy '{prefab.name}' has no EmeraldHealth; not tracked.");
            return true;
        }
        return true;
    }

    // ---------- elites and bosses ----------

    // Bosses and retinues of the current wave, spawned before the regular enemies.
    static readonly Queue<(GameObject prefab, HordeBalance.Kind kind, float share)> _special = new();

    static readonly EliteAffix[] Affixes = { EliteAffix.Explosive, EliteAffix.Regeneration, EliteAffix.Armored, EliteAffix.FrostAura, EliteAffix.Vampiric };

    // Elites ramp in gently: none before EliteFromWave, then a slowly rising chance, never more than
    // 1 + (wave-10)/5 alive at once, one frost aura at a time (stacked auras freeze the player), and a
    // second affix only from wave 20.
    static bool MakeElite(GameObject go)
    {
        int w = State.Wave;
        if (w < HordeSettings.EliteFromWave.Value) return false;
        int alive = 0; bool frost = false;
        foreach (var a in Alive)
        {
            var e = a.h != null ? a.h.GetComponent<EliteEnemyController>() : null;
            if (e == null) continue;
            alive++;
            if (e.HasAffix(EliteAffix.FrostAura)) frost = true;
        }
        if (alive >= 1 + Math.Max(0, w - 10) / 5) return false;
        float chance = Mathf.Min(HordeSettings.EliteChanceMax.Value,
            HordeSettings.EliteChanceBase.Value + HordeSettings.EliteChancePerWave.Value * (w - HordeSettings.EliteFromWave.Value + 1)) / 100f;
        if (Rng.NextDouble() >= chance) return false;
        try
        {
            var pool = frost ? Affixes.Where(x => x != EliteAffix.FrostAura).ToArray() : Affixes;
            var a1 = pool[Rng.Next(pool.Length)];
            bool plus = w >= 20 && Rng.NextDouble() < Mathf.Min(0.25f, (w - 19) * 0.01f);
            var el = go.GetComponent<EliteEnemyController>() ?? go.AddComponent<EliteEnemyController>();
            if (plus)
            {
                var rest = pool.Where(x => x != a1 && !(x == EliteAffix.FrostAura && (frost || a1 == EliteAffix.FrostAura))).ToArray();
                var a2 = rest[Rng.Next(rest.Length)];
                el.InitializeElite(a1, new Il2CppSystem.Nullable<EliteAffix>(a2), true);
            }
            else el.InitializeElite(a1, new Il2CppSystem.Nullable<EliteAffix>(), false);
            return true;
        }
        catch (Exception e) { Plugin.L.Warning("Horde: elite setup failed: " + e.Message); return false; }
    }

    static Transform Nearest(List<Transform> players, Vector3 p)
    {
        Transform best = null; float bd = float.MaxValue;
        foreach (var t in players) { float d = (t.position - p).sqrMagnitude; if (d < bd) { bd = d; best = t; } }
        return best;
    }

    // Spawned enemies start idle: point them at the nearest player shortly after spawn (once Emerald
    // has initialised) and once more a few seconds later.
    static void RetryAggro()
    {
        var players = Net.Players().ToList();
        for (int i = 0; i < Alive.Count; i++)
        {
            var a = Alive[i];
            if (a.aggroLeft <= 0 || Time.time < a.aggroAt || a.s == null) continue;
            var t = Nearest(players, a.h.transform.position);
            if (t != null)
            {
                try { EmeraldAPI.Combat.OverrideCombatTarget(a.s, t); }
                catch (Exception e) { Plugin.L.Warning("Horde: aggro failed: " + e.Message); }
            }
            Alive[i] = (a.h, a.s, Time.time + 3f, a.aggroLeft - 1, a.tier);
        }
    }

    // ---------- side room ----------

    static void SetSideOpen(bool open)
    {
        if (_sideOpen == open) return;
        _sideOpen = open;
        if (_gate != null) _gate.SetActive(!open);
    }

    // The boss room's own portcullis slides toward its open or closed pose (the gate's own
    // precomputed positions), so the break opens it the same way a boss kill does.
    static void MoveGates()
    {
        bool open = _sideOpen || State.Phase == HordePhase.Break;
        foreach (var g in _bossGates)
        {
            var t = g?.gateTransform;
            if (t == null) continue;
            float speed = Mathf.Max(0.5f, g.openSpeed) * Time.deltaTime;
            if (g.openMode == BossGate.OpenMode.Rotate)
                t.localRotation = Quaternion.RotateTowards(t.localRotation, open ? g._openRotation : g._closedRotation, speed * 90f);
            else
                t.localPosition = Vector3.MoveTowards(t.localPosition, open ? g._openPosition : g._closedPosition, speed);
        }
    }

    // Backup for the invisible wall: a player past the arch while it is closed goes back to the hall.
    static void EnforceLock()
    {
        bool open = State.Phase == HordePhase.Break || (Net.IsAuthority && _sideOpen);
        if (open) return;
        var me = FirstPersonController.Instance;
        if (me == null || AlongOf(me.transform.position) < _gateP + 0.5f) return;
        MovePlayer(me.transform, _arenaCenter + _axis * -3f);
        ShowToast(Ru ? $"Комната откроется после волны {NextBreakWave()}" : $"The room opens after wave {NextBreakWave()}");
    }

    // The way back up in the start room ends the run like the portal does, but not mid-block.
    internal static bool ExitAllowed()
    {
        if (!_setup) return true;
        bool ok = State.Phase == HordePhase.Ready || State.Phase == HordePhase.Break;
        if (!ok) ShowToast(Ru ? $"Выход закрыт до перерыва после волны {NextBreakWave()}" : $"The exit is closed until the break after wave {NextBreakWave()}");
        return ok;
    }

    static int NextBreakWave()
    {
        int b = Math.Max(1, HordeSettings.WavesPerBlock.Value);
        return (State.Wave / b + 1) * b;
    }

    static void ResetStatues()
    {
        foreach (var (s, lights) in Statues)
        {
            if (s == null) continue;
            s.hasPrayed = false;
            if (s.objectToFadeOut != null) s.objectToFadeOut.SetActive(true);
            foreach (var (l, i) in lights) if (l != null) { l.enabled = true; l.intensity = i; }
            foreach (var ps in s.GetComponentsInChildren<ParticleSystem>(true)) { ps.gameObject.SetActive(true); ps.Play(); }
        }
    }

    // ---------- statues and the bank chest ----------

    internal static PrayersStatue StatueFor(bool evil)
    {
        foreach (var (st, _) in Statues)
        {
            if (st == null) continue;
            bool isEvil = false;
            var pool = st.boonPool;
            for (int i = 0; pool != null && i < pool.Count; i++) if (pool[i] != null && pool[i].isEvil) { isEvil = true; break; }
            if (isEvil == evil) return st;
        }
        return Statues.Count > 0 ? Statues[0].s : null;
    }

    // Praying makes a statue fade out; the horde uses them again and again.
    internal static void RestoreStatues() => ResetStatues();

    static DungeonChest _bank;
    static float _bankReplaceAt;
    static CustomDungeonGenerator _gen;

    internal static bool IsBank(DungeonChest c) => c != null && _bank != null && c.Pointer == _bank.Pointer;

    // Right before it opens, the chest is filled with the gear the bosses left.
    internal static void FillBank(DungeonChest c)
    {
        try
        {
            var items = HordeProgress.BankItems;
            c.maxSlots = items.Count;
            c.Initialize(null, _gen?.ActiveBiome, HordeProgress.ChestLevel, new Il2CppSystem.Random(Environment.TickCount));
            c.inventory.Clear();
            foreach (var it in items) { var slot = new ChestSlot(); slot.item = it; slot.amount = 1; c.inventory.Add(slot); }
            Plugin.L.Msg($"Horde: bank opens with {items.Count} items: {string.Join(", ", items.Select(i => i.name))}.");
        }
        catch (Exception e) { Plugin.L.Error("Horde: bank fill failed: " + e.Message); }
    }

    internal static void ReplaceBankLater() => _bankReplaceAt = Time.time + 4f;

    static void SpawnBank(CustomDungeonGenerator g)
    {
        _gen = g;
        if (_bank != null) { UnityEngine.Object.Destroy(_bank.gameObject, 0.1f); _bank = null; }
        var at = OnNav(At((_entryP + _hallEndP) * 0.5f, 7f)); // well clear of the pillar
        var go = MakeChest(g, at, Quaternion.LookRotation(-_perp), "Horde_Bank", 1);
        _bank = go != null ? go.GetComponent<DungeonChest>() : null;
    }

    static GameObject MakeChest(CustomDungeonGenerator g, Vector3 at, Quaternion rot, string name, int level)
    {
        try
        {
            var go = new GameObject(name);
            go.transform.position = at;
            go.transform.rotation = rot;
            go.layer = InteractionLayer();
            var c = go.AddComponent<DungeonChest>();
            c.commonModel = g.chestCommonModel; c.uncommonModel = g.chestUncommonModel; c.rareModel = g.chestRareModel;
            c.epicModel = g.chestEpicModel; c.legendaryModel = g.chestLegendaryModel; c.blasphemousModel = g.chestBlasphemousModel;
            c.lightCommon = g.chestLightCommon; c.lightUncommon = g.chestLightUncommon; c.lightRare = g.chestLightRare;
            c.lightEpic = g.chestLightEpic; c.lightLegendary = g.chestLightLegendary; c.lightBlasphemous = g.chestLightBlasphemous;
            c.vfxCommon = g.itemVfxCommon; c.vfxUncommon = g.itemVfxUncommon; c.vfxRare = g.itemVfxRare;
            c.vfxEpic = g.itemVfxEpic; c.vfxLegendary = g.itemVfxLegendary; c.vfxBlasphemous = g.itemVfxBlasphemous;
            c.localizedOpenAction = g.chestLocalizedOpenAction; c.localizedChestObject = g.chestLocalizedChestObject;
            c.lidName = g.chestLidName; c.lidOpenRotation = g.chestLidOpenRotation; c.openDuration = g.chestOpenDuration;
            c.chestOpenVFXPrefab = g.chestOpenVFXPrefab;
            c.Initialize(null, g.ActiveBiome, level, new Il2CppSystem.Random(Environment.TickCount));
            if (go.GetComponent<Collider>() == null)
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0, 0.35f, 0); box.size = new Vector3(1.4f, 0.8f, 0.9f);
            }
            return go;
        }
        catch (Exception e) { Plugin.L.Error("Horde: chest spawn failed: " + e); return null; }
    }

    static int InteractionLayer()
    {
        var fpc = FirstPersonController.Instance;
        int mask = fpc != null ? fpc.interactionLayer.value : 0;
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
        return 0;
    }

    // ---------- input and UI ----------

    static bool NearPillar(out Transform me)
    {
        me = FirstPersonController.Instance?.transform;
        return me != null && _pillar != null && (me.position - _pillar.transform.position).sqrMagnitude < 3.5f * 3.5f;
    }

    // The pillar only answers while there is nothing to fight.
    internal static void PillarPressed()
    {
        if (State.Phase != HordePhase.Ready && State.Phase != HordePhase.Break) return;
        Net.Send(HordeCommand.Continue);
    }

    internal static string PillarActionText() => State.Phase switch
    {
        HordePhase.Ready => Ru ? "Начать" : "Start",
        HordePhase.Break => Ru ? "Продолжить" : "Continue",
        _ => Ru ? "Идёт волна" : "Wave in progress",
    };

    internal static void ShowToast(string s) { Toast = s; _toastUntil = Time.time + 3f; HordeHud.Announce(s, null, "info"); }

    internal static bool Ru
    {
        get
        {
            try { return UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale?.Identifier.Code?.StartsWith("ru") == true; }
            catch { return false; }
        }
    }


    internal static void OnGUI() { }

}
