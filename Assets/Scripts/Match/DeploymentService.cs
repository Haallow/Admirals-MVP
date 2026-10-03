using System.Collections.Generic;
using UnityEngine;

// Drafts are private setup state; only committed ships enter MatchState and the grid.
public sealed class DeploymentDraft
{
    public PlayerId Player { get; }
    public string ZoneId { get; }
    public int RosterSlot { get; }
    public ShipType ShipType { get; }
    public Vector2Int Anchor { get; }
    public int RotationDegrees { get; }
    public IReadOnlyList<Vector2Int> Cells { get; }

    internal DeploymentDraft(PlayerId player, string zoneId, int slot, ShipType type,
        Vector2Int anchor, int rotation, List<Vector2Int> cells)
    {
        Player = player;
        ZoneId = zoneId;
        RosterSlot = slot;
        ShipType = type;
        Anchor = anchor;
        RotationDegrees = rotation;
        Cells = cells.AsReadOnly();
    }
}

public sealed class DeploymentService
{
    private readonly MatchState match;
    private readonly GridManager grid;
    private readonly MapDefinition map;
    private readonly Dictionary<PlayerId, string> assignments = new Dictionary<PlayerId, string>();
    private readonly Dictionary<PlayerId, Dictionary<int, DeploymentDraft>> drafts =
        new Dictionary<PlayerId, Dictionary<int, DeploymentDraft>>();
    private readonly HashSet<PlayerId> confirmed = new HashSet<PlayerId>();

    public bool IsComplete { get; private set; }
    public bool IsValid { get; private set; }
    public IEnumerable<DeploymentDraft> GetVisibleDrafts(PlayerId viewer, PlayerId owner)
    {
        if (viewer == owner || IsComplete)
            return drafts[owner].Values;
        return new DeploymentDraft[0];
    }

    public DeploymentService(MatchState match, GridManager grid, MapDefinition map,
        string playerAZoneId, string playerBZoneId)
    {
        this.match = match;
        this.grid = grid;
        this.map = map;
        drafts[PlayerId.PlayerA] = new Dictionary<int, DeploymentDraft>();
        drafts[PlayerId.PlayerB] = new Dictionary<int, DeploymentDraft>();

        string error = null;
        if (map == null || !map.ValidateDeploymentZones(out error) ||
            playerAZoneId == playerBZoneId ||
            !map.TryGetZone(playerAZoneId, out _) || !map.TryGetZone(playerBZoneId, out _))
        {
            Debug.LogError($"Invalid match deployment zones: {error ?? "assignments must name distinct authored zones"}");
            return;
        }

        assignments[PlayerId.PlayerA] = playerAZoneId;
        assignments[PlayerId.PlayerB] = playerBZoneId;
        IsValid = true;
    }

    public string GetAssignedZone(PlayerId player) =>
        assignments.TryGetValue(player, out string id) ? id : null;

    public bool IsConfirmed(PlayerId player) => confirmed.Contains(player);

    public bool TryGetDraft(PlayerId viewer, PlayerId owner, int slot, out DeploymentDraft draft)
    {
        draft = null;
        return (viewer == owner || IsComplete) &&
            drafts.TryGetValue(owner, out var playerDrafts) &&
            playerDrafts.TryGetValue(slot, out draft);
    }

    public bool CanPlace(PlayerId player, string zoneId, int slot, Vector2Int anchor, int rotation) =>
        TryValidate(player, zoneId, slot, anchor, rotation, out _);

    public bool TryPlace(PlayerId player, string zoneId, int slot, Vector2Int anchor, int rotation)
    {
        if (!TryValidate(player, zoneId, slot, anchor, rotation, out List<Vector2Int> cells))
            return false;
        ShipType type = match.GetPlayer(player).fleetRoster[slot];
        drafts[player][slot] = new DeploymentDraft(player, zoneId, slot, type, anchor, rotation, cells);
        return true;
    }

