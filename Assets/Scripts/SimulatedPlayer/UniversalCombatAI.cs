using UnityEngine;
using UnityEngine.AI;
using System.Linq;
using System.Collections.Generic;

[RequireComponent(typeof(ReloadSystem))]
public class UniversalCombatAI : MonoBehaviour
{
    public enum Faction { Player, Enemy }

    [Header("Faction")]
    public Faction faction;

    [Header("Combat")]
    public float shootingRange = 10f;
    public float fireRate = 1f;
    public GameObject bulletPrefab;
    public Transform firePoint;

    [Header("Vision")]
    public float viewAngle = 120f;
    public float viewDistance = 25f;
    public LayerMask lineOfSightMask;

    [Header("Movement")]
    public float stoppingDistance = 10f;
    public float strafeInterval = 2f;
    public float strafeStrength = 1f;

    [Header("Patrol")]
    public float patrolRadius = 30f;
    public float patrolWaitTime = 3f;

    [Header("Awareness")]
    public float hearingRange = 30f;

    [Header("Team Communication")]
    public float allyBroadcastRadius = 15f;

    private Transform currentTarget;
    private NavMeshAgent agent;
    private ReloadSystem reloadSystem;

    private float nextFireTime;
    private float patrolWaitTimer;
    private float strafeTimer;
    private float currentStrafe;

