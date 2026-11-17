using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interfaz del juego dibujada con IMGUI (no necesita Canvas ni paquetes extra):
/// vida, XP, oro, cristales, armas, barras de enemigos, altar de mejoras y pantallas.
/// </summary>
public class CartoHUD : MonoBehaviour
{
    public enum Overlay { None, Menu, Pause, Death, Victory, Cinematic }

    enum Page { Root, Controls }   // dentro del menu principal y de la pausa

    struct Floater
    {
        public Vector3 pos;
        public string text;
        public Color color;
        public float born;
        public float size;
    }

    /// <summary>
    /// Valor de una barra que se anima en vez de saltar: 'shown' alcanza al valor real en unos 0,3 s
    /// y 'ghost' (la franja clara) se queda un momento atras para que se vea cuanto se acaba de perder.
    /// </summary>
    class Meter
    {
        public float shown = -1f;
        public float ghost;
        float last;
        float wait;

        public void Tick(float value, float dt)
        {
            if (shown < 0f)
            {
                shown = ghost = last = value;
                return;
            }
            if (value < last - 0.0001f) wait = 0.3f;
            last = value;
            shown = Ease(shown, value, dt, BarTime);
            wait -= dt;
            if (wait <= 0f) ghost = Ease(ghost, shown, dt, BarTime);
            if (ghost < shown) ghost = shown;
        }

        public void Snap(float value)
        {
            shown = ghost = last = value;
        }
    }

    const float BarTime = 0.3f;   // lo que tarda una barra en llegar a su valor

    /// <summary>Acerca 'current' a 'target' con frenada suave: llega (95 %) en 'time' segundos.</summary>
    static float Ease(float current, float target, float dt, float time)
    {
        if (Mathf.Abs(target - current) < 0.0005f) return target;
        return Mathf.Lerp(current, target, 1f - Mathf.Exp(-3f * dt / time));
    }

    static Overlay overlay = Overlay.None;
    static string toast = "";
    static float toastUntil;
    static string prompt = "";
    static int promptFrame = -10;
    static float flashUntil;
    static bool cursorFree;   // Ctrl: raton libre y camara quieta, sin pausar
    static float fadeStart = -10f;   // fundido desde negro al terminar una cinematica
    static Page page = Page.Root;
    static int selected;             // boton elegido en el menu abierto
    static Vector2 lastMouse;
    static int guardFrame = -10;     // fotograma en que se cerro un menu: ese clic no llega al juego
    static string phaseTitle = "";   // aviso de cambio de fase de un jefe
    static string phaseText = "";
    static Color phaseColor;
    static float phaseStart = -10f;
    const float PhaseTime = 3.6f;
    static Color skillFlashColor;    // destello de pantalla al usar la habilidad especial
    static float skillFlashStart = -10f;
    static string edgeText = "";     // aviso de borde del mapa (CartoBounds)
    static float edgeStart = -10f;
    const float EdgeTime = 2.4f;
    static readonly List<Floater> floaters = new List<Floater>();

    GUIStyle style;
    float scale = 1f;

    // Valores animados del HUD (por escena: se reinician al cargar)
    readonly Meter hpMeter = new Meter();
    readonly Meter xpMeter = new Meter();
    readonly Meter bossMeter = new Meter();
    readonly Dictionary<CartoEnemy, Meter> enemyMeters = new Dictionary<CartoEnemy, Meter>();
    CartoEnemy bossShown;
    int levelShown = -1;
    float levelFlash = -10f;     // momento (unscaled) de la ultima subida de nivel
    float goldShown = -1f;
    float goldPulse = -10f;      // momento (unscaled) del ultimo oro ganado
    int goldLast;

