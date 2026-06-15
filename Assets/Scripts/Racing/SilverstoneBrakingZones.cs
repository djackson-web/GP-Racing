/// <summary>
/// Braking zones for the Silverstone GP layout, anchored to the real circuit.
/// Zone positions are arc-length fractions measured on the smoothed spline
/// curve (matching Unity's distance-normalised spline progress), named after
/// the real corners in lap order from the start line on the National straight.
/// Target speeds are the game car's steering-physics limit at each corner's
/// racing-line radius — real-world F1 speeds are not directly drivable because
/// the game car has no downforce model.
/// Tune individual corners here; skill and rubber-banding scale per driver.
/// </summary>
public static class SilverstoneBrakingZones
{
    public static readonly BrakingZone[] Zones =
    {
        new BrakingZone { cornerName = "Copse", entryProgress = 0.0355f, exitProgress = 0.0560f, targetSpeed = 128f },
        new BrakingZone { cornerName = "Maggotts", entryProgress = 0.1270f, exitProgress = 0.1310f, targetSpeed = 142f },
        new BrakingZone { cornerName = "Becketts", entryProgress = 0.1444f, exitProgress = 0.2060f, targetSpeed = 106f },
        new BrakingZone { cornerName = "Chapel", entryProgress = 0.2200f, exitProgress = 0.2270f, targetSpeed = 145f },
        new BrakingZone { cornerName = "Stowe", entryProgress = 0.3525f, exitProgress = 0.3830f, targetSpeed = 108f },
        new BrakingZone { cornerName = "Vale", entryProgress = 0.4395f, exitProgress = 0.4480f, targetSpeed = 60f },
        new BrakingZone { cornerName = "Club", entryProgress = 0.4525f, exitProgress = 0.4600f, targetSpeed = 68f },
        new BrakingZone { cornerName = "Club Exit", entryProgress = 0.4815f, exitProgress = 0.4875f, targetSpeed = 64f },
        new BrakingZone { cornerName = "Abbey", entryProgress = 0.5715f, exitProgress = 0.5840f, targetSpeed = 110f },
        new BrakingZone { cornerName = "Farm", entryProgress = 0.6050f, exitProgress = 0.6160f, targetSpeed = 135f },
        new BrakingZone { cornerName = "Village", entryProgress = 0.6520f, exitProgress = 0.6630f, targetSpeed = 62f },
        new BrakingZone { cornerName = "The Loop", entryProgress = 0.6760f, exitProgress = 0.6905f, targetSpeed = 61f },
        new BrakingZone { cornerName = "Aintree", entryProgress = 0.7095f, exitProgress = 0.7180f, targetSpeed = 96f },
        new BrakingZone { cornerName = "Brooklands", entryProgress = 0.8468f, exitProgress = 0.8680f, targetSpeed = 88f },
        new BrakingZone { cornerName = "Luffield", entryProgress = 0.8790f, exitProgress = 0.9090f, targetSpeed = 84f },
        new BrakingZone { cornerName = "Woodcote", entryProgress = 0.9530f, exitProgress = 0.9640f, targetSpeed = 156f }
    };
}
