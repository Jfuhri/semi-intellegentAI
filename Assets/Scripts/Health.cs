using UnityEngine;

public class Health : MonoBehaviour
{
    [Header("Health Settings")]
    public float maxHealth = 100f;
    public float CurrentHealth;

    void Start() => CurrentHealth = maxHealth;

    public void TakeDamage(float amount, Vector3 hitOrigin)
    {
        CurrentHealth -= amount;
        CurrentHealth = Mathf.Clamp(CurrentHealth, 0f, maxHealth);

        if (!CompareTag("Player"))
        {
            var shooterAI = GetComponent<EnemyShootAndMove>();
            if (shooterAI != null)
                shooterAI.OnHitByPlayer(hitOrigin);

            var shotgunAI = GetComponent<EnemyShotgunAndMove>();
            if (shotgunAI != null)
                shotgunAI.OnHitByPlayer(hitOrigin);
        }

        if (CurrentHealth <= 0)
            Die();
    }

    public void TakeHeal(float amount)
    {
        CurrentHealth += amount;
        CurrentHealth = Mathf.Clamp(CurrentHealth, 0f, maxHealth);
    }

    private void Die()
    {
        if (CompareTag("Player"))
            UnityEngine.SceneManagement.SceneManager.LoadScene("GameOver");
        else
            Destroy(gameObject);
    }

    public float GetHealth() => CurrentHealth;
    public float GetMaxHealth() => maxHealth;
}