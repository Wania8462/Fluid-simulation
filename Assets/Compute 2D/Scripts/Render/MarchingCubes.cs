using System;
using System.Collections.Generic;
using UnityEngine;

public class MarchingCubes : MonoBehaviour
{
    [SerializeField] private int gridLength;
    [SerializeField] private float isoLevel = 1.0f;
    [SerializeField] private ComputeShader compute;
    [SerializeField] private Mesh[] marchingSquareMeshes = new Mesh[16];

    private readonly HashSet<Mesh> marchingSquareMeshSet = new();

    private ComputeBuffer gridDensitiesBuffer;
    private ComputeBuffer squareCasesBuffer;

    private int gridWidth;
    private int gridHeight;
    private int numSquares;
    private int classifySquaresKernel;
    private bool buffersInitialized;

    public void Setup()
    {
        Resolution resolution = Screen.currentResolution;
        gridWidth = Mathf.Max(2, resolution.width / gridLength);
        gridHeight = Mathf.Max(2, resolution.height / gridLength);

        BuildMeshSet();
        InitializeBuffers();
    }

    public void SetGridDensities(float[] densities)
    {
        if (!buffersInitialized)
            return;

        int expectedLength = gridWidth * gridHeight;
        if (densities == null || densities.Length != expectedLength)
        {
            Debug.LogWarning($"MarchingCubes: Expected {expectedLength} density values but got {densities?.Length ?? 0}.");
            return;
        }

        gridDensitiesBuffer.SetData(densities);
    }

    public void ClassifySquares()
    {
        if (!buffersInitialized)
            return;

        compute.SetInt("gridWidth", gridWidth);
        compute.SetInt("gridHeight", gridHeight);
        compute.SetFloat("isoLevel", isoLevel);
        compute.SetBuffer(classifySquaresKernel, "GridDensities", gridDensitiesBuffer);
        compute.SetBuffer(classifySquaresKernel, "SquareCaseIndices", squareCasesBuffer);

        compute.GetKernelThreadGroupSizes(classifySquaresKernel, out uint threadGroupSizeX, out _, out _);
        int groupCountX = Mathf.CeilToInt(numSquares / (float)threadGroupSizeX);
        compute.Dispatch(classifySquaresKernel, Mathf.Max(1, groupCountX), 1, 1);
    }

    public ComputeBuffer GetSquareCaseBuffer() => squareCasesBuffer;

    private void BuildMeshSet()
    {
        if (marchingSquareMeshes == null || marchingSquareMeshes.Length != 16)
            Array.Resize(ref marchingSquareMeshes, 16);

        marchingSquareMeshSet.Clear();

        for (int i = 0; i < marchingSquareMeshes.Length; i++)
        {
            if (marchingSquareMeshes[i] != null)
                marchingSquareMeshSet.Add(marchingSquareMeshes[i]);
        }

        if (marchingSquareMeshes.Length != 16 || marchingSquareMeshSet.Count != 16)
            Debug.LogWarning($"MarchingCubes: Expected 16 unique meshes for marching squares, found {marchingSquareMeshSet.Count}.");
    }

    private void InitializeBuffers()
    {
        buffersInitialized = false;

        if (compute == null)
            return;

        classifySquaresKernel = compute.FindKernel("ClassifySquares");

        int gridPointCount = gridWidth * gridHeight;
        numSquares = (gridWidth - 1) * (gridHeight - 1);

        gridDensitiesBuffer?.Release();
        squareCasesBuffer?.Release();

        gridDensitiesBuffer = new ComputeBuffer(gridPointCount, sizeof(float));
        squareCasesBuffer = new ComputeBuffer(numSquares, sizeof(int));

        buffersInitialized = true;
    }

    private void OnDestroy()
    {
        gridDensitiesBuffer?.Release();
        squareCasesBuffer?.Release();
    }
}
