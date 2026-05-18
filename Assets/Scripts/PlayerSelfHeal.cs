using UnityEngine;
using System.Collections;

public class PlayerSelfHealSystem : MonoBehaviour
{
    [Header("Healing")]
    public float healAmount = 50f;
    public float healTime = 2f;
    public float healCooldown = 10f;

    [Header("Input")]
    public KeyCode healKey = KeyCode.H;

    [Header("Heal Limits")]
    public int maxHeals = 3;
    private int remainingHeals;

    [Header("Threshold")]
    public float healThresholdPercent = 100f;
    // 100 = can heal anytime, lower if you want "only when injured"

    private Health health;
    private bool isHealing;
    private float nextHealTime;

    void Start()
    {
        health = GetComponent<Health>();
        remainingHeals = maxHeals;
    }

    void Update()
    {
        if (Input.GetKeyDown(healKey))
        {
            TryHeal();
        }
    }

    void TryHeal()
    {
        if (health == null) return;
        if (isHealing) return;
        if (Time.time < nextHealTime) return;
        if (remainingHeals <= 0) return;

        float percent = (health.GetHealth() / health.GetMaxHealth()) * 100f;

        if (percent > healThresholdPercent)
            return;

        StartCoroutine(HealRoutine());
    }

    IEnumerator HealRoutine()
    {
        isHealing = true;

        // optional: you could lock movement here later if needed

        yield return new WaitForSeconds(healTime);

        float newHealth = Mathf.Min(
            health.GetHealth() + healAmount,
            health.GetMaxHealth()
        );

        float delta = newHealth - health.GetHealth();

        if (delta > 0)
            health.TakeDamage(-delta, transform.position);

        remainingHeals--;

        nextHealTime = Time.time + healCooldown;
        isHealing = false;
    }

    public int GetRemainingHeals()
    {
        return remainingHeals;
    }

    public bool IsHealing()
    {
        return isHealing;
    }
}