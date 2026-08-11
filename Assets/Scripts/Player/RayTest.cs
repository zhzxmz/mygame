using UnityEngine;

public class RayTest : MonoBehaviour
{
    public Camera playerCamera;
    public float distance = 5f;

    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            Detect();
        }
    }

    void Detect()
{
    Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));

    Debug.DrawRay(ray.origin, ray.direction * distance, Color.red, 0.1f);

    RaycastHit[] hits = Physics.RaycastAll(ray, distance);

    Debug.Log("---- RaycastAll ----");

    foreach (var h in hits)
    {
        Debug.Log(
            $"命中物体: {h.collider.gameObject.name} | " +
            $"Collider: {h.collider.GetType().Name} | " +
            $"距离: {h.distance}"
        );
    }
}
}

