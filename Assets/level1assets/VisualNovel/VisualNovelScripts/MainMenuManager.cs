using UnityEngine;
using UnityEngine.SceneManagement;

public class NewMonoBehaviourScript : MonoBehaviour
{
    public GameObject SettingPanel;

    void Start()
    {
        
    }


    void Update()
    {
        
    }

    public void StartGame()
    {
        SceneManager.LoadScene("Level 1.1");
    }

    public void ContinueGame()
    {
       
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    public void Settings()
    {
        SettingPanel.SetActive(true);
    }
}
