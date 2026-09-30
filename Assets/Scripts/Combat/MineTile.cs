using UnityEngine;

// A live mine on the board, placed during Staging by a SwordFish.
// Plain class, not a MonoBehaviour — same convention as ShipInstance/ChargeState.
// Owned by MatchState.mines. GridManager reads this list to resolve detonations.
public class MineTile
{
    // Who deployed this mine (used for fog: only the enemy's mine is hidden).
    public PlayerId owner;

    // Board position.
    public Vector2Int position;

    // Flat damage applied on detonation — copied from MineProfile.damage at deploy time.
    // No roll, no armor reduction, cannot be blocked by defenses.
    public int damage;

    public MineTile(PlayerId owner, Vector2Int position, int damage)
    {
        this.owner    = owner;
        this.position = position;
        this.damage   = damage;
    }
}
