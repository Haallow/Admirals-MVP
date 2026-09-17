public static class ShipFactory
{
    public static ShipInstance CreateShip(ShipType type)
    {
        ShipInstance ship = new ShipInstance();

        switch (type)
        {
            case ShipType.WolfClass:
                ShipData.BuildWolfClass(ship);
                break;
            case ShipType.AthenaClass:
                ShipData.BuildAthenaClass(ship);
                break;
            case ShipType.SwordFishClass:
                ShipData.BuildSwordFishClass(ship);
                break;
        }

        return ship;
    }
}
