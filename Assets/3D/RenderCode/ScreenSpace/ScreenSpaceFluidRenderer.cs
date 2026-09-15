using System;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public struct ScreenSpaceFluidSettings
{
    [Tooltip("Radius of the sphere drawn for each particle, in world units. About 0.6 times the rest spacing (2.5 in 3DSandbox), so neighbouring spheres overlap into one surface without looking blobby. Separate from the simulation's particleRadius, which would leave gaps")]
    public float particleRenderRadius;

    [Header("Smoothing")]
    [Tooltip("Radius of the depth and thickness smoothing in world units (3 standard deviations). A little more than the rest spacing (2.5 in 3DSandbox), so the bumps between neighbouring spheres are smoothed away")]
    public float smoothingRadius;
    [Tooltip("Horizontal and vertical pass pairs of the depth smoothing. Each pair is two full-screen passes; 0 leaves the raw spheres")]
    [Range(0, 4)] public int smoothingIterations;
    [Tooltip("Depth difference, in world units, over which the depth smoothing stops blending: taps whose raw depth differs by 3 times this get almost no weight. About the render radius, so bumps on one surface blend but separate sheets of fluid and body edges stay sharp")]
    public float depthRangeSigma;
    [Tooltip("Largest smoothing radius as a fraction of the screen height. Caps the cost when fluid is close to the camera; a fraction gives the same look at every resolution")]
    [Range(0.002f, 0.03f)] public float maxScreenRadius;

    [Header("Shading")]
    [Tooltip("Absorption per world unit of fluid for red, green and blue: the scene seen through t units of fluid is multiplied by exp(-t * extinction). The defaults let about 70% of red through the 31-unit-deep settled fluid in 3DSandbox and 5% through the 250-unit tank, so deeper fluid looks bluer")]
    public Vector3 extinction;
    [Tooltip("Sideways shift of the scene behind the fluid, in world units per world unit of fluid it's seen through, scaled by how far the surface tilts. Water bends light by about 1 - 1 / 1.33 = 0.25 at small angles")]
    public float refractionStrength;
    [Tooltip("Largest refraction shift, as a fraction of the screen's width and height. Screen-space refraction can only show what's on screen and can pick up objects in front of the fluid, so the lookup stays near the pixel")]
    [Range(0, 0.1f)] public float maxRefractionOffset;

    [Header("Debug")]
    [Tooltip("Composite shows the shaded fluid. The other views show one stage of the pipeline")]
    public ScreenSpaceFluidRenderer.DebugView debugView;
    [Tooltip("Eye depth, in world units, that the depth views show as white")]
    public float depthDisplayScale;
    [Tooltip("World units of fluid along a view ray that the thickness views show as white. The settled fluid in 3DSandbox is about 31 units deep")]
    public float thicknessDisplayScale;
}

// Draws the fluid as one smooth surface instead of billboards. Particles are drawn as spheres into eye-depth and
// thickness textures, which are smoothed and then shaded over a copy of the scene by a full-screen pass. The commands are
// re-recorded every frame into one command buffer that the camera runs after transparent objects
public class ScreenSpaceFluidRenderer : MonoBehaviour
{
    public enum DebugView
    {
        Composite,
        RawDepth,
        SmoothDepth,
        Normals,
        Thickness,
        SmoothThickness
    }

    // Eye depth where there is no fluid, FLUID_NO_DEPTH in FluidCommon.hlsl
    public const float NoDepth = 1e7f;

    // Names of the passes in FluidParticles.shader, FluidSmooth.shader and FluidComposite.shader
    private const string DepthPassName = "Depth";
    private const string ThicknessPassName = "Thickness";
    private const string BilateralHPassName = "BilateralH";
    private const string BilateralVPassName = "BilateralV";
    private const string GaussHPassName = "GaussH";
    private const string GaussVPassName = "GaussV";
    private const string CompositePassName = "Composite";

    private static readonly int PositionsId = Shader.PropertyToID("Positions");
    private static readonly int ParticleRenderRadiusId = Shader.PropertyToID("particleRenderRadius");
    private static readonly int SmoothingRadiusId = Shader.PropertyToID("smoothingRadius");
    private static readonly int DepthRangeSigmaId = Shader.PropertyToID("depthRangeSigma");
    private static readonly int MaxScreenRadiusId = Shader.PropertyToID("maxScreenRadius");
    private static readonly int ExtinctionId = Shader.PropertyToID("extinction");
    private static readonly int RefractionStrengthId = Shader.PropertyToID("refractionStrength");
    private static readonly int MaxRefractionOffsetId = Shader.PropertyToID("maxRefractionOffset");
    private static readonly int DebugViewId = Shader.PropertyToID("debugView");
    private static readonly int DepthDisplayScaleId = Shader.PropertyToID("depthDisplayScale");
    private static readonly int ThicknessDisplayScaleId = Shader.PropertyToID("thicknessDisplayScale");
    private static readonly int FluidProjId = Shader.PropertyToID("_FluidProj");
    private static readonly int FluidInvProjId = Shader.PropertyToID("_FluidInvProj");
    private static readonly int FluidCamToWorldId = Shader.PropertyToID("_FluidCamToWorld");

