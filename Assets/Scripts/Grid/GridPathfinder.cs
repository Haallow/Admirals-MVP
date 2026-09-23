using System.Collections.Generic;
using UnityEngine;

public static class GridPathfinder
{
    private static readonly Vector2Int[] Directions =
    {
        new Vector2Int(-1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(1, -1),
        new Vector2Int(-1, 0),
        new Vector2Int(1, 0),
        new Vector2Int(-1, 1),
        new Vector2Int(0, 1),
        new Vector2Int(1, 1)
    };

    public static MovementPathResult FindPath(
        GridManager grid,
        Vector2Int start,
        Vector2Int destination,
        int movementBudget,
        ShipInstance movingShip = null)
    {
        if (grid == null || movementBudget < 0 ||
            !grid.IsInBounds(start) || !grid.IsInBounds(destination))
        {
            return MovementPathResult.Unreachable();
        }

        if (!CanTraverse(grid, start, movingShip) ||
            !CanTraverse(grid, destination, movingShip))
        {
            return MovementPathResult.Unreachable();
        }

        var costs = new Dictionary<Vector2Int, int>();
        var previous = new Dictionary<Vector2Int, Vector2Int>();
        var open = new List<Vector2Int> { start };
        costs[start] = 0;

        while (open.Count > 0)
        {
            Vector2Int current = RemoveLowestCost(open, costs);
            if (current == destination)
            {
                List<Vector2Int> path = ReconstructPath(previous, start, destination);
                return MovementPathResult.FromPath(path, costs[current], movementBudget);
            }

            foreach (Vector2Int direction in Directions)
            {
                Vector2Int neighbor = current + direction;
                if (!grid.IsInBounds(neighbor) || !CanTraverse(grid, neighbor, movingShip))
                {
                    continue;
                }

                int newCost = costs[current] + grid.GetTerrainMovementCost(neighbor);
                int knownCost;
                if (costs.TryGetValue(neighbor, out knownCost) && newCost >= knownCost)
                {
                    continue;
                }

                costs[neighbor] = newCost;
                previous[neighbor] = current;
                if (!open.Contains(neighbor))
                {
                    open.Add(neighbor);
                }
            }
        }

        return MovementPathResult.Unreachable();
    }

    public static HashSet<Vector2Int> FindReachableCells(
        GridManager grid,
        Vector2Int start,
        int movementBudget,
        ShipInstance movingShip = null)
    {
        var reachable = new HashSet<Vector2Int>();
        if (grid == null || movementBudget < 0 ||
            !grid.IsInBounds(start) || !CanTraverse(grid, start, movingShip))
        {
            return reachable;
        }

        var costs = new Dictionary<Vector2Int, int>();
        var open = new List<Vector2Int> { start };
        costs[start] = 0;

        while (open.Count > 0)
        {
            Vector2Int current = RemoveLowestCost(open, costs);
            int currentCost = costs[current];
            reachable.Add(current);

            foreach (Vector2Int direction in Directions)
            {
                Vector2Int neighbor = current + direction;
                if (!grid.IsInBounds(neighbor) || !CanTraverse(grid, neighbor, movingShip))
                {
                    continue;
                }

                int newCost = currentCost + grid.GetTerrainMovementCost(neighbor);
                if (newCost > movementBudget)
                {
                    continue;
                }

                int knownCost;
                if (costs.TryGetValue(neighbor, out knownCost) && newCost >= knownCost)
                {
                    continue;
                }

                costs[neighbor] = newCost;
                if (!open.Contains(neighbor))
                {
                    open.Add(neighbor);
                }
            }
        }

        return reachable;
    }

    private static bool CanTraverse(GridManager grid, Vector2Int position, ShipInstance movingShip)
    {
        Tile tile = grid.GetTile(position);
        return tile != null &&
               tile.IsPassable &&
               (tile.Occupant == null || tile.Occupant == movingShip);
    }

    private static Vector2Int RemoveLowestCost(List<Vector2Int> open, Dictionary<Vector2Int, int> costs)
    {
        int bestIndex = 0;
        for (int i = 1; i < open.Count; i++)
        {
            if (costs[open[i]] < costs[open[bestIndex]])
            {
                bestIndex = i;
            }
        }

        Vector2Int result = open[bestIndex];
        open.RemoveAt(bestIndex);
        return result;
    }

    private static List<Vector2Int> ReconstructPath(
        Dictionary<Vector2Int, Vector2Int> previous,
        Vector2Int start,
        Vector2Int destination)
    {
        var path = new List<Vector2Int>();
        Vector2Int current = destination;

        while (current != start)
        {
            path.Add(current);
            current = previous[current];
        }

        path.Add(start);
        path.Reverse();
        return path;
    }
}
