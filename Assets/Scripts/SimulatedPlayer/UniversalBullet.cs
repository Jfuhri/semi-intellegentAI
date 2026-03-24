using UnityEngine;

public class UniversalBullet : MonoBehaviour
{
    public float speed = 20f;
    public float damage = 10f;
    public float lifeTime = 5f;

    [HideInInspector]
    public UniversalCombatAI.Faction shooterFaction;

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = transform.forward * speed;
        }

        Destroy(gameObject, lifeTime);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.isTrigger) return;

        // Check if it hit a UniversalCombatAI
        UniversalCombatAI targetAI = other.GetComponent<UniversalCombatAI>();
        if (targetAI != null)
        {
            // Only damage opposing factions
            if (targetAI.faction != shooterFaction)
            {
                Health health = targetAI.GetComponent<Health>();
                if (health != null)
                    health.TakeDamage(damage, transform.position);

                Destroy(gameObject);
                return;
            }
            else
            {
                // Hit ally, ignore
                return;
            }
        }

        // Check by tags for legacy or bot targets
        bool isEnemyTag = false;

        if (shooterFaction == UniversalCombatAI.Faction.Player &&
            (other.CompareTag("Enemy") || other.CompareTag("EnemyBot")))
            isEnemyTag = true;
        else if (shooterFaction == UniversalCombatAI.Faction.Enemy &&
                 (other.CompareTag("Player") || other.CompareTag("PlayerBot")))
            isEnemyTag = true;

        if (isEnemyTag)
        {
            Health health = other.GetComponent<Health>();
            if (health != null)
                health.TakeDamage(damage, transform.position);

            Destroy(gameObject);
            return;
        }

        // Optional: hit environment/destructibles
        Health otherHealth = other.GetComponent<Health>();
        if (otherHealth != null)
            otherHealth.TakeDamage(damage, transform.position);

        Destroy(gameObject);
    }
}