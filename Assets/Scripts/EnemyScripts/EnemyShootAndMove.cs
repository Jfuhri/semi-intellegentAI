using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

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
    private float patrolBiasWeight = 0f;

    [Header("Backup System")]
    public float allyBroadcastRadius = 20f;

    [Header("Healer System")]
    public float healRequestThreshold = 40f; // call for healer if health drops below this
    public float healerBroadcastRadius = 20f;

    private Transform player;
    private NavMeshAgent agent;
    private float nextFireTime;
    private float patrolWaitTimer;
    private Vector3 currentPatrolTarget;
    private bool isPatrolling = true;
    private Vector3 lastKnownPlayerPosition;
    private ReloadSystem reloadSystem;

    private Health healthComponent;

    void OnEnable() => GlobalEventManager.OnGunshot += HandleGunshot;
    void OnDisable() => GlobalEventManager.OnGunshot -= HandleGunshot;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        agent = GetComponent<NavMeshAgent>();
        reloadSystem = GetComponent<ReloadSystem>();
        healthComponent = GetComponent<Health>();

        if (reloadSystem == null)
            Debug.LogWarning($"{name} has no ReloadSystem attached!");
        if (healthComponent == null)
            Debug.LogWarning($"{name} has no Health component attached!");

        lastKnownPlayerPosition = transform.position;
        SetNewPatrolPoint();
    }

    void Update()
    {
        if (player == null) return;

        patrolBiasWeight = Mathf.Max(0f, patrolBiasWeight - biasDecayRate * Time.deltaTime);
        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= shootingRange && IsInFieldOfView() && HasLineOfSight())
        {
            agent.isStopped = true;
            FacePlayer();
            lastKnownPlayerPosition = player.position;
            patrolBiasWeight = 1f;

            BroadcastToNearbyAllies(lastKnownPlayerPosition);

            if (Time.time >= nextFireTime && reloadSystem != null && !reloadSystem.isReloading)
            {
                if (reloadSystem.TryConsumeAmmo())
                {
                    Shoot();
                    nextFireTime = Time.time + 1f / fireRate;
                }
            }
        }
        else if (IsInFieldOfView() && HasLineOfSight())
        {
            agent.isStopped = false;
            agent.SetDestination(player.position);
            lastKnownPlayerPosition = player.position;
            patrolBiasWeight = 1f;
            isPatrolling = false;

            BroadcastToNearbyAllies(lastKnownPlayerPosition);
        }
        else
        {
            PatrolBehavior();
        }
    }

    public void OnHitByPlayer(Vector3 hitOrigin)
    {
        lastKnownPlayerPosition = hitOrigin;
        patrolBiasWeight = 1f;
        isPatrolling = true;
        SetNewPatrolPoint();

        BroadcastToNearbyAllies(hitOrigin);

        // New: broadcast heal request if health is low
        if (healthComponent != null && healthComponent.CurrentHealth <= healRequestThreshold)
        {
            BroadcastHealRequest();
        }

        if (player != null && reloadSystem != null && !reloadSystem.isReloading)
        {
            FacePlayer();
            if (Time.time >= nextFireTime && reloadSystem.TryConsumeAmmo())
            {
                Shoot();
                nextFireTime = Time.time + 1f / fireRate;
            }
        }
    }

    void BroadcastToNearbyAllies(Vector3 targetPosition)
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, allyBroadcastRadius);
        foreach (var col in hitColliders)
        {
            if (col.gameObject != this.gameObject && col.CompareTag("Enemy"))
            {
                EnemyShootAndMove ally = col.GetComponent<EnemyShootAndMove>();
                if (ally != null)
                {
                    ally.ReceiveBackupCall(targetPosition);
                }
            }
        }
    }

    void BroadcastHealRequest()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, healerBroadcastRadius);
        foreach (var col in hitColliders)
        {
            if (col.CompareTag("Enemy"))
            {
                HealerEnemy healer = col.GetComponent<HealerEnemy>();
                if (healer != null)
                {
                    healer.ReceiveHealRequest(this.gameObject);
                }
            }
        }
    }

    public void ReceiveBackupCall(Vector3 targetPosition)
    {
        lastKnownPlayerPosition = targetPosition;
        patrolBiasWeight = 1f;
        isPatrolling = true;
        SetNewPatrolPoint();
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

    void Shoot()
    {
        if (bulletPrefab && firePoint)
        {
            Vector3 direction = (player.position - firePoint.position).normalized;
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            Instantiate(bulletPrefab, firePoint.position, lookRotation);

            GlobalEventManager.RaiseGunshot(firePoint.position, this);
        }
    }

    void HandleGunshot(Vector3 gunshotPosition, Object source)
    {
        if (source == this) return;

        if (Vector3.Distance(transform.position, gunshotPosition) <= hearingRange)
        {
            lastKnownPlayerPosition = gunshotPosition;
            patrolBiasWeight += biasIncreasePerShot;
            patrolBiasWeight = Mathf.Clamp01(patrolBiasWeight);

            if (isPatrolling)
                SetNewPatrolPoint();
        }
    }

    void FacePlayer()
    {
        Vector3 direction = (player.position - transform.position).normalized;
        direction.y = 0f;
        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * 10f);
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
        Gizmos.DrawWireSphere(transform.position, allyBroadcastRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, healerBroadcastRadius);
    }
}