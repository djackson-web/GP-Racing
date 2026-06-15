using UnityEngine;

public class FacePlayer : MonoBehaviour
{
    [SerializeField] private float _rotationSpeed = 5f;
    [SerializeField] private float _maxRotationAngle = 90f;

    private Transform _player;
    private float _originalRotationY;
    private float _currentDelta;

    private void Start()
    {
        _player = GameObject.Find("Player").transform;
        _originalRotationY = transform.eulerAngles.y;
        _currentDelta = 0f;
    }

    private void Update()
    {
        if (_player == null)
        {
            return;
        }

        Vector3 direction = _player.position - transform.position;
        direction.y = 0;

        float targetAngle = Quaternion.LookRotation(direction).eulerAngles.y;
        float targetDelta = Mathf.DeltaAngle(_originalRotationY, targetAngle);

        targetDelta = Mathf.Clamp(targetDelta, -_maxRotationAngle, _maxRotationAngle);
        _currentDelta = Mathf.LerpAngle(_currentDelta, targetDelta, _rotationSpeed * Time.deltaTime);

        transform.rotation = Quaternion.Euler(0, _originalRotationY + _currentDelta, 0);
    }
}
