using UnityEngine;

/// <summary>
/// Caps the frame rate at the monitor's refresh rate. Runs by itself when the game starts; nothing to add to a scene.
///
/// Why: running uncapped (600+ fps) the CPU queues frames faster than the screen can show them, the queue fills up,
/// and the game freezes for 20-45 ms every few frames while it drains (seen in the profiler as
/// GfxDeviceD3D12.WaitForLastPresentation). In builds VSync (Quality Settings) already prevents that; in the Editor's
/// Game view VSync is not applied, so this cap does the same job there.
/// </summary>
public static class FrameRateLimiter
{
    private const int FallbackFrameRate = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        double refreshRate = Screen.currentResolution.refreshRateRatio.value;
        Application.targetFrameRate = refreshRate > 1.0 ? Mathf.RoundToInt((float)refreshRate) : FallbackFrameRate;
    }
}
