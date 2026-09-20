using UnityEngine;

public static class AIDeploymentPlanner
{
    public static DeploymentAI.Decision ChooseDeployment(GridManager gridManager, ShipInstance ship)
    {
        return DeploymentAI.ChooseDeployment(ship, gridManager, gridManager.DeploymentDeadSpaceColumns);
    }

    public static bool TryDeploy(GridManager gridManager, TurnManager turnManager, ShipInstance ship, PlayerId player, bool alreadyDecided)
    {
        if (alreadyDecided || ship == null)
        {
            return alreadyDecided;
        }

        DeploymentAI.Decision decision = ChooseDeployment(gridManager, ship);
        if (gridManager.DeployShip(ship, player, decision.anchor, decision.rotationDegrees, gridManager.DeploymentDeadSpaceColumns))
        {
            Debug.Log($"[Deployment AI] Player {player} selected {decision.anchor}, rotation {decision.rotationDegrees}, score {decision.score:F1}.");
            turnManager.ConfirmDeployment(player);
            return true;
        }

        Debug.LogError("[Deployment AI] Could not find a valid deployment position.");
        return alreadyDecided;
    }
}
