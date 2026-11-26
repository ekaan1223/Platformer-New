using UnityEngine; // gives us access to Unity classes (MonoBehaviour, etc.)

// GROUND HAZARD
// Marks a surface as DAMAGING GROUND (lava, spikes, poison, electric floor...).
// Put it on the ground object itself, or on a trigger box placed over it; both work.
// Right now it only does one thing: enemies (EnemyAI) read it and refuse to walk onto that ground.
//
// When you build the real damage later, THIS is the script to extend: add a damage amount and an interval here
// and hurt the player in OnTriggerStay / OnCollisionStay through IDamageable.
// Nothing in EnemyAI has to change, because enemies only ask "is this a hazard?".

public class GroundHazard : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Tooltip("ON = enemies treat this ground as dangerous and will not walk onto it. " +
             "Turn it OFF for a hazard that should not stop enemies (for example one that only hurts the player).")]
    [SerializeField] private bool enemiesAvoid = true;

    // EnemyAI reads this.
    public bool EnemiesAvoid
    {
        get { return enemiesAvoid; }
    }
}
