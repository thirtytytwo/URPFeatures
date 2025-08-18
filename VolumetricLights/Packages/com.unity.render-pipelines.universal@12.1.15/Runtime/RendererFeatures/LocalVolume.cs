using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LocalVolume : MonoBehaviour
{
    public static List<LocalVolume> AllVolumes = new List<LocalVolume>();

    public Matrix4x4 VolumeMatrix;
    [Header("介质物理量")] 
    public Color OutScatter = Color.white;
    public Color Emission = Color.black;
    [Range(0.0f, 1.0f)]public float Extinction = 0.0f;
    [Range(-1.0f, 1.0f)] public float PhaseG = 0.0f;

    private void Start()
    {
        AllVolumes.Add(this);
    }

    private void Update()
    {
        UpdateMatrix();
    }

    private void UpdateMatrix()
    {
        Vector4 scale = transform.transform.localScale * 0.5f;
        var world2Local = transform.worldToLocalMatrix;
        var ortho = Matrix4x4.Ortho(-scale.x, scale.x, -scale.y, scale.y, -scale.z, scale.z);
        var scaleMatrix = Matrix4x4.Scale(scale);
        VolumeMatrix =  ortho * scaleMatrix * world2Local;
    }

    public Vector4 GetOutScatterAndExtinction()
    {
        return new Vector4(OutScatter.r, OutScatter.g, OutScatter.b, Extinction);
    }

    public Vector4 GetEmissionAndPhaseG()
    {
        return new Vector4(Emission.r, Emission.g, Emission.b, PhaseG);
    }
}
