using System;
using UnityEngine;

namespace FellSellPlus.Shop;

// A game bug Mad saw before any mod: the checkout line freezes because the counter keeps "serving"
// a customer who will never pay (nothing on the counter, no coins coming), or because the first
// customer never arrives at the counter. Everyone behind waits for good. After 15 s of either, the
// stuck customer is released (sent home) or put at the counter.
internal static class QueueWatchdog
{
    const float Limit = 15f;
    static IntPtr _served, _head;
    static float _servedSince, _headSince, _next;

    public static void Tick()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1f;
        var q = CheckoutQueue.Instance;
        if (q == null) return;
        try
        {
            ServedCheck(q);
            HeadCheck(q);
        }
        catch (Exception e) { Plugin.L.LogWarning("Checkout watchdog: " + e.Message); }
    }

    // Served but no coins on the counter and none being collected: the payment never happens.
    static void ServedCheck(CheckoutQueue q)
    {
        var npc = q._npcBeingServed;
        bool idle = npc != null && q.SpawnedGoldAmount <= 0 && !q._coinsReadyToCollect && !q.IsCollecting;
        if (!idle) { _served = IntPtr.Zero; return; }
        if (npc.Pointer != _served) { _served = npc.Pointer; _servedSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _servedSince < Limit) return;
        q.ReleaseNpcBeingServed(npc);
        npc.GoHome();
        Plugin.L.LogInfo($"Checkout: a customer stood at the counter for {Time.unscaledTime - _servedSince:0} s without paying; sent home so the line moves.");
        _served = IntPtr.Zero;
    }

    // The front customer keeps "walking" to the counter while nobody is being served.
    static void HeadCheck(CheckoutQueue q)
    {
        var list = q._queue;
        var npc = list != null && list.Count > 0 ? list[0] : null;
        bool stuck = npc != null && q._npcBeingServed == null && !q.IsNpcAtQueueSpot(npc) && q.servePoint != null;
        if (!stuck) { _head = IntPtr.Zero; return; }
        if (npc.Pointer != _head) { _head = npc.Pointer; _headSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _headSince < Limit) return;
        var agent = npc._agent;
        if (agent != null && agent.isOnNavMesh) agent.Warp(q.servePoint.position);
        else npc.transform.position = q.servePoint.position;
        q.RefreshQueueTargetsWithoutCleaning();
        Plugin.L.LogInfo($"Checkout: the first customer was stuck on the way to the counter for {Time.unscaledTime - _headSince:0} s; moved to the counter.");
        _head = IntPtr.Zero;
    }
}
