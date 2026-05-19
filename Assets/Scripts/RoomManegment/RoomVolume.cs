using UnityEngine;

public class RoomVolume : MonoBehaviour
{
    public Collider roomBounds;

    public float lastVisitedTime = -999f;

    void Reset()
    {
        roomBounds = GetComponent<Collider>();
    }

    public Vector3 GetRandomPointInside()
    {
        if (roomBounds == null)
            return transform.position;

        Bounds b = roomBounds.bounds;

        return new Vector3(
            Random.Range(b.min.x, b.max.x),
            transform.position.y,
            Random.Range(b.min.z, b.max.z)
        );
    }

    public void MarkVisited()
    {
        lastVisitedTime = Time.time;
    }

    public bool Contains(Vector3 point)
    {
        return roomBounds != null && roomBounds.bounds.Contains(point);
    }
}