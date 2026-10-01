using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Represents a live reconnaissance plane on the board.
/// Created by GridManager.DeployPlane and stored in MatchState.planes.
/// Planes are board units that move, provide passive halo vision, and expire after fuel turns.
/// They do NOT occupy tiles in GridManager.tiles (they are airborne at altitude).
/// </summary>
public class PlaneUnit
{
    public PlayerId owner;
    public Vector2Int position;
    public int fuelRemaining;     // decrements each End phase; removed at 0
    public int movementRange;     // copied from PlaneProfile at deploy time
    public Vector2Int positionAtTurnStart; // for movement range check in Staging phase
    public VisionLayer visionLayer; // absolute halo, built at deploy time
    public ShipInstance launchedFrom; // ship that deployed this plane (for sortie refund)
    public string profileId;          // profile id of the plane
    public bool deployedThisTurn;     // true if deployed during current Staging phase

    public PlaneUnit(PlayerId owner, Vector2Int position, int fuelTurns,
        int movementRange, int visionRange, ShipInstance launchedFrom = null, string profileId = null)
    {
        this.owner               = owner;
        this.position            = position;
        this.fuelRemaining       = fuelTurns;
        this.movementRange       = movementRange;
        this.positionAtTurnStart = position;
        this.launchedFrom        = launchedFrom;
        this.profileId           = profileId;
        this.deployedThisTurn    = true;

        // Build the passive absolute halo layer at deploy time.
        this.visionLayer = new VisionLayer(
            id:                "Plane Recon",
            shape:             ShapeType.Halo,
            range:             visionRange,
            detects:           DomainType.Both,
            visionType:        VisionType.Absolute,
            isPassive:         true,
            onlyWhileSurfaced: false
        );
    }

    // Returns the single occupied cell for movement and vision calculations.
    public List<Vector2Int> GetOccupiedCells()
    {
        return new List<Vector2Int> { position };
    }
}

