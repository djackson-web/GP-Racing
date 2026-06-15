using System;
using UnityEngine;

[Serializable]
public class KerbZone
{
    [Range(0f, 1f)] public float startProgress;
    [Range(0f, 1f)] public float endProgress;
}