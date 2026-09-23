using System.Collections.Generic;
using UnityEngine;

public class ProvisionalMovementState
{
    public ShipInstance Ship { get; private set; }
    public Vector2Int OriginalAnchor { get; private set; }
    public int OriginalRotation { get; private set; }
    public Vector2Int PreviewAnchor { get; private set; }
    public int PreviewRotation { get; private set; }
    public MovementPathResult Path { get; private set; }
    public bool IsValid { get; private set; }

    public ProvisionalMovementState(ShipInstance ship)
    {
        Ship = ship;
        OriginalAnchor = ship.anchor;
        OriginalRotation = ship.rotationDegrees;
        PreviewAnchor = OriginalAnchor;
        PreviewRotation = OriginalRotation;
        Path = MovementPathResult.Unreachable();
        IsValid = true;
    }

    public void SetPreview(
        Vector2Int previewAnchor,
        int previewRotation,
        MovementPathResult path,
        bool isValid)
    {
        PreviewAnchor = previewAnchor;
        PreviewRotation = previewRotation;
        Path = path;
        IsValid = isValid;
    }

    public List<Vector2Int> GetPreviewCells()
    {
        return FootprintUtil.GetWorldCells(
            PreviewAnchor,
            Ship.footprintOffsets,
            PreviewRotation);
    }
}
