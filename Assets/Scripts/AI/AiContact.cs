using System.Collections.Generic;
using UnityEngine;

// A cluster of adjacent cells the AI's own fog currently knows about. This is what the
// fleet commander reasons over instead of real enemy ShipInstances, so it stays fog-honest:
//   - Marked cells give position only (no class, no domain).
//   - Identified cells (passive Absolute vision) give the class.
//   - Otherwise the class set is deduced from footprint length, which is NOT guaranteed:
//     a partly seen ship only gives a minimum length.
public class AIContact
{
    public readonly List<Vector2Int> Cells = new List<Vector2Int>();
    public Vector2Int Centroid;

    // Lower bound on how many ships this cluster contains (cluster larger than the
    // longest known footprint must be more than one ship).
    public int MinShips = 1;

    // Classes that could explain this cluster. Never assumes a single answer unless identified.
    public readonly HashSet<ShipType> Candidates = new HashSet<ShipType>();

    // Classes revealed by Identified cells. Empty when only Marked cells are known.
    public readonly HashSet<ShipType> IdentifiedClasses = new HashSet<ShipType>();

    public bool IsIdentified => IdentifiedClasses.Count > 0;
}

public static class AIContactBuilder
{
    private static readonly Vector2Int[] Neighbors =
    {
        new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
        new Vector2Int(-1, 0),                         new Vector2Int(1, 0),
        new Vector2Int(-1, 1),  new Vector2Int(0, 1),  new Vector2Int(1, 1)
    };

    // FogGrid can't enumerate its known cells (it only answers per-cell), so this scans the
    // board using GridManager's own width/height. Nothing about map size is hardcoded.
    public static List<AIContact> Build(GridManager gridManager, FogGrid fog)
    {
        var knownList = new List<Vector2Int>();
        var known = new HashSet<Vector2Int>();
        for (int x = 0; x < gridManager.width; x++)
        {
            for (int y = 0; y < gridManager.height; y++)
            {
                var cell = new Vector2Int(x, y);
                if (fog.IsKnown(cell))
                {
                    knownList.Add(cell);
                    known.Add(cell);
                }
            }
        }

        var contacts = new List<AIContact>();
        var visited = new HashSet<Vector2Int>();

        foreach (Vector2Int start in knownList)
        {
            if (visited.Contains(start)) continue;
            contacts.Add(FloodFill(start, known, visited, gridManager, fog));
        }

        return contacts;
    }

    private static AIContact FloodFill(
        Vector2Int start,
        HashSet<Vector2Int> known,
        HashSet<Vector2Int> visited,
        GridManager gridManager,
        FogGrid fog)
    {
        var contact = new AIContact();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            contact.Cells.Add(cell);

            foreach (Vector2Int step in Neighbors)
            {
                Vector2Int next = cell + step;
                if (known.Contains(next) && visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        Finish(contact, gridManager, fog);
        return contact;
    }

    private static void Finish(AIContact contact, GridManager gridManager, FogGrid fog)
    {
        int sumX = 0;
        int sumY = 0;
        foreach (Vector2Int cell in contact.Cells)
        {
            sumX += cell.x;
            sumY += cell.y;

            // Reading the occupant's class is only legitimate because this cell is Identified
            // (passive Absolute vision reveals class). Marked cells are never read this way.
            if (fog.GetState(cell) == FogState.Identified)
            {
                Tile tile = gridManager.GetTile(cell);
                if (tile != null && tile.Occupant != null)
                {
                    contact.IdentifiedClasses.Add(tile.Occupant.shipType);
                }
            }
        }

        contact.Centroid = new Vector2Int(
            Mathf.RoundToInt((float)sumX / contact.Cells.Count),
            Mathf.RoundToInt((float)sumY / contact.Cells.Count));

        int size = contact.Cells.Count;
        int maxLength = FleetRoles.MaxFootprintLength;
        contact.MinShips = Mathf.Max(1, (size + maxLength - 1) / maxLength);

        if (contact.IsIdentified)
        {
            // Limitation: a cluster mixing an Identified ship with an adjacent merely-Marked
            // one will report only the identified class. Acceptable for Stage 1.
            contact.Candidates.UnionWith(contact.IdentifiedClasses);
        }
        else if (size > maxLength)
        {
            // More than one ship: any class could be in there.
            foreach (ShipType type in FleetRoles.AllTypes)
            {
                contact.Candidates.Add(type);
            }
        }
        else
        {
            // A partly seen ship only gives a minimum length, so keep every class that is
            // at least as long as what we can see.
            foreach (ShipType type in FleetRoles.AllTypes)
            {
                if (FleetRoles.GetFootprintLength(type) >= size)
                {
                    contact.Candidates.Add(type);
                }
            }
        }
    }
}