    private Vector3 patrolPoint;
    private bool isPatrolling = true;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        reloadSystem = GetComponent<ReloadSystem>();
        SetNewPatrolPoint();
        strafeTimer = Random.Range(0f, strafeInterval); // randomize initial strafe
    }

    void Update()
    {
        FindTarget();

        if (currentTarget == null)
        {
            Patrol();
            return;
        }

        float distance = Vector3.Distance(transform.position, currentTarget.position);

        if (CanSeeTarget())
        {
            CombatBehavior(distance);
        }
        else
        {
            ChaseOrPatrol();
        }
    }

    // =========================
    // TARGETING BASED ON TAGS & FACTION
    // =========================
    void FindTarget()
    {
        string[] enemyTags = faction == Faction.Player ? new[] { "Enemy", "EnemyBot" } : new[] { "Player", "PlayerBot" };

        float closestDist = Mathf.Infinity;
        Transform closest = null;

        foreach (string tag in enemyTags)
        {
            GameObject[] potentialTargets = GameObject.FindGameObjectsWithTag(tag);
            foreach (var target in potentialTargets)
            {
                if (!target.activeInHierarchy) continue;

                float dist = Vector3.Distance(transform.position, target.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = target.transform;
                }
            }
        }

        currentTarget = closest;
    }

    // =========================
    // COMBAT
    // =========================
    void CombatBehavior(float distance)
    {
        FaceTarget();

        if (distance > stoppingDistance)
        {
            agent.isStopped = false;

            // Tactical weighted approach with squad awareness
            Vector3 squadCenter = GetNearbyAllyCenter();
            Vector3 offset = Random.insideUnitSphere * 3f;
            offset.y = 0f;
            Vector3 tacticalPoint = Vector3.Lerp(Vector3.Lerp(patrolPoint, currentTarget.position + offset, 0.3f),
                                                 squadCenter, 0.5f); // weighted between patrol/target and squad

            agent.SetDestination(tacticalPoint);
        }
        else
        {
            Strafe();
        }

        if (distance <= shootingRange && Time.time >= nextFireTime)
        {
            if (reloadSystem == null || (!reloadSystem.isReloading && reloadSystem.TryConsumeAmmo()))
            {
                Shoot();
                nextFireTime = Time.time + 1f / fireRate;
            }
        }

        BroadcastToAllies(currentTarget.position);
    }

    void Strafe()
    {
        agent.isStopped = false;
        strafeTimer -= Time.deltaTime;
        if (strafeTimer <= 0f)
        {
            strafeTimer = strafeInterval;
            currentStrafe = Random.Range(-strafeStrength, strafeStrength);
        }

        Vector3 move = transform.right * currentStrafe;
        agent.SetDestination(transform.position + move);
    }

    void Shoot()
    {
        if (bulletPrefab && firePoint)
        {
            Vector3 dir = (currentTarget.position - firePoint.position).normalized;
            Quaternion rot = Quaternion.LookRotation(dir);

            var bullet = Instantiate(bulletPrefab, firePoint.position, rot);
            var ub = bullet.GetComponent<UniversalBullet>();
            if (ub != null)
                ub.shooterFaction = faction;

            GlobalEventManager.RaiseGunshot(firePoint.position, this);
        }
    }

    // =========================
    // VISION
    // =========================
    bool CanSeeTarget()
    {
        if (currentTarget == null) return false;

        Vector3 dir = currentTarget.position - transform.position;
        float angle = Vector3.Angle(transform.forward, dir);
        if (angle > viewAngle / 2f || dir.magnitude > viewDistance)
            return false;

        if (Physics.Raycast(firePoint.position, dir.normalized, out RaycastHit hit, viewDistance, lineOfSightMask))
        {
            var ai = hit.transform.GetComponent<UniversalCombatAI>();
            if (ai != null && ai.faction != this.faction) return true;

            // Also allow tagged enemies
            if ((faction == Faction.Player && (hit.transform.CompareTag("Enemy") || hit.transform.CompareTag("EnemyBot"))) ||
                (faction == Faction.Enemy && (hit.transform.CompareTag("Player") || hit.transform.CompareTag("PlayerBot"))))
                return true;
        }

        return false;
    }

    void FaceTarget()
    {
        if (currentTarget == null) return;
        Vector3 dir = (currentTarget.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
        {
            Quaternion rot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, Time.deltaTime * 10f);
        }
    }

    // =========================
    // PATROL / WANDER
    // =========================
    void ChaseOrPatrol()
    {
        if (currentTarget != null)
        {
            Vector3 squadCenter = GetNearbyAllyCenter();
            Vector3 offset = Random.insideUnitSphere * 5f;
            offset.y = 0f;
            Vector3 blended = Vector3.Lerp(patrolPoint, currentTarget.position + offset, 0.5f);
            blended = Vector3.Lerp(blended, squadCenter, 0.5f); // adjust toward squad center

            agent.isStopped = false;
            agent.SetDestination(blended);
            isPatrolling = false;
        }
        else
        {
            Patrol();
        }
    }

    void Patrol()
    {
        if (!isPatrolling)
        {
            isPatrolling = true;
            SetNewPatrolPoint();
        }

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
        Vector3 random = Random.insideUnitSphere * patrolRadius;
        random.y = 0;
        Vector3 target = transform.position + random;
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, patrolRadius, NavMesh.AllAreas))
        {
            patrolPoint = hit.position;
            agent.SetDestination(patrolPoint);
        }
    }

    // =========================
    // SQUAD SUPPORT
    // =========================
    Vector3 GetNearbyAllyCenter()
    {
        Collider[] allies = Physics.OverlapSphere(transform.position, allyBroadcastRadius);
        List<Vector3> positions = new List<Vector3>();

        foreach (var col in allies)
        {
            if (col.gameObject == gameObject) continue;
            var ai = col.GetComponent<UniversalCombatAI>();
            if (ai != null && ai.faction == faction)
                positions.Add(col.transform.position);
        }

        if (positions.Count == 0) return transform.position;
        return positions.Aggregate((a, b) => a + b) / positions.Count; // average position
    }

    void BroadcastToAllies(Vector3 targetPos)
    {
        UniversalCombatAI[] allUnits = GameObject.FindObjectsByType<UniversalCombatAI>(FindObjectsSortMode.None);
        foreach (var unit in allUnits)
        {
            if (unit == this) continue;
            if (unit.faction != this.faction) continue;
            if (!unit.gameObject.activeInHierarchy) continue;

            unit.ReceiveTarget(targetPos);
        }
    }

    public void ReceiveTarget(Vector3 pos)
    {
        agent.SetDestination(pos);
    }

    // =========================
    // DEBUG
    // =========================
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 left = Quaternion.Euler(0, -viewAngle / 2f, 0) * transform.forward;
        Vector3 right = Quaternion.Euler(0, viewAngle / 2f, 0) * transform.forward;

        Gizmos.DrawRay(transform.position, left * viewDistance);
        Gizmos.DrawRay(transform.position, right * viewDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, allyBroadcastRadius);

    }
}