    private bool TryValidate(PlayerId player, string zoneId, int slot, Vector2Int anchor,
        int rotation, out List<Vector2Int> cells)
    {
        cells = null;
        if (!IsValid || IsComplete || confirmed.Contains(player) ||
            !assignments.TryGetValue(player, out string assigned) || assigned != zoneId ||
            !map.TryGetZone(zoneId, out MapDeploymentZone zone) ||
            slot < 0 || slot >= match.GetPlayer(player).fleetRoster.Count ||
            rotation % 90 != 0) return false;

        ShipInstance definition = ShipFactory.CreateShip(match.GetPlayer(player).fleetRoster[slot]);
        if (!grid.CanPlaceShip(definition, anchor, rotation)) return false;
        cells = FootprintUtil.GetWorldCells(anchor, definition.footprintOffsets, rotation);
        foreach (Vector2Int cell in cells)
        {
            if (!zone.Contains(cell)) return false;

            foreach (var playerDrafts in drafts)
            foreach (var other in playerDrafts.Value)
            {
                if (playerDrafts.Key == player && other.Key == slot) continue;
                foreach (Vector2Int otherCell in other.Value.Cells)
                    if (otherCell == cell) return false;
            }
        }
        return true;
    }

    public bool CanConfirm(PlayerId player)
    {
        if (!IsValid || IsComplete || !assignments.ContainsKey(player)) return false;
        int count = match.GetPlayer(player).fleetRoster.Count;
        if (drafts[player].Count != count) return false;
        for (int slot = 0; slot < count; slot++)
        {
            if (!drafts[player].TryGetValue(slot, out DeploymentDraft draft)) return false;
            bool wasConfirmed = confirmed.Remove(player);
            bool valid = TryValidate(player, draft.ZoneId, slot, draft.Anchor, draft.RotationDegrees, out _);
            if (wasConfirmed) confirmed.Add(player);
            if (!valid) return false;
        }
        return true;
    }

    public bool Confirm(PlayerId player)
    {
        if (!CanConfirm(player)) return false;
        confirmed.Add(player);
        if (confirmed.Count < 2) return true;

        // Recheck both complete formations before any occupancy or roster mutation.
        if (!CanConfirm(PlayerId.PlayerA) || !CanConfirm(PlayerId.PlayerB))
        {
            confirmed.Remove(player);
            return false;
        }
        foreach (PlayerId owner in new[] { PlayerId.PlayerA, PlayerId.PlayerB })
        {
            PlayerState state = match.GetPlayer(owner);
            for (int slot = 0; slot < state.fleetRoster.Count; slot++)
            {
                DeploymentDraft draft = drafts[owner][slot];
                ShipInstance ship = ShipFactory.CreateShip(draft.ShipType);
                ship.owner = owner;
                ship.anchor = draft.Anchor;
                ship.rotationDegrees = draft.RotationDegrees;
                ship.anchorAtTurnStart = draft.Anchor;
                grid.PlaceShip(ship, ship.GetOccupiedCells());
                state.ships.Add(ship);
            }
        }
        IsComplete = true;
        return true;
    }

    // Local deterministic setup uses the same legality query and placement command.
    public bool DraftPlayerB()
    {
        string zoneId = GetAssignedZone(PlayerId.PlayerB);
        if (zoneId == null) return false;
        for (int slot = 0; slot < match.playerB.fleetRoster.Count; slot++)
        {
            bool placed = false;
            for (int y = 0; y < grid.height && !placed; y++)
            for (int x = grid.width - 1; x >= 0 && !placed; x--)
            for (int rotation = 180; rotation < 540 && !placed; rotation += 90)
            {
                int normalized = rotation % 360;
                Vector2Int anchor = new Vector2Int(x, y);
                if (CanPlace(PlayerId.PlayerB, zoneId, slot, anchor, normalized))
                    placed = TryPlace(PlayerId.PlayerB, zoneId, slot, anchor, normalized);
            }
            if (!placed) return false;
        }
        return Confirm(PlayerId.PlayerB);
    }
}
