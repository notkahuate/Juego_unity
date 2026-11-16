using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interfaz del juego dibujada con IMGUI (no necesita Canvas ni paquetes extra):
/// vida, XP, oro, cristales, armas, barras de enemigos, altar de mejoras y pantallas.
/// </summary>
public class CartoHUD : MonoBehaviour
{
    public enum Overlay { None, Intro, Pause, Death, Victory, Cinematic }

    struct Floater
    {
        public Vector3 pos;
        public string text;
        public Color color;
        public float born;
    }

    static Overlay overlay = Overlay.None;
    static string toast = "";
    static float toastUntil;
    static string prompt = "";
    static int promptFrame = -10;
    static float flashUntil;
    static bool cursorFree;   // Ctrl: raton libre y camara quieta, sin pausar
    static float fadeStart = -10f;   // fundido desde negro al terminar una cinematica
    static readonly List<Floater> floaters = new List<Floater>();

    GUIStyle style;
    float scale = 1f;

    public static bool OverlayActive { get { return overlay != Overlay.None; } }
    public static bool Blocking { get { return overlay != Overlay.None || CartoShop.IsOpen; } }
    public static bool CursorFree { get { return cursorFree; } }
    /// <summary>La pantalla actual tiene el juego detenido (Time.timeScale = 0).</summary>
    public static bool PausesTime { get { return overlay == Overlay.Intro || overlay == Overlay.Pause || overlay == Overlay.Victory; } }

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

    public static void AddFloater(Vector3 worldPos, string text, Color color)
    {
        Floater f = new Floater();
        f.pos = worldPos;
        f.text = text;
        f.color = color;
        f.born = Time.time;
        floaters.Add(f);
        if (floaters.Count > 40) floaters.RemoveAt(0);
    }

    public static void ShowOverlay(Overlay o)
    {
        overlay = o;
        if (o == Overlay.Intro || o == Overlay.Pause || o == Overlay.Victory) Time.timeScale = 0f;
        else Time.timeScale = 1f;
        ApplyCursor();
    }

    /// <summary>
    /// Unico sitio que decide el estado del raton: libre en pausa o si se libero con Ctrl;
    /// bloqueado en el resto de casos. La camara solo gira con el raton bloqueado.
    /// </summary>
    static void ApplyCursor()
    {
        bool free = overlay == Overlay.Pause || (overlay == Overlay.None && cursorFree);
        Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = free;
    }

    // ------------------------------------------------------------ logica

    void Awake()
    {
        floaters.Clear();
        CartoShop.IsOpen = false;
        cursorFree = false;
        if (overlay == Overlay.Pause || overlay == Overlay.Death || overlay == Overlay.Cinematic) overlay = Overlay.None;
    }

