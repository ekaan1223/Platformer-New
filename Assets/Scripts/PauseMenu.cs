using TMPro;                       // gives us TextMeshProUGUI, for the placeholder button texts
using UnityEngine;                 // gives us access to Unity classes (MonoBehaviour, GameObject, Time, etc.)
using UnityEngine.EventSystems;    // gives us EventSystem, which lets UI buttons receive clicks
using UnityEngine.Events;          // gives us UnityAction, the "function to call" a button gets
using UnityEngine.InputSystem;     // gives us InputAction, for the pause key
using UnityEngine.InputSystem.UI;  // gives us InputSystemUIInputModule, so buttons work with the new Input System
using UnityEngine.SceneManagement; // gives us SceneManager, to load the saved level
using UnityEngine.UI;              // gives us Image, Button, Canvas pieces for the placeholder menu

// PAUSE MENU
// Press Esc (or gamepad Start) to pause: the game freezes, the sounds pause and the mouse is freed.
// The menu has: Continue, Options, Save Game, Load Game, Quit Game. Esc again = back / continue.
//
// PLACEHOLDERS: if the "Pause Panel" / "Options Panel" slots are EMPTY, this script builds a simple grey menu by itself,
// so it works right away. When your real menu is ready, build it in the scene, drag it into the slot, and point its
// buttons (On Click) at the public functions below: Continue, OpenOptions, CloseOptions, SaveGame, LoadGame, QuitGame.
// Nothing else has to change.
//
// SAVING (placeholder too): right now "save" only remembers WHICH LEVEL you are in. Load Game and the main menu's
// Continue button load that level. To save more later (position, memories, keys), change SaveGame and LoadGame only.
//
// Put it on the GameManager object (it survives the player's death, and every level has one).

[DefaultExecutionOrder(100)] // run after CursorLocker, which also reacts to Esc, so our "lock the mouse again" on Continue wins
public class PauseMenu : MonoBehaviour // MonoBehaviour = a script Unity can put on an object and call every frame
{
    public const string SavedLevelKey = "SavedLevel"; // the name the saved level is stored under (the main menu reads it too)

    [Header("Screens (empty = a simple placeholder is built)")]
    public GameObject pausePanel;   // the pause menu UI. Hidden until the game is paused
    public GameObject optionsPanel; // the options UI. Opened from the pause menu

    [Header("Input (empty = Esc / gamepad Start)")]
    public InputAction pauseAction = new InputAction("Pause", InputActionType.Button); // press to pause / go back

    public bool IsPaused { get; private set; } // true while the pause menu is open. Other scripts can read it

    private float timeScaleBefore;  // the game speed before pausing (a dialogue may already have frozen it), put back on Continue
    private bool soundPausedBefore; // were the sounds already paused before (by a dialogue)? Put back on Continue
    private CursorLocker cursorLocker; // frees the mouse for the menu and locks it again afterwards (found in Awake)
    private Transform placeholderCanvas; // the canvas the placeholder screens are built on (made only if needed)

    // Awake runs once, when the object is created.
    private void Awake()
    {
        cursorLocker = GetComponent<CursorLocker>();                         // the cursor locker on this object...
        if (cursorLocker == null) cursorLocker = FindFirstObjectByType<CursorLocker>(); // ...or anywhere in the scene

        if (pauseAction.bindings.Count == 0) // no keys set in the Inspector: give default ones
        {
            pauseAction.AddBinding("<Keyboard>/escape"); // Esc on the keyboard
            pauseAction.AddBinding("<Gamepad>/start");   // Start on a gamepad
        }

        if (pausePanel == null) pausePanel = BuildPlaceholderPauseScreen();     // no real menu yet: build the grey one
        if (optionsPanel == null) optionsPanel = BuildPlaceholderOptionsScreen(); // no real options yet: build the grey one

        pausePanel.SetActive(false);   // hidden until the game is paused
        optionsPanel.SetActive(false); // hidden until Options is clicked

        MakeSureClicksWork(); // buttons need an EventSystem in the scene; add one if the level has none
    }

    private void OnEnable() => pauseAction.Enable();   // start listening to the pause key
    private void OnDisable() => pauseAction.Disable(); // stop listening

    // OnDestroy runs when the object goes away (e.g. the level restarts or loads while paused).
    private void OnDestroy()
    {
        if (!IsPaused) return;       // not paused: nothing to clean up
        Time.timeScale = 1f;         // never leave the next level frozen
        AudioListener.pause = false; // never leave the next level silent
    }

    // Update runs every frame (even while paused: only timeScale stops, Update keeps running).
    private void Update()
    {
        if (!pauseAction.WasPressedThisFrame()) return; // the pause key was not pressed this frame: nothing to do

        if (!IsPaused) Pause();                              // playing: open the pause menu
        else if (optionsPanel.activeSelf) CloseOptions();    // in the options: go back to the pause menu
        else Continue();                                     // in the pause menu: back to the game
    }

