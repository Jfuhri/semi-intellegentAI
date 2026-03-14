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

    [Header("Tactical Movement")]
    public float separationRadius = 2f;
    public float separationStrength = 2f;
    public float retreatHealthThreshold = 25f;
    public float retreatDistance = 6f;
    public float flankDistance = 3f;

    private Transform player;
    private NavMeshAgent agent;
    private float nextFireTime;
    private float patrolWaitTimer;
    private Vector3 currentPatrolTarget;
    private bool isPatrolling = true;
    private Vector3 lastKnownPlayerPosition;

    private ReloadSystem reloadSystem;
    private Health health;

    void OnEnable() => GlobalEventManager.OnGunshot += HandleGunshot;
    void OnDisable() => GlobalEventManager.OnGunshot -= HandleGunshot;

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        agent = GetComponent<NavMeshAgent>();
        reloadSystem = GetComponent<ReloadSystem>();
        health = GetComponent<Health>();

        if (reloadSystem == null)
            Debug.LogWarning($"{name} has no ReloadSystem attached!");

        lastKnownPlayerPosition = transform.position;
        SetNewPatrolPoint();
    }

    void Update()
    {
        if (player == null) return;

        patrolBiasWeight = Mathf.Max(0f, patrolBiasWeight - biasDecayRate * Time.deltaTime);

        ApplySeparation();

        float distance = Vector3.Distance(transform.position, player.position);

        if (ShouldRetreat())
        {
            TacticalRetreat();
            return;
        }

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

            Vector3 flankPos = GetFlankPosition();
            agent.SetDestination(flankPos);

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

    void ApplySeparation()
    {
        Collider[] nearbyAllies = Physics.OverlapSphere(transform.position, separationRadius);

        Vector3 separationMove = Vector3.zero;
        int count = 0;

        foreach (Collider ally in nearbyAllies)
        {
            if (ally.gameObject != gameObject && ally.CompareTag("Enemy"))
            {
                Vector3 diff = transform.position - ally.transform.position;
                diff.y = 0;

                if (diff.magnitude > 0.01f)
                {
                    separationMove += diff.normalized / diff.magnitude;
                    count++;
                }
            }
        }

        if (count > 0)
        {
            separationMove /= count;
            agent.Move(separationMove * separationStrength * Time.deltaTime);
        }
    }

    bool ShouldRetreat()
    {
        if (health == null) return false;

        float percent = (health.GetHealth() / health.GetMaxHealth()) * 100f;
        return percent <= retreatHealthThreshold;
    }

    void TacticalRetreat()
    {
        agent.isStopped = false;

        Vector3 retreatDir = (transform.position - player.position).normalized;
        Vector3 retreatTarget = transform.position + retreatDir * retreatDistance;

        if (NavMesh.SamplePosition(retreatTarget, out NavMeshHit hit, retreatDistance, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }

    Vector3 GetFlankPosition()
    {
        Vector3 dirToPlayer = (player.position - transform.position).normalized;

        Vector3 right = Vector3.Cross(Vector3.up, dirToPlayer);

        float side = Random.value > 0.5f ? 1f : -1f;

        Vector3 flank = player.position + (right * side * flankDistance);

        if (NavMesh.SamplePosition(flank, out NavMeshHit hit, flankDistance, NavMesh.AllAreas))
            return hit.position;

        return player.position;
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

    public void OnHitByPlayer(Vector3 hitOrigin)
    {
        lastKnownPlayerPosition = hitOrigin;
        patrolBiasWeight = 1f;
        isPatrolling = true;

        SetNewPatrolPoint();

        BroadcastToNearbyAllies(hitOrigin);

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
            if (col.gameObject != gameObject && col.CompareTag("Enemy"))
            {
                EnemyShootAndMove ally = col.GetComponent<EnemyShootAndMove>();

                if (ally != null)
                    ally.ReceiveBackupCall(targetPosition);
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
        Vector3 direction = (player.position + Vector3.up) - firePoint.position;

        if (Physics.Raycast(firePoint.position, direction.normalized, out RaycastHit hit, viewDistance, lineOfSightMask))
            return hit.transform.CompareTag("Player");

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

        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                lookRotation,
                Time.deltaTime * 10f
            );
        }
    }
}