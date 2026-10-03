using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MapDefinition", menuName = "Admirals/Map Definition")]
public class MapDefinition : ScriptableObject
{
    [Min(1)] public int width = 30;
    [Min(1)] public int height = 15;
    [SerializeField] private List<MapTerrainEntry> terrainEntries = new List<MapTerrainEntry>();
    [SerializeField] private List<MapDeploymentZone> deploymentZones = new List<MapDeploymentZone>();

    public IReadOnlyList<MapDeploymentZone> DeploymentZones => deploymentZones;

    public bool TryGetZone(string zoneId, out MapDeploymentZone zone)
    {
        zone = deploymentZones?.Find(candidate => candidate != null && candidate.id == zoneId);
        return zone != null;
    }

    public bool ValidateDeploymentZones(out string error)
    {
        if (deploymentZones == null)
        {
            error = "Deployment zones are missing.";
            return false;
        }

        var ids = new HashSet<string>();
        var occupied = new HashSet<Vector2Int>();
        foreach (MapDeploymentZone zone in deploymentZones)
        {
            if (zone == null || string.IsNullOrWhiteSpace(zone.id) || !ids.Add(zone.id) ||
                zone.regions == null || zone.regions.Count == 0)
            {
                error = "Deployment zones need unique nonempty IDs and at least one region.";
                return false;
            }

            foreach (RectInt region in zone.regions)
            {
                if (region.width <= 0 || region.height <= 0 || region.xMin < 0 || region.yMin < 0 ||
                    region.xMax > width || region.yMax > height)
                {
                    error = $"Deployment region in {zone.id} is empty or outside the map.";
                    return false;
                }

                foreach (Vector2Int cell in region.allPositionsWithin)
                {
                    if (!occupied.Add(cell))
                    {
                        error = $"Deployment regions overlap at {cell}.";
                        return false;
                    }
                }
            }
        }

        error = deploymentZones.Count >= 2 ? null : "At least two deployment zones are required.";
        return error == null;
    }

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
public class MapDeploymentZone
{
    public string id;
    public List<RectInt> regions = new List<RectInt>();

    public bool Contains(Vector2Int cell)
    {
        foreach (RectInt region in regions)
        {
            if (region.Contains(cell)) return true;
        }
        return false;
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
