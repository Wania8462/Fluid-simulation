using UnityEngine;
using UnityEngine.Rendering;

public class BodyRenderer : MonoBehaviour
{
    private const string DepthPassName = "DepthOnly";

    [Tooltip("Unit cube, edge length 1, such as Unity's built-in Cube")]
    [SerializeField] private Mesh cubeMesh;
    [Tooltip("Unit sphere, diameter 1, such as Unity's built-in Sphere")]
    [SerializeField] private Mesh sphereMesh;
    [Tooltip("A material with the Custom/Body3D shader")]
    [SerializeField] private Material material;
    [SerializeField] private Color color = Color.gray;

    private GraphicsBuffer cubeCommandBuf;
    private GraphicsBuffer sphereCommandBuf;
    private RenderParams cubeRp;
    private RenderParams sphereRp;
    private int depthPass;

    public void Setup(SimulationManager sim)
    {
        // Each draw covers every body; the shader collapses bodies of the other shape
        cubeRp = SetupDraw(sim, cubeMesh, 0, ref cubeCommandBuf);
        sphereRp = SetupDraw(sim, sphereMesh, 1, ref sphereCommandBuf);

        depthPass = material.FindPass(DepthPassName);
        if (depthPass < 0)
            throw new System.InvalidOperationException($"{material.shader.name} has no {DepthPassName} pass, so the screen-space fluid can't be hidden behind bodies");
    }

    private RenderParams SetupDraw(SimulationManager sim, Mesh mesh, int shape, ref GraphicsBuffer commandBuf)
    {
        ComputeHelper.Release(commandBuf);
        commandBuf = ComputeHelper.CreateCommandBuffer();
        commandBuf.SetData(ComputeHelper.CreateCommandData(mesh, sim.Buffers["BodyStates"].count));

        RenderParams rp = ComputeHelper.CreateRenderParams(material);
        rp.matProps.SetBuffer("BodyStates", sim.Buffers["BodyStates"]);
        rp.matProps.SetBuffer("BodyProperties", sim.Buffers["BodyProperties"]);
        rp.matProps.SetInteger("drawShape", shape);
        return rp;
    }

    public void DrawBodies()
    {
        Draw(cubeRp, cubeMesh, cubeCommandBuf);
        Draw(sphereRp, sphereMesh, sphereCommandBuf);
    }

    // Depth only, so the screen-space fluid is hidden behind bodies
    public void DrawDepth(CommandBuffer cmd)
    {
        cmd.DrawMeshInstancedIndirect(cubeMesh, 0, material, depthPass, cubeCommandBuf, 0, cubeRp.matProps);
        cmd.DrawMeshInstancedIndirect(sphereMesh, 0, material, depthPass, sphereCommandBuf, 0, sphereRp.matProps);
    }

    private void Draw(RenderParams rp, Mesh mesh, GraphicsBuffer commandBuf)
    {
        rp.matProps.SetColor("color", color);
        Graphics.RenderMeshIndirect(rp, mesh, commandBuf, commandCount: 1);
    }

    private void ReleaseBuffers()
    {
        ComputeHelper.Release(cubeCommandBuf);
        ComputeHelper.Release(sphereCommandBuf);
    }

    private void OnDestroy() => ReleaseBuffers();

#if UNITY_EDITOR
    private void OnEnable() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += ReleaseBuffers;

    private void OnDisable() => UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= ReleaseBuffers;
#endif
}
