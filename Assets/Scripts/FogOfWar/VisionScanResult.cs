using System.Collections.Generic;
using UnityEngine;

public class VisionScanResult
{
    public readonly List<Vector2Int> DetectedCells = new List<Vector2Int>();
    public readonly Dictionary<Vector2Int, Vector2Int> BlockedCells =
        new Dictionary<Vector2Int, Vector2Int>();
}
