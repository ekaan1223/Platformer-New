// MEMORY SMALL
// "Memory Small": collect them (3 per level by default) to unlock that level's Memory Main.
// Everything else (touch detection, sound, fade, removal) is inherited from MemoryPickup.

public class MemorySmall : MemoryPickup // built on MemoryPickup
{
    // A small memory can always be collected (as long as the level has a tracker to count it).
    protected override bool CanBeCollected
    {
        get { return Tracker != null; }
    }

    // Collecting it: one more for the level's counter.
    protected override void HandleCollected()
    {
        Tracker.RegisterSmallCollected();
    }
}
