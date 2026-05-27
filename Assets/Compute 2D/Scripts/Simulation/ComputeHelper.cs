using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Translator;

public static class ComputeHelper
{
    private const int commandCount = 1;

    private static readonly HashSet<string> bufferTypes = new()
    {
        "RWStructuredBuffer<", "StructuredBuffer<",
        "RWBuffer<", "Buffer<"
    };

    private static readonly HashSet<string> textureTypes = new()
    {
        "RWTexture2D<", "Texture2D<", "Texture3D<"
    };

    #region Factory methods
    public static ComputeBuffer CreateStructuredBuffer<T>(int count)
    {
        var buf = new ComputeBuffer(count, GetStride<T>());
        return buf;
    }

    public static ComputeBuffer CreateStructuredBufferWithData<T>(T[] data)
    {
        var buf = new ComputeBuffer(data.Length, GetStride<T>());
        buf.SetData(data);
        return buf;
    }

    public static ComputeBuffer CreateStructuredBufferWithData<T>(int count)
    {
        var buf = new ComputeBuffer(count, GetStride<T>());
        buf.SetData(new T[count]);
        return buf;
    }

    public static RenderTexture CreateRenderTexture2D(int width, int height)
    {
        RenderTexture texture = new(width, height, depth: 0, RenderTextureFormat.RInt)
        { enableRandomWrite = true };
        texture.Create();
        return texture;
    }

    public static RenderTexture CreateRenderTexture3D(int width, int depth, int height)
    {
        RenderTexture texture = new(width, height, depth: 0, RenderTextureFormat.RInt)
        {
            enableRandomWrite = true,
            dimension = TextureDimension.Tex3D,
            volumeDepth = depth
        };
        texture.Create();
        return texture;
    }

    public static RenderTexture CopyTexture(RenderTexture texture)
    {
        RenderTexture result = new(texture.width, texture.height, texture.depth, RenderTextureFormat.RInt)
        { enableRandomWrite = true };
        result.Create();
        return result;
    }

    public static GraphicsBuffer CreateCommandBuffer() => new(GraphicsBuffer.Target.IndirectArguments, commandCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);

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
    #endregion

    #region Release helpers
    public static void Release(ComputeBuffer buf)
    {
        if (buf == null) return;
        buf.Release();
    }

    public static void Release(GraphicsBuffer buf)
    {
        if (buf == null) return;
        buf.Release();
    }
    #endregion

    #region Dispatch helpers
    public static void Dispatch(this ComputeShader compute, int kernelIndex, int3 threadGroups)
        => compute.Dispatch(kernelIndex, threadGroups.x, threadGroups.y, threadGroups.z);

    public static void Dispatch(this ComputeShader compute, int kernelIndex, int threadGroupX, int threadGroupY, int threadGroupZ)
        => compute.Dispatch(kernelIndex, threadGroupX, threadGroupY, threadGroupZ);

    public static void Dispatch(this ComputeShader compute, int kernelIndex, int2 threadGroups)
        => compute.Dispatch(kernelIndex, threadGroups.x, threadGroups.y, 1);

    public static void Dispatch(this ComputeShader compute, int kernelIndex, int threadGroupX, int threadGroupY)
        => compute.Dispatch(kernelIndex, threadGroupX, threadGroupY, 1);

    public static void Dispatch(this ComputeShader compute, int kernelIndex, int threadGroupX)
        => compute.Dispatch(kernelIndex, threadGroupX, 1, 1);

    public static int3 GetThreadGroups(this ComputeShader compute, int kernelIndex, int numIterationsX, int numIterationsY = 1, int numIterationsZ = 1)
    {
        compute.GetKernelThreadGroupSizes(kernelIndex, out uint xt, out uint yt, out uint zt);
        return new int3(
            Mathf.CeilToInt(numIterationsX / (float)xt),
            Mathf.CeilToInt(numIterationsY / (float)yt),
            Mathf.CeilToInt(numIterationsZ / (float)zt));
    }

    public static int GetStride<T>()
        => System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
    #endregion

    #region Readback helpres
    public static void LogTexture<T>(RenderTexture texture, int index) where T : struct
    {
        AsyncGPUReadback.Request(texture, 0, request =>
        {
            if (request.hasError) { Debug.Log("Compute helper: couldn't get the texture"); return; }
            Debug.Log(request.GetData<T>()[index]);
        });
    }

    public static void LogTexture<T>(RenderTexture texture, List<int> indices) where T : struct
    {
        AsyncGPUReadback.Request(texture, 0, request =>
        {
            if (request.hasError) { Debug.Log("Compute helper: couldn't get the texture"); return; }
            var data = request.GetData<T>();
            foreach (var i in indices)
                Debug.Log(data[i]);
        });
    }

    public static RenderTexture GetTexture<T>(RenderTexture texture) where T : struct
    {
        RenderTexture result = CopyTexture(texture);
        Graphics.Blit(texture, result);
        return result;
    }

    public static T GetTexture<T>(RenderTexture texture, int index) where T : struct
    {
        T result = default;
        AsyncGPUReadback.Request(texture, 0, request =>
        {
            if (request.hasError) { Debug.Log("Compute helper: couldn't get the texture"); return; }
            result = request.GetData<T>()[index];
        });
        AsyncGPUReadback.WaitAllRequests();
        return result;
    }

