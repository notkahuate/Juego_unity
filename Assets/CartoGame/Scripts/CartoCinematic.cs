using System;
using UnityEngine;

/// <summary>Un plano de cinematica: la camara viaja de 'from' a 'to' mirando a 'lookAt' mientras se lee 'text'.</summary>
[Serializable]
public class CartoShot
{
    public Vector3 from;
    public Vector3 to;
    public Vector3 lookAt;
    public float seconds = 5.5f;
    [TextArea] public string text = "";
}

/// <summary>
/// Cinematica de historia de una escena: una lista de planos con texto narrativo.
/// El constructor la guarda en el objeto "_CartoGame". CartoGame la lanza al empezar la partida
/// (Nexo) y la primera vez que se entra en cada era; CartoHUD dibuja el texto y la salta con Enter.
/// No usa Timeline ni paquetes extra: solo mueve la camara principal.
/// </summary>
public class CartoCinematic : MonoBehaviour
{
    /// <summary>La cinematica que se esta reproduciendo (null si no hay ninguna).</summary>
    public static CartoCinematic Active;

    public CartoShot[] shots = new CartoShot[0];

    const float Fade = 0.5f;   // fundido a negro al entrar y salir de cada plano

    int index;
    float t;
    Action onDone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void DomainReset()
    {
        Active = null;
    }

    public bool HasShots { get { return shots != null && shots.Length > 0; } }

    CartoShot Current { get { return shots[Mathf.Clamp(index, 0, shots.Length - 1)]; } }

    public string Caption { get { return HasShots ? Current.text : ""; } }

    /// <summary>Cuanto negro hay en pantalla (1 = todo negro) segun el fundido del plano actual.</summary>
    public float Blackness
    {
        get
        {
            if (!HasShots) return 1f;
            float fromStart = t / Fade;
            float toEnd = (Current.seconds - t) / Fade;
            return 1f - Mathf.Clamp01(Mathf.Min(fromStart, toEnd));
        }
    }

    /// <summary>Empieza la cinematica. 'done' se llama al terminar o al saltarla.</summary>
    public void Play(Action done)
    {
        if (!HasShots)
        {
            if (done != null) done();
            return;
        }
        index = 0;
        t = 0f;
        onDone = done;
        Active = this;
        CartoHUD.ShowOverlay(CartoHUD.Overlay.Cinematic);
        ApplyCamera();
    }

    public void Skip()
    {
        Finish();
    }

    void Finish()
    {
        if (Active != this) return;
        Active = null;
        Action done = onDone;
        onDone = null;
        CartoHUD.FadeFromBlack();
        if (done != null) done();
    }

    void LateUpdate()
    {
        if (Active != this) return;
        t += Time.unscaledDeltaTime;
        if (t >= Current.seconds)
        {
            index++;
            t = 0f;
            if (index >= shots.Length)
            {
                Finish();
                return;
            }
        }
        ApplyCamera();
    }

    void ApplyCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        CartoShot s = Current;
        Vector3 pos = Vector3.Lerp(s.from, s.to, Mathf.Clamp01(t / Mathf.Max(0.1f, s.seconds)));
        cam.transform.position = pos;
        Vector3 dir = s.lookAt - pos;
        if (dir.sqrMagnitude > 0.0001f) cam.transform.rotation = Quaternion.LookRotation(dir);
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
    }
}
