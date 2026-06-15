#nullable enable
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Speedometer : MonoBehaviour
{
    private const float RedlineThreshold = 0.95f;
    private const float MaximumFillAmount = 0.75f;

    [SerializeField] private VehicleSpeed _vehicleSpeed = null!;
    [SerializeField] private GearSystem _gearSystem = null!;
    [SerializeField] private Image _barFill = null!;
    [SerializeField] private Image _redlineOverlay = null!;
    [SerializeField] private TextMeshProUGUI _speedText = null!;

    private void Update()
    {
        float currentSpeed = _vehicleSpeed.SpeedMilesPerHour;
        float speedFraction = CalculateSpeedFraction(currentSpeed);

        UpdateBarFill(speedFraction);
        UpdateRedlineOverlay(speedFraction);
        UpdateSpeedText(currentSpeed);
    }

    private float CalculateSpeedFraction(float speedMilesPerHour)
    {
        return Mathf.InverseLerp(
            _gearSystem.CurrentGearMinimumSpeed,
            _gearSystem.CurrentGearMaximumSpeed,
            speedMilesPerHour
        );
    }

    private void UpdateBarFill(float speedFraction)
    {
        _barFill.fillAmount = Mathf.Clamp01(speedFraction) * MaximumFillAmount;
    }

    private void UpdateRedlineOverlay(float speedFraction)
    {
        bool isInRedline = speedFraction >= RedlineThreshold;

        if (isInRedline)
        {
            float redlineFraction = Mathf.InverseLerp(RedlineThreshold, 1f, speedFraction);
            _redlineOverlay.fillAmount = Mathf.Lerp(RedlineThreshold * MaximumFillAmount, MaximumFillAmount, redlineFraction);
        }
        else
        {
            _redlineOverlay.fillAmount = 0f;
        }
    }

    private void UpdateSpeedText(float speedMilesPerHour)
    {
        _speedText.text = Mathf.RoundToInt(speedMilesPerHour).ToString();
    }
}
