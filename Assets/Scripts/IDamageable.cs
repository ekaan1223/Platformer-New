using UnityEngine; // gives us access to Unity classes (Vector3)

// IDAMAGEABLE
// Anything that can be hurt. The sword, the stomp and enemy weapons only know about this "interface", never about
// enemies, crates or bosses directly. Damageable, BossHealth and TiltWhenHit all have it.
// An interface is a promise: "every script that says ': IDamageable' has a TakeDamage function".

public interface IDamageable
{
    void TakeDamage(float amount, Vector3 hitDirection); // amount = how much damage; hitDirection = where the hit pushes (flat, length 1)
}
