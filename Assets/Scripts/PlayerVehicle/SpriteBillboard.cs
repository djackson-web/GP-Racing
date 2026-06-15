using UnityEngine;

public class SpriteBillboard : MonoBehaviour
{
    // Minimum squared magnitude to avoid dividing by near-zero when the camera is directly above
    private const float MinimumForwardMagnitudeSquared = 0.001f;

    private Camera _mainCamera;

    private void Start()
    {
        _mainCamera = Camera.main;
    }

    private void LateUpdate()
    {
        Vector3 cameraForwardFlat = _mainCamera.transform.forward;
        cameraForwardFlat.y = 0f;

        if (cameraForwardFlat.sqrMagnitude > MinimumForwardMagnitudeSquared)
        {
            transform.rotation = Quaternion.LookRotation(-cameraForwardFlat);
        }
    }
}
