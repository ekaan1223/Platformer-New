using Unity.VisualScripting.Antlr3.Runtime;
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
        if (PauseScreen != null)
            PauseScreen.SetActive(isPaused);
        Time.timeScale = isPaused ? 0f : 1f;
    }


    public void OpenMainMenuSettings()
    {
        CloseAllScreens();
        //if (PauseScreen != null)
        //{
          //  PauseScreen.SetActive(true);
        //}
        //if (SettingsScreen != null)
        //{
        //    SettingsScreen.SetActive(true);
        //}
    }
    public void OpenSettingsFromPause()
    {
        CloseAllScreens();
        if (SettingsScreen != null)
            SettingsScreen.SetActive(true);
    }
    void CloseAllScreens()
    { 
        if (PauseScreen != null) PauseScreen.SetActive(false);
        if (SettingsScreen != null) SettingsScreen.SetActive(false);
        if (MemoriesScreen != null) MemoriesScreen.SetActive(false);
        if (ProgressionScreen != null) ProgressionScreen.SetActive(false);
        if (SoundScreen != null) SoundScreen.SetActive(false);
        if (CreditsScreen != null) CreditsScreen.SetActive(false);
    }

    public void OpenMemoriesScreen()
    {
        if (SettingsScreen != null) SettingsScreen.SetActive(true);
        if (MemoriesScreen != null) MemoriesScreen.SetActive(true);
    }

    public void OpenProgressionScreen()
    {
        if (SettingsScreen != null) SettingsScreen.SetActive(true);
        if (ProgressionScreen != null) ProgressionScreen.SetActive(true);
    }

    public void OpenSoundScreen()
    {
        if (SettingsScreen != null) SettingsScreen.SetActive(true);
        if (SoundScreen != null) SoundScreen.SetActive(true);
    }

    public void OpenCreditsScreen()
    {
        if (SettingsScreen != null) SettingsScreen.SetActive(true);
        if (CreditsScreen != null) CreditsScreen.SetActive(true);
    }

    public void ResumeGame()
    {
        isPaused = false;
        CloseAllScreens();
        if (PauseScreen != null) PauseScreen.SetActive(false);
        Time.timeScale = 1f;
    }

    public void QuitSettings()
    {
        CloseAllScreens();
    }
}