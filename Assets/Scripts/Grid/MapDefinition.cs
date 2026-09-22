using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MapDefinition", menuName = "Admirals/Map Definition")]
public class MapDefinition : ScriptableObject
{
    [Min(1)] public int width = 30;
    [Min(1)] public int height = 15;
    [SerializeField] private List<MapTerrainEntry> terrainEntries = new List<MapTerrainEntry>();

    public bool TryGetTerrain(Vector2Int position, out TerrainType terrainType, out int movementCost)
    {
        foreach (var entry in terrainEntries)
        {
            if (entry.position != position) continue;

            terrainType = entry.terrainType;
            movementCost = entry.GetMovementCost();
            return true;
        }

        terrainType = TerrainType.Normal;
        movementCost = 1;
        return false;
    }

    private void OnValidate()
    {
        width = Mathf.Max(1, width);
        height = Mathf.Max(1, height);

        foreach (var entry in terrainEntries)
        {
            entry.Normalize();
        }
    }
}

[Serializable]
public class MapTerrainEntry
{
    public Vector2Int position;
    public TerrainType terrainType = TerrainType.Normal;
    [Min(1)] public int movementCost = 1;

    public int GetMovementCost()
    {
        if (terrainType == TerrainType.Impassable) return 0;
        return terrainType == TerrainType.Normal ? 1 : Mathf.Max(2, movementCost);
    }

    public void Normalize()
    {
        movementCost = terrainType == TerrainType.Costly
            ? Mathf.Max(2, movementCost)
            : terrainType == TerrainType.Normal ? 1 : 0;
    }
}
