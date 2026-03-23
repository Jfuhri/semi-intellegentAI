using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(ReloadSystem))]
public class EnemyShotgunAndMove : MonoBehaviour
{
    [Header("Combat")]
    public float shootingRange = 10f;
    public float fireRate = 1f;
    public GameObject pelletPrefab;
    public Transform firePoint;
    public int pelletCount = 6;
    public float spreadAngle = 15f;

    [Header("Vision")]
    public float viewAngle = 120f;
    public float viewDistance = 25f;
    public LayerMask lineOfSightMask;

    [Header("Patrol")]
    public float patrolRadius = 30f;
    public float patrolWaitTime = 3f;
    public float hearingRange = 30f;
    public float biasIncreasePerShot = 0.25f;
    public float biasDecayRate = 0.1f;
    private float patrolBiasWeight = 0f;

    [Header("Backup Communication")]
    public float alertRadius = 15f;
    public bool isAlerted = false;

    private Transform target;
    private NavMeshAgent agent;
    private float nextFireTime;
    private float patrolWaitTimer;
    private Vector3 currentPatrolTarget;
    private bool isPatrolling = true;
    private Vector3 lastKnownTargetPosition;

    private ReloadSystem reloadSystem;

    void OnEnable() => GlobalEventManager.OnGunshot += HandleGunshot;
    void OnDisable() => GlobalEventManager.OnGunshot -= HandleGunshot;

    void Start()
    {
        // Find Player or PlayerBot
        GameObject targetObj = GameObject.FindGameObjectWithTag("Player")
            ?? GameObject.FindGameObjectWithTag("PlayerBot");
        target = targetObj?.transform;

        agent = GetComponent<NavMeshAgent>();
        reloadSystem = GetComponent<ReloadSystem>();

        if (reloadSystem == null)
            Debug.LogWarning($"{name} has no ReloadSystem attached!");

        lastKnownTargetPosition = transform.position;
        SetNewPatrolPoint();
    }

    // Unified check for player/bot tags
    bool IsTargetTag(GameObject obj)
    {
        return obj.CompareTag("Player") || obj.CompareTag("PlayerBot");
    }

    void Update()
    {
        if (target == null) return;

        patrolBiasWeight = Mathf.Max(0f, patrolBiasWeight - biasDecayRate * Time.deltaTime);
        float distance = Vector3.Distance(transform.position, target.position);

        if (distance <= shootingRange && IsInFieldOfView() && HasLineOfSight())
        {
            if (!isAlerted)
            {
                isAlerted = true;
                AlertNearbyAllies();
            }

            agent.isStopped = true;
            FaceTarget();
            lastKnownTargetPosition = target.position;
            patrolBiasWeight = 1f;

            if (Time.time >= nextFireTime && reloadSystem != null && !reloadSystem.isReloading)
            {
                if (reloadSystem.TryConsumeAmmo())
                {
                    ShootShotgun();
                    nextFireTime = Time.time + 1f / fireRate;
                }
            }
        }
        else if (IsInFieldOfView() && HasLineOfSight())
        {
            if (!isAlerted)
            {
                isAlerted = true;
                AlertNearbyAllies();
            }

            agent.isStopped = false;
            agent.SetDestination(target.position);
            lastKnownTargetPosition = target.position;
            patrolBiasWeight = 1f;
            isPatrolling = false;
        }
        else
        {
            PatrolBehavior();
        }
    }

