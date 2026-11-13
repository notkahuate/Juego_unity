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

    // Ultimo punto de control activado en esta escena (se pierde al cambiar de escena).
    [System.NonSerialized] public CartoCheckpoint checkpoint;

    public bool HasCheckpoint { get { return checkpoint != null; } }

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

    /// <summary>
    /// Reaparece en el punto de control sin recargar la escena: los enemigos derrotados siguen
    /// derrotados y los que quedaban vivos vuelven a su sitio con la vida completa.
    /// </summary>
    public void RespawnAtCheckpoint()
    {
        CartoPlayer player = CartoPlayer.Instance;
        if (checkpoint == null || player == null)
        {
            RespawnAtHub();
            return;
        }
        CartoGameState.gold = Mathf.FloorToInt(CartoGameState.gold * 0.75f);
        CartoGameState.health = CartoGameState.maxHealth;
        Time.timeScale = 1f;

        CartoEnemy[] enemies = CartoEnemy.All.ToArray();
        for (int i = 0; i < enemies.Length; i++)
            if (enemies[i] != null) enemies[i].ResetToHome();

        player.Revive(checkpoint.RespawnPosition, checkpoint.yaw);
        Camera cam = Camera.main;
        CartoCamera orbit = cam != null ? cam.GetComponent<CartoCamera>() : null;
        if (orbit != null) orbit.yaw = checkpoint.yaw;
        CartoHUD.Toast("Reapareces en el punto de control", 3f);
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
