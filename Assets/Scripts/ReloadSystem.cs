using UnityEngine;
using System.Collections;

public class ReloadSystem : MonoBehaviour
{
    [Header("Ammo Settings")]
    public int magazineSize = 8;          // bullets/pellets per magazine
    public int currentAmmo = 8;           // starts full
    public float reloadTime = 2f;         // seconds to reload
    public bool infiniteAmmo = false;     // optional

    [Header("State")]
    public bool isReloading = false;

    // Optional events for UI or animation
    public System.Action<int, int> OnAmmoChanged; // current, max
    public System.Action OnReloadStart;
    public System.Action OnReloadComplete;

    private void Start()
    {
        currentAmmo = magazineSize;
        OnAmmoChanged?.Invoke(currentAmmo, magazineSize);
    }

    /// <summary>
    /// Call this to attempt a shot. Returns true if weapon can fire.
    /// </summary>
    public bool TryConsumeAmmo()
    {
        if (isReloading) return false;

        if (currentAmmo > 0)
        {
            currentAmmo--;
            OnAmmoChanged?.Invoke(currentAmmo, magazineSize);
            return true;
        }
        else
        {
            StartReload();
            return false;
        }
    }

    /// <summary>
    /// Forces a reload manually.
    /// </summary>
    public void StartReload()
    {
        if (isReloading || infiniteAmmo || currentAmmo == magazineSize) return;
        StartCoroutine(ReloadCoroutine());
    }

    private IEnumerator ReloadCoroutine()
    {
        isReloading = true;
        OnReloadStart?.Invoke();

        yield return new WaitForSeconds(reloadTime);

        currentAmmo = magazineSize;
        isReloading = false;
        OnReloadComplete?.Invoke();
        OnAmmoChanged?.Invoke(currentAmmo, magazineSize);
    }
}