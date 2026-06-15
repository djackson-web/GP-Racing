using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : RacerBase
{
    private const float LowSpeedSteeringThreshold = 20f;
    private const float MphToMetersPerSecond = 0.44704f;

    [SerializeField] private float _topSpeed = 220f;
    [SerializeField] private float _acceleration = 25f;
    [SerializeField] private float _drag = 30f;
    [SerializeField] private float _brakeForce = 110f;
    [SerializeField] private float _steeringRate = 60f;
    [SerializeField] private float _minimumHighSpeedSteeringFactor = 0.35f;
    [SerializeField] private float _trackLimitSlowdown = 80f;
    [SerializeField] private float _trackLimitMinimumSpeed = 30f;
    [SerializeField] private float _engineBrakingForce = 60f;

    private GearSystem _gearSystem;
    private PlayerInput _playerInput;
    private InputAction _moveAction;
    private InputAction _shiftUpAction;
    private InputAction _shiftDownAction;

    private bool _isCrashed;

    private bool _isTrackingBrake;
    private float _brakeSessionStartSpeed;
    private float _brakeSessionStartTime;
    private float _brakeSessionDistanceMeters;

    public float TopSpeed
    {
        get { return _topSpeed; }
    }

    protected override void Awake()
    {
        base.Awake();
        _gearSystem = GetComponent<GearSystem>();
        _playerInput = GetComponent<PlayerInput>();
        _moveAction = _playerInput.actions["Move"];
        _shiftUpAction = _playerInput.actions["ShiftUp"];
        _shiftDownAction = _playerInput.actions["ShiftDown"];
    }

    protected override void Update()
    {
        if (_isCrashed)
        {
            currentSpeed = 0f;
            base.Update();
            return;
        }

        HandleGearInput();

        Vector2 input = _moveAction.ReadValue<Vector2>();
        float throttle = Mathf.Max(0f, input.y);
        float brake = Mathf.Max(0f, -input.y);

        ApplyThrottleAndBrake(throttle, brake);
        ApplyTrackLimitPenalty();
        TrackBrakeSession(brake);

        float steer = input.x;
        float lowSpeedFactor = Mathf.Clamp01(currentSpeed / LowSpeedSteeringThreshold);
        float highSpeedFactor = Mathf.Lerp(1f, _minimumHighSpeedSteeringFactor, currentSpeed / _topSpeed);
        heading += _steeringRate * steer * lowSpeedFactor * highSpeedFactor * Time.deltaTime;

        base.Update();
    }

    private void HandleGearInput()
    {
        if (_shiftUpAction.WasPressedThisFrame())
        {
            _gearSystem.ShiftUp();
        }

        if (_shiftDownAction.WasPressedThisFrame())
        {
            _gearSystem.ShiftDown();
        }
    }

    private void ApplyThrottleAndBrake(float throttle, float brake)
    {
        currentSpeed += _acceleration * throttle * Time.deltaTime;
        currentSpeed -= _brakeForce * brake * Time.deltaTime;

        float speedFraction = currentSpeed / _topSpeed;
        currentSpeed -= _drag * speedFraction * speedFraction * Time.deltaTime;

        if (currentSpeed > _gearSystem.CurrentGearMaximumSpeed)
        {
            currentSpeed -= _engineBrakingForce * Time.deltaTime;
        }

        currentSpeed = Mathf.Clamp(currentSpeed, 0f, _topSpeed);
    }

    private void ApplyTrackLimitPenalty()
    {
        if (IsAtTrackLimit)
        {
            currentSpeed -= _trackLimitSlowdown * Time.deltaTime;
            currentSpeed = Mathf.Max(_trackLimitMinimumSpeed, currentSpeed);
        }
    }

    private void TrackBrakeSession(float brake)
    {
        if (brake > 0f && !_isTrackingBrake)
        {
            _isTrackingBrake = true;
            _brakeSessionStartSpeed = currentSpeed;
            _brakeSessionStartTime = Time.time;
            _brakeSessionDistanceMeters = 0f;
        }

        if (_isTrackingBrake)
        {
            _brakeSessionDistanceMeters += currentSpeed * MphToMetersPerSecond * Time.deltaTime;

            bool brakeReleased = brake <= 0f;
            bool carStopped = currentSpeed <= 0f;

            if (brakeReleased || carStopped)
            {
                float duration = Time.time - _brakeSessionStartTime;
                Debug.Log($"Brake session — start: {_brakeSessionStartSpeed:F1} mph | end: {currentSpeed:F1} mph | time: {duration:F2}s | distance: {_brakeSessionDistanceMeters:F0}m");
                _isTrackingBrake = false;
            }
        }
    }

    public void SetCrashed(bool crashed)
    {
        _isCrashed = crashed;
    }
}