    // Temporary render textures. Each is also a global texture of the same name, which is how the shaders read it
    private static readonly int FluidDepthTexId = Shader.PropertyToID("_FluidDepthTex");
    private static readonly int FluidDepthTempId = Shader.PropertyToID("_FluidDepthTemp");
    private static readonly int FluidThicknessTexId = Shader.PropertyToID("_FluidThicknessTex");
    private static readonly int FluidThicknessTempId = Shader.PropertyToID("_FluidThicknessTemp");
    private static readonly int FluidSceneTexId = Shader.PropertyToID("_FluidSceneTex");

    private static readonly Color NoDepthColor = new(NoDepth, NoDepth, 0, 0);

    [Tooltip("The camera the fluid is drawn for")]
    [SerializeField] private Camera cam;
    [Tooltip("FluidParticles.shader")]
    [SerializeField] private Shader particleShader;
    [Tooltip("FluidSmooth.shader")]
    [SerializeField] private Shader smoothShader;
    [Tooltip("FluidComposite.shader")]
    [SerializeField] private Shader compositeShader;
    [SerializeField] private ScreenSpaceFluidSettings settings = new()
    {
        particleRenderRadius = 1.5f,
        smoothingRadius = 3,
        smoothingIterations = 2,
        depthRangeSigma = 1.5f,
        maxScreenRadius = 0.02f,
        extinction = new(0.012f, 0.005f, 0.0016f),
        refractionStrength = 0.25f,
        maxRefractionOffset = 0.03f,
        debugView = DebugView.Composite,
        depthDisplayScale = 500,
        thicknessDisplayScale = 100
    };

    private CommandBuffer cmd;
    private Material particleMaterial;
    private Material smoothMaterial;
    private Material compositeMaterial;
    private int depthPass;
    private int thicknessPass;
    private int bilateralHPass;
    private int bilateralVPass;
    private int gaussHPass;
    private int gaussVPass;
    private int compositePass;
    private int numParticles;
    private BodyRenderer bodyRenderer;

    public void Setup(SimulationManager sim, BodyRenderer bodyRenderer)
    {
        // Camera.AddCommandBuffer does nothing under a scriptable render pipeline, so the fluid would silently not draw
        if (GraphicsSettings.currentRenderPipeline != null)
            throw new InvalidOperationException($"it needs the Built-in Render Pipeline, but {GraphicsSettings.currentRenderPipeline.name} is active");

        if (cam == null)
            throw new InvalidOperationException("no camera is assigned");

        if (particleMaterial == null) particleMaterial = new Material(particleShader);
        if (smoothMaterial == null) smoothMaterial = new Material(smoothShader);
        if (compositeMaterial == null) compositeMaterial = new Material(compositeShader);
        depthPass = FindPass(particleMaterial, DepthPassName);
        thicknessPass = FindPass(particleMaterial, ThicknessPassName);
        bilateralHPass = FindPass(smoothMaterial, BilateralHPassName);
        bilateralVPass = FindPass(smoothMaterial, BilateralVPassName);
        gaussHPass = FindPass(smoothMaterial, GaussHPassName);
        gaussVPass = FindPass(smoothMaterial, GaussVPassName);
        compositePass = FindPass(compositeMaterial, CompositePassName);

        particleMaterial.SetBuffer(PositionsId, sim.Buffers["Positions"]);
        numParticles = sim.numParticles;
        this.bodyRenderer = bodyRenderer;
    }

