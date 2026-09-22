using System.Collections.Generic;
using UnityEngine;

// Decides *when* vision runs and who it runs for. The rules themselves live in VisionResolver.
public class FogManager
{
    private readonly FogGrid playerAFog = new FogGrid();
    private readonly FogGrid playerBFog = new FogGrid();

    public FogGrid GetFogGrid(PlayerId owner)
    {
        return owner == PlayerId.PlayerA ? playerAFog : playerBFog;
    }

    // Rebuilt from scratch every time so fog is never sticky (Milestone 5 design decision).
    public void RecomputeAllPassive(MatchState match)
    {
        RecomputePassive(PlayerId.PlayerA, match);
        RecomputePassive(PlayerId.PlayerB, match);
    }

    // Automatic for now: Search has no player input in Milestone 5, so scanning is a phase side effect.
    public void RunActiveSearch(PlayerId owner, MatchState match)
    {
        FogGrid fog = GetFogGrid(owner);
        List<ShipInstance> enemies = GetEnemyShips(owner, match);

        foreach (var ship in match.GetPlayer(owner).ships)
        {
            foreach (var layer in ship.visionLayers)
            {
                if (layer.isPassive) continue;

                // Active scans only ever Mark: they reveal that something is there, never what it is.
                foreach (var cell in VisionResolver.GetDetectedCells(ship, layer, enemies))
                {
                    fog.MarkActive(cell, FogState.Marked);
                }
            }
        }
    }

    public void ClearAllActiveMarks()
    {
        playerAFog.ClearActiveMarks();
        playerBFog.ClearActiveMarks();
    }

    private void RecomputePassive(PlayerId owner, MatchState match)
    {
        FogGrid fog = GetFogGrid(owner);
        List<ShipInstance> enemies = GetEnemyShips(owner, match);
        fog.ResetPassive();

        foreach (var ship in match.GetPlayer(owner).ships)
        {
            foreach (var layer in ship.visionLayers)
            {
                if (!layer.isPassive) continue;

                // Absolute layers identify, Sensor layers only mark.
                FogState result = layer.visionType == VisionType.Absolute ? FogState.Identified : FogState.Marked;

                foreach (var cell in VisionResolver.GetDetectedCells(ship, layer, enemies))
                {
                    fog.MarkPassive(cell, result);
                }
            }
        }
    }

    private static List<ShipInstance> GetEnemyShips(PlayerId owner, MatchState match)
    {
        PlayerId enemy = owner == PlayerId.PlayerA ? PlayerId.PlayerB : PlayerId.PlayerA;
        return match.GetPlayer(enemy).ships;
    }
}