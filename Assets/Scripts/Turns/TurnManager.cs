using System;
using UnityEngine;

public class TurnManager : MonoBehaviour
{
    [SerializeField] private PlayerId currentPlayer = PlayerId.PlayerA;
    [SerializeField] private Phase currentPhase = Phase.Move;

    public PlayerId CurrentPlayer => currentPlayer;
    public Phase CurrentPhase => currentPhase;
    public bool MatchStarted { get; private set; }

    // Fired every time AdvancePhase changes the phase. Anything that needs
    // to react to a phase transition (Fog, later Staging actions, later
    // End-phase ticks) subscribes to this instead of TurnManager needing
    // to know those systems exist.
    public event Action<Phase> PhaseChanged;

    // Subscribers can reject leaving the current phase before it changes.
    // TurnManager does not need to know which gameplay rule they validate.
    public event Func<Phase, bool> PhaseAdvanceRequested;

    public bool StartMatch()
    {
        if (MatchStarted) return false;
        currentPlayer = PlayerId.PlayerA;
        currentPhase = Phase.Move;
        MatchStarted = true;
        LogState();
        PhaseChanged?.Invoke(currentPhase);
        return true;
    }

    public void AdvancePhase()
    {
        if (!MatchStarted) return;
        if (PhaseAdvanceRequested != null)
        {
            foreach (Func<Phase, bool> guard in PhaseAdvanceRequested.GetInvocationList())
            {
                if (!guard(currentPhase))
                {
                    return;
                }
            }
        }

        switch (currentPhase)
        {
            case Phase.Move:
                currentPhase = Phase.Staging;
                break;
            case Phase.Staging:
                currentPhase = Phase.Search;
                break;
            case Phase.Search:
                currentPhase = Phase.Battle;
                break;
            case Phase.Battle:
                currentPhase = Phase.End;
                break;
            case Phase.End:
                currentPhase = Phase.Move;
                SwitchPlayer();
                break;
        }

        LogState();
        PhaseChanged?.Invoke(currentPhase);
    }

    private void SwitchPlayer()
    {
        currentPlayer = currentPlayer == PlayerId.PlayerA ? PlayerId.PlayerB : PlayerId.PlayerA;
    }

    private void LogState()
    {
        Debug.Log($"{currentPlayer} turn: {currentPhase} phase");
    }
}