    public static List<T> GetTextureStripe<T>(RenderTexture texture, int XIndex) where T : struct
    {
        List<T> result = new();
        AsyncGPUReadback.Request(texture, 0, request =>
        {
            if (request.hasError) { Debug.Log("Compute helper: couldn't get the texture"); return; }
            var data = request.GetData<T>();
            for (int y = 0; y < texture.height; y++)
                result.Add(data[XIndex + y * texture.width]);
        });
        AsyncGPUReadback.WaitAllRequests();
        return result;
    }

    public static T[] GetTexture<T>(RenderTexture texture, List<int> indices) where T : struct
    {
        T[] result = new T[indices.Count];
        AsyncGPUReadback.Request(texture, 0, request =>
        {
            if (request.hasError) { Debug.Log("Compute helper: couldn't get the texture"); return; }
            var data = request.GetData<T>();
            result = indices.Select(i => data[i]).ToArray();
        });
        AsyncGPUReadback.WaitAllRequests();
        return result;
    }

    public static T[,] GetTextureAs2DArr<T>(RenderTexture texture) where T : struct
    {
        T[,] result = new T[texture.width, texture.height];
        AsyncGPUReadback.Request(texture, 0, request =>
        {
            if (request.hasError) { Debug.Log("GPU sim manager: couldn't get the texture"); return; }
            var data = request.GetData<T>();
            for (int i = 0; i < texture.height; i++)
                for (int j = 0; j < texture.width; j++)
                    result[j, i] = data[i * texture.width + j];
        });
        AsyncGPUReadback.WaitAllRequests();
        return result;
    }

    public static void LogBuffer<T>(ComputeBuffer buffer, int index) where T : struct
    {
        AsyncGPUReadback.Request(buffer, request =>
        {
            if (request.hasError) { Debug.Log("Compute helper: couldn't get the buffer"); return; }
            Debug.Log(request.GetData<T>()[index]);
        });
    }

    public static void LogBuffer<T>(ComputeBuffer buffer, List<int> indices) where T : struct
    {
        AsyncGPUReadback.Request(buffer, request =>
        {
            if (request.hasError) { Debug.Log("Compute helper: couldn't get the buffer"); return; }
            var data = request.GetData<T>();
            foreach (var i in indices)
                Debug.Log(data[i]);
        });
    }

    public static T[] GetBuffer<T>(ComputeBuffer buffer) where T : struct
    {
        T[] result = null;
        AsyncGPUReadback.Request(buffer, request =>
        {
            if (request.hasError) { Debug.Log("Compute helper: couldn't get the buffer"); return; }
            result = request.GetData<T>().ToArray();
        });
        AsyncGPUReadback.WaitAllRequests();
        return result;
    }

    public static T GetBuffer<T>(ComputeBuffer buffer, int index) where T : struct
    {
        T result = default;
        AsyncGPUReadback.Request(buffer, request =>
        {
            if (request.hasError) { Debug.Log("Compute helper: couldn't get the buffer"); return; }
            result = request.GetData<T>()[index];
        });
        AsyncGPUReadback.WaitAllRequests();
        return result;
    }

    public static T[] GetBuffer<T>(ComputeBuffer buffer, List<int> indices) where T : struct
    {
        T[] result = new T[indices.Count];
        AsyncGPUReadback.Request(buffer, request =>
        {
            if (request.hasError) { Debug.Log("Compute helper: couldn't get the buffer"); return; }
            var data = request.GetData<T>();
            result = indices.Select(i => data[i]).ToArray();
        });
        AsyncGPUReadback.WaitAllRequests();
        return result;
    }
    #endregion

    #region Shader reflection helpers
    public static Dictionary<string, int> GetKernels(ComputeShader compute)
    {
        // Won't work in build!!!
        string path = AssetDatabase.GetAssetPath(compute);
        string source = TranslatorManager.GetTranslatedVer(path);
        string[] lines = source.Split('\n').Where(l => l.Contains("#pragma kernel")).ToArray();
        Dictionary<string, int> result = new();

        for (int i = 0; i < lines.Length; i++)
            result.Add(lines[i].Split(' ').Last(), i);

        return result;
    }

    public static Dictionary<string, ComputeBuffer> GetBuffers(ComputeShader compute)
    {
        string path = AssetDatabase.GetAssetPath(compute);
        string source = TranslatorManager.GetTranslatedVer(path);
        string[] lines = source.Split('\n');
        string[] bufferLines = lines.Where(line => bufferTypes.Any(t => line.Contains(t))).ToArray();
        Dictionary<string, ComputeBuffer> result = new();

        for (int i = 0; i < bufferLines.Length; i++)
            result.Add(bufferLines[i].Split(' ').Last()[..^1], null);

        return result;
    }

    public static Dictionary<string, RenderTexture> GetTextures(ComputeShader compute)
    {
        string path = AssetDatabase.GetAssetPath(compute);
        string source = TranslatorManager.GetTranslatedVer(path);
        string[] lines = source.Split('\n');
        string[] bufferLines = lines.Where(line => textureTypes.Any(t => line.Contains(t))).ToArray();
        Dictionary<string, RenderTexture> result = new();

        for (int i = 0; i < bufferLines.Length; i++)
            result.Add(bufferLines[i].Split(' ').Last()[..^1], null);

        return result;
    }
    #endregion
}