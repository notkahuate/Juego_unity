using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Controlador de cada escena (objeto "_CartoGame").</summary>
public class CartoGame : MonoBehaviour
{
    public enum Zone { Hub, Pirate, Samurai }

    public static CartoGame Instance;

    public Zone zone = Zone.Hub;
    public string zoneTitle = "Nexo del Tiempo";
    public string objective = "";

    void Awake()
    {
        Instance = this;
        Time.timeScale = 1f;
        if (zone == Zone.Hub)
        {
            // Volver al Nexo cura por completo.
            CartoGameState.health = CartoGameState.maxHealth;
        }
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (zone == Zone.Hub)
        {
            CartoGameState.health = CartoGameState.maxHealth;
            CartoPlayer player = CartoPlayer.Instance;
            if (player != null)
            {
                player.health.maxHealth = CartoGameState.maxHealth;
                player.health.current = CartoGameState.maxHealth;
            }
        }

        if (zone == Zone.Hub && !CartoGameState.introShown)
        {
            CartoHUD.ShowOverlay(CartoHUD.Overlay.Intro);
        }
        else if (zone == Zone.Hub && CartoGameState.AllCrystals && !CartoGameState.victoryShown)
        {
            CartoGameState.victoryShown = true;
            CartoHUD.ShowOverlay(CartoHUD.Overlay.Victory);
        }
        else
        {
            CartoHUD.Toast(zoneTitle, 3f);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void OnPlayerDied()
    {
        CartoHUD.ShowOverlay(CartoHUD.Overlay.Death);
    }

    public static void RespawnAtHub()
    {
        CartoGameState.gold = Mathf.FloorToInt(CartoGameState.gold * 0.75f);
        CartoGameState.health = CartoGameState.maxHealth;
        Time.timeScale = 1f;
        SceneManager.LoadScene(CartoGameState.SceneHub);
    }

    public static void NewGame()
    {
        CartoGameState.Reset();
        Time.timeScale = 1f;
        SceneManager.LoadScene(CartoGameState.SceneHub);
    }
}
