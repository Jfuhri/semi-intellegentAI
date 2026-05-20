using UnityEngine;

public class RoomVolume : MonoBehaviour
{
    [Header("Room")]
    public Collider roomBounds;

    [HideInInspector]
    public float lastVisitedTime = -999f;

    [Header("Debug")]
    public bool drawDebug = true;

    [Header("Debug Timer Display")]
    public bool showTimerLabel = true;
    public Vector3 labelOffset = Vector3.up * 2f;

    public float TimeSinceVisited()
    {
        return Time.time - lastVisitedTime;
    }

    void Reset()
    {
        roomBounds = GetComponent<Collider>();
    }

    public Vector3 GetRandomPointInside()
    {
        if (roomBounds == null)
            return transform.position;

        Bounds bounds = roomBounds.bounds;

        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            transform.position.y,
            Random.Range(bounds.min.z, bounds.max.z)
        );
    }

    public bool ContainsPoint(Vector3 point)
    {
        if (roomBounds == null)
            return false;

        return roomBounds.bounds.Contains(point);
    }

    public void MarkVisited()
    {
        lastVisitedTime = Time.time;
    }

    void OnDrawGizmos()
    {
        if (!drawDebug || roomBounds == null)
            return;

        float timeSinceVisit =
            Application.isPlaying
            ? Time.time - lastVisitedTime
            : 999f;

        // Freshly checked = green
        // Old unchecked = red

        float normalized =
            Mathf.Clamp01(timeSinceVisit / 20f);

        Gizmos.color =
            Color.Lerp(Color.green, Color.red, normalized);

        Bounds bounds = roomBounds.bounds;

        Gizmos.DrawWireCube(bounds.center, bounds.size);

#if UNITY_EDITOR
        if (showTimerLabel)
        {
            UnityEditor.Handles.color = Color.white;

            string label =
                Application.isPlaying
                ? $"Checked: {timeSinceVisit:F1}s ago"
                : "Room Timer";

            UnityEditor.Handles.Label(
                bounds.center + labelOffset,
                label
            );
        }
#endif
    }
}