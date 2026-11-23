using UnityEngine;

public sealed class GestureDebugBillboard : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera targetCamera = Camera.main;
        if (targetCamera == null)
            return;

        Vector3 direction = targetCamera.transform.position - transform.position;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }
}
