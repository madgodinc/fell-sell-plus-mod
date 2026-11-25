using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FellSellPlus.Shop;

// The game orders the checkout line by when a customer took their item (JoinQueue), so the counter
// waits for someone still walking from a far shelf. With ServeInArrivalOrder, customers who have
// reached their spot in line move ahead of those still walking, in the order they arrived; the
// game's own RefreshQueueTargetsWithoutCleaning then re-targets everyone. Nothing is reordered while
// someone is being served or coins are waiting.
internal static class QueueOrder
{
    static float _next;
    static readonly Dictionary<IntPtr, float> Arrived = new();

    public static void Tick()
    {
        if (!ShopSettings.ServeInArrivalOrder.Value || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.4f;
        var q = CheckoutQueue.Instance;
        if (q == null) return;
        var list = q._queue;
        if (list == null || list.Count < 2) { if (list == null || list.Count == 0) Arrived.Clear(); return; }

        var present = new HashSet<IntPtr>();
        for (int i = 0; i < list.Count; i++)
        {
            var npc = list[i];
            if (npc == null) continue;
            present.Add(npc.Pointer);
            if (!Arrived.ContainsKey(npc.Pointer) && q.IsNpcAtQueueSpot(npc)) Arrived[npc.Pointer] = Time.unscaledTime;
        }
        foreach (var k in Arrived.Keys.Where(k => !present.Contains(k)).ToList()) Arrived.Remove(k);

        if (q._npcBeingServed != null || q._coinsReadyToCollect || q.IsCollecting) return;
        var first = list[0];
        if (first == null || Arrived.ContainsKey(first.Pointer)) return; // the front customer is ready

        var npcs = new List<NPC_Controller>();
        for (int i = 0; i < list.Count; i++) if (list[i] != null) npcs.Add(list[i]);
        var ordered = npcs.Where(n => Arrived.ContainsKey(n.Pointer)).OrderBy(n => Arrived[n.Pointer])
                          .Concat(npcs.Where(n => !Arrived.ContainsKey(n.Pointer))).ToList();
        if (ordered.Count == 0 || ordered[0].Pointer == first.Pointer) return;

        list.Clear();
        foreach (var n in ordered) list.Add(n);
        q.RefreshQueueTargetsWithoutCleaning();
    }
}
