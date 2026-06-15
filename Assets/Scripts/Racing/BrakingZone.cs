/// <summary>
/// One authored braking zone: a corner (or corner complex) with a known
/// position on the track and the speed a perfect driver should carry through
/// it. The AI brakes so it reaches <see cref="targetSpeed"/> by
/// <see cref="entryProgress"/> and holds it until <see cref="exitProgress"/>.
/// </summary>
[System.Serializable]
public struct BrakingZone
{
    public string cornerName;

    /// <summary>Lap progress (0-1) where the corner begins; braking must be done here.</summary>
    public float entryProgress;

    /// <summary>Lap progress (0-1) where the corner ends and full throttle resumes.</summary>
    public float exitProgress;

    /// <summary>Corner speed in mph for a perfect driver; skill scales it down.</summary>
    public float targetSpeed;
}
