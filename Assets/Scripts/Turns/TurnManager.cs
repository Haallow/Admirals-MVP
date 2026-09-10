using UnityEngine;

public class TurnManager : MonoBehaviour
{
    [SerializeField] private PlayerId currentPlayer = PlayerId.PlayerA;
    [SerializeField] private Phase currentPhase = Phase.Move;

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

    private void SwitchPlayer()
    {
        currentPlayer = currentPlayer == PlayerId.PlayerA ? PlayerId.PlayerB : PlayerId.PlayerA;
    }

    private void LogState()
    {
        Debug.Log($"{currentPlayer} turn: {currentPhase} phase");
    }
}