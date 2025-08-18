using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FrustemPreviewMono : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        var list = FrustemPreviewFeature.FrustemPreviewPass.VolumePositions;
        foreach (var val in list)
        {
            Gizmos.DrawSphere(val, 0.1f);
        }
    }
}
