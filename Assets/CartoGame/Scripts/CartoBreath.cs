using UnityEngine;

/// <summary>
/// Respiracion: despues de la animacion mueve un poco el torso, el pecho, los hombros y el cuello
/// para que un personaje quieto no parezca una estatua. Corriendo casi no se nota.
/// Solo funciona con modelos humanoides (usa los huesos del Avatar).
/// </summary>
public class CartoBreath : MonoBehaviour
{
    public float breathsPerSecond = 0.26f;
    public float chestAngle = 1.7f;        // grados que se abre el pecho al inhalar
    public float chestScale = 0.02f;       // cuanto se hincha el pecho
    public float shoulderLift = 2.4f;      // grados que suben los hombros
    public float sway = 0.9f;              // balanceo lento del peso del cuerpo

    /// <summary>Hueso con su pose de animacion recordada, para no acumular el giro si el Animator no lo reescribe.</summary>
    class Bone
    {
        public Transform t;
        public Quaternion pose;
        public Quaternion written;
        public bool used;

        public Quaternion Pose()
        {
            if (!used || t.localRotation != written) pose = t.localRotation;   // la animacion lo ha movido
            used = true;
            return pose;
        }

        /// <summary>Gira el hueso 'angle' grados alrededor de un eje del mundo, partiendo de su pose animada.</summary>
        public void Turn(float angle, Vector3 worldAxis)
        {
            Quaternion parent = t.parent != null ? t.parent.rotation : Quaternion.identity;
            t.rotation = Quaternion.AngleAxis(angle, worldAxis) * (parent * Pose());
            written = t.localRotation;
        }
    }

    Bone spine, chest, neck, leftShoulder, rightShoulder;
    Vector3 chestBaseScale = Vector3.one;
    Vector3 lastPos;
    float phase;
    float weight = 1f;

    void Start()
    {
        Animator anim = GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman || anim.avatar == null)
        {
            enabled = false;
            return;
        }
        spine = Find(anim, HumanBodyBones.Spine);
        chest = Find(anim, HumanBodyBones.Chest);
        neck = Find(anim, HumanBodyBones.Neck);
        leftShoulder = Find(anim, HumanBodyBones.LeftShoulder);
        rightShoulder = Find(anim, HumanBodyBones.RightShoulder);
        if (chest == null)
        {
            chest = spine;
            spine = null;
        }
        if (chest == null)
        {
            enabled = false;
            return;
        }
        chestBaseScale = chest.t.localScale;
        lastPos = transform.position;
        phase = Random.value * 6.28f;
    }

    static Bone Find(Animator anim, HumanBodyBones id)
    {
        Transform t = anim.GetBoneTransform(id);
        if (t == null) return null;
        Bone b = new Bone();
        b.t = t;
        return b;
    }

    void OnDisable()
    {
        if (chest != null && chest.t != null) chest.t.localScale = chestBaseScale;
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            // Quieto respira hondo; en movimiento, apenas.
            float speed = (transform.position - lastPos).magnitude / dt;
            lastPos = transform.position;
            weight = Mathf.MoveTowards(weight, speed > 0.5f ? 0.25f : 1f, dt * 2.5f);
        }

        float t = Time.time * breathsPerSecond * 6.2832f + phase;
        float s = Mathf.Sin(t);              // +1 pulmones llenos, -1 vacios
        float fill = s * 0.5f + 0.5f;
        Vector3 right = transform.right;
        Vector3 fwd = transform.forward;

        // De padre a hijo: cada hueso parte de la rotacion ya corregida de su padre.
        if (spine != null) spine.Turn(Mathf.Sin(t * 0.37f) * sway * weight, fwd);
        chest.Turn(-chestAngle * s * weight, right);
        chest.t.localScale = chestBaseScale * (1f + chestScale * fill * weight);
        if (leftShoulder != null) leftShoulder.Turn(-shoulderLift * fill * weight, fwd);
        if (rightShoulder != null) rightShoulder.Turn(shoulderLift * fill * weight, fwd);
        // El cuello compensa parte del giro del pecho: la cabeza se queda estable.
        if (neck != null) neck.Turn(chestAngle * s * weight * 0.7f, right);
    }
}
