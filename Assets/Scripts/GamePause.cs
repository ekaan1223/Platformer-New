using UnityEngine; // gives us access to Unity classes (Time, AudioListener, RuntimeInitializeOnLoadMethod, etc.)

// GAME PAUSE
// The ONE place that freezes and unfreezes the game (Time.timeScale) and pauses the sounds (AudioListener.pause).
// No other script changes Time.timeScale or AudioListener.pause any more: they all ask this script.
//
// Two things can freeze the game:
//   - a pause menu (PauseMenu in the levels, MainMenuManager on the start screen)
//   - a conversation that freezes the game (DialoguePlayer)
// Each one only says "I want the game frozen" or "I don't any more". The game stays frozen while AT LEAST ONE of
// them wants it. So:
//   - closing the pause menu during a frozen conversation keeps the game frozen (the conversation still wants it)
//   - a conversation that ends while the pause menu is open can't unfreeze the game (the menu still wants it)
//
// "static" = there is exactly one of it, it lives outside any scene and needs no object in the scene.

public static class GamePause
{
    private static bool menuWantsPause;     // true while a pause menu is open
    private static bool dialogueWantsPause; // true while a conversation that freezes the game is on screen

    // True while a pause menu is open. DialoguePlayer and Interactable read it, so they wait while the menu is open.
    public static bool IsMenuOpen
    {
        get { return menuWantsPause; }
    }

    // Called by PauseMenu and MainMenuManager: true when their menu opens, false when it closes.
    public static void SetMenuPause(bool paused)
    {
        menuWantsPause = paused; // remember what the menu wants
        Apply();                 // freeze or unfreeze the game
    }

    // Called by DialoguePlayer: true when a freezing conversation starts, false when it ends.
    public static void SetDialoguePause(bool paused)
    {
        dialogueWantsPause = paused; // remember what the conversation wants
        Apply();                     // freeze or unfreeze the game
    }

    // Called right before a level loads (restart, portal, load game, start game): forget every pause,
    // so the next level never starts frozen or silent.
    public static void ResetAll()
    {
        menuWantsPause = false;
        dialogueWantsPause = false;
        Apply();
    }

    // Puts the game speed and the sounds in line with what is wanted.
    private static void Apply()
    {
        bool frozen = menuWantsPause || dialogueWantsPause; // frozen if at least one of them wants it

        if (frozen) Time.timeScale = 0f; // 0 = everything stops (movement, physics, animations)
        else Time.timeScale = 1f;        // 1 = normal speed

        AudioListener.pause = frozen; // pause every sound while frozen (voice lines ignore this on purpose)
    }

    // Static data survives into the next Play session if "Enter Play Mode Options" skips the script reload.
    // This runs when Play starts and makes sure we start unpaused.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearOnPlay()
    {
        menuWantsPause = false;
        dialogueWantsPause = false;
    }
}