    void PatrolBehavior()
    {
        if (!isPatrolling)
        {
            isPatrolling = true;
            patrolWaitTimer = 0f;
            SetNewPatrolPoint();
        }

        agent.isStopped = false;

        if (!agent.pathPending && agent.remainingDistance < 1f)
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
        Vector3 basePoint = Vector3.Lerp(transform.position, lastKnownTargetPosition, patrolBiasWeight);
        Vector3 randomOffset = Random.insideUnitSphere * patrolRadius * (1f - patrolBiasWeight);
        randomOffset.y = 0f;
        Vector3 candidatePoint = basePoint + randomOffset;

        if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit hit, patrolRadius, NavMesh.AllAreas))
        {
            currentPatrolTarget = hit.position;
            agent.SetDestination(currentPatrolTarget);
        }
    }

    // Reacts to gunshots from Player or PlayerBot
    void HandleGunshot(Vector3 gunshotPosition, Object source)
    {
        GameObject sourceObj = source as GameObject;
        if (sourceObj == null || !IsTargetTag(sourceObj)) return;

        if (Vector3.Distance(transform.position, gunshotPosition) <= hearingRange)
        {
            lastKnownTargetPosition = gunshotPosition;
            patrolBiasWeight += biasIncreasePerShot;
            patrolBiasWeight = Mathf.Clamp01(patrolBiasWeight);

            if (isPatrolling)
                SetNewPatrolPoint();
        }
    }

    void ShootShotgun()
    {
        for (int i = 0; i < pelletCount; i++)
        {
            float hAngle = Random.Range(-spreadAngle / 2f, spreadAngle / 2f);
            float vAngle = Random.Range(-spreadAngle / 2f, spreadAngle / 2f);
            Quaternion spreadRotation = Quaternion.Euler(vAngle, hAngle, 0f);
            Vector3 shootDirection = spreadRotation * transform.forward;

            Instantiate(pelletPrefab, firePoint.position, Quaternion.LookRotation(shootDirection));
        }

        GlobalEventManager.RaiseGunshot(transform.position, this.gameObject);
    }

    void AlertNearbyAllies()
    {
        Collider[] allies = Physics.OverlapSphere(transform.position, alertRadius);
        foreach (Collider ally in allies)
        {
            if (ally.CompareTag("Enemy") && ally.gameObject != gameObject)
            {
                EnemyShotgunAndMove allyAI = ally.GetComponent<EnemyShotgunAndMove>();
                if (allyAI != null && !allyAI.isAlerted)
                {
                    allyAI.OnAlerted(lastKnownTargetPosition);
                }
            }
        }
    }

    public void OnAlerted(Vector3 targetPosition)
    {
        isAlerted = true;
        lastKnownTargetPosition = targetPosition;
        isPatrolling = false;
    }

    bool HasLineOfSight()
    {
        Vector3 direction = (target.position + Vector3.up * 1f) - firePoint.position;
        if (Physics.Raycast(firePoint.position, direction.normalized, out RaycastHit hit, viewDistance, lineOfSightMask))
        {
            return IsTargetTag(hit.transform.gameObject);
        }
        return false;
    }

    bool IsInFieldOfView()
    {
        Vector3 directionToTarget = target.position - transform.position;
        float angle = Vector3.Angle(transform.forward, directionToTarget);
        return angle <= viewAngle / 2f && directionToTarget.magnitude <= viewDistance;
    }

    void FaceTarget()
    {
        Vector3 direction = (target.position - transform.position).normalized;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 10f);
        }
    }

    // Unified reaction for Player and PlayerBot hits
    public void OnHitByPlayer(Vector3 hitDirection, string sourceTag = "Player")
    {
        if (!(sourceTag == "Player" || sourceTag == "PlayerBot")) return;

        lastKnownTargetPosition = transform.position + hitDirection.normalized * 5f;

        if (NavMesh.SamplePosition(lastKnownTargetPosition, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
            agent.isStopped = false;
            isPatrolling = false;
        }

        if (Time.time >= nextFireTime && reloadSystem != null && !reloadSystem.isReloading)
        {
            if (reloadSystem.TryConsumeAmmo())
            {
                ShootShotgun();
                nextFireTime = Time.time + 1f / fireRate;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 leftBoundary = Quaternion.Euler(0, -viewAngle / 2f, 0) * transform.forward;
        Vector3 rightBoundary = Quaternion.Euler(0, viewAngle / 2f, 0) * transform.forward;
        Gizmos.DrawRay(transform.position, leftBoundary * viewDistance);
        Gizmos.DrawRay(transform.position, rightBoundary * viewDistance);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(currentPatrolTarget, 1f);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, hearingRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, alertRadius);
    }
}