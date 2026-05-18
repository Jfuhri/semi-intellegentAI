using UnityEngine;
using UnityEngine.AI;

public class CoverSystem : MonoBehaviour
{
    [Header("Cover Search")]
    public float searchRadius = 12f;
    public int samplePoints = 18;
    public float minPlayerDistance = 4f;
    public LayerMask lineOfSightMask;

    [Header("Scoring")]
    public float losWeight = 10f;        // strong priority: breaking vision
    public float distanceWeight = 1f;     // prefer not too far/too close
    public float flankBias = 0.5f;        // encourages side movement

    Transform player;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    public bool TryGetCoverPoint(Vector3 fromPosition, out Vector3 bestPoint)
    {
        bestPoint = fromPosition;

        if (player == null)
            return false;

        float bestScore = float.MinValue;

        for (int i = 0; i < samplePoints; i++)
        {
            Vector3 randomDir = Random.insideUnitSphere * searchRadius;
            randomDir.y = 0;

            Vector3 candidate = fromPosition + randomDir;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, searchRadius, NavMesh.AllAreas))
                continue;

            Vector3 point = hit.position;

            // must not be too close to player
            float distToPlayer = Vector3.Distance(point, player.position);
            if (distToPlayer < minPlayerDistance)
                continue;

            float score = 0f;

            // 1. Line of sight check (highest priority)
            if (!HasLineOfSight(point))
                score += losWeight;
            else
                score -= losWeight;

            // 2. Distance preference (not too far, not too close)
            score -= distToPlayer * distanceWeight;

            // 3. Flank bias (encourages spread instead of stacking)
            Vector3 toPlayer = (player.position - fromPosition).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, toPlayer);

            float sideAlignment = Mathf.Abs(Vector3.Dot((point - fromPosition).normalized, side));
            score += sideAlignment * flankBias;

            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = point;
            }
        }

        return bestScore > float.MinValue;
    }

    bool HasLineOfSight(Vector3 point)
    {
        Vector3 dir = (player.position - point).normalized;

        if (Physics.Raycast(point + Vector3.up * 1.5f, dir, out RaycastHit hit, 50f, lineOfSightMask))
        {
            return hit.transform.CompareTag("Player");
        }

        return false;
    }
}