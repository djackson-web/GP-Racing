using System;
using UnityEngine;

/// <summary>
/// Pairs a prefab with a relative selection weight for use in a weighted random palette.
/// Weights are normalised across all entries in the palette at sample time.
/// </summary>
[Serializable]
public class WeightedPrefab
{
    /// <summary>The prefab to instantiate when this entry is selected.</summary>
    public GameObject Prefab = null;

    /// <summary>
    /// Relative probability that this entry is selected from the palette.
    /// Normalised across all palette entries at sample time.
    /// </summary>
    [Range(0f, 1f)]
    public float Weight = 1f;
}
