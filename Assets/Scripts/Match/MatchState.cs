using System.Collections.Generic;

// Owns both players' fleets. This is what Fog resolution, AI, and anything
// needing "every ship in the game" should read from, instead of two separate
// hardcoded fields on GridManager.
public class MatchState
{
    public PlayerState playerA;
    public PlayerState playerB;

    public MatchState(PlayerState playerA, PlayerState playerB)
    {
        this.playerA = playerA;
        this.playerB = playerB;
    }

    public PlayerState GetPlayer(PlayerId id)
    {
        return id == PlayerId.PlayerA ? playerA : playerB;
    }

    public List<ShipInstance> AllShips()
    {
        List<ShipInstance> all = new List<ShipInstance>();
        all.AddRange(playerA.ships);
        all.AddRange(playerB.ships);
        return all;
    }
}