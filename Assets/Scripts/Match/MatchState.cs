using System.Collections.Generic;

// Owns both players' fleets. This is what Fog resolution, AI, and anything
// needing "every ship in the game" should read from, instead of two separate
// hardcoded fields on GridManager.
public class MatchState
{
    public PlayerState playerA;
    public PlayerState playerB;

    // All live mines on the board, owned by both sides.
    // GridManager is authoritative for deploying and removing mines.
    public List<MineTile> mines = new List<MineTile>();

    // All live reconnaissance planes on the board, owned by both sides.
    // GridManager is authoritative for deploying, moving, and removing planes.
    public List<PlaneUnit> planes = new List<PlaneUnit>();

    private readonly List<TurnSummary> turnSummaries = new List<TurnSummary>();
    public IReadOnlyList<TurnSummary> TurnSummaries => turnSummaries.AsReadOnly();
    public TurnSummary CurrentTurnSummary { get; private set; }
    public MatchResult Result { get; private set; }

    public MatchState(PlayerState playerA, PlayerState playerB)
    {
        this.playerA = playerA;
        this.playerB = playerB;
    }

    public PlayerState GetPlayer(PlayerId id)
    {
        return id == PlayerId.PlayerA ? playerA : playerB;
    }

    internal void BeginTurn(PlayerId player)
    {
        if (Result == null && CurrentTurnSummary == null)
            CurrentTurnSummary = new TurnSummary(turnSummaries.Count + 1, player);
    }

    internal TurnSummary CompleteTurn(bool partial)
    {
        if (CurrentTurnSummary == null) return null;
        TurnSummary completed = CurrentTurnSummary.Snapshot(partial);
        turnSummaries.Add(completed);
        CurrentTurnSummary = null;
        return completed;
    }

    internal void SetResult(PlayerId? winner)
    {
        if (Result == null)
            Result = new MatchResult(winner, CurrentTurnSummary?.Number ?? turnSummaries.Count);
    }

    public List<ShipInstance> AllShips()
    {
        List<ShipInstance> all = new List<ShipInstance>();
        all.AddRange(playerA.ships);
        all.AddRange(playerB.ships);
        return all;
    }
}
