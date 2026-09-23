using System;
using UnityEngine;

public class TurnManager : MonoBehaviour
{
    [SerializeField] private PlayerId currentPlayer = PlayerId.PlayerA;
    [SerializeField] private Phase currentPhase = Phase.Deployment;
    private bool playerADeploymentConfirmed;
    private bool playerBDeploymentConfirmed;

    public PlayerId CurrentPlayer => currentPlayer;
    public Phase CurrentPhase => currentPhase;

    // Fired every time AdvancePhase changes the phase. Anything that needs
    // to react to a phase transition (Fog, later Staging actions, later
    // End-phase ticks) subscribes to this instead of TurnManager needing
    // to know those systems exist.
    public event Action<Phase> PhaseChanged;

    private void Start()
    {
        LogState();
    }

    public void AdvancePhase()
    {
        switch (currentPhase)
        {
            case Phase.Deployment:
                return;
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

    public void StartCombat()
    {
        currentPlayer = PlayerId.PlayerA;
        currentPhase = Phase.Move;
        LogState();
    }

    public void ConfirmDeployment(PlayerId player)
    {
        if (player == PlayerId.PlayerA)
        {
            playerADeploymentConfirmed = true;
        }
        else
        {
            playerBDeploymentConfirmed = true;
        }

        Debug.Log($"[Deployment] {player} confirmed.");
        if (playerADeploymentConfirmed && playerBDeploymentConfirmed)
        {
            StartCombat();
        }
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