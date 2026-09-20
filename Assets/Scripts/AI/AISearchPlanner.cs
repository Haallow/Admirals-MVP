using System.Collections.Generic;
using UnityEngine;

public static class AISearchPlanner
{
    public static void DecideSearch(GridManager gridManager, ShipInstance aiShip, ShipInstance enemyShip, TurnManager turnManager)
    {
        if (aiShip == null || enemyShip == null || aiShip.searchPatterns == null || aiShip.searchPatterns.Count == 0)
        {
            return;
        }

        SearchPatternDefinition bestPattern = null;
        Vector2Int bestAnchor = aiShip.anchor;
        int bestPatternIndex = aiShip.selectedSearchPatternIndex;
        int bestScore = int.MinValue;

        for (int patternIndex = 0; patternIndex < aiShip.searchPatterns.Count; patternIndex++)
        {
            SearchPatternDefinition pattern = aiShip.searchPatterns[patternIndex];
            if (pattern == null || !pattern.IsAvailable(aiShip))
            {
                continue;
            }

            for (int x = 0; x < gridManager.Width; x++)
            {
                for (int y = 0; y < gridManager.Height; y++)
                {
                    Vector2Int anchor = new Vector2Int(x, y);
                    int detectedCount = 0;
                    int coveredCount = 0;

                    foreach (var cell in pattern.GetCells(anchor, aiShip.rotationDegrees))
                    {
                        if (!gridManager.IsInBounds(cell))
                        {
                            continue;
                        }

                        coveredCount++;
                        if (enemyShip.GetOccupiedCells().Contains(cell))
                        {
                            detectedCount++;
                        }
                    }

                    int score = detectedCount * 100 - Mathf.Max(0, coveredCount - detectedCount);
                    if (detectedCount > 0 && score > bestScore)
                    {
                        bestScore = score;
                        bestPattern = pattern;
                        bestAnchor = anchor;
                        bestPatternIndex = patternIndex;
                    }
                }
            }
        }

        if (bestPattern == null)
        {
            bestPatternIndex = Mathf.Clamp(aiShip.selectedSearchPatternIndex, 0, aiShip.searchPatterns.Count - 1);
            bestPattern = aiShip.searchPatterns[bestPatternIndex];
            bestAnchor = aiShip.anchor;
        }

        aiShip.selectedSearchPatternIndex = bestPatternIndex;
        List<Vector2Int> detectedTiles = gridManager.ResolveSearch(aiShip, bestAnchor);
        gridManager.SetSearchPreview(aiShip, bestAnchor, true);

        Debug.Log($"[AI Search] Selected pattern {bestPattern.id} at {bestAnchor}. Detected {detectedTiles.Count} enemy tile(s): {string.Join(", ", detectedTiles)}");
        turnManager.AdvancePhase();
    }
}
