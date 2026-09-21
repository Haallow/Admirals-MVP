using System.Collections.Generic;
using UnityEngine;

// One player's view of the board. Pure state: what vision finds is decided in VisionResolver,
// and when it runs is decided by FogManager.
public class FogGrid
{
    // Two layers so End-phase clearing wipes active scan marks without touching passive coverage.
    private readonly Dictionary<Vector2Int, FogState> passive = new Dictionary<Vector2Int, FogState>();
    private readonly Dictionary<Vector2Int, FogState> active = new Dictionary<Vector2Int, FogState>();

    public void ResetPassive() { passive.Clear(); }
    public void ClearActiveMarks() { active.Clear(); }

    public void MarkPassive(Vector2Int pos, FogState state) { Upgrade(passive, pos, state); }
    public void MarkActive(Vector2Int pos, FogState state) { Upgrade(active, pos, state); }

    public FogState GetState(Vector2Int pos)
    {
        passive.TryGetValue(pos, out FogState p);
        active.TryGetValue(pos, out FogState a);
        return p > a ? p : a;
    }

    public bool IsKnown(Vector2Int pos)
    {
        return GetState(pos) != FogState.Unknown;
    }

    // TEMP debug helper for Milestone 5 verification.
    public string Describe()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in passive) sb.Append($"{kv.Key}={kv.Value} ");
        foreach (var kv in active) sb.Append($"{kv.Key}={kv.Value}(active) ");
        return sb.Length == 0 ? "nothing known" : sb.ToString();
    }

    // Never downgrades. This is why passive layers need no Absolute-first ordering:
    // Identified always beats Marked no matter which layer writes first.
    private static void Upgrade(Dictionary<Vector2Int, FogState> layer, Vector2Int pos, FogState newState)
    {
        layer.TryGetValue(pos, out FogState current);
        if (newState > current) layer[pos] = newState;
    }
}