    public static bool OverlayActive { get { return overlay != Overlay.None; } }
    public static bool Blocking { get { return overlay != Overlay.None || CartoShop.IsOpen || Time.frameCount <= guardFrame; } }
    public static bool MenuOpen { get { return overlay == Overlay.Menu; } }
    public static bool CursorFree { get { return cursorFree; } }
    /// <summary>La pantalla actual tiene el juego detenido (Time.timeScale = 0).</summary>
    public static bool PausesTime { get { return overlay == Overlay.Pause || overlay == Overlay.Victory; } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void DomainReset()
    {
        overlay = Overlay.None;
        toast = "";
        toastUntil = 0f;
        prompt = "";
        promptFrame = -10;
        flashUntil = 0f;
        cursorFree = false;
        fadeStart = -10f;
        page = Page.Root;
        selected = 0;
        guardFrame = -10;
        edgeText = "";
        edgeStart = -10f;
        floaters.Clear();
    }

    // ------------------------------------------------------------ API estatica

    public static void Toast(string text, float seconds)
    {
        toast = text;
        toastUntil = Time.unscaledTime + seconds;
    }

    public static void SetPrompt(string text)
    {
        prompt = text;
        promptFrame = Time.frameCount;
    }

    public static void DamageFlash()
    {
        flashUntil = Time.unscaledTime + 0.35f;
    }

    public static void FadeFromBlack()
    {
        fadeStart = Time.unscaledTime;
    }

    /// <summary>Aviso grande en pantalla: un jefe ha cambiado de fase.</summary>
    public static void PhaseWarning(string who, string text, Color color)
    {
        phaseTitle = who != null ? who.ToUpper() : "";
        phaseText = text != null ? text : "";
        phaseColor = color;
        phaseStart = Time.unscaledTime;
    }

    /// <summary>Destello de toda la pantalla, del color de la habilidad especial que se acaba de usar.</summary>
    public static void SkillFlash(Color color)
    {
        skillFlashColor = color;
        skillFlashStart = Time.unscaledTime;
    }

    /// <summary>Aviso breve de que el cartografo ha llegado al borde del area jugable.</summary>
    public static void EdgeWarning(string text)
    {
        edgeText = text;
        edgeStart = Time.unscaledTime;
    }

    /// <summary>Numero flotante sobre el mundo. 'size' = 1 normal; mayor para golpes fuertes.</summary>
    public static void AddFloater(Vector3 worldPos, string text, Color color, float size = 1f)
    {
        Floater f = new Floater();
        // Un poco de desorden para que varios golpes seguidos no se tapen entre si.
        f.pos = worldPos + new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0f, 0.3f), Random.Range(-0.4f, 0.4f));
        f.text = text;
        f.color = color;
        f.size = size;
        f.born = Time.time;
        floaters.Add(f);
        if (floaters.Count > 40) floaters.RemoveAt(0);
    }

    public static void ShowOverlay(Overlay o)
    {
        if (overlay == Overlay.Menu || overlay == Overlay.Pause) guardFrame = Time.frameCount;
        overlay = o;
        page = Page.Root;
        selected = 0;
        // La pausa y la victoria detienen el juego; el menu principal no (el Nexo sigue vivo detras).
        Time.timeScale = PausesTime ? 0f : 1f;
        ApplyCursor();
    }

    /// <summary>
    /// Unico sitio que decide el estado del raton: libre en los menus (principal y pausa) o si se
    /// libero con Ctrl; bloqueado en el resto de casos. La camara solo gira con el raton bloqueado.
    /// </summary>
    static void ApplyCursor()
    {
        bool free = overlay == Overlay.Menu || overlay == Overlay.Pause || (overlay == Overlay.None && cursorFree);
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }

    // ------------------------------------------------------------ logica

    void Awake()
    {
        floaters.Clear();
        CartoShop.IsOpen = false;
        cursorFree = false;
        edgeStart = -10f;
        phaseStart = -10f;
        if (overlay == Overlay.Menu || overlay == Overlay.Pause || overlay == Overlay.Death || overlay == Overlay.Cinematic)
            overlay = Overlay.None;
    }

    void Update()
    {
        if (CartoInput.FullscreenDown) CartoGame.ToggleFullscreen();
        AnimateHud(Time.unscaledDeltaTime);

        switch (overlay)
        {
            case Overlay.None:
                if (CartoInput.PauseDown)
                {
                    if (CartoShop.IsOpen) CartoShop.Close();
                    else ShowOverlay(Overlay.Pause);
                }
                else if (CartoInput.FreeCursorDown)
                {
                    cursorFree = !cursorFree;
                    ApplyCursor();
                    Toast(cursorFree ? "Cámara liberada  —  Ctrl para volver a bloquearla" : "Cámara bloqueada", 2f);
                }
                else if (!cursorFree && Cursor.lockState != CursorLockMode.Locked && CartoInput.AttackDown)
                {
                    // El editor suelta el raton con Esc: un clic lo vuelve a capturar.
                    ApplyCursor();
                }
                break;

            case Overlay.Menu:
                UpdateMenu();
                break;

            case Overlay.Cinematic:
                if (CartoCinematic.Active == null) ShowOverlay(Overlay.None);
                else if (CartoInput.ConfirmDown) CartoCinematic.Active.Skip();
                break;

            case Overlay.Pause:
                if (page == Page.Root && CartoInput.PauseDown) ShowOverlay(Overlay.None);
                else UpdateMenu();
                break;

            case Overlay.Death:
                if (CartoInput.ConfirmDown)
                {
                    ShowOverlay(Overlay.None);
                    CartoGame game = CartoGame.Instance;
                    if (game != null && game.HasCheckpoint) game.RespawnAtCheckpoint();
                    else CartoGame.RespawnAtHub();
                }
                break;

            case Overlay.Victory:
                if (CartoInput.ConfirmDown) ShowOverlay(Overlay.None);
                else if (CartoInput.NewGameDown)
                {
                    ShowOverlay(Overlay.None);
                    CartoGame.NewGame();
                }
                break;
        }
    }

    /// <summary>Avanza las barras y contadores animados (en tiempo real: tambien durante el hit-stop).</summary>
    void AnimateHud(float dt)
    {
        CartoPlayer player = CartoPlayer.Instance;
        float hp = player != null ? player.health.current : CartoGameState.health;
        float maxHp = CartoGameState.maxHealth;
        hpMeter.Tick(maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f, dt);

        // Al subir de nivel la barra de XP vuelve a empezar desde cero (y destella).
        float xp = Mathf.Clamp01((float)CartoGameState.xp / Mathf.Max(1, CartoGameState.XpToNext));
        if (levelShown != CartoGameState.level)
        {
            if (levelShown >= 0 && CartoGameState.level > levelShown)
            {
                xpMeter.Snap(0f);
                levelFlash = Time.unscaledTime;
            }
            levelShown = CartoGameState.level;
        }
        xpMeter.Tick(xp, dt);

        // El oro cuenta hacia su valor y late al ganar.
        int gold = CartoGameState.gold;
        if (goldShown < 0f) goldShown = gold;
        if (gold > goldLast) goldPulse = Time.unscaledTime;
        goldLast = gold;
        goldShown = Mathf.Abs(gold - goldShown) < 0.6f ? gold : Ease(goldShown, gold, dt, BarTime);

        CartoEnemy boss = null;
        for (int i = 0; i < CartoEnemy.All.Count; i++)
        {
            CartoEnemy e = CartoEnemy.All[i];
            if (e == null || e.IsDead) continue;
            if (e.health.isBoss)
            {
                if (e.Aggro) boss = e;
                continue;
            }
            Meter m;
            if (!enemyMeters.TryGetValue(e, out m))
            {
                m = new Meter();
                enemyMeters[e] = m;
            }
            m.Tick(e.health.Percent, dt);
        }
        if (boss != bossShown)
        {
            bossShown = boss;
            if (boss != null) bossMeter.Snap(boss.health.Percent);
        }
        if (boss != null) bossMeter.Tick(boss.health.Percent, dt);

        // Limpieza: fuera los enemigos que ya no existen.
        if (enemyMeters.Count > CartoEnemy.All.Count + 6)
        {
            List<CartoEnemy> gone = new List<CartoEnemy>();
            foreach (KeyValuePair<CartoEnemy, Meter> kv in enemyMeters)
                if (kv.Key == null || kv.Key.IsDead) gone.Add(kv.Key);
            for (int i = 0; i < gone.Count; i++) enemyMeters.Remove(gone[i]);
        }
    }

    // ------------------------------------------------------------ menus (principal y pausa)

    static readonly string[] MenuNewItems = { "Jugar", "Controles", "Salir" };
    static readonly string[] MenuResumeItems = { "Continuar", "Nueva partida", "Controles", "Salir" };
    static readonly string[] PauseItems = { "Continuar", "Controles", "Volver al menú", "Salir del juego" };
    static readonly string[] BackItems = { "Volver" };

    /// <summary>Botones de la pantalla abierta. Con una partida en curso el menu principal ofrece continuarla.</summary>
    static string[] CurrentItems()
    {
        if (page == Page.Controls) return BackItems;
        if (overlay == Overlay.Pause) return PauseItems;
        return CartoGameState.introShown ? MenuResumeItems : MenuNewItems;
    }

    static float ControlsHeight { get { return 72f + ControlKeys.Length * 32f; } }   // en unidades de 1080p

    static Rect ControlsBox(float W, float H, float s)
    {
        float w = Mathf.Min(840f * s, W - 80f * s);
        return new Rect(W * 0.5f - w * 0.5f, H * 0.2f, w, ControlsHeight * s);
    }

    /// <summary>Columna de botones de la pausa (a su derecha va la coleccion de armas).</summary>
    static Rect PauseColumn(float W, float H, float s)
    {
        float total = Mathf.Min(1520f * s, W - 60f * s);
        return new Rect(W * 0.5f - total * 0.5f, H * 0.25f, Mathf.Min(440f * s, total * 0.4f), 424f * s);
    }

    /// <summary>Rectangulo en pantalla del boton i: lo comparten el dibujo (OnGUI) y el raton (Update).</summary>
    static Rect ButtonRect(int i, int count)
    {
        float W = Screen.width, H = Screen.height;
        float s = H / 1080f;
        if (page == Page.Controls)
        {
            Rect box = ControlsBox(W, H, s);
            return new Rect(W * 0.5f - 190f * s, box.yMax + 26f * s, 380f * s, 62f * s);
        }
        if (overlay == Overlay.Pause)
        {
            Rect col = PauseColumn(W, H, s);
            return new Rect(col.x + 26f * s, col.y + 66f * s + i * 82f * s, col.width - 52f * s, 64f * s);
        }
        float bw = 470f * s, bh = 66f * s;
        return new Rect(W * 0.5f - bw * 0.5f, H * 0.47f + i * (bh + 16f * s), bw, bh);
    }

    /// <summary>
    /// Navegacion de los botones: W/S o flechas y Enter, o raton y clic. Devuelve el boton pulsado (o -1).
    /// </summary>
    int UpdateButtons(int count)
    {
        if (selected >= count) selected = 0;
        if (CartoInput.MenuUpDown) selected = (selected + count - 1) % count;
        if (CartoInput.MenuDownDown) selected = (selected + 1) % count;

        Vector2 mouse = CartoInput.MousePosition;
        mouse.y = Screen.height - mouse.y;   // IMGUI mide desde arriba
        int hover = -1;
        for (int i = 0; i < count; i++)
            if (ButtonRect(i, count).Contains(mouse)) hover = i;
        bool click = CartoInput.AttackDown;
        // El raton solo elige al moverse o hacer clic, para no pelear con el teclado.
        if (hover >= 0 && (click || (mouse - lastMouse).sqrMagnitude > 1f)) selected = hover;
        lastMouse = mouse;

        if (click && hover >= 0) return hover;
        if (CartoInput.ConfirmDown) return selected;
        return -1;
    }

    void UpdateMenu()
    {
        string[] items = CurrentItems();
        int hit = UpdateButtons(items.Length);
        if (page == Page.Controls)
        {
            if (hit >= 0 || CartoInput.PauseDown)
            {
                page = Page.Root;
                selected = Mathf.Max(0, System.Array.IndexOf(CurrentItems(), "Controles"));
            }
            return;
        }
        if (hit < 0) return;

        switch (items[hit])
        {
            case "Jugar":
            case "Continuar":
                if (overlay == Overlay.Pause)
                {
                    ShowOverlay(Overlay.None);
                }
                else
                {
                    CartoGameState.menuPending = false;
                    ShowOverlay(Overlay.None);
                    FadeFromBlack();
                    if (CartoGame.Instance != null) CartoGame.Instance.BeginZone();
                }
                break;
            case "Nueva partida":
                ShowOverlay(Overlay.None);
                CartoGame.NewGame();
                break;
            case "Controles":
                page = Page.Controls;
                selected = 0;
                break;
            case "Volver al menú":
                ShowOverlay(Overlay.None);
                CartoGame.BackToMenu();
                break;
            case "Salir":
            case "Salir del juego":
                CartoGame.Quit();
                break;
        }
    }

    // ------------------------------------------------------------ dibujo

    static readonly Color Cyan = CartoArt.Cyan;
    static readonly Color Gold = CartoArt.Gold;
    static readonly Color Dim = CartoArt.A(CartoArt.Parchment, 0.78f);
    static readonly Color Panel = CartoArt.A(CartoArt.Ink, 0.62f);        // fondo de los grupos del HUD
    static readonly Color Card = CartoArt.A(CartoArt.Parchment, 0.07f);   // fondo de los paneles de menu
    static readonly GUIContent measure = new GUIContent();

    // Controles (tecla, accion). Se dibujan como tabla en el menu principal y en la pausa.
    static readonly string[] ControlKeys =
    {
        "WASD", "Ratón", "Shift", "Espacio", "Clic izquierdo", "Clic derecho",
        "F", "Q  /  R", "E", "1, 2, 3", "Ctrl", "Esc", "Enter", "F11"
    };
    static readonly string[] ControlActions =
    {
        "Moverse (también mientras atacas)", "Cámara (rueda = zoom)", "Correr", "Esquivar (cancela el ataque)",
        "Ataque cuerpo a cuerpo", "Ataque a distancia",
        "Habilidad especial del arma equipada",
        "Cambiar arma cercana / a distancia", "Interactuar (portal, altar, cristal)", "Comprar mejoras en el altar",
        "Liberar / bloquear cámara", "Pausa", "Aceptar / saltar cinemática", "Pantalla completa / ventana"
    };

    void DrawRect(Rect r, Color c)
    {
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    void Bar(Rect r, float pct, Color fill)
    {
        Bar(r, pct, pct, fill, CartoArt.A(CartoArt.Ink, 0.92f));
    }

    /// <summary>
    /// Barra con marco de tinta, relleno con brillo arriba y sombra abajo, y una franja clara
    /// ('ghost') que muestra lo que se acaba de perder.
    /// </summary>
    void Bar(Rect r, float pct, float ghost, Color fill, Color frame)
    {
        float b = Mathf.Max(1f, 2f * scale);
        pct = Mathf.Clamp01(pct);
        ghost = Mathf.Clamp01(ghost);
        Rect inner = new Rect(r.x + b, r.y + b, r.width - 2f * b, r.height - 2f * b);
        DrawRect(r, frame);
        DrawRect(inner, new Color(0f, 0f, 0f, 0.55f));
        if (ghost > pct) DrawRect(new Rect(inner.x, inner.y, inner.width * ghost, inner.height), CartoArt.A(CartoArt.Parchment, 0.8f));
        Rect f = new Rect(inner.x, inner.y, inner.width * pct, inner.height);
        DrawRect(f, fill);
        DrawRect(new Rect(f.x, f.y, f.width, f.height * 0.38f), new Color(1f, 1f, 1f, 0.17f));
        DrawRect(new Rect(f.x, f.yMax - f.height * 0.22f, f.width, f.height * 0.22f), new Color(0f, 0f, 0f, 0.2f));
    }

    /// <summary>Grupo del HUD: fondo de tinta translucido con un filete de color arriba.</summary>
    void HudPanel(Rect r, Color accent)
    {
        DrawRect(r, Panel);
        DrawRect(new Rect(r.x, r.y, r.width, Mathf.Max(2f, 3f * scale)), accent);
    }

    // ------------------------------------------------------------ iconos (se dibujan una vez en una textura)

    static Texture2D iconCircle, iconRing, iconDiamond, iconHeart;

    delegate bool Inside(float x, float y);

    /// <summary>Icono blanco (se tine al dibujarlo) a partir de una forma en coordenadas -1..1, con bordes suaves.</summary>
    static Texture2D MakeIcon(Inside shape)
    {
        const int n = 64, ss = 4;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.hideFlags = HideFlags.HideAndDontSave;
        Color[] px = new Color[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int hits = 0;
                for (int j = 0; j < ss; j++)
                    for (int i = 0; i < ss; i++)
                        if (shape((x + (i + 0.5f) / ss) / n * 2f - 1f, (y + (j + 0.5f) / ss) / n * 2f - 1f)) hits++;
                px[y * n + x] = new Color(1f, 1f, 1f, hits / (float)(ss * ss));
            }
        }
        t.SetPixels(px);
        t.Apply(false);
        return t;
    }

    static void EnsureIcons()
    {
        if (iconCircle != null) return;
        iconCircle = MakeIcon((x, y) => x * x + y * y <= 0.92f * 0.92f);
        iconRing = MakeIcon((x, y) => { float d = x * x + y * y; return d <= 0.92f * 0.92f && d >= 0.62f * 0.62f; });
        iconDiamond = MakeIcon((x, y) => Mathf.Abs(x) * 1.35f + Mathf.Abs(y) <= 0.95f);
        iconHeart = MakeIcon((x, y) =>
        {
            float hx = x * 1.2f, hy = y * 1.2f + 0.12f;
            float a = hx * hx + hy * hy - 1f;
            return a * a * a - hx * hx * hy * hy * hy <= 0f;
        });
    }

    void Icon(Rect r, Texture2D tex, Color c)
    {
        if (tex == null) return;
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, tex);
        GUI.color = old;
    }

    /// <summary>Icono centrado en (cx, cy) con lado 'size'.</summary>
    void IconAt(float cx, float cy, float size, Texture2D tex, Color c)
    {
        Icon(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), tex, c);
    }

    /// <summary>Raton pequeno con el boton izquierdo o el derecho encendido.</summary>
    void MouseIcon(Rect r, bool left, Color on)
    {
        float gap = Mathf.Max(1f, 2f * scale);
        DrawRect(r, CartoArt.A(CartoArt.Parchment, 0.28f));
        float half = (r.width - gap) * 0.5f;
        DrawRect(new Rect(left ? r.x : r.x + half + gap, r.y, half, r.height * 0.45f), on);
        DrawRect(new Rect(left ? r.x + half + gap : r.x, r.y, half, r.height * 0.45f), CartoArt.A(CartoArt.Parchment, 0.5f));
    }

    int FontSize(float size)
    {
        return Mathf.Max(13, Mathf.RoundToInt(size * scale));
    }

    /// <summary>Texto con contorno oscuro en las cuatro diagonales, legible sobre fondos claros u oscuros.</summary>
    void Label(Rect r, string text, float size, Color color, TextAnchor anchor, bool wrap = true, bool heavy = false)
    {
        style.fontSize = FontSize(size);
        style.alignment = anchor;
        style.wordWrap = wrap;
        float o = Mathf.Max(1f, (heavy ? 2.4f : 1.6f) * scale);
        style.normal.textColor = new Color(0f, 0f, 0f, (heavy ? 0.95f : 0.8f) * color.a);
        GUI.Label(new Rect(r.x - o, r.y - o, r.width, r.height), text, style);
        GUI.Label(new Rect(r.x + o, r.y - o, r.width, r.height), text, style);
        GUI.Label(new Rect(r.x - o, r.y + o, r.width, r.height), text, style);
        GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, style);
        if (heavy)
        {
            // Contorno grueso (ocho direcciones) para los numeros que flotan sobre el mundo.
            GUI.Label(new Rect(r.x - o, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x + o, r.y, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y - o, r.width, r.height), text, style);
            GUI.Label(new Rect(r.x, r.y + o * 1.5f, r.width, r.height), text, style);
        }
        style.normal.textColor = color;
        GUI.Label(r, text, style);
    }

    /// <summary>Ancho en pantalla de un texto de una sola linea.</summary>
    float TextWidth(string text, float size)
    {
        style.fontSize = FontSize(size);
        measure.text = text;
        return style.CalcSize(measure).x;
    }

    /// <summary>Texto centrado de una linea con un fondo oscuro ajustado a su ancho.</summary>
    void Banner(float centerX, float y, float height, string text, float size, Color color, float backAlpha)
    {
        float w = TextWidth(text, size) + 56f * scale;
        Rect r = new Rect(centerX - w * 0.5f, y, w, height);
        DrawRect(r, CartoArt.A(CartoArt.Ink, backAlpha));
        DrawRect(new Rect(r.x, r.yMax - Mathf.Max(2f, 3f * scale), r.width, Mathf.Max(2f, 3f * scale)), CartoArt.A(color, 0.85f));
        Label(r, text, size, color, TextAnchor.MiddleCenter, false);
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontStyle = FontStyle.Bold;
            style.wordWrap = true;
            style.richText = false;
            style.clipping = TextClipping.Overflow;
        }
        scale = Screen.height / 1080f;
        float W = Screen.width, H = Screen.height;
        float s = scale;

        if (overlay == Overlay.Cinematic)
        {
            DrawCinematic(W, H);
            return;
        }
        if (overlay == Overlay.Menu)
        {
            DrawMenu(W, H);
            return;
        }

        EnsureIcons();
        DrawWorld(W, H);

        // Destello carmesi al recibir dano
        float flash = flashUntil - Time.unscaledTime;
        if (flash > 0f) DrawRect(new Rect(0, 0, W, H), CartoArt.A(CartoArt.Crimson, 0.3f * Mathf.Clamp01(flash / 0.35f)));

        float now = Time.unscaledTime;
        Color parchment = CartoArt.Parchment;

        // --- Arriba a la izquierda (lo mas importante): nivel, vida y experiencia ---
        CartoPlayer player = CartoPlayer.Instance;
        float hp = player != null ? player.health.current : CartoGameState.health;
        float maxHp = CartoGameState.maxHealth;
        bool low = hpMeter.shown < 0.3f && hp > 0f;
        float beat = low ? 0.5f + 0.5f * Mathf.Sin(now * 9f) : 0f;       // latido con poca vida
        float lvlUp = Mathf.Clamp01(1f - (now - levelFlash) / 0.8f);    // destello al subir de nivel

        HudPanel(new Rect(20 * s, 18 * s, 500 * s, 106 * s), low ? CartoArt.Crimson : Cyan);
        // Insignia de nivel
        float bx = 70 * s, by = 72 * s;
        IconAt(bx, by, (88f + 14f * lvlUp) * s, iconDiamond, Color.Lerp(Cyan, Gold, lvlUp));
        IconAt(bx, by, 70 * s, iconDiamond, CartoArt.A(CartoArt.Ink, 0.92f));
        Label(new Rect(bx - 44 * s, by - 24 * s, 88 * s, 46 * s), CartoGameState.level.ToString(), 30, parchment, TextAnchor.MiddleCenter, false);
        // Vida
        IconAt(142 * s, 50 * s, (36f + 7f * beat) * s, iconHeart, CartoArt.Crimson);
        Rect hpRect = new Rect(168 * s, 31 * s, 336 * s, 38 * s);
        Bar(hpRect, hpMeter.shown, hpMeter.ghost, CartoArt.Crimson,
            low ? Color.Lerp(CartoArt.A(CartoArt.Ink, 0.92f), CartoArt.Crimson, beat * 0.8f) : CartoArt.A(CartoArt.Ink, 0.92f));
        Label(hpRect, Mathf.CeilToInt(hp) + " / " + Mathf.RoundToInt(maxHp), 23, parchment, TextAnchor.MiddleCenter, false);
        // Experiencia: barra fina, texto pequeno
        Rect xpRect = new Rect(124 * s, 80 * s, 380 * s, 15 * s);
        Bar(xpRect, xpMeter.shown, xpMeter.shown, Color.Lerp(Cyan, Gold, lvlUp), CartoArt.A(CartoArt.Ink, 0.92f));
        Label(new Rect(124 * s, 97 * s, 380 * s, 22 * s), "NIVEL " + CartoGameState.level + "     XP  " + CartoGameState.xp + " / " + CartoGameState.XpToNext,
            15, Dim, TextAnchor.MiddleLeft, false);

        // --- Arriba a la derecha: oro y cristales ---
        float pulse = Mathf.Clamp01(1f - (now - goldPulse) / 0.35f);
        Rect res = new Rect(W - 320 * s, 18 * s, 300 * s, 106 * s);
        HudPanel(res, Gold);
        IconAt(res.x + 36 * s, res.y + 34 * s, 38 * s, iconCircle, Gold);
        IconAt(res.x + 36 * s, res.y + 34 * s, 26 * s, iconRing, new Color(0.62f, 0.45f, 0.1f));
        Label(new Rect(res.x + 64 * s, res.y + 10 * s, res.width - 84 * s, 48 * s), Mathf.RoundToInt(goldShown).ToString(),
            31f + 5f * pulse, Gold, TextAnchor.MiddleRight, false);
        Label(new Rect(res.x + 18 * s, res.y + 64 * s, 150 * s, 32 * s), "Cristales", 18, Dim, TextAnchor.MiddleLeft, false);
        for (int i = 0; i < CartoGameState.crystals.Length; i++)
        {
            // Un rombo por cristal: lleno con el color de su era si ya se recupero, hueco si falta.
            float cx = res.xMax - 38 * s - (CartoGameState.crystals.Length - 1 - i) * 44 * s;
            float cy = res.y + 80 * s;
            Color cc = i == 0 ? Cyan : CartoArt.Magenta;
            if (CartoGameState.crystals[i])
            {
                IconAt(cx, cy, 40 * s, iconDiamond, cc);
                IconAt(cx - 2 * s, cy + 3 * s, 16 * s, iconDiamond, new Color(1f, 1f, 1f, 0.55f));
            }
            else
            {
                IconAt(cx, cy, 40 * s, iconDiamond, CartoArt.A(parchment, 0.35f));
                IconAt(cx, cy, 30 * s, iconDiamond, CartoArt.A(CartoArt.Ink, 0.95f));
            }
        }

        // --- Arriba al centro: objetivo (baja una fila si no cabe entre los paneles) y jefe ---
        float topY = 20f * s;
        CartoGame game = CartoGame.Instance;
        if (game != null && !string.IsNullOrEmpty(game.objective))
        {
            float ow = TextWidth(game.objective, 20) + 60f * s;
            if (W * 0.5f - ow * 0.5f < 540f * s || W * 0.5f + ow * 0.5f > W - 340f * s) topY = 136f * s;
            Rect ob = new Rect(W * 0.5f - ow * 0.5f, topY, ow, 58 * s);
            DrawRect(ob, CartoArt.A(CartoArt.Ink, 0.5f));
            Label(new Rect(ob.x, ob.y + 3 * s, ob.width, 20 * s), "OBJETIVO", 13, CartoArt.A(Cyan, 0.9f), TextAnchor.MiddleCenter, false);
            Label(new Rect(ob.x, ob.y + 22 * s, ob.width, 32 * s), game.objective, 20, parchment, TextAnchor.MiddleCenter, false);
        }

        if (bossShown != null && !bossShown.IsDead)
        {
            Rect br = new Rect(W * 0.5f - 380 * s, topY + 108 * s, 760 * s, 28 * s);
            Label(new Rect(br.x, br.y - 40 * s, br.width, 38 * s),
                bossShown.health.displayName + (bossShown.Phase2 ? "   ·   FASE 2" : ""), 27,
                bossShown.Phase2 ? Color.Lerp(bossShown.phaseColor, parchment, 0.4f) : parchment, TextAnchor.MiddleCenter, false);
            Bar(br, bossMeter.shown, bossMeter.ghost, CartoArt.Magenta, CartoArt.A(CartoArt.Ink, 0.92f));
            // Marca de media vida: ahi cambia de fase
            if (bossShown.phased) DrawRect(new Rect(br.center.x - 1.5f * s, br.y - 4 * s, 3f * s, br.height + 8 * s), CartoArt.A(parchment, 0.85f));
            IconAt(br.x - 22 * s, br.center.y, 34 * s, iconDiamond, CartoArt.Magenta);
            IconAt(br.xMax + 22 * s, br.center.y, 34 * s, iconDiamond, CartoArt.Magenta);
        }

        // --- Abajo a la derecha: armas equipadas, cada una con su boton del raton ---
        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        if (lib != null)
        {
            CartoWeaponDef m = lib.Get(CartoGameState.meleeId);
            CartoWeaponDef r = lib.Get(CartoGameState.rangedId);
            Rect wp = new Rect(W - 500 * s, H - 122 * s, 480 * s, 104 * s);
            HudPanel(wp, Cyan);
            for (int i = 0; i < 2; i++)
            {
                CartoWeaponDef d = i == 0 ? m : r;
                Rect row = new Rect(wp.x + 16 * s, wp.y + (12 + i * 46) * s, wp.width - 32 * s, 40 * s);
                MouseIcon(new Rect(row.x, row.y + 5 * s, 22 * s, 30 * s), i == 0, d != null ? Gold : CartoArt.A(parchment, 0.4f));
                if (d != null)
                {
                    Label(new Rect(row.x + 38 * s, row.y, row.width - 130 * s, row.height), d.displayName, 24, parchment, TextAnchor.MiddleLeft, false);
                    Label(row, "Nv." + (CartoGameState.WeaponLevel(d.id) + 1), 21, Gold, TextAnchor.MiddleRight, false);
                }
                else
                {
                    Label(new Rect(row.x + 38 * s, row.y, row.width - 38 * s, row.height),
                        i == 0 ? "sin arma" : "sin arma a distancia", 20, CartoArt.A(parchment, 0.5f), TextAnchor.MiddleLeft, false);
                }
            }
        }

        // --- Habilidad especial (F): junto a las armas, con su recarga a la vista ---
        if (player != null)
        {
            CartoPlayer.Skill skill = player.CurrentSkill;
            Color sc = CartoPlayer.SkillColor(skill);
            float left = player.SkillLeft;
            float total = Mathf.Max(0.1f, player.SkillTotal);
            bool ready = left <= 0f;
            Rect sk = new Rect(W - 760 * s, H - 122 * s, 244 * s, 104 * s);
            HudPanel(sk, sc);
            Rect key = new Rect(sk.x + 14 * s, sk.y + 16 * s, 60 * s, 60 * s);
            float line = Mathf.Max(1f, 2f * s);
            float keyPulse = ready ? 0.5f + 0.5f * Mathf.Sin(now * 4f) : 0f;
            DrawRect(key, CartoArt.A(sc, ready ? 0.26f + 0.16f * keyPulse : 0.1f));
            DrawRect(new Rect(key.x, key.y, key.width, line), CartoArt.A(sc, ready ? 1f : 0.45f));
            DrawRect(new Rect(key.x, key.yMax - line, key.width, line), CartoArt.A(sc, ready ? 1f : 0.45f));
            DrawRect(new Rect(key.x, key.y, line, key.height), CartoArt.A(sc, ready ? 1f : 0.45f));
            DrawRect(new Rect(key.xMax - line, key.y, line, key.height), CartoArt.A(sc, ready ? 1f : 0.45f));
            // Mientras recarga, la tecla esta tapada y se va destapando de arriba abajo.
            if (!ready) DrawRect(new Rect(key.x, key.y + key.height * (1f - left / total), key.width, key.height * (left / total)), new Color(0f, 0f, 0f, 0.6f));
            Label(key, ready ? "F" : Mathf.CeilToInt(left).ToString(), ready ? 32 : 27, ready ? parchment : Dim, TextAnchor.MiddleCenter, false);
            float tx = key.xMax + 12 * s;
            Label(new Rect(tx, sk.y + 10 * s, sk.xMax - tx - 8 * s, 20 * s), "HABILIDAD", 13, CartoArt.A(sc, 0.95f), TextAnchor.MiddleLeft, false);
            Label(new Rect(tx, sk.y + 30 * s, sk.xMax - tx - 8 * s, 50 * s), CartoPlayer.SkillTitle(skill), 19, ready ? parchment : Dim, TextAnchor.UpperLeft, true);
            Bar(new Rect(sk.x + 14 * s, sk.yMax - 18 * s, sk.width - 28 * s, 9 * s), ready ? 1f : 1f - left / total, sc);
        }

        // Vision temporal activa: marco cian en la pantalla y cuanto le queda
        float slow = CartoEnemy.SlowLeft;
        if (slow > 0f && overlay == Overlay.None)
        {
            float a = Mathf.Clamp01(slow / 0.4f) * (0.45f + 0.2f * Mathf.Sin(now * 6f));
            float b = 7f * s;
            DrawRect(new Rect(0, 0, W, b), CartoArt.A(Cyan, a));
            DrawRect(new Rect(0, H - b, W, b), CartoArt.A(Cyan, a));
            DrawRect(new Rect(0, b, b, H - 2f * b), CartoArt.A(Cyan, a));
            DrawRect(new Rect(W - b, b, b, H - 2f * b), CartoArt.A(Cyan, a));
            Rect vr = new Rect(W * 0.5f - 160 * s, H * 0.2f, 320 * s, 10 * s);
            Label(new Rect(vr.x, vr.y - 24 * s, vr.width, 22 * s), "VISIÓN TEMPORAL", 15, Cyan, TextAnchor.MiddleCenter, false);
            Bar(vr, slow / 4f, Cyan);
        }

        // Destello al usar la habilidad
        float sf = 1f - (now - skillFlashStart) / 0.3f;
        if (sf > 0f) DrawRect(new Rect(0, 0, W, H), CartoArt.A(skillFlashColor, 0.22f * sf));

        // --- Abajo a la izquierda (lo menos importante): recordatorio de controles, pequeno y tenue ---
        Rect cp = new Rect(20 * s, H - 84 * s, 640 * s, 66 * s);
        DrawRect(cp, CartoArt.A(CartoArt.Ink, 0.45f));
        Color hint = CartoArt.A(parchment, 0.62f);
        Label(new Rect(cp.x + 14 * s, cp.y + 5 * s, cp.width - 28 * s, 28 * s),
            "WASD mover     Shift correr     Espacio esquivar     E interactuar", 17, hint, TextAnchor.MiddleLeft, false);
        Label(new Rect(cp.x + 14 * s, cp.y + 33 * s, cp.width - 28 * s, 28 * s),
            "F habilidad     Q / R cambiar arma     Ctrl liberar cámara     Esc pausa", 17, hint, TextAnchor.MiddleLeft, false);
        if (cursorFree && overlay == Overlay.None)
        {
            Rect ft = new Rect(20 * s, cp.y - 46 * s, 520 * s, 38 * s);
            DrawRect(ft, CartoArt.A(CartoArt.Ink, 0.85f));
            DrawRect(new Rect(ft.x, ft.y, 5 * s, ft.height), Gold);
            Label(ft, "CÁMARA LIBRE  —  Ctrl para bloquearla", 19, Gold, TextAnchor.MiddleCenter, false);
        }

        // --- Mensajes ---
        if (Time.unscaledTime < toastUntil && !string.IsNullOrEmpty(toast) && overlay == Overlay.None)
            Banner(W * 0.5f, H * 0.24f, 62 * s, toast, 29, Gold, 0.72f);
        if (Time.frameCount - promptFrame <= 1 && overlay == Overlay.None && !CartoShop.IsOpen)
            Banner(W * 0.5f, H * 0.72f, 56 * s, prompt, 27, CartoArt.Parchment, 0.76f);

        // Aviso de borde del mapa: aparece, se queda un momento y se desvanece
        float edge = Time.unscaledTime - edgeStart;
        if (edge >= 0f && edge < EdgeTime && overlay == Overlay.None && !string.IsNullOrEmpty(edgeText))
        {
            float a = Mathf.Clamp01(edge / 0.2f) * Mathf.Clamp01((EdgeTime - edge) / 0.5f);
            float ew = TextWidth(edgeText, 25) + 72f * s;
            Rect er = new Rect(W * 0.5f - ew * 0.5f, H * 0.61f, ew, 52f * s);
            DrawRect(er, CartoArt.A(CartoArt.Ink, 0.8f * a));
            DrawRect(new Rect(er.x, er.yMax - 4f * s, er.width, 4f * s), CartoArt.A(CartoArt.Magenta, a));
            Label(er, edgeText, 25, CartoArt.A(CartoArt.Parchment, a), TextAnchor.MiddleCenter, false);
        }

        // Cambio de fase de un jefe: franja que cruza la pantalla
        float pt = Time.unscaledTime - phaseStart;
        if (pt >= 0f && pt < PhaseTime && overlay == Overlay.None)
        {
            float a = Mathf.Clamp01(pt / 0.15f) * Mathf.Clamp01((PhaseTime - pt) / 0.5f);
            float bh = 168f * s * (0.55f + 0.45f * Mathf.Clamp01(pt / 0.2f));
            Rect band = new Rect(0, H * 0.4f - bh * 0.5f, W, bh);
            float lineH = Mathf.Max(2f, 4f * s);
            DrawRect(band, CartoArt.A(CartoArt.Ink, 0.86f * a));
            DrawRect(new Rect(0, band.y, W, lineH), CartoArt.A(phaseColor, a));
            DrawRect(new Rect(0, band.yMax - lineH, W, lineH), CartoArt.A(phaseColor, a));
            Label(new Rect(0, H * 0.4f - 74f * s, W, 28f * s), "FASE 2", 21, CartoArt.A(phaseColor, a), TextAnchor.MiddleCenter, false);
            Label(new Rect(0, H * 0.4f - 46f * s, W, 62f * s), phaseTitle, 46, CartoArt.A(CartoArt.Parchment, a), TextAnchor.MiddleCenter, false);
            Label(new Rect(0, H * 0.4f + 22f * s, W, 40f * s), phaseText, 24, CartoArt.A(phaseColor, a), TextAnchor.MiddleCenter, false);
        }

        if (CartoShop.IsOpen && overlay == Overlay.None) DrawShop(W, H);
        if (overlay != Overlay.None) DrawOverlay(W, H);

        // Fundido desde negro al terminar una cinematica
        float fade = 1f - (Time.unscaledTime - fadeStart) / 0.6f;
        if (fade > 0f) DrawRect(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, Mathf.Clamp01(fade)));
    }

    /// <summary>
    /// Cinematica (sin HUD): barras de cine que entran al empezar, fundidos a negro, texto que se
    /// escribe letra por letra (abajo, o centrado en las tarjetas en negro), glitch temporal y rotulo.
    /// </summary>
    void DrawCinematic(float W, float H)
    {
        float s = scale;
        CartoCinematic c = CartoCinematic.Active;
        float black = c != null ? c.Blackness : 1f;
        float glitch = c != null ? c.Glitch : 0f;
        if (black > 0f) DrawRect(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, black));
        if (glitch > 0.01f) DrawGlitch(W, H, glitch);

        float bars = c != null ? c.Letterbox : 1f;
        float top = H * 0.1f * bars, bottom = H * 0.2f * bars;
        DrawRect(new Rect(0, 0, W, top), Color.black);
        DrawRect(new Rect(0, H - bottom, W, bottom), Color.black);

        if (c != null)
        {
            float alpha = c.TextAlpha;
            // Con glitch el texto tiembla de lado.
            float jx = glitch > 0f ? (CartoCinematic.Hash(Mathf.FloorToInt(Time.unscaledTime * 20f) + 5) - 0.5f) * 16f * s * glitch : 0f;
            if (c.IsCard)
            {
                // Tarjeta: una frase en el centro del negro, con cursor mientras se escribe.
                bool cursor = c.Typing || Mathf.FloorToInt(Time.unscaledTime * 2.5f) % 2 == 0;
                float tw = Mathf.Min(TextWidth(c.Caption, 44), W - 120f * s);
                Rect r = new Rect(W * 0.5f - tw * 0.5f + jx, H * 0.44f, tw + 60f * s, 70f * s);
                GlitchLabel(r, c.CaptionTyped + (cursor ? "_" : ""), 44, CartoArt.A(CartoArt.Parchment, alpha), TextAnchor.MiddleLeft, false, glitch);
            }
            else
            {
                // Texto narrativo en la barra de abajo. Alineado a la izquierda: al escribirse no baila.
                float colW = Mathf.Min(1300f * s, W - 80f * s);
                Rect r = new Rect(W * 0.5f - colW * 0.5f + jx, H - H * 0.2f + 24f * s, colW, H * 0.2f - 60f * s);
                GlitchLabel(r, c.CaptionTyped, 29, CartoArt.A(CartoArt.Parchment, alpha * (1f - black)), TextAnchor.UpperLeft, true, glitch);
            }

            // Rotulo del cierre
            float ta = c.TitleAlpha;
            if (ta > 0f)
            {
                float ty = H * 0.15f;
                Label(new Rect(0, ty, W, 110f * s), c.Title, 80, CartoArt.A(Cyan, ta), TextAnchor.MiddleCenter, false);
                float lw = 660f * s * ta;
                DrawRect(new Rect(W * 0.5f - lw * 0.5f, ty + 118f * s, lw, Mathf.Max(2f, 3f * s)), CartoArt.A(Gold, ta));
            }
        }
        Label(new Rect(W - 340 * s, H - 48 * s, 320 * s, 36 * s), "ENTER  saltar", 20, CartoArt.A(CartoArt.Parchment, 0.6f * bars), TextAnchor.MiddleRight, false);
    }

    /// <summary>Texto que, con glitch, se desdobla en cian y magenta a los lados.</summary>
    void GlitchLabel(Rect r, string text, float size, Color color, TextAnchor anchor, bool wrap, float glitch)
    {
        if (glitch > 0.05f)
        {
            float d = (2f + 7f * glitch) * scale;
            Label(new Rect(r.x - d, r.y, r.width, r.height), text, size, CartoArt.A(CartoArt.Cyan, 0.65f * color.a), anchor, wrap);
            Label(new Rect(r.x + d, r.y, r.width, r.height), text, size, CartoArt.A(CartoArt.Magenta, 0.65f * color.a), anchor, wrap);
        }
        Label(r, text, size, color, anchor, wrap);
    }

    static Texture2D scanlines;

    /// <summary>
    /// Glitch temporal: lineas de barrido, franjas de color que saltan de sitio, cortes en negro y un
    /// velo de color. Cambia a saltos (no cada fotograma) para que parezca una senal rota.
    /// </summary>
    void DrawGlitch(float W, float H, float amount)
    {
        float s = scale;
        if (scanlines == null)
        {
            scanlines = new Texture2D(1, 4, TextureFormat.RGBA32, false);
            scanlines.hideFlags = HideFlags.HideAndDontSave;
            scanlines.wrapMode = TextureWrapMode.Repeat;
            scanlines.filterMode = FilterMode.Point;
            scanlines.SetPixels(new[] { new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0.3f), new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0f) });
            scanlines.Apply(false);
        }
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.5f * amount);
        GUI.DrawTextureWithTexCoords(new Rect(0, 0, W, H), scanlines, new Rect(0f, 0f, 1f, H / 4f));
        GUI.color = old;

        int step = Mathf.FloorToInt(Time.unscaledTime * 22f) * 131;
        // Velo de color en toda la pantalla
        DrawRect(new Rect(0, 0, W, H), CartoArt.A(CartoCinematic.Hash(step) > 0.5f ? CartoArt.Cyan : CartoArt.Magenta, 0.09f * amount));

        // Franjas horizontales
        int strips = 4 + Mathf.RoundToInt(amount * 11f);
        for (int i = 0; i < strips; i++)
        {
            float a = CartoCinematic.Hash(step + i * 7 + 1);
            float b = CartoCinematic.Hash(step + i * 7 + 2);
            float c = CartoCinematic.Hash(step + i * 7 + 3);
            float d = CartoCinematic.Hash(step + i * 7 + 4);
            float h = (2f + b * b * 46f) * s;
            float w = W * (0.25f + c * 0.75f);
            float x = (W - w) * d;
            Color col = a < 0.4f ? CartoArt.Cyan : (a < 0.75f ? CartoArt.Magenta : (a < 0.9f ? CartoArt.Parchment : Color.black));
            DrawRect(new Rect(x, CartoCinematic.Hash(step + i * 7 + 5) * H, w, h), CartoArt.A(col, (0.12f + c * 0.5f) * amount));
        }

        // De vez en cuando, un corte: una banda entera se queda en negro
        if (CartoCinematic.Hash(step + 977) < 0.35f * amount)
        {
            float y = CartoCinematic.Hash(step + 978) * H * 0.85f;
            DrawRect(new Rect(0, y, W, H * (0.03f + 0.09f * CartoCinematic.Hash(step + 979))), new Color(0f, 0f, 0f, 0.85f));
        }
        // Y un destello blanco justo al estallar
        if (amount > 0.85f) DrawRect(new Rect(0, 0, W, H), CartoArt.A(CartoArt.Parchment, (amount - 0.85f) * 2.2f));
    }

    void DrawWorld(float W, float H)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float s = scale;

        // Barras de vida de los enemigos
        for (int i = 0; i < CartoEnemy.All.Count; i++)
        {
            CartoEnemy e = CartoEnemy.All[i];
            if (e == null || e.IsDead || e.health.isBoss) continue;
            if (!e.Aggro && e.health.Percent >= 1f) continue;
            Vector3 sp = cam.WorldToScreenPoint(e.transform.position + Vector3.up * (e.BarHeight + (e.fused ? 0.85f : 0.35f)));
            if (sp.z <= 0f || sp.z > 40f) continue;
            Meter m;
            float shown = e.health.Percent, ghost = shown;
            if (enemyMeters.TryGetValue(e, out m) && m.shown >= 0f)
            {
                shown = m.shown;
                ghost = m.ghost;
            }
            float w = 118f * s, h = 14f * s;
            Bar(new Rect(sp.x - w * 0.5f, H - sp.y - h, w, h), shown, ghost, CartoArt.Crimson, CartoArt.A(CartoArt.Ink, 0.92f));
            // Nombre del tipo, con su color, mientras esta cerca y en guardia
            if (e.Aggro && sp.z < 26f && !string.IsNullOrEmpty(e.health.displayName))
            {
                Label(new Rect(sp.x - 160f * s, H - sp.y - h - 25f * s, 320f * s, 22f * s), e.health.displayName, 15,
                    Color.Lerp(e.typeColor, CartoArt.Parchment, 0.35f), TextAnchor.MiddleCenter, false);
            }
        }

        // Numeros flotantes: nacen grandes y encogen (golpe), suben frenando y se apagan al final.
        const float life = 0.95f;
        for (int i = floaters.Count - 1; i >= 0; i--)
        {
            Floater f = floaters[i];
            float age = Time.time - f.born;
            if (age > life || age < 0f)
            {
                floaters.RemoveAt(i);
                continue;
            }
            float t = age / life;
            float rise = 1.5f * (1f - (1f - t) * (1f - t));
            Vector3 sp = cam.WorldToScreenPoint(f.pos + Vector3.up * rise);
            if (sp.z <= 0f) continue;
            float pop = 1f + 0.55f * Mathf.Clamp01(1f - age / 0.12f);
            Color c = f.color;
            c.a = Mathf.Clamp01((life - age) / 0.3f);
            float size = 30f * (f.size > 0f ? f.size : 1f) * pop;
            Label(new Rect(sp.x - 160 * s, H - sp.y - 30 * s, 320 * s, 60 * s), f.text, size, c, TextAnchor.MiddleCenter, false, true);
        }
    }

    void DrawShop(float W, float H)
    {
        float s = scale;
        Rect box = new Rect(W * 0.5f - 560 * s, H * 0.5f - 245 * s, 1120 * s, 490 * s);
        DrawRect(box, CartoArt.A(CartoArt.Ink, 0.94f));
        DrawRect(new Rect(box.x, box.y, box.width, 6 * s), Cyan);
        Label(new Rect(box.x, box.y + 22 * s, box.width, 50 * s), "ALTAR DE MEJORAS", 38, Cyan, TextAnchor.MiddleCenter, false);
        Label(new Rect(box.x, box.y + 78 * s, box.width, 38 * s), "Oro disponible: " + CartoGameState.gold, 27, Gold, TextAnchor.MiddleCenter, false);
        for (int n = 1; n <= 3; n++)
        {
            Rect row = new Rect(box.x + 40 * s, box.y + (144 + (n - 1) * 74) * s, box.width - 80 * s, 60 * s);
            DrawRect(row, new Color(1f, 1f, 1f, 0.09f));
            Label(new Rect(row.x + 20 * s, row.y, row.width - 40 * s, row.height), CartoShop.OptionText(n), 23, CartoArt.Parchment, TextAnchor.MiddleLeft, false);
        }
        Label(new Rect(box.x + 40 * s, box.yMax - 104 * s, box.width - 80 * s, 36 * s),
            "Pulsa 1, 2 o 3 para comprar. Las mejoras de arma se aplican al arma equipada (Q / R para cambiarla).",
            20, Dim, TextAnchor.MiddleCenter);
        Label(new Rect(box.x, box.yMax - 56 * s, box.width, 38 * s), "[E] o [Esc]  Cerrar", 23, CartoArt.Parchment, TextAnchor.MiddleCenter, false);
    }

    void DrawButtons()
    {
        string[] items = CurrentItems();
        for (int i = 0; i < items.Length; i++) DrawButton(ButtonRect(i, items.Length), items[i], i == selected);
    }

    /// <summary>Boton de menu: claro sobre el panel; el elegido (teclado o raton encima) crece y pasa a oro.</summary>
    void DrawButton(Rect r, string text, bool chosen)
    {
        float s = scale;
        if (chosen) r = new Rect(r.x - 10f * s, r.y - 2f * s, r.width + 20f * s, r.height + 4f * s);
        float line = Mathf.Max(1f, 2f * s);
        Color edge = chosen ? Gold : CartoArt.A(Cyan, 0.45f);
        DrawRect(r, chosen ? CartoArt.A(Gold, 0.26f) : CartoArt.A(CartoArt.Parchment, 0.1f));
        DrawRect(new Rect(r.x, r.y, r.width, line), edge);
        DrawRect(new Rect(r.x, r.yMax - line, r.width, line), edge);
        DrawRect(new Rect(r.x, r.y, 6f * s, r.height), chosen ? Gold : Cyan);
        Label(r, text, chosen ? 31 : 28, chosen ? Gold : CartoArt.Parchment, TextAnchor.MiddleCenter, false);
    }

    /// <summary>Panel de menu de la biblia de arte: fondo de tinta y filete cian arriba.</summary>
    void DrawPanel(Rect r, float alpha)
    {
        DrawRect(r, CartoArt.A(CartoArt.Ink, alpha));
        DrawRect(new Rect(r.x, r.y, r.width, 4f * scale), Cyan);
    }

    /// <summary>Menu principal: titulo, botones y, en su pagina, la tabla de controles. El Nexo se ve detras.</summary>
    void DrawMenu(float W, float H)
    {
        float s = scale;
        Rect foot = new Rect(0, H * 0.9f, W, 44 * s);
        DrawRect(new Rect(0, 0, W, H), CartoArt.A(CartoArt.Ink, page == Page.Controls ? 0.82f : 0.5f));
        DrawRect(new Rect(0, 0, W, H * 0.34f), CartoArt.A(CartoArt.Ink, 0.4f));
        DrawRect(new Rect(0, H * 0.84f, W, H * 0.16f), CartoArt.A(CartoArt.Ink, 0.5f));

        if (page == Page.Controls)
        {
            Label(new Rect(0, H * 0.07f, W, 90 * s), "EL CARTÓGRAFO DEL TIEMPO", 54, Cyan, TextAnchor.MiddleCenter, false);
            DrawControls(ControlsBox(W, H, s));
            DrawButtons();
            Label(foot, "Esc o Enter: volver", 21, Dim, TextAnchor.MiddleCenter, false);
        }
        else
        {
            float ty = H * 0.14f;
            Label(new Rect(0, ty, W, 104 * s), "EL CARTÓGRAFO DEL TIEMPO", 80, Cyan, TextAnchor.MiddleCenter, false);
            DrawRect(new Rect(W * 0.5f - 300 * s, ty + 112 * s, 600 * s, Mathf.Max(2f, 3f * s)), Gold);
            Label(new Rect(0, ty + 126 * s, W, 44 * s), "Las eras se han mezclado. Recupera los cristales del tiempo.",
                27, CartoArt.Parchment, TextAnchor.MiddleCenter, false);

            string[] items = CurrentItems();
            Rect first = ButtonRect(0, items.Length);
            Rect last = ButtonRect(items.Length - 1, items.Length);
            DrawPanel(new Rect(first.x - 34 * s, first.y - 34 * s, first.width + 68 * s, last.yMax - first.y + 68 * s), 0.8f);
            DrawButtons();
            Label(foot, "W / S o ratón: elegir          Enter o clic: aceptar          F11: pantalla completa",
                21, Dim, TextAnchor.MiddleCenter, false);
        }

        float fade = 1f - (Time.unscaledTime - fadeStart) / 0.6f;
        if (fade > 0f) DrawRect(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, Mathf.Clamp01(fade)));
    }

    /// <summary>Tabla de controles: tecla a la izquierda (dorado) y accion a la derecha.</summary>
    void DrawControls(Rect box)
    {
        float s = scale;
        DrawRect(box, Card);
        DrawRect(new Rect(box.x, box.y, box.width, 4 * s), Cyan);
        Label(new Rect(box.x, box.y + 10 * s, box.width, 36 * s), "CONTROLES", 26, Cyan, TextAnchor.MiddleCenter, false);
        float keyW = 190 * s;
        float x = box.x + 24 * s;
        float y = box.y + 56 * s;
        float row = 32 * s;
        for (int i = 0; i < ControlKeys.Length; i++)
        {
            if (i % 2 == 1) DrawRect(new Rect(box.x + 12 * s, y, box.width - 24 * s, row), new Color(1f, 1f, 1f, 0.04f));
            Label(new Rect(x, y, keyW, row), ControlKeys[i], 21, Gold, TextAnchor.MiddleRight, false);
            Label(new Rect(x + keyW + 24 * s, y, box.width - keyW - 72 * s, row), ControlActions[i], 21, CartoArt.Parchment, TextAnchor.MiddleLeft, false);
            y += row;
        }
    }

    /// <summary>
    /// Coleccion de armas del menu de pausa: todas las del catalogo (CartoWeaponLibrary) agrupadas
    /// por era, marcando cuales se han obtenido (CartoGameState.owned) y cuales siguen bloqueadas.
    /// </summary>
    void DrawCollection(Rect box)
    {
        float s = scale;
        Color locked = new Color(1f, 1f, 1f, 0.45f);
        DrawRect(box, Card);
        DrawRect(new Rect(box.x, box.y, box.width, 4 * s), Cyan);
        Label(new Rect(box.x, box.y + 10 * s, box.width, 36 * s), "COLECCIÓN DE ARMAS", 26, Cyan, TextAnchor.MiddleCenter, false);

        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        if (lib == null) return;
        List<string> eras = new List<string>();
        for (int i = 0; i < lib.weapons.Count; i++)
            if (!eras.Contains(lib.weapons[i].era)) eras.Add(lib.weapons[i].era);

        float x = box.x + 28 * s, w = box.width - 56 * s;
        float y = box.y + 56 * s;
        float row = 32 * s;
        for (int e = 0; e < eras.Count; e++)
        {
            int total = 0, got = 0;
            for (int i = 0; i < lib.weapons.Count; i++)
            {
                if (lib.weapons[i].era != eras[e]) continue;
                total++;
                if (CartoGameState.Owns(lib.weapons[i].id)) got++;
            }
            Label(new Rect(x, y, w, row), CartoWeaponLibrary.EraTitle(eras[e]), 23, Gold, TextAnchor.MiddleLeft, false);
            Label(new Rect(x, y, w, row), got + " / " + total, 21, Gold, TextAnchor.MiddleRight, false);
            DrawRect(new Rect(x, y + row, w, Mathf.Max(1f, 2f * s)), new Color(1f, 1f, 1f, 0.25f));
            y += row + 6 * s;

            for (int i = 0; i < lib.weapons.Count; i++)
            {
                CartoWeaponDef d = lib.weapons[i];
                if (d.era != eras[e]) continue;
                bool owned = CartoGameState.Owns(d.id);
                bool equipped = owned && (d.id == CartoGameState.meleeId || d.id == CartoGameState.rangedId);

                // Marca: cuadro lleno = obtenida, cuadro hueco = bloqueada.
                Rect mark = new Rect(x + 6 * s, y + 9 * s, 14 * s, 14 * s);
                DrawRect(mark, owned ? new Color(0.45f, 0.9f, 0.5f) : locked);
                if (!owned)
                {
                    float b = Mathf.Max(1f, 2f * s);
                    DrawRect(new Rect(mark.x + b, mark.y + b, mark.width - 2f * b, mark.height - 2f * b), new Color(0.05f, 0.03f, 0.12f));
                }

                Label(new Rect(x + 34 * s, y, w * 0.4f, row), d.displayName, 22, owned ? CartoArt.Parchment : locked, TextAnchor.MiddleLeft, false);
                Label(new Rect(x + w * 0.43f, y, w * 0.27f, row), d.ranged ? "a distancia" : "cuerpo a cuerpo", 19,
                    owned ? Dim : locked, TextAnchor.MiddleLeft, false);
                string status = !owned ? "Bloqueada"
                    : "Nv." + (CartoGameState.WeaponLevel(d.id) + 1) + (equipped ? "  ·  equipada" : "");
                Label(new Rect(x, y, w, row), status, 21, !owned ? locked : (equipped ? Gold : CartoArt.Parchment), TextAnchor.MiddleRight, false);
                y += row;
            }
            y += 8 * s;
        }
    }

    void DrawOverlay(float W, float H)
    {
        float s = scale;
        DrawRect(new Rect(0, 0, W, H), CartoArt.A(CartoArt.Ink, overlay == Overlay.Death ? 0.7f : 0.88f));
        Rect title = new Rect(0, H * 0.07f, W, 90 * s);
        Rect foot = new Rect(0, H * 0.87f, W, 54 * s);
        float colW = Mathf.Min(1240f * s, W - 80f * s);            // columna de texto centrada
        Rect body = new Rect(W * 0.5f - colW * 0.5f, H * 0.34f, colW, H * 0.45f);

        switch (overlay)
        {
            case Overlay.Pause:
            {
                Label(title, "PAUSA", 64, Cyan, TextAnchor.MiddleCenter, false);
                Label(new Rect(0, H * 0.175f, W, 44 * s),
                    "Nivel " + CartoGameState.level + "        Oro " + CartoGameState.gold +
                    "        Cristales " + CartoGameState.CrystalCount + " / 2        Enemigos derrotados " + CartoGameState.kills,
                    27, Gold, TextAnchor.MiddleCenter, false);
                if (page == Page.Controls)
                {
                    DrawControls(ControlsBox(W, H, s));
                    DrawButtons();
                    Label(foot, "Esc o Enter: volver", 21, Dim, TextAnchor.MiddleCenter, false);
                    break;
                }
                // Dos columnas: botones a la izquierda, coleccion de armas a la derecha.
                float total = Mathf.Min(1520f * s, W - 60f * s);
                float gap = 30f * s;
                Rect col = PauseColumn(W, H, s);
                DrawRect(col, Card);
                DrawRect(new Rect(col.x, col.y, col.width, 4 * s), Cyan);
                Label(new Rect(col.x, col.y + 10 * s, col.width, 36 * s), "MENÚ", 26, Cyan, TextAnchor.MiddleCenter, false);
                DrawButtons();
                DrawCollection(new Rect(col.xMax + gap, col.y, total - col.width - gap, col.height));
                Label(foot, "W / S o ratón: elegir          Enter o clic: aceptar          Esc: continuar",
                    21, Dim, TextAnchor.MiddleCenter, false);
                break;
            }

            case Overlay.Death:
            {
                bool atCheckpoint = CartoGame.Instance != null && CartoGame.Instance.HasCheckpoint;
                Label(new Rect(0, H * 0.2f, W, 90 * s), "HAS CAÍDO", 70, CartoArt.Crimson, TextAnchor.MiddleCenter, false);
                Label(new Rect(body.x, H * 0.38f, body.width, body.height),
                    (atCheckpoint
                        ? "El tiempo te devuelve al punto de control. Los enemigos que quedaban en pie se recuperan.\n"
                        : "El tiempo te devuelve al Nexo.\n") +
                    "Pierdes una cuarta parte de tu oro, pero conservas armas, nivel y cristales.",
                    29, CartoArt.Parchment, TextAnchor.UpperCenter);
                Label(foot, atCheckpoint ? "Pulsa ENTER para reaparecer en el punto de control" : "Pulsa ENTER para volver al Nexo",
                    32, Gold, TextAnchor.MiddleCenter, false);
                break;
            }

            case Overlay.Victory:
            {
                Label(new Rect(0, H * 0.12f, W, 90 * s), "¡EL TIEMPO HA SIDO RESTAURADO!", 60, Cyan, TextAnchor.MiddleCenter, false);
                Label(new Rect(body.x, H * 0.28f, body.width, 120 * s),
                    "Has reunido los cristales de la Era Pirata y de la Era Samurái.\n" +
                    "Las épocas vuelven a su lugar gracias al cartógrafo.",
                    29, CartoArt.Parchment, TextAnchor.UpperCenter);
                int total = Mathf.FloorToInt(CartoGameState.playTime);
                string[] names = { "Nivel alcanzado", "Enemigos derrotados", "Oro", "Tiempo de juego" };
                string[] values =
                {
                    CartoGameState.level.ToString(), CartoGameState.kills.ToString(), CartoGameState.gold.ToString(),
                    (total / 60) + " min " + (total % 60) + " s"
                };
                float bw = Mathf.Min(640f * s, W - 80f * s);
                Rect box = new Rect(W * 0.5f - bw * 0.5f, H * 0.46f, bw, 200 * s);
                DrawRect(box, Card);
                for (int i = 0; i < names.Length; i++)
                {
                    Rect row = new Rect(box.x + 30 * s, box.y + (16 + i * 42) * s, box.width - 60 * s, 42 * s);
                    Label(row, names[i], 26, Dim, TextAnchor.MiddleLeft, false);
                    Label(row, values[i], 26, Gold, TextAnchor.MiddleRight, false);
                }
                Label(foot, "ENTER  seguir explorando          N  nueva partida", 29, Gold, TextAnchor.MiddleCenter, false);
                break;
            }
        }
    }
}
