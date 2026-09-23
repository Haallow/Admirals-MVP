using System.Collections.Generic;
using UnityEngine;

public class MovementPathResult
{
    public bool Reachable { get; private set; }
    public bool WithinMovementBudget { get; private set; }
    public int TotalCost { get; private set; }
    public List<Vector2Int> Cells { get; private set; }

    public bool CanMove => Reachable && WithinMovementBudget;

    private MovementPathResult(bool reachable, bool withinMovementBudget, int totalCost, List<Vector2Int> cells)
    {
        Reachable = reachable;
        WithinMovementBudget = withinMovementBudget;
        TotalCost = totalCost;
        Cells = cells;
    }

    public static MovementPathResult Unreachable()
    {
        return new MovementPathResult(false, false, 0, new List<Vector2Int>());
    }

    public static MovementPathResult FromPath(List<Vector2Int> cells, int totalCost, int movementBudget)
    {
        return new MovementPathResult(true, totalCost <= movementBudget, totalCost, cells);
    }
}
