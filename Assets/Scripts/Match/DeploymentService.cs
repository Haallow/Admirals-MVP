using System.Collections.Generic;
using UnityEngine;


public static class DeploymentService
{
    public static void DeployAll(MatchState match, GridManager gridManager)
    {
        DeployFleet(match.playerA, gridManager, anchorX: 1, rotationDegrees: 0);
        DeployFleet(match.playerB, gridManager, anchorX: gridManager.width - 3, rotationDegrees: 180);
    }

    private static void DeployFleet(PlayerState player, GridManager gridManager, int anchorX, int rotationDegrees)
    {
        int y = 1;

        foreach (ShipType type in player.fleetRoster)
        {
            ShipInstance ship = ShipFactory.CreateShip(type);
            ship.owner = player.owner;
            ship.anchor = new Vector2Int(anchorX, y);
            ship.rotationDegrees = rotationDegrees;
            ship.anchorAtTurnStart = ship.anchor;

            if (!gridManager.CanPlaceShip(ship, ship.anchor, ship.rotationDegrees))
            {
                Debug.LogWarning($"Deployment spot invalid for {type} ({player.owner}) at {ship.anchor}. Ship not placed.");
                continue;
            }

            gridManager.PlaceShip(ship, ship.GetOccupiedCells());
            player.ships.Add(ship);
            ship.LogStatBlock($"{type} [{player.owner}]");

            y += 3;
        }
    }
}