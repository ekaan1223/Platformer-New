using UnityEngine;

public class Sett : MonoBehaviour
{
    public GameObject PauseScreen;
    public GameObject SettingsScreen;
    public GameObject MemoriesScreen;
    public GameObject ProgressionScreen;
    public GameObject SoundScreen;
    public GameObject CreditsScreen;
    private bool isPaused = false;
    void Start()
    {
        
    }

   
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            PauseGame();
        }
    }

    public void PauseGame()
    {
        isPaused = !isPaused;
        CloseAllScreens();
        PauseScreen.SetActive(isPaused);
        Time.timeScale = isPaused ? 0f : 1f;

    }

    void CloseAllScreens()
    {
        if (SettingsScreen != null)
            SettingsScreen.SetActive(false);
        if (MemoriesScreen != null)
            MemoriesScreen.SetActive(false);
        if (ProgressionScreen != null)
            ProgressionScreen.SetActive(false);
        if (SoundScreen != null)
            SoundScreen.SetActive(false);
        if (CreditsScreen != null)
            CreditsScreen.SetActive(false);
    }

    public void OpenSettingsScreen()
    {
        CloseAllScreens();
        SettingsScreen.SetActive(true);
    }
    public void OpenMemoriesScreen()
    {
        CloseAllScreens();
        MemoriesScreen.SetActive(true);
    }

    public void OpenProgressionScreen()
    {
        CloseAllScreens();
        ProgressionScreen.SetActive(true);
    }

    public void OpenSoundScreen()
    {
        CloseAllScreens();
        SoundScreen.SetActive(true);
    }

    public void OpenCreditsScreen()
    {
        CloseAllScreens();
        CreditsScreen.SetActive(true);
    }

    public void ResumeGame()
    {
        isPaused = false;
        PauseScreen.SetActive(false);
        Time.timeScale = 1f;
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
