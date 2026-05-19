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

    private CoverSystem coverSystem;

    private RoomVolume currentRoom;

    void OnEnable() => GlobalEventManager.OnGunshot += HandleGunshot;
    void OnDisable() => GlobalEventManager.OnGunshot -= HandleGunshot;

    void Start()
    {
        GameObject targetObj = GameObject.FindGameObjectWithTag("Player")
            ?? GameObject.FindGameObjectWithTag("PlayerBot");

        target = targetObj != null ? targetObj.transform : null;

        agent = GetComponent<NavMeshAgent>();
        reloadSystem = GetComponent<ReloadSystem>();

        if (reloadSystem == null)
            Debug.LogWarning($"{name} has no ReloadSystem attached!");

        lastKnownTargetPosition = transform.position;
        SetNewPatrolPoint();

        coverSystem = GetComponent<CoverSystem>();
    }

    bool IsTargetTag(GameObject obj)
    {
        return obj != null && (obj.CompareTag("Player") || obj.CompareTag("PlayerBot"));
    }

    void Update()
    {
        if (target == null) return;

        patrolBiasWeight = Mathf.Max(0f, patrolBiasWeight - biasDecayRate * Time.deltaTime);

        float distance = Vector3.Distance(transform.position, target.position);

        // =========================
        // COVER OVERRIDE CHECK
        // =========================
        if (coverSystem != null && coverSystem.seekCoverWhenVisible)
        {
            bool playerVisible = IsInFieldOfView() && HasLineOfSight();

            if (playerVisible)
            {
                agent.isStopped = false;

                if (Time.time >= nextFireTime + 999f)
                {
                    // effectively disables shooting (safe guard)
                }

                if (coverSystem.TryGetCoverPoint(transform.position, out Vector3 coverPoint))
                {
                    agent.SetDestination(coverPoint);
                    isPatrolling = false;
                }

                return; // IMPORTANT: skip all combat logic
            }
        }

        // =========================
        // NORMAL COMBAT BEHAVIOR
        // =========================
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
        if (RoomManager.Instance != null)
        {
            RoomVolume room = RoomManager.Instance.GetNextRoomForEnemy(transform.position);

            if (room != null)
            {
                Vector3 point = room.GetRandomPointInside();

                if (NavMesh.SamplePosition(point, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    currentPatrolTarget = hit.position;
                    agent.SetDestination(currentPatrolTarget);
                    return;
                }
            }
        }

        // fallback (your current system)
        Vector3 candidate = transform.position + Random.insideUnitSphere * patrolRadius;
        candidate.y = 0;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit2, patrolRadius, NavMesh.AllAreas))
        {
            currentPatrolTarget = hit2.position;
            agent.SetDestination(currentPatrolTarget);
        }
    }

    void HandleGunshot(Vector3 gunshotPosition, Object source)
    {
        if (source is GameObject go && !IsTargetTag(go)) return;

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

            Quaternion spread = Quaternion.Euler(vAngle, hAngle, 0f);
            Vector3 dir = spread * transform.forward;

            Instantiate(pelletPrefab, firePoint.position, Quaternion.LookRotation(dir));
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
                EnemyShotgunAndMove ai = ally.GetComponent<EnemyShotgunAndMove>();

                if (ai != null && !ai.isAlerted)
                {
                    ai.OnAlerted(lastKnownTargetPosition);
                }
            }
        }
    }

    public void OnAlerted(Vector3 pos)
    {
        isAlerted = true;
        lastKnownTargetPosition = pos;
        isPatrolling = false;
    }

    bool HasLineOfSight()
    {
        Vector3 dir = (target.position + Vector3.up * 1f) - firePoint.position;

        if (Physics.Raycast(firePoint.position, dir.normalized, out RaycastHit hit, viewDistance, lineOfSightMask))
        {
            return IsTargetTag(hit.transform.gameObject);
        }

        return false;
    }

    bool IsInFieldOfView()
    {
        Vector3 toTarget = target.position - transform.position;
        float angle = Vector3.Angle(transform.forward, toTarget);

        return angle <= viewAngle / 2f && toTarget.magnitude <= viewDistance;
    }

    void FaceTarget()
    {
        Vector3 dir = (target.position - transform.position).normalized;
        dir.y = 0f;

        if (dir.sqrMagnitude > 0.01f)
        {
            Quaternion rot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 10f);
        }
    }

    public void OnHitByPlayer(Vector3 hitOrigin)
    {
        if (target == null) return;

        lastKnownTargetPosition = hitOrigin;

        isPatrolling = true;
        patrolBiasWeight = 1f;

        SetNewPatrolPoint();

        // optional: alert nearby allies on hit
        AlertNearbyAllies();

        // small reactive shot (keeps behavior consistent with rifle AI)
        if (Time.time >= nextFireTime && reloadSystem != null && !reloadSystem.isReloading)
        {
            if (reloadSystem.TryConsumeAmmo())
            {
                FaceTarget();
                ShootShotgun();
                nextFireTime = Time.time + 1f / fireRate;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        RoomVolume room = other.GetComponent<RoomVolume>();

        if (room != null)
        {
            currentRoom = room;
            room.MarkVisited(Time.time);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 left = Quaternion.Euler(0, -viewAngle / 2f, 0) * transform.forward;
        Vector3 right = Quaternion.Euler(0, viewAngle / 2f, 0) * transform.forward;

        Gizmos.DrawRay(transform.position, left * viewDistance);
        Gizmos.DrawRay(transform.position, right * viewDistance);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(currentPatrolTarget, 1f);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, hearingRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, alertRadius);
    }
}