    // ---------- The buttons (public, so your real menu's buttons can call them from On Click) ----------

    // Freezes the game and opens the pause menu.
    public void Pause()
    {
        if (IsPaused) return; // already paused: nothing to do

        IsPaused = true;                        // remember we are paused
        timeScaleBefore = Time.timeScale;       // remember the game speed, to put it back on Continue
        soundPausedBefore = AudioListener.pause; // remember if the sounds were already paused

        Time.timeScale = 0f;        // freeze the game (movement, physics, animations)
        AudioListener.pause = true; // pause every sound

        pausePanel.SetActive(true);  // show the pause menu
        optionsPanel.SetActive(false); // start on the main page, not the options

        if (cursorLocker != null) cursorLocker.UnlockForUI(); // free the mouse so the buttons can be clicked
    }

    // "Continue": closes the menu and unfreezes the game.
    public void Continue()
    {
        if (!IsPaused) return; // not paused: nothing to do

        IsPaused = false;                         // not paused anymore
        pausePanel.SetActive(false);              // hide the pause menu
        optionsPanel.SetActive(false);            // hide the options too, just in case
        Time.timeScale = timeScaleBefore;         // put the game speed back (still frozen if a dialogue had frozen it)
        AudioListener.pause = soundPausedBefore;  // put the sounds back the way they were

        if (cursorLocker != null) cursorLocker.LockForGameplay(); // lock the mouse again for looking around

        PlayerController player = FindFirstObjectByType<PlayerController>(); // the player (null if it died and was removed)
        if (player != null) player.ClearAttackPress(); // forget the click on "Continue", so it doesn't swing the sword
    }

    // "Options": swaps the pause menu for the options screen.
    public void OpenOptions()
    {
        pausePanel.SetActive(false);  // hide the pause menu
        optionsPanel.SetActive(true); // show the options
    }

    // "Back" on the options screen: swaps back to the pause menu.
    public void CloseOptions()
    {
        optionsPanel.SetActive(false); // hide the options
        pausePanel.SetActive(true);    // show the pause menu again
    }

    // "Save Game": remembers the current level. (PLACEHOLDER: save more here later, e.g. position, memories, keys.)
    public void SaveGame()
    {
        string level = SceneManager.GetActiveScene().name; // the name of the level we are in
        PlayerPrefs.SetString(SavedLevelKey, level);        // store it under SavedLevelKey
        PlayerPrefs.Save();                                 // write it to disk right away
        Debug.Log("Game saved: " + level, this);            // so you can see it worked
    }

    // "Load Game": loads the saved level. (PLACEHOLDER: load the extra saved things here later.)
    public void LoadGame()
    {
        if (!PlayerPrefs.HasKey(SavedLevelKey)) // nothing was ever saved
        {
            Debug.LogWarning("There is no saved game yet.", this);
            return; // stay in the pause menu
        }

        Time.timeScale = 1f;         // the loaded level must not start frozen
        AudioListener.pause = false; // or silent
        IsPaused = false;            // so OnDestroy has nothing left to clean up
        SceneManager.LoadScene(PlayerPrefs.GetString(SavedLevelKey)); // load the saved level
    }

    // "Quit Game": closes the game. In the Editor it stops Play mode instead (Application.Quit does nothing there).
    public void QuitGame()
    {
#if UNITY_EDITOR // this part only exists inside the Unity Editor
        UnityEditor.EditorApplication.isPlaying = false; // stop Play mode
#else            // this part only exists in the built game
        Application.Quit(); // close the game
#endif
    }

    // ---------- Placeholder screens (only built when the slots are empty) ----------

    // Builds the grey pause menu: a dark background, a title and five buttons.
    private GameObject BuildPlaceholderPauseScreen()
    {
        GameObject screen = MakeScreen("Placeholder Pause Screen"); // full-screen dark background
        MakeText(screen.transform, "PAUSED", 64, 220);              // the title near the top

        MakeButton(screen.transform, "Continue", 100, Continue);     // each button: its text, its height on screen, and what it calls
        MakeButton(screen.transform, "Options", 30, OpenOptions);
        MakeButton(screen.transform, "Save Game", -40, SaveGame);
        MakeButton(screen.transform, "Load Game", -110, LoadGame);
        MakeButton(screen.transform, "Quit Game", -180, QuitGame);

        return screen; // hand it back so Awake can put it in the Pause Panel slot
    }