    // Records this frame's commands. No camera matrices are recorded: the draws use the camera's own matrices when the
    // buffer runs, because the camera can still move this frame after the simulation raises StepFinished
    public void Draw()
    {
        particleMaterial.SetFloat(ParticleRenderRadiusId, settings.particleRenderRadius);
        smoothMaterial.SetFloat(SmoothingRadiusId, settings.smoothingRadius);
        smoothMaterial.SetFloat(DepthRangeSigmaId, settings.depthRangeSigma);
        smoothMaterial.SetFloat(MaxScreenRadiusId, settings.maxScreenRadius);
        compositeMaterial.SetVector(ExtinctionId, settings.extinction);
        compositeMaterial.SetFloat(RefractionStrengthId, settings.refractionStrength);
        compositeMaterial.SetFloat(MaxRefractionOffsetId, settings.maxRefractionOffset);
        compositeMaterial.SetInteger(DebugViewId, (int)settings.debugView);
        compositeMaterial.SetFloat(DepthDisplayScaleId, settings.depthDisplayScale);
        compositeMaterial.SetFloat(ThicknessDisplayScaleId, settings.thicknessDisplayScale);

        cmd.Clear();

        // Every draw comes before the first Blit, because Blit replaces the camera's projection matrix with its own
        cmd.BeginSample("SSF Depth");
        cmd.GetTemporaryRT(FluidDepthTexId, -1, -1, 24, FilterMode.Point, RenderTextureFormat.RGFloat);
        cmd.SetRenderTarget(FluidDepthTexId);
        cmd.ClearRenderTarget(true, true, NoDepthColor);
        bodyRenderer.DrawDepth(cmd);
        cmd.DrawProcedural(Matrix4x4.identity, particleMaterial, depthPass, MeshTopology.Triangles, 6, numParticles);
        cmd.EndSample("SSF Depth");

        // Half resolution (-2 is half the camera size): thickness varies smoothly, so a quarter of the pixels loses little
        // and saves most of the additive overdraw. The bodies go into this target's own depth buffer too, or fluid behind a
        // body would add to the thickness of the fluid in front of it
        cmd.BeginSample("SSF Thickness");
        cmd.GetTemporaryRT(FluidThicknessTexId, -2, -2, 24, FilterMode.Bilinear, RenderTextureFormat.RHalf);
        cmd.SetRenderTarget(FluidThicknessTexId);
        cmd.ClearRenderTarget(true, true, Color.clear);
        bodyRenderer.DrawDepth(cmd);
        cmd.DrawProcedural(Matrix4x4.identity, particleMaterial, thicknessPass, MeshTopology.Triangles, 6, numParticles);
        cmd.EndSample("SSF Thickness");

        // Each iteration blurs horizontally into the temporary and vertically back, so the result ends in _FluidDepthTex.
        // The full-screen passes read their sources by name, not through Blit's _MainTex, which gave FluidSmooth's passes
        // scene colours in Play mode. Blit still provides the target and the full-screen quad
        cmd.BeginSample("SSF Smooth depth");
        cmd.GetTemporaryRT(FluidDepthTempId, -1, -1, 0, FilterMode.Point, RenderTextureFormat.RGFloat);
        for (int i = 0; i < settings.smoothingIterations; i++)
        {
            cmd.Blit(FluidDepthTexId, FluidDepthTempId, smoothMaterial, bilateralHPass);
            cmd.Blit(FluidDepthTempId, FluidDepthTexId, smoothMaterial, bilateralVPass);
        }
        cmd.ReleaseTemporaryRT(FluidDepthTempId);
        cmd.EndSample("SSF Smooth depth");

        // Skipped in the Thickness view, which shows thickness before smoothing
        if (settings.debugView != DebugView.Thickness)
        {
            cmd.BeginSample("SSF Smooth thickness");
            cmd.GetTemporaryRT(FluidThicknessTempId, -2, -2, 0, FilterMode.Bilinear, RenderTextureFormat.RHalf);
            cmd.Blit(FluidThicknessTexId, FluidThicknessTempId, smoothMaterial, gaussHPass);
            cmd.Blit(FluidThicknessTempId, FluidThicknessTexId, smoothMaterial, gaussVPass);
            cmd.ReleaseTemporaryRT(FluidThicknessTempId);
            cmd.EndSample("SSF Smooth thickness");
        }

        // The composite reads the scene behind the fluid, so the camera target is copied before being drawn over
        cmd.BeginSample("SSF Composite");
        cmd.GetTemporaryRT(FluidSceneTexId, -1, -1, 0, FilterMode.Bilinear, RenderTextureFormat.DefaultHDR);
        cmd.Blit(BuiltinRenderTextureType.CameraTarget, FluidSceneTexId);
        cmd.Blit(FluidSceneTexId, BuiltinRenderTextureType.CameraTarget, compositeMaterial, compositePass);
        cmd.EndSample("SSF Composite");

        cmd.ReleaseTemporaryRT(FluidDepthTexId);
        cmd.ReleaseTemporaryRT(FluidThicknessTexId);
        cmd.ReleaseTemporaryRT(FluidSceneTexId);
    }

    // Blit passes can't use Unity's camera matrices, so the ones they need are set as globals right before this camera
    // renders, after everything that moves it this frame has run
    private void SetCameraGlobals(Camera camera)
    {
        if (camera != cam)
            return;

        Matrix4x4 projection = cam.projectionMatrix;
        Shader.SetGlobalMatrix(FluidProjId, projection);
        Shader.SetGlobalMatrix(FluidInvProjId, projection.inverse);
        Shader.SetGlobalMatrix(FluidCamToWorldId, cam.cameraToWorldMatrix);
    }

    // A misspelt name gives -1, which would draw every pass of the shader
    private static int FindPass(Material material, string name)
    {
        int pass = material.FindPass(name);
        if (pass < 0)
            throw new InvalidOperationException($"{material.shader.name} has no pass named {name}");
        return pass;
    }

    // RenderManager enables this only in screen-space mode, so billboard mode has no command buffer on the camera
    private void OnEnable()
    {
        cmd = new CommandBuffer { name = "Screen Space Fluid" };
        if (cam != null)
            cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, cmd);
        Camera.onPreRender += SetCameraGlobals;
    }

    // Also runs before the object is destroyed and before scripts reload, so the buffer is always released
    private void OnDisable()
    {
        Camera.onPreRender -= SetCameraGlobals;
        if (cam != null)
            cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, cmd);
        cmd.Release();
    }

    private void OnDestroy()
    {
        Destroy(particleMaterial);
        Destroy(smoothMaterial);
        Destroy(compositeMaterial);
    }
}
