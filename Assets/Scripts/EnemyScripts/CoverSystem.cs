using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class CoverSystem : MonoBehaviour
{
    [Header("Debug")]
    public bool forceFindCover = false;

    [Header("Testing Override Mode")]
    public bool forceCoverOnlyMode = false; 
    public float coverRepathCooldown = 1.5f;

    [Header("Automatic Cover")]
    public bool seekCoverWhenVisible = true;

    [Header("Cover Search")]
    public float searchRadius = 12f;
    public int samplePoints = 18;
    public float minPlayerDistance = 4f;
    public LayerMask lineOfSightMask;

    [Header("Scoring")]
    public float losWeight = 10f;
    public float distanceWeight = 1f;
    public float flankBias = 0.5f;

    [Header("Gizmos")]
    public bool drawCoverPoints = true;
    public Color validCoverColor = Color.green;
    public Color invalidCoverColor = Color.red;
    public Color selectedCoverColor = Color.cyan;

    private Transform player;
    private NavMeshAgent agent;

    private List<Vector3> validCoverPoints = new List<Vector3>();
    private List<Vector3> invalidCoverPoints = new List<Vector3>();
    private Vector3 selectedCoverPoint;

    private float nextCoverSearchTime;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        agent = GetComponent<NavMeshAgent>();
    }

    void Update()
    {
        if (player == null || agent == null)
            return;

        // =========================
        // HARD OVERRIDE MODE
        // =========================
        if (forceCoverOnlyMode)
        {
            agent.isStopped = false;

            if (Time.time >= nextCoverSearchTime)
            {
                if (TryGetCoverPoint(transform.position, out Vector3 coverPoint))
                {
                    agent.SetDestination(coverPoint);
                }

                nextCoverSearchTime = Time.time + coverRepathCooldown;
            }

            return; // ❌ BLOCK ALL OTHER AI BEHAVIOR
        }

        // =========================
        // DEBUG FORCE COVER
        // =========================
        if (forceFindCover)
        {
            if (TryGetCoverPoint(transform.position, out Vector3 point))
            {
                agent.SetDestination(point);
            }

            return;
        }

        // =========================
        // NORMAL COVER BEHAVIOR
        // =========================
        if (seekCoverWhenVisible && PlayerHasLineOfSight())
        {
            if (Time.time >= nextCoverSearchTime)
            {
                if (TryGetCoverPoint(transform.position, out Vector3 coverPoint))
                {
                    agent.isStopped = false;
                    agent.SetDestination(coverPoint);
                }

                nextCoverSearchTime = Time.time + coverRepathCooldown;
            }
        }
    }

    bool PlayerHasLineOfSight()
    {
        Vector3 enemyCenter = transform.position + Vector3.up * 1.2f;
        Vector3 direction = enemyCenter - (player.position + Vector3.up * 1.6f);

        float distance = direction.magnitude;

        if (Physics.Raycast(
            player.position + Vector3.up * 1.6f,
            direction.normalized,
            out RaycastHit hit,
            distance,
            lineOfSightMask))
        {
            return hit.transform == transform;
        }

        return false;
    }

    public bool TryGetCoverPoint(Vector3 fromPosition, out Vector3 bestPoint)
    {
        bestPoint = fromPosition;

        validCoverPoints.Clear();
        invalidCoverPoints.Clear();

        float bestScore = float.MinValue;

        Vector3 toPlayerBase = (player.position - fromPosition).normalized;

        for (int i = 0; i < samplePoints; i++)
        {
            Vector3 randomDir = Random.insideUnitSphere * searchRadius;
            randomDir.y = 0f;

            Vector3 candidate = fromPosition + randomDir;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, searchRadius, NavMesh.AllAreas))
                continue;

            Vector3 point = hit.position;

            float distToPlayer = Vector3.Distance(point, player.position);

            if (distToPlayer < minPlayerDistance)
            {
                invalidCoverPoints.Add(point);
                continue;
            }

            bool isCover = IsActuallyInCover(point);

            // =========================
            // COMMITMENT-BASED SCORING
            // =========================
            float score = 0f;

            Vector3 moveDir = (point - fromPosition).normalized;

            // 1. ESCAPE MOMENTUM (MOST IMPORTANT CHANGE)
            float escapeBias = Vector3.Dot(moveDir, -toPlayerBase);
            score += escapeBias * losWeight * 1.5f;

            // 2. COVER VALUE (still important, but not absolute)
            score += isCover ? losWeight : -losWeight * 0.3f;

            // 3. SIDE FLANK PREFERENCE (reduced importance)
            Vector3 side = Vector3.Cross(Vector3.up, toPlayerBase);
            float flank = Mathf.Abs(Vector3.Dot(moveDir, side));
            score += flank * flankBias;

            // 4. DISTANCE PENALTY (softened so it doesn't block hallway escape)
            score -= distToPlayer * (distanceWeight * 0.5f);

            // =========================
            // VISUAL DEBUG GROUPING
            // =========================
            if (isCover)
                validCoverPoints.Add(point);
            else
                invalidCoverPoints.Add(point);

            // BEST PICK
            if (score > bestScore)
            {
                bestScore = score;
                bestPoint = point;
            }
        }

        selectedCoverPoint = bestPoint;

        return bestScore > float.MinValue;
    }

    bool IsActuallyInCover(Vector3 point)
    {
        Vector3 playerEye = player.position + Vector3.up * 1.6f;

        Vector3[] testPoints =
        {
            point + Vector3.up * 1.2f,
            point + Vector3.up * 1.2f + transform.right * 0.5f,
            point + Vector3.up * 1.2f - transform.right * 0.5f
        };

        int blocked = 0;

        foreach (Vector3 t in testPoints)
        {
            Vector3 dir = t - playerEye;

            if (Physics.Raycast(playerEye, dir.normalized, out RaycastHit hit, dir.magnitude, lineOfSightMask))
            {
                if (!hit.transform.CompareTag("Enemy"))
                    blocked++;
            }
        }

        return blocked >= 2;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawCoverPoints) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, searchRadius);

        Gizmos.color = validCoverColor;
        foreach (var p in validCoverPoints)
            Gizmos.DrawSphere(p, 0.25f);

        Gizmos.color = invalidCoverColor;
        foreach (var p in invalidCoverPoints)
            Gizmos.DrawWireSphere(p, 0.2f);

        Gizmos.color = selectedCoverColor;
        Gizmos.DrawSphere(selectedCoverPoint, 0.4f);

        Gizmos.DrawLine(transform.position, selectedCoverPoint);
    }
}