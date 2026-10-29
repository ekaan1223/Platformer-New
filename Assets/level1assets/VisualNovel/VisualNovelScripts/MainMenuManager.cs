using UnityEngine;                 // gives us access to Unity classes (MonoBehaviour, GameObject, Time, etc.)
using UnityEngine.InputSystem;     // gives us Keyboard, for the pause key
using UnityEngine.SceneManagement; // gives us SceneManager, to load levels

// MAIN MENU MANAGER
// Every button of the start screen calls one of the public functions below (Button > On Click in the Inspector).
// Put ONE on the start screen and drag the menu screens into the slots. Empty slots are simply skipped.
// The settings screen is the "frame"; Memories / Progression / Sound / Credits open on top of it.

public class MainMenuManager : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Header("Level")]
    public string firstLevelName = "Level 1.1"; // the scene Start / New Game / Continue load (must be in File > Build Profiles)

    [Header("Screens (drag your UI panels in here)")]
    public GameObject PauseScreen;       // the pause panel (G opens and closes it)
    public GameObject SettingsScreen;    // the settings panel, the frame the other screens open in
    public GameObject MemoriesScreen;    // the memories tab
    public GameObject ProgressionScreen; // the progression tab
    public GameObject SoundScreen;       // the sound tab
    public GameObject CreditsScreen;     // the credits tab

    private bool isPaused = false; // true while the pause screen is open

    // Update runs every frame.
    private void Update()
    {
        if (Keyboard.current == null) return;            // no keyboard plugged in: nothing to read
        if (!Keyboard.current.gKey.wasPressedThisFrame) return; // G was not pressed this frame: nothing to do

        if (isPaused) ResumeGame(); // already paused: G closes the pause screen
        else PauseGame();           // not paused: G opens it
    }

    // ---------- Starting the game ----------

    // The "Start" button: loads the first level.
    public void StartGame()
    {
        Time.timeScale = 1f;                    // never start a level frozen (the pause screen may have frozen the game)
        SceneManager.LoadScene(firstLevelName); // load the first level
    }

    // The "Continue" button. There is no save file yet, so for now it simply starts the first level.
    public void ContinueGame()
    {
        StartGame(); // when saving is added, load the saved level here instead
    }

    // The "New Game" button: forgets all progress, then starts the first level.
    public void NewGame()
    {
        PlayerPrefs.DeleteAll();         // delete everything saved with PlayerPrefs
        PlayerPrefs.Save();              // write that to disk right away
        MemoryProgressStore.ResetAll();  // forget the collected main memories too
        StartGame();                     // load the first level
    }

    // The "Quit" button: closes the game (does nothing in the Editor, only in a built game).
    public void QuitGame()
    {
        Application.Quit(); // close the game
    }

    // ---------- Pause ----------

    // Opens the pause screen and freezes the game.
    public void PauseGame()
    {
        isPaused = true;                                // remember we are paused
        if (PauseScreen != null) PauseScreen.SetActive(true); // show the pause screen
        Time.timeScale = 0f;                            // freeze the game
    }

    // Closes every screen and unfreezes the game.
    public void ResumeGame()
    {
        isPaused = false;    // not paused anymore
        CloseAllScreens();   // hide the pause screen and any settings tab
        Time.timeScale = 1f; // unfreeze the game
    }

    // ---------- Settings screens ----------

    // Closes every screen (used by the start screen's settings button).
    public void OpenMainMenuSettings()
    {
        CloseAllScreens(); // hide everything
    }

    // Opens the settings screen from the pause screen.
    public void OpenSettingsFromPause()
    {
        OpenOnSettings(null); // just the settings frame, no tab
    }

    public void OpenMemoriesScreen() => OpenOnSettings(MemoriesScreen);       // the memories tab
    public void OpenProgressionScreen() => OpenOnSettings(ProgressionScreen); // the progression tab
    public void OpenSoundScreen() => OpenOnSettings(SoundScreen);             // the sound tab
    public void OpenCreditsScreen() => OpenOnSettings(CreditsScreen);         // the credits tab

    // Closes the settings screen.
    public void QuitSettings()
    {
        CloseAllScreens(); // hide everything
    }

    // Closes everything, then opens the settings frame and one tab on top of it (tab = null: only the frame).
    private void OpenOnSettings(GameObject tab)
    {
        CloseAllScreens();                                         // start from a clean screen
        if (SettingsScreen != null) SettingsScreen.SetActive(true); // the frame
        if (tab != null) tab.SetActive(true);                       // the tab inside it
    }

    // Hides every screen. Empty slots are skipped.
    private void CloseAllScreens()
    {
        if (PauseScreen != null) PauseScreen.SetActive(false);             // hide the pause screen
        if (SettingsScreen != null) SettingsScreen.SetActive(false);       // hide the settings frame
        if (MemoriesScreen != null) MemoriesScreen.SetActive(false);       // hide the memories tab
        if (ProgressionScreen != null) ProgressionScreen.SetActive(false); // hide the progression tab
        if (SoundScreen != null) SoundScreen.SetActive(false);             // hide the sound tab
        if (CreditsScreen != null) CreditsScreen.SetActive(false);         // hide the credits tab
    }
}
