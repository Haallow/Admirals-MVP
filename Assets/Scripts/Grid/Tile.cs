using UnityEngine;

// Plain data, no MonoBehaviour. Created and owned by GridManager.
public class Tile
{
    public Vector2Int Position { get; private set; }

    public ShipInstance Occupant;
    public bool IsValid = true; // reserved for non-rectangular boards later

    public Tile(Vector2Int position)
    {
        Position = position;
    }
}