    void Update()
    {
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

            case Overlay.Intro:
                if (CartoInput.ConfirmDown)
                {
                    CartoGameState.introShown = true;
                    ShowOverlay(Overlay.None);
                    Toast("Nexo del Tiempo  —  Cruza un portal para viajar a una era", 5f);
                }
                break;

            case Overlay.Cinematic:
                if (CartoCinematic.Active == null) ShowOverlay(Overlay.None);
                else if (CartoInput.ConfirmDown) CartoCinematic.Active.Skip();
                break;

            case Overlay.Pause:
                if (CartoInput.PauseDown || CartoInput.ConfirmDown) ShowOverlay(Overlay.None);
                else if (CartoInput.NewGameDown)
                {
                    ShowOverlay(Overlay.None);
                    CartoGame.NewGame();
                }
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

    // ------------------------------------------------------------ dibujo

    static readonly Color Cyan = new Color(0.6f, 0.95f, 1f);
    static readonly Color Gold = new Color(1f, 0.85f, 0.3f);
    static readonly Color Dim = new Color(1f, 1f, 1f, 0.78f);
    static readonly Color Panel = new Color(0f, 0f, 0f, 0.45f);        // fondo de los grupos del HUD
    static readonly Color Card = new Color(1f, 1f, 1f, 0.07f);         // fondo de los paneles de menu
    static readonly GUIContent measure = new GUIContent();

    // Controles (tecla, accion). Se dibujan como tabla en la pantalla inicial y en la pausa.
    static readonly string[] ControlKeys =
    {
        "WASD", "Ratón", "Shift", "Espacio", "Clic izquierdo", "Clic derecho",
        "Q  /  R", "E", "1, 2, 3", "Ctrl", "Esc"
    };
    static readonly string[] ControlActions =
    {
        "Moverse", "Cámara (rueda = zoom)", "Correr", "Esquivar", "Ataque cuerpo a cuerpo", "Ataque a distancia",
        "Cambiar arma cercana / a distancia", "Interactuar (portal, altar, cristal)", "Comprar mejoras en el altar",
        "Liberar / bloquear cámara", "Pausa"
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
        DrawRect(r, new Color(0f, 0f, 0f, 0.78f));
        float b = Mathf.Max(1f, 2f * scale);
        DrawRect(new Rect(r.x + b, r.y + b, (r.width - 2f * b) * Mathf.Clamp01(pct), r.height - 2f * b), fill);
    }

    int FontSize(float size)
    {
        return Mathf.Max(13, Mathf.RoundToInt(size * scale));
    }

    /// <summary>Texto con contorno oscuro en las cuatro diagonales, legible sobre fondos claros u oscuros.</summary>
    void Label(Rect r, string text, float size, Color color, TextAnchor anchor, bool wrap = true)
    {
        style.fontSize = FontSize(size);
        style.alignment = anchor;
        style.wordWrap = wrap;
        float o = Mathf.Max(1f, 1.6f * scale);
        style.normal.textColor = new Color(0f, 0f, 0f, 0.8f * color.a);
        GUI.Label(new Rect(r.x - o, r.y - o, r.width, r.height), text, style);
        GUI.Label(new Rect(r.x + o, r.y - o, r.width, r.height), text, style);
        GUI.Label(new Rect(r.x - o, r.y + o, r.width, r.height), text, style);
        GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, style);
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
        DrawRect(r, new Color(0f, 0f, 0f, backAlpha));
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
        Color white = Color.white;

        if (overlay == Overlay.Cinematic)
        {
            DrawCinematic(W, H);
            return;
        }

        DrawWorld(W, H);

        // Destello rojo al recibir dano
        float flash = flashUntil - Time.unscaledTime;
        if (flash > 0f) DrawRect(new Rect(0, 0, W, H), new Color(0.8f, 0f, 0f, 0.3f * Mathf.Clamp01(flash / 0.35f)));

        // --- Arriba a la izquierda: vida y experiencia ---
        CartoPlayer player = CartoPlayer.Instance;
        float hp = player != null ? player.health.current : CartoGameState.health;
        float maxHp = CartoGameState.maxHealth;
        DrawRect(new Rect(20 * s, 18 * s, 440 * s, 92 * s), Panel);
        Rect hpRect = new Rect(30 * s, 26 * s, 420 * s, 40 * s);
        Bar(hpRect, maxHp > 0f ? hp / maxHp : 0f, new Color(0.85f, 0.18f, 0.2f));
        Label(hpRect, "Vida  " + Mathf.CeilToInt(hp) + " / " + Mathf.RoundToInt(maxHp), 23, white, TextAnchor.MiddleCenter, false);
        Rect xpRect = new Rect(30 * s, 72 * s, 420 * s, 30 * s);
        Bar(xpRect, (float)CartoGameState.xp / CartoGameState.XpToNext, new Color(0.25f, 0.5f, 0.95f));
        Label(xpRect, "Nivel " + CartoGameState.level + "     XP " + CartoGameState.xp + " / " + CartoGameState.XpToNext, 18, white, TextAnchor.MiddleCenter, false);

        // --- Arriba a la derecha: oro y cristales ---
        DrawRect(new Rect(W - 330 * s, 18 * s, 310 * s, 92 * s), Panel);
        Label(new Rect(W - 320 * s, 22 * s, 286 * s, 44 * s), "Oro  " + CartoGameState.gold, 31, Gold, TextAnchor.MiddleRight, false);
        Label(new Rect(W - 320 * s, 66 * s, 286 * s, 38 * s), "Cristales  " + CartoGameState.CrystalCount + " / 2", 24, Cyan, TextAnchor.MiddleRight, false);

        // --- Arriba al centro: objetivo (baja una fila si la pantalla es estrecha) y jefe ---
        float topY = W >= 1750f * s ? 20f * s : 120f * s;
        CartoGame game = CartoGame.Instance;
        if (game != null && !string.IsNullOrEmpty(game.objective))
            Banner(W * 0.5f, topY, 40 * s, game.objective, 21, white, 0.45f);

        CartoEnemy boss = null;
        for (int i = 0; i < CartoEnemy.All.Count; i++)
        {
            CartoEnemy e = CartoEnemy.All[i];
            if (e != null && e.health.isBoss && e.Aggro && !e.IsDead) { boss = e; break; }
        }
        if (boss != null)
        {
            Rect br = new Rect(W * 0.5f - 380 * s, topY + 90 * s, 760 * s, 32 * s);
            Label(new Rect(br.x, br.y - 40 * s, br.width, 38 * s), boss.health.displayName, 27, new Color(1f, 0.8f, 0.5f), TextAnchor.MiddleCenter, false);
            Bar(br, boss.health.Percent, new Color(0.75f, 0.2f, 0.85f));
        }

        // --- Abajo a la derecha: armas equipadas ---
        CartoWeaponLibrary lib = CartoWeaponLibrary.Instance;
        if (lib != null)
        {
            CartoWeaponDef m = lib.Get(CartoGameState.meleeId);
            CartoWeaponDef r = lib.Get(CartoGameState.rangedId);
            string mt = m != null ? m.displayName + "  Nv." + (CartoGameState.WeaponLevel(m.id) + 1) : "—";
            string rt = r != null ? r.displayName + "  Nv." + (CartoGameState.WeaponLevel(r.id) + 1) : "(ninguna)";
            Rect wp = new Rect(W - 560 * s, H - 106 * s, 540 * s, 88 * s);
            DrawRect(wp, Panel);
            Rect row1 = new Rect(wp.x + 16 * s, wp.y + 6 * s, wp.width - 32 * s, 38 * s);
            Rect row2 = new Rect(wp.x + 16 * s, wp.y + 44 * s, wp.width - 32 * s, 38 * s);
            Label(row1, "Clic izq.", 19, Dim, TextAnchor.MiddleLeft, false);
            Label(row1, mt, 25, white, TextAnchor.MiddleRight, false);
            Label(row2, "Clic der.", 19, Dim, TextAnchor.MiddleLeft, false);
            Label(row2, rt, 25, r != null ? white : Dim, TextAnchor.MiddleRight, false);
        }

        // --- Abajo a la izquierda: recordatorio de controles en dos lineas ---
        Rect cp = new Rect(20 * s, H - 96 * s, 720 * s, 78 * s);
        DrawRect(cp, Panel);
        Label(new Rect(cp.x + 16 * s, cp.y + 6 * s, cp.width - 32 * s, 32 * s),
            "WASD mover     Shift correr     Espacio esquivar     E interactuar", 19, Dim, TextAnchor.MiddleLeft, false);
        Label(new Rect(cp.x + 16 * s, cp.y + 40 * s, cp.width - 32 * s, 32 * s),
            "Q / R cambiar arma     Ctrl liberar/bloquear cámara     Esc pausa", 19, Dim, TextAnchor.MiddleLeft, false);
        if (cursorFree && overlay == Overlay.None)
        {
            Rect ft = new Rect(20 * s, cp.y - 46 * s, 520 * s, 38 * s);
            DrawRect(ft, new Color(0.25f, 0.18f, 0f, 0.8f));
            Label(ft, "CÁMARA LIBRE  —  Ctrl para bloquearla", 19, Gold, TextAnchor.MiddleCenter, false);
        }

        // --- Mensajes ---
        if (Time.unscaledTime < toastUntil && !string.IsNullOrEmpty(toast) && overlay == Overlay.None)
            Banner(W * 0.5f, H * 0.24f, 62 * s, toast, 29, Gold, 0.6f);
        if (Time.frameCount - promptFrame <= 1 && overlay == Overlay.None && !CartoShop.IsOpen)
            Banner(W * 0.5f, H * 0.72f, 56 * s, prompt, 27, white, 0.68f);

        if (CartoShop.IsOpen && overlay == Overlay.None) DrawShop(W, H);
        if (overlay != Overlay.None) DrawOverlay(W, H);

        // Fundido desde negro al terminar una cinematica
        float fade = 1f - (Time.unscaledTime - fadeStart) / 0.6f;
        if (fade > 0f) DrawRect(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, Mathf.Clamp01(fade)));
    }

    /// <summary>Cinematica: bandas negras, texto narrativo abajo y fundidos entre planos. Sin HUD.</summary>
    void DrawCinematic(float W, float H)
    {
        float s = scale;
        CartoCinematic c = CartoCinematic.Active;
        float black = c != null ? c.Blackness : 1f;
        if (black > 0f) DrawRect(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, black));
        float top = H * 0.09f, bottom = H * 0.21f;
        DrawRect(new Rect(0, 0, W, top), Color.black);
        DrawRect(new Rect(0, H - bottom, W, bottom), Color.black);
        if (c != null)
        {
            float colW = Mathf.Min(1300f * s, W - 80f * s);
            Label(new Rect(W * 0.5f - colW * 0.5f, H - bottom + 26 * s, colW, bottom - 70 * s), c.Caption, 30,
                new Color(1f, 1f, 1f, 1f - black), TextAnchor.UpperCenter);
        }
        Label(new Rect(W - 340 * s, H - 48 * s, 320 * s, 36 * s), "ENTER  saltar", 20, Dim, TextAnchor.MiddleRight, false);
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
            Vector3 sp = cam.WorldToScreenPoint(e.transform.position + Vector3.up * (e.Height + 0.35f));
            if (sp.z <= 0f || sp.z > 40f) continue;
            float w = 124f * s, h = 15f * s;
            Bar(new Rect(sp.x - w * 0.5f, H - sp.y - h, w, h), e.health.Percent, new Color(0.9f, 0.25f, 0.2f));
        }

        // Numeros flotantes
        for (int i = floaters.Count - 1; i >= 0; i--)
        {
            Floater f = floaters[i];
            float age = Time.time - f.born;
            if (age > 1f || age < 0f)
            {
                floaters.RemoveAt(i);
                continue;
            }
            Vector3 sp = cam.WorldToScreenPoint(f.pos + Vector3.up * (age * 1.4f));
            if (sp.z <= 0f) continue;
            Color c = f.color;
            c.a = Mathf.Clamp01(1.6f - age * 1.6f);
            Label(new Rect(sp.x - 150 * s, H - sp.y - 20 * s, 300 * s, 40 * s), f.text, 28, c, TextAnchor.MiddleCenter, false);
        }
    }

    void DrawShop(float W, float H)
    {
        float s = scale;
        Rect box = new Rect(W * 0.5f - 560 * s, H * 0.5f - 245 * s, 1120 * s, 490 * s);
        DrawRect(box, new Color(0.05f, 0.03f, 0.12f, 0.94f));
        DrawRect(new Rect(box.x, box.y, box.width, 6 * s), Cyan);
        Label(new Rect(box.x, box.y + 22 * s, box.width, 50 * s), "ALTAR DE MEJORAS", 38, Cyan, TextAnchor.MiddleCenter, false);
        Label(new Rect(box.x, box.y + 78 * s, box.width, 38 * s), "Oro disponible: " + CartoGameState.gold, 27, Gold, TextAnchor.MiddleCenter, false);
        for (int n = 1; n <= 3; n++)
        {
            Rect row = new Rect(box.x + 40 * s, box.y + (144 + (n - 1) * 74) * s, box.width - 80 * s, 60 * s);
            DrawRect(row, new Color(1f, 1f, 1f, 0.09f));
            Label(new Rect(row.x + 20 * s, row.y, row.width - 40 * s, row.height), CartoShop.OptionText(n), 23, Color.white, TextAnchor.MiddleLeft, false);
        }
        Label(new Rect(box.x + 40 * s, box.yMax - 104 * s, box.width - 80 * s, 36 * s),
            "Pulsa 1, 2 o 3 para comprar. Las mejoras de arma se aplican al arma equipada (Q / R para cambiarla).",
            20, Dim, TextAnchor.MiddleCenter);
        Label(new Rect(box.x, box.yMax - 56 * s, box.width, 38 * s), "[E] o [Esc]  Cerrar", 23, Color.white, TextAnchor.MiddleCenter, false);
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
            Label(new Rect(x + keyW + 24 * s, y, box.width - keyW - 72 * s, row), ControlActions[i], 21, Color.white, TextAnchor.MiddleLeft, false);
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

                Label(new Rect(x + 34 * s, y, w * 0.4f, row), d.displayName, 22, owned ? Color.white : locked, TextAnchor.MiddleLeft, false);
                Label(new Rect(x + w * 0.43f, y, w * 0.27f, row), d.ranged ? "a distancia" : "cuerpo a cuerpo", 19,
                    owned ? Dim : locked, TextAnchor.MiddleLeft, false);
                string status = !owned ? "Bloqueada"
                    : "Nv." + (CartoGameState.WeaponLevel(d.id) + 1) + (equipped ? "  ·  equipada" : "");
                Label(new Rect(x, y, w, row), status, 21, !owned ? locked : (equipped ? Gold : Color.white), TextAnchor.MiddleRight, false);
                y += row;
            }
            y += 8 * s;
        }
    }

    void DrawOverlay(float W, float H)
    {
        float s = scale;
        DrawRect(new Rect(0, 0, W, H), new Color(0.02f, 0.01f, 0.06f, overlay == Overlay.Death ? 0.66f : 0.86f));
        Rect title = new Rect(0, H * 0.07f, W, 90 * s);
        Rect foot = new Rect(0, H * 0.87f, W, 54 * s);
        float colW = Mathf.Min(1240f * s, W - 80f * s);            // columna de texto centrada
        Rect body = new Rect(W * 0.5f - colW * 0.5f, H * 0.34f, colW, H * 0.45f);

        switch (overlay)
        {
            case Overlay.Intro:
            {
                Label(title, "EL CARTÓGRAFO DEL TIEMPO", 64, Cyan, TextAnchor.MiddleCenter, false);
                Label(new Rect(body.x, H * 0.18f, body.width, 80 * s),
                    "Un desastre temporal ha mezclado todas las eras. Tú, el último cartógrafo, debes viajar " +
                    "a cada época y recuperar los cristales del tiempo.",
                    25, Color.white, TextAnchor.UpperCenter);
                float cw = Mathf.Min(760f * s, W - 80f * s);
                DrawControls(new Rect(W * 0.5f - cw * 0.5f, H * 0.29f, cw, 424 * s));
                Label(new Rect(body.x, H * 0.71f, body.width, 80 * s),
                    "Derrota enemigos para ganar oro y experiencia, consigue las armas de cada era " +
                    "y mejóralas en el Altar del Nexo.",
                    23, Dim, TextAnchor.UpperCenter);
                Label(foot, "Pulsa ENTER para comenzar", 32, Gold, TextAnchor.MiddleCenter, false);
                break;
            }

            case Overlay.Pause:
            {
                Label(title, "PAUSA", 64, Cyan, TextAnchor.MiddleCenter, false);
                Label(new Rect(0, H * 0.175f, W, 44 * s),
                    "Nivel " + CartoGameState.level + "        Oro " + CartoGameState.gold +
                    "        Cristales " + CartoGameState.CrystalCount + " / 2        Enemigos derrotados " + CartoGameState.kills,
                    27, Gold, TextAnchor.MiddleCenter, false);
                // Dos columnas: controles a la izquierda, coleccion de armas a la derecha.
                float total = Mathf.Min(1520f * s, W - 60f * s);
                float gap = 30f * s;
                float leftW = (total - gap) * 0.44f;
                float x0 = W * 0.5f - total * 0.5f;
                float y0 = H * 0.25f;
                DrawControls(new Rect(x0, y0, leftW, 424 * s));
                DrawCollection(new Rect(x0 + leftW + gap, y0, total - gap - leftW, 424 * s));
                Label(foot, "ESC o ENTER  continuar          N  nueva partida", 29, Gold, TextAnchor.MiddleCenter, false);
                break;
            }

            case Overlay.Death:
            {
                bool atCheckpoint = CartoGame.Instance != null && CartoGame.Instance.HasCheckpoint;
                Label(new Rect(0, H * 0.2f, W, 90 * s), "HAS CAÍDO", 70, new Color(1f, 0.3f, 0.3f), TextAnchor.MiddleCenter, false);
                Label(new Rect(body.x, H * 0.38f, body.width, body.height),
                    (atCheckpoint
                        ? "El tiempo te devuelve al punto de control. Los enemigos que quedaban en pie se recuperan.\n"
                        : "El tiempo te devuelve al Nexo.\n") +
                    "Pierdes una cuarta parte de tu oro, pero conservas armas, nivel y cristales.",
                    29, Color.white, TextAnchor.UpperCenter);
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
                    29, Color.white, TextAnchor.UpperCenter);
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
