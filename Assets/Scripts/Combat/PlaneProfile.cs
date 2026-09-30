using System;

/// <summary>
/// Static definition for a ship's plane-launching capability.
/// Mirrored in ShipInstance as a ChargeState for depletion tracking.
/// Planes are reconnaissance units deployed during Staging — they fly,
/// provide vision, and expire after a fixed number of turns.
/// </summary>
[Serializable]
public class PlaneProfile
{
    public string id;
    public int? count;        // 1 for Carrier
    public int launchRange;   // max Chebyshev distance from Carrier hull to deploy cell (suggest 4)
    public int movementRange; // cells the plane can move per Move phase (suggest 5)
    public int visionRange;   // passive absolute halo range (suggest 3)
    public int fuelTurns;     // turns the plane stays on the board (suggest 3)

    public PlaneProfile(string id, int? count, int launchRange, int movementRange,
        int visionRange, int fuelTurns)
    {
        this.id            = id;
        this.count         = count;
        this.launchRange   = launchRange;
        this.movementRange = movementRange;
        this.visionRange   = visionRange;
        this.fuelTurns     = fuelTurns;
    }
}

