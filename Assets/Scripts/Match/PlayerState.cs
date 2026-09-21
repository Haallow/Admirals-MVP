using System.Collections.Generic;

public class PlayerState
{
    public PlayerId owner;
    public List<ShipType> fleetRoster;
    public List<ShipInstance> ships = new List<ShipInstance>();

    public PlayerState(PlayerId owner, List<ShipType> fleetRoster)
    {
        this.owner = owner;
        this.fleetRoster = fleetRoster;
    }
}