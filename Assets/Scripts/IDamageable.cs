using UnityEngine;

/// <summary>
/// Anything that can be hurt. The sword only knows about this interface, never about enemies,
/// crates, or bosses directly. Add it to whatever you want to be hittable.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float amount, Vector3 hitDirection);
}
