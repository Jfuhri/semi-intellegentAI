using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance;

    private RoomVolume[] rooms;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        rooms = Object.FindObjectsByType<RoomVolume>(FindObjectsSortMode.None);
    }

    public RoomVolume GetNextRoomForEnemy(Vector3 fromPosition)
    {
        RoomVolume bestRoom = null;
        float oldestTime = float.MaxValue;

        foreach (var room in rooms)
        {
            float t = room.lastVisitedTime;

            if (t < oldestTime)
            {
                oldestTime = t;
                bestRoom = room;
            }
        }

        return bestRoom;
    }
}