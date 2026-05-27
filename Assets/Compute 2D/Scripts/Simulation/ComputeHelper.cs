using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
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

    // ── Factory methods ──────────────────────────────────────────────────────

    public static ComputeBuffer CreateStructuredBuffer<T>(int count)
    {
        var buf = new ComputeBuffer(count, GetStride<T>());
        TrackComputeBuffer(buf, $"StructuredBuffer<{typeof(T).Name}>[{count}]", (long)count * GetStride<T>());
        return buf;
    }

    public static ComputeBuffer CreateStructuredBufferWithData<T>(T[] data)
    {
        var buf = new ComputeBuffer(data.Length, GetStride<T>());
        buf.SetData(data);
        TrackComputeBuffer(buf, $"StructuredBuffer<{typeof(T).Name}>[{data.Length}]", (long)data.Length * GetStride<T>());
        return buf;
    }

    public static ComputeBuffer CreateStructuredBufferWithData<T>(int count)
    {
        var buf = new ComputeBuffer(count, GetStride<T>());
        buf.SetData(new T[count]);
        TrackComputeBuffer(buf, $"StructuredBuffer<{typeof(T).Name}>[{count}]", (long)count * GetStride<T>());
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

    public static GraphicsBuffer CreateCommandBuffer()
    {
        var buf = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, commandCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
        TrackGraphicsBuffer(buf, "CommandBuffer", (long)commandCount * GraphicsBuffer.IndirectDrawIndexedArgs.size);
        return buf;
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

    // ── Release helpers ──────────────────────────────────────────────────────
    // Always use these instead of .Release() so the tracker stays in sync.

    public static void Release(ComputeBuffer buf)
    {
        if (buf == null) return;
#if UNITY_EDITOR
        _liveComputeBuffers.Remove(buf);
#endif
        buf.Release();
    }

    public static void Release(GraphicsBuffer buf)
    {
        if (buf == null) return;
#if UNITY_EDITOR
        _liveGraphicsBuffers.Remove(buf);
#endif
        buf.Release();
    }

    // ── Dispatch helpers ─────────────────────────────────────────────────────

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

    // ── Async readback helpers ────────────────────────────────────────────────

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

    // ── Shader reflection helpers ─────────────────────────────────────────────

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

    // ── Buffer tracker (Editor only) ─────────────────────────────────────────
    //
    // Every buffer created via ComputeHelper.Create* is automatically registered.
    // Call ComputeHelper.Release(buf) instead of buf.Release() to deregister.
    //
    // How to inspect at runtime:
    //   • Menu:     Fluid Sim → Log Live GPU Buffers
    //   • Code:     ComputeHelper.LogLiveBuffers()
    //   • Memory Profiler package: Window → Analysis → Memory Profiler → Take Snapshot
    //     (lists every ComputeBuffer / GraphicsBuffer with its native name set above)

#if UNITY_EDITOR
    private static readonly Dictionary<ComputeBuffer,  BufferEntry> _liveComputeBuffers  = new();
    private static readonly Dictionary<GraphicsBuffer, BufferEntry> _liveGraphicsBuffers = new();

    private readonly struct BufferEntry
    {
        public readonly string name;
        public readonly long   bytes;
        public readonly string stackTrace;
        public BufferEntry(string name, long bytes, string stackTrace)
        { this.name = name; this.bytes = bytes; this.stackTrace = stackTrace; }
    }

    // Clears the registry on script reload and registers the Play Mode exit hook.
    [InitializeOnLoadMethod]
    private static void RegisterEditorCallbacks()
    {
        _liveComputeBuffers.Clear();
        _liveGraphicsBuffers.Clear();

        // On Play Mode exit: log any survivors before clearing, then force Unity's
        // own "A ComputeBuffer has been leaked" finalizer warnings to fire immediately.
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.ExitingPlayMode) return;

            int leakCount = _liveComputeBuffers.Count + _liveGraphicsBuffers.Count;
            if (leakCount > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"⚠ {leakCount} GPU buffer(s) were not released before Play Mode stopped:");

                foreach (var (_, e) in _liveComputeBuffers)
                    sb.AppendLine($"  [ComputeBuffer]  {e.name}  {e.bytes / 1024f:F1} KB\n    Created at:\n{e.stackTrace}");

                foreach (var (_, e) in _liveGraphicsBuffers)
                    sb.AppendLine($"  [GraphicsBuffer] {e.name}  {e.bytes / 1024f:F1} KB\n    Created at:\n{e.stackTrace}");

                Debug.LogWarning(sb.ToString());
            }

            _liveComputeBuffers.Clear();
            _liveGraphicsBuffers.Clear();

            // Force Unity's built-in "A ComputeBuffer has been leaked" finalizer
            // warnings to appear now instead of at some random future GC cycle.
            System.GC.Collect();
            System.GC.WaitForPendingFinalizers();
        };
    }

    // Clears at the very start of Play Mode so we get a clean slate each run.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ClearOnPlay()
    {
        _liveComputeBuffers.Clear();
        _liveGraphicsBuffers.Clear();
    }

    private static void TrackComputeBuffer(ComputeBuffer buf, string name, long bytes)
    {
        // Setting buf.name makes it visible in RenderDoc, PIX, and
        // Unity's Memory Profiler package under "Native Objects".
        buf.name = name;
        _liveComputeBuffers[buf] = new BufferEntry(name, bytes, StackTraceUtility.ExtractStackTrace());
    }

    private static void TrackGraphicsBuffer(GraphicsBuffer buf, string name, long bytes)
    {
        buf.name = name;
        _liveGraphicsBuffers[buf] = new BufferEntry(name, bytes, StackTraceUtility.ExtractStackTrace());
    }

    /// <summary>
    /// Logs every live ComputeBuffer and GraphicsBuffer to the Console,
    /// alongside Profiler.GetAllocatedMemoryForGraphicsDriver() for comparison.
    /// Call via menu Fluid Sim → Log Live GPU Buffers, or directly in code.
    /// </summary>
    /// <summary>
    /// Forces the GC to run immediately so Unity prints its built-in
    /// "A ComputeBuffer has been leaked" warnings for any buffers that were
    /// dropped without Release(). Safe to call at any time during Play Mode.
    /// </summary>
    [MenuItem("Fluid Sim/Flush Leak Warnings (Force GC)")]
    public static void FlushLeakWarnings()
    {
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        Debug.Log("ComputeHelper: GC flushed. Any leaked-buffer warnings appear above.");
    }

    [MenuItem("Fluid Sim/Log Live GPU Buffers")]
    public static void LogLiveBuffers()
    {
        var sb = new StringBuilder();
        long computeTotal = 0, graphicsTotal = 0;

        sb.AppendLine($"=== Live ComputeBuffers ({_liveComputeBuffers.Count}) ===");
        foreach (var (buf, e) in _liveComputeBuffers)
        {
            computeTotal += e.bytes;
            sb.AppendLine($"  [{e.name}]  {buf.count} × {buf.stride} B  =  {e.bytes / 1024f:F1} KB");
        }

        sb.AppendLine($"\n=== Live GraphicsBuffers ({_liveGraphicsBuffers.Count}) ===");
        foreach (var (buf, e) in _liveGraphicsBuffers)
        {
            graphicsTotal += e.bytes;
            sb.AppendLine($"  [{e.name}]  {buf.count} × {buf.stride} B  =  {e.bytes / 1024f:F1} KB");
        }

        long tracked   = computeTotal + graphicsTotal;
        // Profiler.GetAllocatedMemoryForGraphicsDriver() returns the total memory
        // the graphics driver has allocated — includes textures, meshes, and
        // render targets on top of our tracked buffers.
        long driverMem = Profiler.GetAllocatedMemoryForGraphicsDriver();

        sb.AppendLine($"\n=== Summary ===");
        sb.AppendLine($"  Tracked buffers:         {tracked   / 1024f / 1024f:F2} MB");
        sb.AppendLine($"  Graphics driver total:   {driverMem / 1024f / 1024f:F2} MB  (Profiler.GetAllocatedMemoryForGraphicsDriver)");
        sb.AppendLine($"  Untracked remainder:     {(driverMem - tracked) / 1024f / 1024f:F2} MB  (render textures, meshes, driver overhead)");
        sb.AppendLine($"\nTip: open Window → Analysis → Memory Profiler and take a snapshot");
        sb.AppendLine($"     for a full breakdown including RenderTextures and mesh memory.");

        Debug.Log(sb.ToString());
    }
#endif
}
