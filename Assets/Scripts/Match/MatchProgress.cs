// Immutable match result and completed turn snapshots. MatchState owns their lifecycle.
public sealed class MatchResult
{
    public PlayerId? Winner { get; }
    public bool IsDraw => !Winner.HasValue;
    public int TurnNumber { get; }

    internal MatchResult(PlayerId? winner, int turnNumber)
    {
        Winner = winner;
        TurnNumber = turnNumber;
    }
}

public sealed class TurnSummary
{
    public int Number { get; }
    public PlayerId Player { get; }
    public bool IsPartial { get; internal set; }
    public int Attacks { get; internal set; }
    public int Hits { get; internal set; }
    public int Misses { get; internal set; }
    public int DefensesChosen { get; internal set; }
    public int DefensesAvoided { get; internal set; }
    public int CombatDamageDealt { get; internal set; }
    public int EnemyShipsSunk { get; internal set; }
    public int MinesTriggered { get; internal set; }
    public int MineDamageTaken { get; internal set; }
    public int ShipsLostToMines { get; internal set; }
    public int MineChargesRestored { get; internal set; }
    public int PlanesExpired { get; internal set; }

    internal TurnSummary(int number, PlayerId player)
    {
        Number = number;
        Player = player;
    }

    internal TurnSummary Snapshot(bool partial)
    {
        return new TurnSummary(Number, Player)
        {
            IsPartial = partial,
            Attacks = Attacks,
            Hits = Hits,
            Misses = Misses,
            DefensesChosen = DefensesChosen,
            DefensesAvoided = DefensesAvoided,
            CombatDamageDealt = CombatDamageDealt,
            EnemyShipsSunk = EnemyShipsSunk,
            MinesTriggered = MinesTriggered,
            MineDamageTaken = MineDamageTaken,
            ShipsLostToMines = ShipsLostToMines,
            MineChargesRestored = MineChargesRestored,
            PlanesExpired = PlanesExpired
        };
    }

    public override string ToString()
    {
        return $"Turn {Number} {Player}{(IsPartial ? " (partial)" : "")}: " +
            $"attacks {Attacks}, hits {Hits}, misses {Misses}, defenses {DefensesChosen} " +
            $"(avoided {DefensesAvoided}), combat damage {CombatDamageDealt}, enemy sinks {EnemyShipsSunk}, " +
            $"mines triggered {MinesTriggered}, mine damage taken {MineDamageTaken}, " +
            $"mine losses {ShipsLostToMines}, mine charges restored {MineChargesRestored}, " +
            $"planes expired {PlanesExpired}.";
    }
}
