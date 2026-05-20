using UnityEngine;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(ReloadSystem))]
public class EnemyShootAndMove : MonoBehaviour
{
    [Header("Combat")]
    public float shootingRange = 10f;
    public float fireRate = 1f;
    public GameObject bulletPrefab;
    public Transform firePoint;

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

    [Header("Backup System")]
    public float allyBroadcastRadius = 20f;

    [Header("Debug Cover Test")]
    public bool debugForceCoverKey = false;
    public KeyCode forceCoverKey = KeyCode.C;

    private float patrolBiasWeight = 0f;

    private Transform target;
    private NavMeshAgent agent;

    private ReloadSystem reloadSystem;
    private SelfHealSystem selfHealSystem;
    private CoverSystem coverSystem;

    private float nextFireTime;
    private float patrolWaitTimer;

    private Vector3 currentPatrolTarget;
    private Vector3 lastKnownTargetPosition;

    private bool isPatrolling = true;


    void OnEnable() => GlobalEventManager.OnGunshot += HandleGunshot;
    void OnDisable() => GlobalEventManager.OnGunshot -= HandleGunshot;

    void Start()
    {
        target = FindTarget();

        agent = GetComponent<NavMeshAgent>();
        reloadSystem = GetComponent<ReloadSystem>();
        selfHealSystem = GetComponent<SelfHealSystem>();
        coverSystem = GetComponent<CoverSystem>();

        if (reloadSystem == null)
            Debug.LogWarning($"{name} has no ReloadSystem attached!");

        lastKnownTargetPosition = transform.position;

        SetNewPatrolPoint();
    }

    Transform FindTarget()
    {
        GameObject targetObj =
            GameObject.FindGameObjectWithTag("Player")
            ?? GameObject.FindGameObjectWithTag("PlayerBot");

        return targetObj?.transform;
    }

    bool IsTargetTag(GameObject obj)
    {
        return obj.CompareTag("Player") || obj.CompareTag("PlayerBot");
    }

    void Update()
    {
        if (target == null)
            return;

        if (debugForceCoverKey && Input.GetKeyDown(forceCoverKey))
        {
            ForceSeekCover();
        }

        patrolBiasWeight =
            Mathf.Max(0f, patrolBiasWeight - biasDecayRate * Time.deltaTime);

        float distance =
            Vector3.Distance(transform.position, target.position);

        // =========================
        // SELF HEALING (unchanged)
        // =========================
        if (selfHealSystem != null && selfHealSystem.ShouldHeal())
        {
            agent.isStopped = true;

            StartCoroutine(selfHealSystem.HealRoutine());
            return;
        }

        // =========================
        // COVER OVERRIDE (NEW)
        // =========================
        if (coverSystem != null &&
            coverSystem.seekCoverWhenVisible &&
            IsInFieldOfView() &&
            HasLineOfSight())
        {
            if (coverSystem.TryGetCoverPoint(transform.position, out Vector3 coverPoint))
            {
                agent.isStopped = false;
                agent.SetDestination(coverPoint);
                isPatrolling = false;

                // IMPORTANT: skip combat while in cover mode
                return;
            }
        }

        // =========================
        // CLOSE COMBAT
        // =========================
        if (distance <= shootingRange &&
            IsInFieldOfView() &&
            HasLineOfSight())
        {
            agent.isStopped = true;

            FaceTarget();

            lastKnownTargetPosition = target.position;
            patrolBiasWeight = 1f;

            BroadcastToNearbyAllies(lastKnownTargetPosition);

            if (Time.time >= nextFireTime &&
                !reloadSystem.isReloading)
            {
                if (reloadSystem.TryConsumeAmmo())
                {
                    Shoot();
                    nextFireTime = Time.time + 1f / fireRate;
                }
            }
        }
        // =========================
        // CHASE TARGET
        // =========================
        else if (IsInFieldOfView() && HasLineOfSight())
        {
            agent.isStopped = false;

            agent.SetDestination(target.position);

            lastKnownTargetPosition = target.position;

            patrolBiasWeight = 1f;
            isPatrolling = false;

            BroadcastToNearbyAllies(lastKnownTargetPosition);
        }
        else
        {
            PatrolBehavior();
        }
    }

    public void OnHitByPlayer(Vector3 hitOrigin)
    {
        lastKnownTargetPosition = hitOrigin;

        patrolBiasWeight = 1f;
        isPatrolling = true;

        SetNewPatrolPoint();

        BroadcastToNearbyAllies(hitOrigin);

        // Emergency heal trigger
        if (selfHealSystem != null &&
            selfHealSystem.ShouldHeal())
        {
            StartCoroutine(selfHealSystem.HealRoutine());
        }

        if (target != null &&
            !reloadSystem.isReloading &&
            Time.time >= nextFireTime)
        {
            FaceTarget();

            if (reloadSystem.TryConsumeAmmo())
            {
                Shoot();
                nextFireTime = Time.time + 1f / fireRate;
            }
        }
    }

