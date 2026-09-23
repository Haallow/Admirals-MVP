using System.Collections.Generic;
using UnityEngine;

public class ActiveScanPreviewState
{
    public ShipInstance Ship { get; private set; }
    public VisionLayer Layer { get; private set; }
    public Vector2Int Bow { get; private set; }
    public Vector2Int Forward { get; private set; }

    public ActiveScanPreviewState(
        ShipInstance ship,
        VisionLayer layer,
        Vector2Int bow,
        Vector2Int forward)
    {
        Ship = ship;
        Layer = layer;
        Bow = bow;
        Forward = forward;
    }

    public HashSet<Vector2Int> GetConeCells()
    {
        return VisionResolver.GetConeCells(Bow, Forward, Layer.range);
    }

    public void Rotate(int quarterTurns)
    {
        for (int i = 0; i < Mathf.Abs(quarterTurns); i++)
        {
            Forward = quarterTurns > 0
                ? new Vector2Int(-Forward.y, Forward.x)
                : new Vector2Int(Forward.y, -Forward.x);
        }
    }
}
