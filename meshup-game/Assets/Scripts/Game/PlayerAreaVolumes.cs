using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerAreaVolumes : MonoBehaviour
{
    [Header("Volumes (local space)")]
    [Tooltip("Interior of the waiting room. Fish may swim above this finite-height volume.")]
    [SerializeField] private Vector3 forbiddenVolumeCenter = new(58.1f, -3.29f, 34.1f);
    [SerializeField] private Vector3 forbiddenVolumeSize = new(20f, 10f, 20f);
    [Tooltip("Interior of the corridor connecting the two player rooms.")]
    [SerializeField] private Vector3 corridorForbiddenVolumeCenter = new(36.35f, -3.29f, 27.91f);
    [SerializeField] private Vector3 corridorForbiddenVolumeSize = new(25f, 10f, 10f);
    [Tooltip("Interior of the glass playing room.")]
    [SerializeField] private Vector3 playingRoomForbiddenVolumeCenter = new(17.35f, -3.29f, 24.01f);
    [SerializeField] private Vector3 playingRoomForbiddenVolumeSize = new(17f, 10f, 21f);
    [SerializeField, Min(0f)] private float forbiddenSafetyMargin = 2f;

    public Bounds WaitingRoomBounds(float extraPadding = 0f) =>
        ExpandedBounds(forbiddenVolumeCenter, forbiddenVolumeSize, extraPadding);

    public Bounds CorridorBounds(float extraPadding = 0f) =>
        ExpandedBounds(corridorForbiddenVolumeCenter, corridorForbiddenVolumeSize,
            extraPadding);

    public Bounds PlayingRoomBounds(float extraPadding = 0f) =>
        ExpandedBounds(playingRoomForbiddenVolumeCenter, playingRoomForbiddenVolumeSize,
            extraPadding);

    /// <summary>Checks the horizontal footprint at any height.</summary>
    public bool ContainsFootprint(Vector3 worldPosition, float padding = 0f)
    {
        Vector3 localPosition = transform.InverseTransformPoint(worldPosition);
        float safePadding = Mathf.Max(0f, padding);
        return IsInsideFootprint(localPosition, forbiddenVolumeCenter,
                forbiddenVolumeSize, safePadding)
            || IsInsideFootprint(localPosition, corridorForbiddenVolumeCenter,
                corridorForbiddenVolumeSize, safePadding)
            || IsInsideFootprint(localPosition, playingRoomForbiddenVolumeCenter,
                playingRoomForbiddenVolumeSize, safePadding);
    }

    private Bounds ExpandedBounds(Vector3 center, Vector3 size, float extraPadding)
    {
        var bounds = new Bounds(center, PositiveSize(size));
        bounds.Expand((forbiddenSafetyMargin + Mathf.Max(0f, extraPadding)) * 2f);
        return bounds;
    }

    private static bool IsInsideFootprint(Vector3 position, Vector3 center,
        Vector3 size, float padding)
    {
        Vector3 halfSize = PositiveSize(size) * 0.5f;
        return Mathf.Abs(position.x - center.x) <= halfSize.x + padding
            && Mathf.Abs(position.z - center.z) <= halfSize.z + padding;
    }

    private static Vector3 PositiveSize(Vector3 size)
    {
        return new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y),
            Mathf.Max(0.01f, size.z));
    }

    private void OnValidate()
    {
        forbiddenVolumeSize = PositiveSize(forbiddenVolumeSize);
        corridorForbiddenVolumeSize = PositiveSize(corridorForbiddenVolumeSize);
        playingRoomForbiddenVolumeSize = PositiveSize(playingRoomForbiddenVolumeSize);
    }
}
