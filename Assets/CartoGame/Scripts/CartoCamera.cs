using UnityEngine;

/// <summary>Camara en tercera persona que orbita alrededor del cartografo.</summary>
public class CartoCamera : MonoBehaviour
{
    public float distance = 6f;
    public float minDistance = 2.5f;
    public float maxDistance = 9f;
    public float height = 1.7f;
    public float sensitivity = 2.2f;
    public float minPitch = -20f;
    public float maxPitch = 65f;
    public float yaw = 0f;
    public float pitch = 18f;

    float currentDistance;

    void Start()
    {
        currentDistance = distance;
    }

    void LateUpdate()
    {
        CartoPlayer player = CartoPlayer.Instance;
        if (player == null) return;

        if (!CartoHUD.Blocking && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 look = CartoInput.Look;
            yaw += look.x * sensitivity;
            pitch = Mathf.Clamp(pitch - look.y * sensitivity, minPitch, maxPitch);
            distance = Mathf.Clamp(distance - CartoInput.Scroll * 0.8f, minDistance, maxDistance);
        }

        Vector3 pivot = player.transform.position + Vector3.up * height;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 back = rot * Vector3.back;

        // Evita que la camara atraviese paredes y suelo.
        float target = distance;
        RaycastHit[] hits = Physics.SphereCastAll(pivot, 0.25f, back, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider c = hits[i].collider;
            if (c == null || c is CharacterController) continue;
            if (hits[i].distance <= 0.001f) continue;
            if (hits[i].distance < target) target = hits[i].distance;
        }
        target = Mathf.Max(0.8f, target - 0.1f);
        if (target < currentDistance) currentDistance = target;
        else currentDistance = Mathf.Lerp(currentDistance, target, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));

        transform.position = pivot + back * currentDistance;
        transform.rotation = rot;
    }

    public Vector3 PlanarForward
    {
        get { return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; }
    }
}
