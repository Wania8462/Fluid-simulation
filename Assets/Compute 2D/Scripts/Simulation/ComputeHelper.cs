using Unity.Mathematics;
using UnityEngine;

public static class ComputeHelper
{
    private const int commandCount = 1;

    public static ComputeBuffer CreateStructuredBuffer<T>(int count)
    {
        return new ComputeBuffer(count, GetStride<T>());
    }

    public static ComputeBuffer CreateStructuredBufferWithData<T>(T[] data)
    {
        ComputeBuffer buffer = new(data.Length, GetStride<T>());
        buffer.SetData(data);
        return buffer;
    }

    public static ComputeBuffer CreateStructuredBufferWithData<T>(int count)
    {
        ComputeBuffer buffer = new(count, GetStride<T>());
        buffer.SetData(new T[count]);
        return buffer;
    }

    public static RenderTexture CreateRenderTexture(int width, int height)
    {
        RenderTexture texture = new(width, height, depth: 0, RenderTextureFormat.ARGBFloat)
        { enableRandomWrite = true };
        texture.Create();
        return texture;
    }

    public static GraphicsBuffer CreateCommandBuffer()
    {
        return new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, commandCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
    }

    public static GraphicsBuffer.IndirectDrawIndexedArgs[] CreateCommandData(Mesh mesh, int numMeshes)
    {
        GraphicsBuffer.IndirectDrawIndexedArgs[] commandData = new GraphicsBuffer.IndirectDrawIndexedArgs[commandCount];
        commandData[0].indexCountPerInstance = mesh.GetIndexCount(0);
        commandData[0].instanceCount = (uint)numMeshes;
        return commandData;
    }

    public static RenderParams CreateRenderParams(Material material)
    {
        return new(material)
        {
            worldBounds = new Bounds(Vector3.zero, 10000 * Vector3.one),
            matProps = new MaterialPropertyBlock()
        };
    }

    public static void Dispatch(this ComputeShader compute, int kernelIndex, int3 threadGroups)
    {
        compute.Dispatch(kernelIndex, threadGroups.x, threadGroups.y, threadGroups.z);
    }

    public static void Dispatch(this ComputeShader compute, int kernelIndex, int threadGroupX, int threadGroupY, int threadGroupZ)
    {
        compute.Dispatch(kernelIndex, threadGroupX, threadGroupY, threadGroupZ);
    }

    public static void Dispatch(this ComputeShader compute, int kernelIndex, int2 threadGroups)
    {
        compute.Dispatch(kernelIndex, threadGroups.x, threadGroups.y, 1);
    }

    public static void Dispatch(this ComputeShader compute, int kernelIndex, int threadGroupX, int threadGroupY)
    {
        compute.Dispatch(kernelIndex, threadGroupX, threadGroupY, 1);
    }

    public static void Dispatch(this ComputeShader compute, int kernelIndex, int threadGroupX)
    {
        compute.Dispatch(kernelIndex, threadGroupX, 1, 1);
    }

    public static int3 GetThreadGroups(this ComputeShader compute, int kernelIndex, int numIterationsX, int numIterationsY = 1, int numIterationsZ = 1)
    {
        compute.GetKernelThreadGroupSizes(kernelIndex, out uint xt, out uint yt, out uint zt);
        return new int3(
            Mathf.CeilToInt(numIterationsX / (float)xt),
            Mathf.CeilToInt(numIterationsY / (float)yt),
            Mathf.CeilToInt(numIterationsZ / (float)zt));
    }

    public static int GetStride<T>()
    {
        return System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
    }
}