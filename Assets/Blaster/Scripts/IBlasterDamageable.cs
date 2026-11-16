using UnityEngine;

/// <summary>
/// Everything the blaster knows about one hit. Passed to IBlasterDamageable.TakeBlasterDamage and to the Hit events.
/// </summary>
public struct BlasterHitInfo
{
    public Collider collider;
    public GameObject gameObject;     // the object that was hit
    public Vector3 point;             // world position of the hit
    public Vector3 normal;            // surface normal at the hit
    public Vector3 direction;         // direction the bolt was flying
    public float distance;            // metres from the player's camera to the hit
    public float damage;              // damage of the weapon
    public bool damageableFound;      // true if the object (or a parent) has an IBlasterDamageable
    public BlasterWeapon weapon;      // the weapon that fired
}

/// <summary>
/// Put this on any enemy / target (on the object with the collider, or on a parent of it).
/// BlasterWeapon finds it with GetComponentInParent and calls TakeBlasterDamage when a bolt hits.
///
///   public class Enemy : MonoBehaviour, IBlasterDamageable
///   {
///       public float health = 100f;
///       public void TakeBlasterDamage(BlasterHitInfo hit) { health -= hit.damage; if (health <= 0f) Destroy(gameObject); }
///   }
/// </summary>
public interface IBlasterDamageable
{
    void TakeBlasterDamage(BlasterHitInfo hit);
}
