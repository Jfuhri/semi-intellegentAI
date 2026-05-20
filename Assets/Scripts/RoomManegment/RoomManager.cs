using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance;

    private RoomVolume[] rooms;

    void Awake()
    {
        Instance = this;

        rooms = Object.FindObjectsByType<RoomVolume>(
            FindObjectsSortMode.None);
    }

    public RoomVolume[] GetRooms()
    {
        return rooms;
    }

    public RoomVolume GetNextRoomForEnemy(Vector3 fromPosition)
    {
        if (rooms == null || rooms.Length == 0)
        {
            rooms = Object.FindObjectsByType<RoomVolume>(
                FindObjectsSortMode.None);
        }

        RoomVolume bestRoom = null;
        float oldestVisitTime = -1f;

        foreach (RoomVolume room in rooms)
        {
            if (room == null || room.roomBounds == null)
                continue;

            float timeSinceVisited = room.TimeSinceVisited();

            if (timeSinceVisited > oldestVisitTime)
            {
                oldestVisitTime = timeSinceVisited;
                bestRoom = room;
            }
        }

        return bestRoom;
    }
}