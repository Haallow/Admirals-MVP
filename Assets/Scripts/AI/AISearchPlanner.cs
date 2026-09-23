using System.Collections.Generic;
using UnityEngine;

public static class AISearchPlanner
{
    private const float MinimumCoverageVariance = 2f;
    private const float CoverageVarianceFraction = 0.15f;
    private const float OpposingSideWeight = 6f;

    public static void DecideSearch(GridManager gridManager, ShipInstance aiShip, ShipInstance enemyShip, TurnManager turnManager)
    {
        if (aiShip == null || aiShip.searchPatterns == null || aiShip.searchPatterns.Count == 0)
        {
            return;
        }

        var candidates = new List<SearchCandidate>();
        int bestCoverage = 0;

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
                    var coveredCells = new HashSet<Vector2Int>();

                    foreach (Vector2Int cell in pattern.GetCells(anchor, aiShip.rotationDegrees))
                    {
                        if (gridManager.IsInBounds(cell))
                        {
                            coveredCells.Add(cell);
                        }
                    }

                    int coverage = coveredCells.Count;
                    float opposingSidePreference = GetOpposingSidePreference(
                        coveredCells,
                        gridManager.Width,
                        enemyShip != null ? enemyShip.owner : GetOpposingPlayer(aiShip.owner));
                    bestCoverage = Mathf.Max(bestCoverage, coverage);
                    candidates.Add(new SearchCandidate(pattern, patternIndex, anchor, coverage, opposingSidePreference));
                }
            }
        }

        if (candidates.Count == 0)
        {
            return;
        }

        int coverageVariance = Mathf.Max(
            Mathf.RoundToInt(bestCoverage * CoverageVarianceFraction),
            Mathf.RoundToInt(MinimumCoverageVariance));
        int minimumAcceptedCoverage = bestCoverage - coverageVariance;
        float totalWeight = 0f;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].coverage >= minimumAcceptedCoverage)
            {
                totalWeight += GetCandidateWeight(candidates[i], bestCoverage);
            }
        }

        float randomValue = Random.Range(0f, totalWeight);
        SearchCandidate selectedCandidate = candidates[0];
        for (int i = 0; i < candidates.Count; i++)
        {
            SearchCandidate candidate = candidates[i];
            if (candidate.coverage < minimumAcceptedCoverage)
            {
                continue;
            }

            randomValue -= GetCandidateWeight(candidate, bestCoverage);
            if (randomValue <= 0f)
            {
                selectedCandidate = candidate;
                break;
            }
        }

        aiShip.selectedSearchPatternIndex = selectedCandidate.patternIndex;
        List<Vector2Int> detectedTiles = gridManager.ResolveSearch(aiShip, selectedCandidate.anchor);
        gridManager.SetSearchPreview(aiShip, selectedCandidate.anchor, true);

        Debug.Log($"[AI Search] Selected pattern {selectedCandidate.pattern.id} at {selectedCandidate.anchor}. Coverage {selectedCandidate.coverage}/{bestCoverage}. Detected {detectedTiles.Count} enemy tile(s): {string.Join(", ", detectedTiles)}");
        turnManager.AdvancePhase();
    }

    private static float GetCandidateWeight(SearchCandidate candidate, int bestCoverage)
    {
        if (bestCoverage <= 0)
        {
            return Mathf.Lerp(1f, OpposingSideWeight, candidate.opposingSidePreference);
        }

        float relativeCoverage = (float)candidate.coverage / bestCoverage;
        float coverageWeight = relativeCoverage * relativeCoverage * relativeCoverage;
        float sideWeight = Mathf.Lerp(1f, OpposingSideWeight, candidate.opposingSidePreference);
        return Mathf.Max(0.01f, coverageWeight * sideWeight);
    }

    private static float GetOpposingSidePreference(HashSet<Vector2Int> coveredCells, int gridWidth, PlayerId opposingPlayer)
    {
        if (coveredCells.Count == 0 || gridWidth <= 1)
        {
            return 0.5f;
        }

        float averageX = 0f;
        foreach (Vector2Int cell in coveredCells)
        {
            averageX += cell.x;
        }

        float normalizedX = averageX / coveredCells.Count / (gridWidth - 1);
        return opposingPlayer == PlayerId.PlayerA
            ? 1f - normalizedX
            : normalizedX;
    }

    private static PlayerId GetOpposingPlayer(PlayerId player)
    {
        return player == PlayerId.PlayerA ? PlayerId.PlayerB : PlayerId.PlayerA;
    }

    private struct SearchCandidate
    {
        public SearchPatternDefinition pattern;
        public int patternIndex;
        public Vector2Int anchor;
        public int coverage;
        public float opposingSidePreference;

        public SearchCandidate(SearchPatternDefinition pattern, int patternIndex, Vector2Int anchor, int coverage, float opposingSidePreference)
        {
            this.pattern = pattern;
            this.patternIndex = patternIndex;
            this.anchor = anchor;
            this.coverage = coverage;
            this.opposingSidePreference = opposingSidePreference;
        }
    }
}
