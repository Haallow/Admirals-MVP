using System.Collections.Generic;
using UnityEngine;

public static class DeploymentAI
{
    public struct Decision
    {
        public Vector2Int anchor;
        public int rotationDegrees;
        public float score;
    }

    public static Decision ChooseDeployment(ShipInstance ship, GridManager grid, int deadSpaceRows)
    {
        Decision best = new Decision
        {
            anchor = ship.anchor,
            rotationDegrees = ship.rotationDegrees,
            score = float.NegativeInfinity
        };

        int[] rotations = { 0, 90, 180, 270 };
        for (int rotationIndex = 0; rotationIndex < rotations.Length; rotationIndex++)
        {
            int rotation = rotations[rotationIndex];
            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    Vector2Int anchor = new Vector2Int(x, y);
                    if (!grid.CanDeployShip(ship, ship.owner, anchor, rotation, deadSpaceRows))
                    {
                        continue;
                    }

                    float score = ScorePosition(ship, grid, anchor, rotation, deadSpaceRows);
                    Debug.Log($"[Deployment AI] Candidate {anchor} rotation {rotation}: score={score:F1}");
                    if (score > best.score)
                    {
                        best = new Decision
                        {
                            anchor = anchor,
                            rotationDegrees = rotation,
                            score = score
                        };
                    }
                }
            }
        }

        return best;
    }

    private static float ScorePosition(ShipInstance ship, GridManager grid, Vector2Int anchor, int rotation, int deadSpaceRows)
    {
        List<Vector2Int> cells = FootprintUtil.GetWorldCells(anchor, ship.footprintOffsets, rotation);
        float centerY = (grid.Height - 1) * 0.5f;
        float verticalAccess = 10f - Mathf.Abs(anchor.y - centerY);
        float deadSpaceDistance = 0f;
        float mobility = 0f;
        int safeDeadSpaceColumns = Mathf.Clamp(deadSpaceRows, 0, grid.Width);
        int zoneWidth = (grid.Width - safeDeadSpaceColumns) / 2;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector2Int cell = cells[i];
            int distanceFromDeadSpace = ship.owner == PlayerId.PlayerA
                ? zoneWidth - 1 - cell.x
                : cell.x - (grid.Width - zoneWidth);
            deadSpaceDistance += distanceFromDeadSpace;
        }

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                Vector2Int nextAnchor = anchor + new Vector2Int(dx, dy);
                if (grid.CanPlaceShip(ship, nextAnchor, rotation))
                {
                    mobility += 1f;
                }
            }
        }

        return verticalAccess * 2f + deadSpaceDistance * 0.5f + mobility * 0.25f;
    }
}
