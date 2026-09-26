using System.Collections.Generic;

// Plain data holder for one player's view of the board on a given turn:
// which of their own ships are alive, which enemy ships are alive, and
// which of those enemies their fog currently considers "known". Built once
// per phase by AIController and handed to the planner classes, so none of
// them need to re-derive rosters or fog themselves.
public class AITurnContext
{
    public List<ShipInstance> MyShips { get; }
    public List<ShipInstance> EnemyShips { get; }
    public FogGrid MyFog { get; }
    public List<ShipInstance> KnownEnemies { get; }

    private AITurnContext(
        List<ShipInstance> myShips,
        List<ShipInstance> enemyShips,
        FogGrid myFog,
        List<ShipInstance> knownEnemies)
    {
        MyShips = myShips;
        EnemyShips = enemyShips;
        MyFog = myFog;
        KnownEnemies = knownEnemies;
    }

    public static AITurnContext Build(GridManager gridManager, PlayerId owner)
    {
        MatchState match = gridManager.Match;
        PlayerId enemyOwner = owner == PlayerId.PlayerA ? PlayerId.PlayerB : PlayerId.PlayerA;

        List<ShipInstance> myShips = GetLivingShips(match.GetPlayer(owner).ships);
        List<ShipInstance> enemyShips = GetLivingShips(match.GetPlayer(enemyOwner).ships);
        FogGrid myFog = gridManager.Fog.GetFogGrid(owner);
        List<ShipInstance> knownEnemies = GetKnownEnemies(myFog, enemyShips);

        return new AITurnContext(myShips, enemyShips, myFog, knownEnemies);
    }

    private static List<ShipInstance> GetLivingShips(List<ShipInstance> ships)
    {
        List<ShipInstance> living = new List<ShipInstance>();
        foreach (var ship in ships)
        {
            if (ship.currentHealth > 0)
            {
                living.Add(ship);
            }
        }
        return living;
    }

    // A ship counts as "known" if any of its occupied cells is known in this
    // player's fog -- matches how CombatResolver.IsTargetKnown gates attacks.
    private static List<ShipInstance> GetKnownEnemies(FogGrid fog, List<ShipInstance> enemies)
    {
        List<ShipInstance> known = new List<ShipInstance>();
        foreach (var enemy in enemies)
        {
            foreach (var cell in enemy.GetOccupiedCells())
            {
                if (fog.IsKnown(cell))
                {
                    known.Add(enemy);
                    break;
                }
            }
        }
        return known;
    }
}