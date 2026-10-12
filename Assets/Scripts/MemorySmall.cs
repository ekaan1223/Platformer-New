/// <summary>
/// "Memory Small": collect 3 per level to unlock that level's Memory Main.
/// Everything else (touch detection, sound, fade, removal) is inherited from MemoryPickup.
/// </summary>
public class MemorySmall : MemoryPickup
{
    protected override bool CanBeCollected => Tracker != null;

    protected override void HandleCollected() => Tracker.RegisterSmallCollected();
}
