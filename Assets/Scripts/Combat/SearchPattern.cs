using System;
using System.Collections.Generic;
using UnityEngine;

public enum SearchPatternType
{
    Square3x3,
    Cone,
    Diamond,
    VerticalRectangle
}

[Serializable]
public class SearchPatternDefinition
{
    public string id;
    public SearchPatternType patternType;
    public int range;
    public bool consumesSearchAction = true;
    public DomainType validDomain = DomainType.Both;

    public SearchPatternDefinition(string id, SearchPatternType patternType, int range, DomainType validDomain = DomainType.Both)
    {
        this.id = id;
        this.patternType = patternType;
        this.range = range;
        this.validDomain = validDomain;
    }

    public bool IsAvailable(ShipInstance ship)
    {
        if (ship == null)
        {
            return false;
        }

        if (ship.currentHealth <= 0)
        {
            return false;
        }

        if (validDomain != DomainType.Both && ship.currentDomain != validDomain)
        {
            return false;
        }

        return true;
    }

    public List<Vector2Int> GetCells(Vector2Int origin, int rotationDegrees)
    {
        var cells = new List<Vector2Int>();

        switch (patternType)
        {
            case SearchPatternType.Square3x3:
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        cells.Add(origin + new Vector2Int(x, y));
                    }
                }
                break;

            case SearchPatternType.Cone:
                Vector2Int forward = GetForwardVector(rotationDegrees);
                Vector2Int side = GetRightVector(rotationDegrees);
                for (int i = 1; i <= range; i++)
                {
                    int spread = Mathf.Max(0, i - 1);
                    for (int s = -spread; s <= spread; s++)
                    {
                        cells.Add(origin + (forward * i) + (side * s));
                    }
                }
                cells.Add(origin + forward);
                break;

            case SearchPatternType.Diamond:
                for (int x = -range; x <= range; x++)
                {
                    for (int y = -range; y <= range; y++)
                    {
                        if (Mathf.Abs(x) + Mathf.Abs(y) <= range)
                        {
                            cells.Add(origin + new Vector2Int(x, y));
                        }
                    }
                }
                break;

            case SearchPatternType.VerticalRectangle:
                for (int x = -1; x <= 1; x++)
                {
                    for (int y = -range; y <= range; y++)
                    {
                        cells.Add(origin + new Vector2Int(x, y));
                    }
                }
                break;
        }

        return cells;
    }

    private Vector2Int GetForwardVector(int rotationDegrees)
    {
        switch (((rotationDegrees % 360) + 360) % 360)
        {
            case 0:
                return Vector2Int.right;
            case 90:
                return Vector2Int.up;
            case 180:
                return Vector2Int.left;
            case 270:
                return Vector2Int.down;
            default:
                return Vector2Int.right;
        }
    }

    private Vector2Int GetRightVector(int rotationDegrees)
    {
        switch (((rotationDegrees % 360) + 360) % 360)
        {
            case 0:
                return Vector2Int.up;
            case 90:
                return Vector2Int.left;
            case 180:
                return Vector2Int.down;
            case 270:
                return Vector2Int.right;
            default:
                return Vector2Int.up;
        }
    }
}

public static class SearchPatternCatalog
{
    public static List<SearchPatternDefinition> BuildDefaultPatterns()
    {
        return new List<SearchPatternDefinition>
        {
            new SearchPatternDefinition("Square Scan", SearchPatternType.Square3x3, 1),
            new SearchPatternDefinition("Cone Sweep", SearchPatternType.Cone, 4),
            new SearchPatternDefinition("Diamond Sweep", SearchPatternType.Diamond, 3),
            new SearchPatternDefinition("Vertical Sweep", SearchPatternType.VerticalRectangle, 3)
        };
    }
}
