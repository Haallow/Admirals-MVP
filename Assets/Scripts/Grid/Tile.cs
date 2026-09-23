using UnityEngine;

// Plain data, no MonoBehaviour. Created and owned by GridManager.
public class Tile
{
    public Vector2Int Position { get; private set; }

    public ShipInstance Occupant;
    public bool IsValid = true; // reserved for non-rectangular boards later
    public TerrainType TerrainType { get; private set; }
    public int MovementCost { get; private set; }
    public bool IsPassable => TerrainType != TerrainType.Impassable;

    public Tile(Vector2Int position)
    {
        Position = position;
        SetTerrain(TerrainType.Normal, 1);
    }

    public void SetTerrain(TerrainType terrainType, int movementCost)
    {
        TerrainType = terrainType;
        MovementCost = terrainType == TerrainType.Impassable
            ? 0
            : terrainType == TerrainType.Normal ? 1 : Mathf.Max(2, movementCost);
    }
}
