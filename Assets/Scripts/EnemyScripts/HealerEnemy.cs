using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(ReloadSystem))]
public class HealerEnemy : MonoBehaviour
{
    [Header("Combat")]
    public float shootingRange = 10f;
    public float fireRate = 0.5f; // slower by default
    public GameObject bulletPrefab;
    public Transform firePoint;

    [Header("Healing")]
    public float healAmount = 25f;
    public float healRange = 10f;
    public float healCooldown = 5f;
    private float nextHealTime;

    [Header("Vision")]
    public float viewAngle = 120f;
    public float viewDistance = 25f;
    public LayerMask lineOfSightMask;

    [Header("Patrol")]
    public float patrolRadius = 30f;
    public float patrolWaitTime = 3f;
    public float hearingRange = 30f;
    public float biasIncreasePerEvent = 0.25f;
    public float biasDecayRate = 0.1f;
    private float patrolBiasWeight = 0f;

    [Header("Backup System")]
    public float allyBroadcastRadius = 20f;

    [Header("Healing Requests")]
    public float healerResponseRadius = 30f; // max distance to respond
    private GameObject currentHealTarget;

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
        patrolBiasWeight = Mathf.Max(0f, patrolBiasWeight - biasDecayRate * Time.deltaTime);

        // Heal nearby allies
        if (Time.time >= nextHealTime)
        {
            HealLowestHealthAlly();
            nextHealTime = Time.time + healCooldown;
        }

        // Shooting logic
        if (player != null)
        {
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
            else
            {
                PatrolBehavior();
            }
        }
    }

    public void ReceiveHealRequest(GameObject injuredAlly)
    {
        if (injuredAlly == null) return;

        // Only respond if within range
        if (Vector3.Distance(transform.position, injuredAlly.transform.position) <= healerResponseRadius)
        {
            currentHealTarget = injuredAlly;
            agent.SetDestination(injuredAlly.transform.position);
        }
    }

    void HealLowestHealthAlly()
    {
        GameObject target = currentHealTarget;
        float lowestHealth = Mathf.Infinity;

        // If no requested target or it's out of range, find closest low-health ally
        if (target == null || Vector3.Distance(transform.position, target.transform.position) > healRange)
        {
            Collider[] alliesInRange = Physics.OverlapSphere(transform.position, healRange);

            foreach (Collider col in alliesInRange)
            {
                if (col.gameObject != this.gameObject && col.CompareTag("Enemy"))
                {
                    Health allyHealth = col.GetComponent<Health>();
                    if (allyHealth != null && allyHealth.GetHealth() < lowestHealth)
                    {
                        lowestHealth = allyHealth.GetHealth();
                        target = col.gameObject;
                    }
                }
            }
        }

        // Heal the target if found
        if (target != null)
        {
            Health allyHealth = target.GetComponent<Health>();
            if (allyHealth != null)
            {
                allyHealth.TakeHeal(healAmount);
                currentHealTarget = null; // reset after healing
                                          // Optional: spawn VFX/SFX
            }
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

    void Shoot()
    {
        if (bulletPrefab && firePoint && player != null)
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
            patrolBiasWeight += biasIncreasePerEvent;
            patrolBiasWeight = Mathf.Clamp01(patrolBiasWeight);

            if (isPatrolling)
                SetNewPatrolPoint();
        }
    }

    void BroadcastToNearbyAllies(Vector3 targetPosition)
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, allyBroadcastRadius);
        foreach (var col in hitColliders)
        {
            if (col.gameObject != this.gameObject && col.CompareTag("Enemy"))
            {
                HealerEnemy ally = col.GetComponent<HealerEnemy>();
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
        if (player == null || firePoint == null) return false;
        Vector3 direction = (player.position + Vector3.up * 1f) - firePoint.position;
        if (Physics.Raycast(firePoint.position, direction.normalized, out RaycastHit hit, viewDistance, lineOfSightMask))
            return hit.transform.CompareTag("Player");
        return false;
    }

    bool IsInFieldOfView()
    {
        if (player == null) return false;
        Vector3 directionToPlayer = player.position - transform.position;
        float angle = Vector3.Angle(transform.forward, directionToPlayer);
        return angle <= viewAngle / 2f && directionToPlayer.magnitude <= viewDistance;
    }

    void FacePlayer()
    {
        if (player == null) return;
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 10f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(currentPatrolTarget, 1f);

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(transform.position, hearingRange);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, healRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, allyBroadcastRadius);
    }
}