using System;
using System.Collections.Generic;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[BurstCompile]
public class GaussianSplatRasterizer : MonoBehaviour
{
    public string PointCloudPly;
    public Material splatMaterial;
    public float clippingRadius = 4.0f;

    public ComputeShader splattingCS;
    public ComputeShader sortingRoutine;

    // input file splat data is expected to be in this format
    public struct InputSplat
    {
        public Vector3 pos;
        public Vector3 nor;
        public Vector3 dc0;
        public Vector3 sh0, sh1, sh2, sh3, sh4, sh5, sh6, sh7, sh8, sh9, sh10, sh11, sh12, sh13, sh14;
        public float opacity;
        public Vector3 scale;
        public Quaternion rot;
    }

    public struct CameraData
    {
        public Vector3 pos;
        public Vector3 axisX, axisY, axisZ;
        public float fov;
    }

    NativeArray<InputSplat> SplatData;

    GraphicsBuffer GpuData;
    GraphicsBuffer GpuPositions;
    GraphicsBuffer GpuSortDistances;
    GraphicsBuffer GpuSortKeys;

    FfxParallelSort SorterFfx;
    FfxParallelSort.Args SorterFfxArgs;

    private int splatCount;
    private Bounds bounds;
    public NativeArray<InputSplat> splatData => SplatData;
    public GraphicsBuffer gpuSplatData => GpuData;
    public CameraData[] cameras;

    public static unsafe NativeArray<InputSplat> LoadPLYFile(string plyPath, float maxDist)
    {
        if (!File.Exists(plyPath))
        {
            throw new Exception("ERROR: file " + plyPath + " does not exist.");
        }

        int splatCount = 0;
        PLYFileReader.ReadFile(plyPath, out splatCount, out int vertexBytes, out var plyAttrNames, out var verticesRawData);
        if (UnsafeUtility.SizeOf<InputSplat>() != vertexBytes)
        {
            throw new Exception($"InputVertex size mismatch, we expect {UnsafeUtility.SizeOf<InputSplat>()} file has {vertexBytes}");
        }

        // Reorder spherical harmonics (SHs) to obtain accurate reflections
        NativeArray<float> floatData = verticesRawData.Reinterpret<float>(1);
        ReorderSHs(splatCount, (float*)floatData.GetUnsafePtr());

        // Remove splats deemed too far away
        RemoveDistantSplats(splatCount, maxDist, (float*)floatData.GetUnsafePtr());

        return verticesRawData.Reinterpret<InputSplat>(1);
    }

    [BurstCompile]
    static unsafe void ReorderSHs(int splatCount, float* data)
    {
        int sizeOfSplat = UnsafeUtility.SizeOf<InputSplat>() / 4;
        int shStartOffset = 9;
        int shCount = 15;

        float* tmp = stackalloc float[shCount * 3];

        int idx = shStartOffset;
        for (int i = 0 ; i < splatCount; ++i)
        {
            for (int j = 0; j < shCount; ++j)
            {
                tmp[j * 3 + 0] = data[idx + j];
                tmp[j * 3 + 1] = data[idx + j + shCount];
                tmp[j * 3 + 2] = data[idx + j + shCount * 2];
            }

            for (int j = 0; j < shCount * 3; ++j)
            {
                data[idx + j] = tmp[j];
            }

            idx += sizeOfSplat;
        }

    }

    [BurstCompile]
    static unsafe void RemoveDistantSplats(int splatCount, float maxDist, float* data)
    {
        int sizeOfSplat = UnsafeUtility.SizeOf<InputSplat>() / 4;
        float x, y, z;
        double norm;
        int idx = 0;
        for (int i = 0; i < splatCount; ++i)
        {
            x = data[idx + 0];
            y = data[idx + 1];
            z = data[idx + 2];

            // Push geometry outside of bounding sphere very far outside of camera frustum
            norm = Math.Sqrt(x*x + y*y + z*z);
            if (norm > maxDist)
            {
                data[idx + 0] = 100000;
                data[idx + 1] = 100000;
                data[idx + 2] = 100000;
            }

            idx += sizeOfSplat;
        }

    }

