using System;

// The geometric shape of a scan/vision pattern.
public enum ShapeType
{
    Halo,   // radial ring around the ship
    Cone    // directional cone in front of the ship
}

// How much information a vision layer reveals.
// Sensor = marks a tile (something is there, no identity).
// Absolute = full reveal (tile contents known). Assumption flagged in the master plan.
public enum VisionType
{
    Sensor,
    Absolute
}

// One vision/scan layer on a ship. A ship can have multiple (passive sonar + active sonar, etc.).
// This is definition data only — activation logic lives in the future Scan system (Milestone 5+).
[Serializable]
public class VisionLayer
{
    public string id;
    public ShapeType shape;
    public int range;           // radius for Halo; forward distance for Cone
    public DomainType detects;  // which domain targets this layer can see
    public VisionType visionType;
    public bool isPassive;      // true = always on; false = consumes the Search phase action

    // Special case for layers that are only meaningful while the ship is surfaced.
    // e.g. Wolf Class "Default Absolute Vision" — present but irrelevant when submerged.
    // Real enforcement deferred to the Fog of War milestone; stored here so the data is correct.
    public bool onlyWhileSurfaced;

    public VisionLayer(string id, ShapeType shape, int range, DomainType detects,
        VisionType visionType, bool isPassive, bool onlyWhileSurfaced = false)
    {
        this.id = id;
        this.shape = shape;
        this.range = range;
        this.detects = detects;
        this.visionType = visionType;
        this.isPassive = isPassive;
        this.onlyWhileSurfaced = onlyWhileSurfaced;
    }
}
