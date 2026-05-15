using UnityEngine;
using System.Collections;

public class SelfHealSystem : MonoBehaviour
{
    [Header("Healing")]
    public float healAmount = 50f;
    public float healTime = 2f;
    public float healCooldown = 10f;

    [Header("Threshold")]
    public float healThresholdPercent = 35f;

    [Header("Heal Limits")]
    public int maxHeals = 2;   // how many times this enemy can heal total
    private int remainingHeals;

    private Health health;
    private bool isHealing;
    private float nextHealTime;

    void Start()
    {
        health = GetComponent<Health>();
        remainingHeals = maxHeals;
    }

    public bool ShouldHeal()
    {
        if (health == null) return false;
        if (isHealing) return false;
        if (Time.time < nextHealTime) return false;
        if (remainingHeals <= 0) return false;

        float percent = (health.GetHealth() / health.GetMaxHealth()) * 100f;
        return percent <= healThresholdPercent;
    }

    public IEnumerator HealRoutine()
    {
        if (remainingHeals <= 0)
            yield break;

        isHealing = true;

        yield return new WaitForSeconds(healTime);

        float newHealth = Mathf.Min(
            health.GetHealth() + healAmount,
            health.GetMaxHealth()
        );

        float delta = newHealth - health.GetHealth();
        if (delta > 0)
            health.TakeDamage(-delta, transform.position);

        remainingHeals--;

        if (remainingHeals <= 0)
        {
            Debug.Log($"{gameObject.name} is out of heals.");
        }

        nextHealTime = Time.time + healCooldown;
        isHealing = false;
    }

    // optional helper if you want UI / AI checks later
    public int GetRemainingHeals()
    {
        return remainingHeals;
    }
}