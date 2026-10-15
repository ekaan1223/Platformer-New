using UnityEngine;

/// <summary>
/// Marks a surface as DAMAGING GROUND (lava, spikes, poison, electric floor...).
/// Put it on the ground object itself or on a trigger volume placed over it; both work.
/// Right now it only does one thing: enemies read it and refuse to walk onto that ground.
///
/// When you build the real damage later, THIS is the script to extend: add a damage amount and
/// interval here and apply them in OnTriggerStay / OnCollisionStay through IDamageable.
/// Nothing in EnemyAI has to change, because enemies only ask "is this a hazard?".
/// </summary>
public class GroundHazard : MonoBehaviour
{
    [Tooltip("ON = enemies treat this ground as dangerous and will not walk onto it. " +
             "Turn it OFF for a hazard that should not stop enemies (for example one that only hurts the player).")]
    [SerializeField] private bool enemiesAvoid = true;

    public bool EnemiesAvoid => enemiesAvoid;
}
