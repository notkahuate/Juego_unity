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

    /// <summary>
    /// El ejecutable arranca siempre en pantalla completa, aunque la ultima vez se cerrara en
    /// ventana (Unity recuerda el ultimo modo). F11 alterna despues.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void StartFullscreen()
    {
        if (Application.isEditor) return;
        Resolution r = Screen.currentResolution;
        Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
    }

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

        // Al iniciar el juego (o al volver desde la pausa) el Nexo abre con el menu principal;
        // "Jugar" llama a BeginZone. Entrando directo a una era no hay menu.
        if (zone == Zone.Hub && CartoGameState.menuPending)
        {
            CartoHUD.ShowOverlay(CartoHUD.Overlay.Menu);
        }
        else
        {
            CartoGameState.menuPending = false;
            BeginZone();
        }
    }

    /// <summary>Arranca la zona: historia inicial, victoria, presentacion de la era o solo su rotulo.</summary>
    public void BeginZone()
    {
        // Cinematica de historia (la crea el constructor; si la escena no la tiene, se omite).
        CartoCinematic cinematic = GetComponent<CartoCinematic>();
        bool hasCinematic = cinematic != null && cinematic.HasShots;
        int z = (int)zone;

        if (zone == Zone.Hub && !CartoGameState.introShown)
        {
            // Inicio de la partida: la historia y, al terminar o saltarla, a jugar.
            if (hasCinematic) cinematic.Play(EndIntro);
            else EndIntro();
        }
        else if (zone == Zone.Hub && CartoGameState.AllCrystals && !CartoGameState.victoryShown)
        {
            CartoGameState.victoryShown = true;
            CartoHUD.ShowOverlay(CartoHUD.Overlay.Victory);
        }
        else if (zone != Zone.Hub && hasCinematic && z < CartoGameState.eraIntroShown.Length && !CartoGameState.eraIntroShown[z])
        {
            // Primera visita a esta era: presentacion breve.
            CartoGameState.eraIntroShown[z] = true;
            cinematic.Play(EndEraIntro);
        }
        else
        {
            CartoHUD.Toast(zoneTitle, 3f);
        }
    }

    void EndIntro()
    {
        CartoGameState.introShown = true;
        CartoHUD.ShowOverlay(CartoHUD.Overlay.None);
        CartoHUD.Toast("Nexo del Tiempo  —  Cruza un portal para viajar a una era", 5f);
    }

    void EndEraIntro()
    {
        CartoHUD.ShowOverlay(CartoHUD.Overlay.None);
        CartoHUD.Toast(zoneTitle, 3f);
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
        CartoGameState.menuPending = false;
        Time.timeScale = 1f;
        SceneManager.LoadScene(CartoGameState.SceneHub);
    }

    /// <summary>Vuelve al menu principal (en el Nexo) conservando la partida en curso.</summary>
    public static void BackToMenu()
    {
        CartoGameState.menuPending = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(CartoGameState.SceneHub);
    }

    /// <summary>Cierra el juego: en el ejecutable sale de la aplicacion; en el editor detiene Play.</summary>
    public static void Quit()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>F11: alterna entre pantalla completa (resolucion del monitor) y ventana de 1280x720.</summary>
    public static void ToggleFullscreen()
    {
        if (Application.isEditor)
        {
            CartoHUD.Toast("F11: la pantalla completa solo cambia en el ejecutable", 2.5f);
            return;
        }
        if (Screen.fullScreen)
        {
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        }
        else
        {
            Resolution r = Screen.currentResolution;
            Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
        }
    }
}
