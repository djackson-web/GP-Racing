using UnityEngine;

public class GearSystem : MonoBehaviour
{
    private const int LowestGear = 1;
    private const int HighestGear = 2;
    private const float LowGearMinimumSpeed = 0f;
    private const float LowGearMaximumSpeed = 100f;
    private const float HighGearMinimumSpeed = 100f;
    private const float HighGearMaximumSpeed = 212f;

    public int CurrentGear { get; private set; } = 1;
    public float CurrentGearMinimumSpeed { get; private set; } = LowGearMinimumSpeed;
    public float CurrentGearMaximumSpeed { get; private set; } = LowGearMaximumSpeed;

    public void ShiftUp()
    {
        if (CurrentGear >= HighestGear)
        {
            return;
        }

        CurrentGear++;
        UpdateGearRange();
    }

    public void ShiftDown()
    {
        if (CurrentGear <= LowestGear)
        {
            return;
        }

        CurrentGear--;
        UpdateGearRange();
    }

    public void ResetToFirstGear()
    {
        CurrentGear = LowestGear;
        UpdateGearRange();
    }

    private void UpdateGearRange()
    {
        if (CurrentGear == LowestGear)
        {
            CurrentGearMinimumSpeed = LowGearMinimumSpeed;
            CurrentGearMaximumSpeed = LowGearMaximumSpeed;
        }
        else
        {
            CurrentGearMinimumSpeed = HighGearMinimumSpeed;
            CurrentGearMaximumSpeed = HighGearMaximumSpeed;
        }
    }
}
