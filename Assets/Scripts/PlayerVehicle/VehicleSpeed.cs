using UnityEngine;

public class VehicleSpeed : MonoBehaviour
{
    public const float MilesPerHourConversion = 2.23694f;

    private PlayerController _playerController;

    public float SpeedMilesPerHour { get; private set; }

    private void Awake()
    {
        _playerController = GetComponent<PlayerController>();
        if (_playerController == null)
        {
            _playerController = FindAnyObjectByType<PlayerController>();
        }
    }

    private void Update()
    {
        SpeedMilesPerHour = CalculateSpeedInMilesPerHour();
    }

    private float CalculateSpeedInMilesPerHour()
    {
        if (_playerController == null)
        {
            return 0f;
        }

        return _playerController.CurrentSpeed;
    }
}
