
public enum FleetStance
{
    Hunt,              // nothing known: look for the enemy
    Advance,           // press the attack (winning or healthy-even)
    Hold,              // stay tight and let the enemy come (slightly behind)
    DefensiveFallback, // losing: pull back toward home and stay compact
    Flank,             // reserved for Stage 3+
    Retreat            // badly hurt or badly outnumbered: disengage
}