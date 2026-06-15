using UnityEngine;

/// <summary>
/// Controls the third-person chase camera that follows the player along the spline.
/// Positioned behind and above the target each frame, with rotation lag for a
/// smoother feel. At higher speeds, applies three escalating effects:
/// widening field of view, forward push, and screen shake.
/// </summary>
public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField] private PlayerController _playerController;
    [SerializeField] private float _behindDistance = 4f;
    [SerializeField] private float _heightOffset = 1.5f;
    [SerializeField] private float _rotationLagSpeed = 5f;

    [Header("Speed Effects")]
    [SerializeField] private float _baseFieldOfView = 60f;
    [SerializeField] private float _maximumFieldOfView = 85f;
    [SerializeField] private float _fieldOfViewLerpSpeed = 3f;
    [SerializeField] private float _maximumShakeAmount = 0.02f;
    [SerializeField] private float _shakeFrequency = 20f;
    [SerializeField] private float _maximumPushForward = 1f;

    private Camera _mainCamera;
    private Quaternion _currentRotation;
    private float _shakeTime;

    private void Start()
    {
        _mainCamera = GetComponent<Camera>();
        _currentRotation = _target.rotation;
        Vector3 offset = _currentRotation * new Vector3(0f, _heightOffset, -_behindDistance);
        transform.position = _target.position + offset;
        transform.rotation = _currentRotation;
    }

    private void LateUpdate()
    {
        _currentRotation = Quaternion.Lerp(_currentRotation, _target.rotation, _rotationLagSpeed * Time.deltaTime);

        float speedFraction = _playerController.CurrentSpeed / _playerController.TopSpeed;

        float pushForward = _maximumPushForward * speedFraction;
        Vector3 offset = _currentRotation * new Vector3(0f, _heightOffset, -(_behindDistance - pushForward));
        transform.position = _target.position + offset;
        transform.rotation = _currentRotation;

        ApplyFieldOfView(speedFraction);
        ApplyShake(speedFraction);
    }

    private void ApplyFieldOfView(float speedFraction)
    {
        float targetFieldOfView = Mathf.Lerp(_baseFieldOfView, _maximumFieldOfView, speedFraction);
        _mainCamera.fieldOfView = Mathf.Lerp(_mainCamera.fieldOfView, targetFieldOfView, _fieldOfViewLerpSpeed * Time.deltaTime);
    }

    private void ApplyShake(float speedFraction)
    {
        // Quadratic scale so shake only becomes noticeable near top speed
        float shakeAmount = _maximumShakeAmount * speedFraction * speedFraction;
        _shakeTime += Time.deltaTime * _shakeFrequency;
        float shakeOffsetX = Mathf.Sin(_shakeTime * 1.3f) * shakeAmount;
        float shakeOffsetY = Mathf.Sin(_shakeTime * 0.7f) * shakeAmount;
        transform.position += transform.right * shakeOffsetX + transform.up * shakeOffsetY;
    }
}
