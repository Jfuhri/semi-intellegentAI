using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class UniversalPatrolAI : MonoBehaviour
{
    [Header("Patrol Settings")]
    public float patrolRadius = 30f;       // Max distance from current position for next patrol
    public float patrolWaitTime = 2f;      // Wait time at each patrol point
    public float stoppingDistance = 1f;    // How close to get before picking next point

    private NavMeshAgent agent;
    private Vector3 patrolPoint;
    private float patrolWaitTimer;
    private bool isPatrolling = true;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.stoppingDistance = stoppingDistance;
        SetNewPatrolPoint();
    }

    void Update()
    {
        Patrol();
    }

    void Patrol()
    {
        if (!isPatrolling)
        {
            isPatrolling = true;
            patrolWaitTimer = 0f;
            SetNewPatrolPoint();
        }

        // If agent reached patrol point
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            patrolWaitTimer += Time.deltaTime;

            if (patrolWaitTimer >= patrolWaitTime)
            {
                SetNewPatrolPoint();
                patrolWaitTimer = 0f;
            }
        }
    }

    void SetNewPatrolPoint()
    {
        Vector3 randomOffset = Random.insideUnitSphere * patrolRadius;
        randomOffset.y = 0f;

        Vector3 candidatePoint = transform.position + randomOffset;

        if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit hit, patrolRadius, NavMesh.AllAreas))
        {
            patrolPoint = hit.position;
            agent.SetDestination(patrolPoint);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, patrolRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(patrolPoint, 0.5f);
    }
}