    public void OnEnable()
    {
        Camera.onPreCull += OnPreCullCamera;

        cameras = null;
        if (splatMaterial == null)
        {
            Debug.LogWarning($"{nameof(GaussianSplatRasterizer)} material/shader references are not set up");
            return;
        }

        SplatData = LoadPLYFile(PointCloudPly, clippingRadius);
        splatCount = SplatData.Length;
        if (splatCount == 0)
        {
            Debug.LogWarning($"{nameof(GaussianSplatRasterizer)} has no splats to render");
            return;
        }

        // Compute per-splat bounds
        NativeArray<Vector3> inputPositions = new(splatCount, Allocator.Temp);
        bounds = new Bounds(SplatData[0].pos, Vector3.zero);
        for (var i = 0; i < splatCount; ++i)
        {
            var pos = SplatData[i].pos;
            inputPositions[i] = pos;
            bounds.Encapsulate(pos);
        }


        var bcen = bounds.center;
        bcen.z *= -1;
        bounds.center = bcen;

        GpuPositions = new GraphicsBuffer(GraphicsBuffer.Target.Structured, splatCount, 12);
        GpuPositions.SetData(inputPositions);
        inputPositions.Dispose();

        GpuData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, splatCount, UnsafeUtility.SizeOf<InputSplat>());
        GpuData.SetData(SplatData, 0, 0, splatCount);

        int splatCountNextPot = Mathf.NextPowerOfTwo(splatCount);
        GpuSortDistances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, splatCountNextPot, 4);
        GpuSortKeys = new GraphicsBuffer(GraphicsBuffer.Target.Structured, splatCountNextPot, 4);

        // init keys buffer to splat indices
        splattingCS.SetBuffer(0, "_SplatSortKeys", GpuSortKeys);
        splattingCS.SetInt("_SplatCountPOT", GpuSortDistances.count);
        splattingCS.GetKernelThreadGroupSizes(0, out uint gsX, out uint gsY, out uint gsZ);
        splattingCS.Dispatch(0, (GpuSortDistances.count + (int)gsX - 1)/(int)gsX, 1, 1);

        splatMaterial.SetBuffer("_DataBuffer", GpuData);
        splatMaterial.SetBuffer("_OrderBuffer", GpuSortKeys);

        SorterFfx = new FfxParallelSort(sortingRoutine);
        SorterFfxArgs.inputKeys = GpuSortDistances;
        SorterFfxArgs.inputValues = GpuSortKeys;
        SorterFfxArgs.count = (uint) splatCount;
        if (SorterFfx.Valid)
            SorterFfxArgs.resources = FfxParallelSort.SupportResources.Load((uint)splatCount);
    }

    void OnPreCullCamera(Camera cam)
    {
        if (GpuData == null)
            return;

        SortPoints(cam);
        Graphics.DrawProcedural(splatMaterial, bounds, MeshTopology.Triangles, 6, splatCount, cam);
    }

    public void OnDisable()
    {
        Camera.onPreCull -= OnPreCullCamera;
        SplatData.Dispose();
        GpuData?.Dispose();
        GpuPositions?.Dispose();
        GpuSortDistances?.Dispose();
        GpuSortKeys?.Dispose();
        SorterFfxArgs.resources.Dispose();
    }

    void SortPoints(Camera cam)
    {
        // WorldToCamera mtrix with z-order reversed (increasing)!
        Matrix4x4 worldToCamMatrix = cam.worldToCameraMatrix;
        worldToCamMatrix.m20 *= -1;
        worldToCamMatrix.m21 *= -1;
        worldToCamMatrix.m22 *= -1;

        // Calculate distance to the camera for each splat
        splattingCS.SetBuffer(1, "_InputPositions", GpuPositions);
        splattingCS.SetBuffer(1, "_SplatSortDistances", GpuSortDistances);
        splattingCS.SetBuffer(1, "_SplatSortKeys", GpuSortKeys);
        splattingCS.SetMatrix("_WorldToCameraMatrix", worldToCamMatrix);
        splattingCS.SetInt("_SplatCount", splatCount);
        splattingCS.SetInt("_SplatCountPOT", GpuSortDistances.count);
        splattingCS.GetKernelThreadGroupSizes(1, out uint gsX, out uint gsY, out uint gsZ);
        splattingCS.Dispatch(1, (GpuSortDistances.count + (int)gsX - 1)/(int)gsX, 1, 1);

        // Sort splats using GPU sort
        CommandBuffer cmd = new CommandBuffer {name = "GPUSort"};
        SorterFfx.Dispatch(cmd, SorterFfxArgs);
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Dispose();
    }
}