    // Builds the grey options screen: a title, a "coming soon" note and a Back button.
    private GameObject BuildPlaceholderOptionsScreen()
    {
        GameObject screen = MakeScreen("Placeholder Options Screen"); // full-screen dark background
        MakeText(screen.transform, "OPTIONS", 64, 220);                // the title
        MakeText(screen.transform, "Coming soon", 32, 60);             // a note until the real options exist
        MakeButton(screen.transform, "Back", -110, CloseOptions);      // back to the pause menu

        return screen; // hand it back so Awake can put it in the Options Panel slot
    }

    // Makes a full-screen dark panel on the placeholder canvas (the canvas is made the first time).
    private GameObject MakeScreen(string screenName)
    {
        if (placeholderCanvas == null) // no canvas yet: make one
        {
            GameObject canvasObject = new GameObject("Placeholder Pause Canvas"); // a new empty object for the canvas
            canvasObject.transform.SetParent(transform, false);                   // keep it under this object, tidy in the Hierarchy
            Canvas canvas = canvasObject.AddComponent<Canvas>();                  // the canvas that draws the UI
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;                    // drawn on top of the game, on the screen
            canvas.sortingOrder = 100;                                            // above every other UI (dialogue, HUD)
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();      // makes the UI the same size on every screen
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;      // scale with the screen...
            scaler.referenceResolution = new Vector2(1920f, 1080f);               // ...as if it were 1920 x 1080
            canvasObject.AddComponent<GraphicRaycaster>();                        // lets the mouse click things on this canvas
            placeholderCanvas = canvasObject.transform;                           // remember it for the next screen
        }

        GameObject screen = new GameObject(screenName);       // a new object for this screen
        screen.transform.SetParent(placeholderCanvas, false); // put it on the canvas
        Image background = screen.AddComponent<Image>();      // the background picture (a plain colour)
        background.color = new Color(0f, 0f, 0f, 0.7f);       // black, 70% see-through-ish: the frozen game shows behind
        RectTransform rect = screen.GetComponent<RectTransform>(); // the UI position/size of the screen
        rect.anchorMin = Vector2.zero; // stretch from the bottom-left corner...
        rect.anchorMax = Vector2.one;  // ...to the top-right corner = the whole screen
        rect.offsetMin = Vector2.zero; // no gap at the bottom-left
        rect.offsetMax = Vector2.zero; // no gap at the top-right
        return screen; // hand it back
    }

    // Makes a centred line of text at height y (pixels from the middle of the screen).
    private TextMeshProUGUI MakeText(Transform parent, string words, float size, float y)
    {
        GameObject textObject = new GameObject(words);             // a new object named after its text
        textObject.transform.SetParent(parent, false);             // put it on the screen
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>(); // the text itself
        text.text = words;                                         // what it says
        text.fontSize = size;                                      // how big
        text.alignment = TextAlignmentOptions.Center;              // centred
        text.color = Color.white;                                  // white on the dark background
        RectTransform rect = textObject.GetComponent<RectTransform>(); // its UI position/size
        rect.sizeDelta = new Vector2(800f, 100f);                  // wide enough for a title
        rect.anchoredPosition = new Vector2(0f, y);                // centred left/right, at height y
        return text; // hand it back
    }

    // Makes a button with a label at height y that calls "onClick" when clicked.
    private void MakeButton(Transform parent, string label, float y, UnityAction onClick)
    {
        GameObject buttonObject = new GameObject(label + " Button"); // a new object named after the button
        buttonObject.transform.SetParent(parent, false);             // put it on the screen
        Image image = buttonObject.AddComponent<Image>();            // the button's background
        image.color = new Color(0.25f, 0.25f, 0.25f, 1f);            // dark grey
        Button button = buttonObject.AddComponent<Button>();         // makes it clickable (and lighter when hovered)
        button.onClick.AddListener(onClick);                         // what happens when it is clicked
        RectTransform rect = buttonObject.GetComponent<RectTransform>(); // its UI position/size
        rect.sizeDelta = new Vector2(360f, 56f);                     // the button's size in pixels
        rect.anchoredPosition = new Vector2(0f, y);                  // centred left/right, at height y

        TextMeshProUGUI text = MakeText(buttonObject.transform, label, 30, 0f); // the label, in the middle of the button
        text.rectTransform.sizeDelta = rect.sizeDelta;                          // as big as the button
    }

    // Buttons only receive clicks if the scene has an EventSystem. Some levels have none, so add one if needed.
    private void MakeSureClicksWork()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return; // the level already has one: nothing to do

        GameObject eventSystem = new GameObject("EventSystem");   // a new object for it
        eventSystem.AddComponent<EventSystem>();                  // sends clicks to the UI
        eventSystem.AddComponent<InputSystemUIInputModule>();     // reads the mouse / gamepad with the new Input System
    }
}
