using System.Collections.Generic;
using UnityEngine;

public static class FootprintUtil
{
    public static List<Vector2Int> RotateOffsets(List<Vector2Int> offsets, int degrees)
    {
        List<Vector2Int> rotated = new List<Vector2Int>();
        foreach (var offset in offsets)
        {
            rotated.Add(RotateSingle(offset, degrees));
        }
        return rotated;
    }

    private static Vector2Int RotateSingle(Vector2Int offset, int degrees)
    {
        int normalized = ((degrees % 360) + 360) % 360;
        switch (normalized)
        {
            case 0:
                return offset;
            case 90:
                return new Vector2Int(-offset.y, offset.x);
            case 180:
                return new Vector2Int(-offset.x, -offset.y);
            case 270:
                return new Vector2Int(offset.y, -offset.x);
            default:
                Debug.LogWarning($"Rotation {degrees} is not a multiple of 90. Using 0.");
                return offset;
        }
    }

    public static List<Vector2Int> GetWorldCells(Vector2Int anchor, List<Vector2Int> offsets, int rotationDegrees)
    {
        List<Vector2Int> rotatedOffsets = RotateOffsets(offsets, rotationDegrees);
        List<Vector2Int> worldCells = new List<Vector2Int>();
        foreach (var offset in rotatedOffsets)
        {
            worldCells.Add(anchor + offset);
        }
        return worldCells;
    }
}
