using UnityEngine;

public class TurnManager : MonoBehaviour
{
    [SerializeField] private PlayerId currentPlayer = PlayerId.PlayerA;
    [SerializeField] private Phase currentPhase = Phase.Deployment;
    private bool playerADeploymentConfirmed;
    private bool playerBDeploymentConfirmed;

    public PlayerId CurrentPlayer => currentPlayer;
    public Phase CurrentPhase => currentPhase;

    private void Start()
    {
        LogState();
    }

    // No real Search/Battle actions yet. This milestone only proves the
    // loop cycles correctly and gates who can act when.
    public void AdvancePhase()
    {
        switch (currentPhase)
        {
            case Phase.Deployment:
                return;
            case Phase.Move:
                currentPhase = Phase.Search;
                break;
            case Phase.Search:
                currentPhase = Phase.Battle;
                break;
            case Phase.Battle:
                currentPhase = Phase.Move;
                SwitchPlayer();
                break;
        }

        LogState();
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