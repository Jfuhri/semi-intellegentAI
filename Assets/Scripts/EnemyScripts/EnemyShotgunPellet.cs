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

    private Transform player;
    private NavMeshAgent agent;
    private float nextFireTime;
    private float patrolWaitTimer;
    private Vector3 currentPatrolTarget;
    private bool isPatrolling = true;
    private Vector3 lastKnownPlayerPosition;

    private ReloadSystem reloadSystem;

    void OnEnable() => GlobalEventManager.OnGunshot += HandleGunshot;
    void OnDisable() => GlobalEventManager.OnGunshot -= HandleGunshot;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        agent = GetComponent<NavMeshAgent>();
        reloadSystem = GetComponent<ReloadSystem>();

        if (reloadSystem == null)
            Debug.LogWarning($"{name} has no ReloadSystem attached!");

        lastKnownPlayerPosition = transform.position;
        SetNewPatrolPoint();
    }

    void Update()
    {
        if (player == null) return;

        patrolBiasWeight = Mathf.Max(0f, patrolBiasWeight - biasDecayRate * Time.deltaTime);
        float distance = Vector3.Distance(transform.position, player.position);

        // Player in range
        if (distance <= shootingRange && IsInFieldOfView() && HasLineOfSight())
        {
            if (!isAlerted)
            {
                isAlerted = true;
                AlertNearbyAllies();
            }

            agent.isStopped = true;
            FacePlayer();
            lastKnownPlayerPosition = player.position;
            patrolBiasWeight = 1f;

            // Only shoot if ReloadSystem allows ammo
            if (Time.time >= nextFireTime && reloadSystem != null && !reloadSystem.isReloading)
            {
                if (reloadSystem.TryConsumeAmmo())
                {
                    ShootShotgun();
                    nextFireTime = Time.time + 1f / fireRate;
                }
            }
        }
        // Player seen but not in shooting range
        else if (IsInFieldOfView() && HasLineOfSight())
        {
            if (!isAlerted)
            {
                isAlerted = true;
                AlertNearbyAllies();
            }

            agent.isStopped = false;
            agent.SetDestination(player.position);
            lastKnownPlayerPosition = player.position;
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
        Vector3 basePoint = Vector3.Lerp(transform.position, lastKnownPlayerPosition, patrolBiasWeight);
        Vector3 randomOffset = Random.insideUnitSphere * patrolRadius * (1f - patrolBiasWeight);
        randomOffset.y = 0f;
        Vector3 candidatePoint = basePoint + randomOffset;

        if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit hit, patrolRadius, NavMesh.AllAreas))
        {
            currentPatrolTarget = hit.position;
            agent.SetDestination(currentPatrolTarget);
        }
    }

    void HandleGunshot(Vector3 position, Object source)
    {
        if (source == this) return;

        if (Vector3.Distance(transform.position, position) <= hearingRange)
        {
            lastKnownPlayerPosition = position;
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

        GlobalEventManager.RaiseGunshot(transform.position, this);
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
                    allyAI.OnAlerted(player.position);
                }
            }
        }
    }

    public void OnAlerted(Vector3 playerPosition)
    {
        isAlerted = true;
        lastKnownPlayerPosition = playerPosition;
        isPatrolling = false;
        Debug.Log($"{gameObject.name} is alerted by ally!");
    }

    bool HasLineOfSight()
    {
        Vector3 direction = (player.position + Vector3.up * 1f) - firePoint.position;
        if (Physics.Raycast(firePoint.position, direction.normalized, out RaycastHit hit, viewDistance, lineOfSightMask))
        {
            return hit.transform.CompareTag("Player");
        }
        return false;
    }

    bool IsInFieldOfView()
    {
        Vector3 directionToPlayer = player.position - transform.position;
        float angle = Vector3.Angle(transform.forward, directionToPlayer);
        return angle <= viewAngle / 2f && directionToPlayer.magnitude <= viewDistance;
    }

    void FacePlayer()
    {
        Vector3 direction = (player.position - transform.position).normalized;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 10f);
        }
    }

    public void OnHitByPlayer(Vector3 hitDirection)
    {
        Vector3 evadeTarget = transform.position + hitDirection.normalized * 5f;
        if (NavMesh.SamplePosition(evadeTarget, out NavMeshHit hit, 5f, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
            agent.isStopped = false;
            isPatrolling = false;
        }

        // Optional: shoot back after being hit
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