    void BroadcastToNearbyAllies(Vector3 targetPosition)
    {
        Collider[] hitColliders =
            Physics.OverlapSphere(transform.position, allyBroadcastRadius);

        foreach (var col in hitColliders)
        {
            if (col.gameObject != gameObject &&
                col.CompareTag("Enemy"))
            {
                EnemyShootAndMove ally =
                    col.GetComponent<EnemyShootAndMove>();

                if (ally != null)
                    ally.ReceiveBackupCall(targetPosition);
            }
        }
    }

    public void ReceiveBackupCall(Vector3 targetPosition)
    {
        lastKnownTargetPosition = targetPosition;

        patrolBiasWeight = 1f;

        isPatrolling = true;

        SetNewPatrolPoint();
    }

    bool HasLineOfSight()
    {
        Vector3 direction =
            (target.position + Vector3.up) - firePoint.position;

        if (Physics.Raycast(
            firePoint.position,
            direction.normalized,
            out RaycastHit hit,
            viewDistance,
            lineOfSightMask))
        {
            return IsTargetTag(hit.transform.gameObject);
        }

        return false;
    }

    bool IsInFieldOfView()
    {
        Vector3 directionToTarget =
            target.position - transform.position;

        float angle =
            Vector3.Angle(transform.forward, directionToTarget);

        return angle <= viewAngle / 2f &&
               directionToTarget.magnitude <= viewDistance;
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

        if (!agent.pathPending &&
            agent.remainingDistance < 1f)
        {
            patrolWaitTimer += Time.deltaTime;

            if (patrolWaitTimer >= patrolWaitTime)
            {
                SetNewPatrolPoint();
                patrolWaitTimer = 0f;
            }
        }
    }

    private RoomVolume currentRoomTarget;
    private bool hasMarkedRoomVisited;

    void SetNewPatrolPoint()
    {
        Vector3 targetPoint;

        RoomVolume targetRoom = null;

        if (RoomManager.Instance != null)
        {
            targetRoom =
                RoomManager.Instance.GetNextRoomForEnemy(transform.position);
        }

        // =========================
        // ROOM-BASED PATROL
        // =========================
        if (targetRoom != null)
        {
            targetRoom.MarkVisited();

            targetPoint = targetRoom.GetRandomPointInside();
        }
        else
        {
            // fallback old patrol logic

            Vector3 basePoint =
                Vector3.Lerp(
                    transform.position,
                    lastKnownTargetPosition,
                    patrolBiasWeight);

            Vector3 randomOffset =
                Random.insideUnitSphere * patrolRadius;

            randomOffset.y = 0f;

            targetPoint = basePoint + randomOffset;
        }

        if (NavMesh.SamplePosition(
            targetPoint,
            out NavMeshHit hit,
            patrolRadius,
            NavMesh.AllAreas))
        {
            currentPatrolTarget = hit.position;

            agent.SetDestination(currentPatrolTarget);
        }
    }

    void Shoot()
    {
        if (bulletPrefab == null || firePoint == null)
            return;

        Vector3 direction =
            (target.position - firePoint.position).normalized;

        Quaternion lookRotation =
            Quaternion.LookRotation(direction);

        Instantiate(
            bulletPrefab,
            firePoint.position,
            lookRotation);

        GlobalEventManager.RaiseGunshot(
            firePoint.position,
            this);
    }

    void HandleGunshot(Vector3 gunshotPosition, Object source)
    {
        if (source == this)
            return;

        if (Vector3.Distance(
            transform.position,
            gunshotPosition) <= hearingRange)
        {
            lastKnownTargetPosition = gunshotPosition;

            patrolBiasWeight += biasIncreasePerShot;

            patrolBiasWeight =
                Mathf.Clamp01(patrolBiasWeight);

            if (isPatrolling)
                SetNewPatrolPoint();
        }
    }

    void FaceTarget()
    {
        Vector3 direction =
            (target.position - transform.position).normalized;

        direction.y = 0f;

        if (direction == Vector3.zero)
            return;

        Quaternion lookRotation =
            Quaternion.LookRotation(direction);

        transform.rotation =
            Quaternion.Slerp(
                transform.rotation,
                lookRotation,
                Time.deltaTime * 10f);
    }

    public void ForceSeekCover()
    {
        if (coverSystem == null)
            return;

        if (coverSystem.TryGetCoverPoint(
            transform.position,
            out Vector3 coverPoint))
        {
            agent.isStopped = false;

            agent.SetDestination(coverPoint);

            isPatrolling = false;

            Debug.DrawLine(
                transform.position,
                coverPoint,
                Color.green,
                2f);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        Vector3 leftBoundary =
            Quaternion.Euler(0, -viewAngle / 2f, 0) *
            transform.forward;

        Vector3 rightBoundary =
            Quaternion.Euler(0, viewAngle / 2f, 0) *
            transform.forward;

        Gizmos.DrawRay(transform.position, leftBoundary * viewDistance);
        Gizmos.DrawRay(transform.position, rightBoundary * viewDistance);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(currentPatrolTarget, 1f);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, hearingRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, allyBroadcastRadius);
    }
}