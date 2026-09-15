# Task: screen-space fluid rendering for the 3D GPU simulation

## Objective

Add a screen-space fluid renderer (SSFR) to the 3D simulation in `Assets/3D`, so the fluid draws as a smooth, refractive, light-absorbing liquid surface instead of individual billboards. The technique comes from a reference project at `C:\Users\Ivan\Downloads\Fluid-Sim-main\Fluid-Sim-main`. Use it to learn the technique, but don't copy it: its pipeline, units, data layout and several of its formulas don't fit this project (details below).

The result must be:
1. **Correct.** Every intermediate texture can be checked in a debug view, and depth, normal and projection conventions are the same in every pass.
2. **Fast.** The cost is known per pass. Nothing is allocated per frame, and no full-screen work is done that the image doesn't need.
3. **Well structured.** A few files with clear jobs, each owning one step of the pipeline, fitting the existing RenderManager / renderer pattern. No god-class and no copy-pasted helper functions across shaders.

Work in phases: **investigate → plan (get approval) → implement in stages with checks → report.** Don't edit any existing file until the user has approved the plan.

---

## 1. Verified facts about this project

These were checked on 2026-09-14. Re-check any you depend on, because the project may have changed.

**Render pipeline: Built-in, not URP.**
- `Packages/manifest.json` has `com.unity.render-pipelines.universal` 17.3.0, and Unity is 6000.3.9f1.
- However, `ProjectSettings/GraphicsSettings.asset:40` has `m_CustomRenderPipeline: {fileID: 0}`, and both quality levels (`QualitySettings.asset:50` and `:104`) point at pipeline assets that were deleted in commit `981dda8` (`Assets/Redundant/Unity utilities/PC_RPAsset.asset` and the others). No `UniversalRenderPipelineAsset` exists anywhere in `Assets`, so Unity falls back to the **Built-in Render Pipeline**.
- The comment at `Assets/3D/RenderCode/BodyShader.shader:5` saying "URP draws the pass…" is stale.
- **Phase 0:** confirm this at runtime (`GraphicsSettings.currentRenderPipeline == null`) and tell the user. If URP turns out to be active, stop and re-plan: `Camera.AddCommandBuffer` does nothing under an SRP, so the pass would need a `ScriptableRendererFeature` with Render Graph (compatibility mode is off: `Assets/Resources/UniversalRenderPipelineGlobalSettings.asset:169`).

**Assemblies** (see memory `3d-assemblies`)
- `Assets/3D/SimulationCode` is the `3DSimulation` asmdef, and `Assets/3D/RenderCode` is `3DRender`.
- Render references Sim, never the reverse. The simulation must not name any render type.
- Shared helpers live in `Assets/CustomLibrary` (`ComputeHelper`, `MeshGenerator`).
- Naming trap: `ComputeHelper.CreateCommandBuffer()` returns an *indirect-args `GraphicsBuffer`*, not a `UnityEngine.Rendering.CommandBuffer`. Don't mix them up, and don't reuse that name for anything new.

**Render flow**
- `SimulationManager` raises `BuffersChanged(SimulationManager)` after `Setup` or a body add/remove rebuilds buffers (`SimulationManager.cs:51`, `:94`, `:284`).
- It raises `StepFinished` at the end of every `Update`, paused or not (`:53`, `:110`).
- `RenderManager` is the only subscriber. It calls each renderer's `Setup(sim)` and then `Draw` (`RenderManager.cs:15-47`), and sets a global `lightDirection` (the direction light travels) every frame (`:38`).
- Renderers submit with `Graphics.RenderMeshIndirect` (`ParticleRenerer.cs:84-88`, `BodyRenderer.cs:41-52`).
- **Buffers are replaced** on `R` (full `Setup`) and on `AddBody`/`RemoveBody`. Anything that binds `sim.Buffers[...]` must rebind in `Setup`. Replaced buffers are released at the start of the next `Update` (`SimulationManager.cs:88-95`).

**Particle data**
- `sim.Buffers["Positions"]` is `StructuredBuffer<float4>` with w = 0 (`Simulation.compute:40`; memory `3d-fluid-port`). The reference project reads `float3`, so its shaders read the wrong stride here.
- `sim.Buffers["Velocities"]` is float4.
- The count is `sim.numParticles`. Positions are final for the frame when `StepFinished` fires.

**Rigid bodies**
- `BodyRenderer` draws cubes and spheres from `BodyStates`/`BodyProperties` with `Custom/Body3D`. The pose is computed in the vertex shader (`BodyShader.shader:37-57`), and each draw covers every body.
- **Body3D has no `ShadowCaster` pass.** In Built-in forward rendering, `_CameraDepthTexture` is drawn with that pass, so **bodies are missing from the camera depth texture**. This matters for fluid occlusion (see §3.4).

**Scene scale** (`Assets/3D/3DSandbox.unity`)
- Simulation values: `interactionRadius` 6 (`:351`), `particleRadius` 0.5 (`:388`), and `restDensity` 5, `stiffness` 200, `nearStiffness` 20 (`:356-358`).
- Spawn: a 50³ particle cube with `spacing` 3 and `tankPadding` 100 (`:423-430`), so about 125k particles in a **250-unit tank** (walls at ±125, `Spawn3DParticles.cs:110-118`).
- Rest spacing is about 2.49 units, so particles drawn at `particleRadius` 0.5 would leave large gaps.
- Camera: perspective, FOV 60, near 0.3, far 1000, HDR on, MSAA allowed but quality `antiAliasing: 0` (`QualitySettings.asset:25,79`).
- Color space is Linear (`ProjectSettings.asset:50`).
- Every length in the reference project is tuned for a sim about 30x smaller (`smoothingRadius` 0.2). **None of its numbers carry over.**

**Conventions the user set** (memories `gpu-port-conventions`, `split-large-files`, `explain-constraint-origins`, `offline-compile-checks`)
- Ask before changing existing code.
- Split files that grow well past a few hundred lines.
- When you state a constraint, say where it comes from.
- The user usually has Unity open, so use the offline compile checks from memory `offline-compile-checks` (d3dcompiler_47 for shaders, a scratch csproj build for C#).
- `.compute` files must keep LF line endings, because `ComputeHelper.GetKernels/GetBuffers` split on `'\n'` (`ComputeHelper.cs:278-302`).
- Existing file/class names keep their typos (`ParticleRenerer`).

---

## 2. The reference pipeline: what to take, what to fix, what to drop

Read these files: `Assets/Scripts/Rendering/ScreenSpace/FluidRenderTest.cs`, `Shaders/*.shader`, `Smoothing/Bilateral_1D/*`, `Smoothing/GaussSmooth/*`, and the foam files for context.

**Take the technique:**
1. **Depth pass.** Draw camera-facing quads per particle, discard outside the unit disc, and write the sphere-impostor depth of the nearest surface.
2. **Thickness pass.** Draw the same quads additively with depth test on and depth write off, at reduced resolution.
3. **Smooth depth** with a *separable* bilateral filter. The radius is a world-space radius converted to pixels at each pixel's depth, clamped to a maximum. The range weight comes from the depth difference. Optionally blur thickness too.
4. **Normals** from smoothed depth. For each axis, use whichever one-sided difference (left or right, up or down) has the smaller |Δz|, so silhouettes don't smear.
5. **Composite.** Fresnel blends a reflected colour with a refracted colour, and the refracted colour is attenuated by Beer–Lambert `exp(-thickness * extinction)`.

**Reference bugs and shortcuts. Don't reproduce them:**
- **Mixed depth conventions.** `ParticleDepth.shader:55-58` stores *radial distance* from the camera but converts it with a *view-z* formula. `LinearDepthToUnityDepth` (`:42-46`) uses `(z - near)/(far - near)` where Unity's `Linear01Depth` inverse needs `z/far`.
- **Wrong projection inside blits.** `BilateralPass.hlsl:51` and `GaussPass.hlsl:39` read `UNITY_MATRIX_P`, and `NormalsFromDepth.shader:51` reads `unity_CameraInvProjection`, all inside `cmd.Blit`. Built-in `Blit` loads its own orthographic projection, so `UNITY_MATRIX_P` there is not the camera's. The pixel radius only looked right because the settings were tuned around it.
- **Broken world-radius branch.** `GaussPass.hlsl:67-74` assigns the world-space `radius` to `radiusInt` instead of the converted pixel radius.
- **Truncated random vector.** `FluidRender.shader:239-242` declares `RandomSNorm3` as returning `float` but builds a `float3`, so the value is truncated. This only matters if you port the tiled floor, which you shouldn't.
- **Command buffer churn.** `FluidRenderTest.cs:223-224` calls `Camera.main.RemoveAllCommandBuffers()` and re-adds its buffer every frame. That deletes other systems' command buffers and churns the camera's command buffer list.
- **O(r²) blur.** `BilateralFilter2D.shader:85-101` is a full 2D bilateral, which costs O(r²) per pixel. Don't port it; the separable 1D version is the one to use.
- **Unsafe foam counters.** `FluidSim.compute:359` resets a shared counter from whichever thread has `id.x == 0`, which races with the other threads. `:379` computes `MaxWhiteParticleCount - particleIndex - 1` in `uint`, which underflows once the counter passes the maximum. Foam is out of scope (§6), but don't carry this pattern over later.
- **Wrong channel comment.** The channel-layout comment at `FluidRenderTest.cs:111` is wrong. The packed texture is `(depth, thick, thick, depth)`, with R and G smoothed and A kept as the raw depth used for bilateral range weights.
- **Shaders loaded by name.** `Shader.Find("Hidden/...")` (`Bilateral1D.cs:25`) finds nothing in a build unless something references the shader. Use serialized `Shader` fields, matching this project's style.

**Drop (out of scope, nothing in this scene uses it):**
- The shadow camera and shadow map.
- The ray-traced tiled floor and 3×3 "AA" environment sampling (9 environment evaluations per sample, twice per pixel).
- Foam, spray and bubbles.
- The HSV helpers.

---

## 3. Design decisions to adopt

Adopt these unless Phase 0 turns up a concrete reason not to. If it does, say why in the plan.

### 3.1 One depth convention everywhere
- Store **linear eye depth** (positive distance along the camera's forward axis) in an `RFloat` target. Unity view space looks down −z, so eye depth = `-viewPos.z`.
- Clear it to a named sentinel constant (for example `FLUID_NO_DEPTH = 1e7`) defined once in the shared include.
- **Sphere impostor.** For a fragment at disc offset `(x, y)`, set `nz = sqrt(1 - x² - y²)` and `eyeDepth = centreEyeDepth - nz * radius`.
- **Hardware depth** (`SV_Depth`): `raw = (1 / eyeDepth - _ZBufferParams.w) / _ZBufferParams.z`, the exact inverse of `LinearEyeDepth`. It handles reversed Z because `_ZBufferParams` already accounts for it.
- **View position from uv and depth:** `viewPos = viewRayAtDepth1(uv) * eyeDepth`, where the ray has `-z = 1`. Build it from an explicitly passed inverse projection, not `unity_CameraInvProjection`.
- **Pixels per world unit at depth z:** `_FluidProj._m11 * textureHeight / (2 * z)`. This uses the vertical FOV, so aspect ratio doesn't affect it.

### 3.2 Pass camera matrices explicitly
- Every full-screen pass that needs camera information reads globals set from C# each frame: `_FluidProj` (`cam.projectionMatrix`), `_FluidInvProj`, `_FluidCamToWorld`, and texture sizes via `_TexelSize`.
- Never read `UNITY_MATRIX_P` or `UNITY_MATRIX_V` in a `Blit` pass (see §2 for why).
- Particle draws happen *before* any blit in the command buffer. Call `cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix)` before them, so Unity applies its render-texture projection flip.
- Add a debug view (§3.7) and have the user confirm that nothing is upside down. This can't be checked offline.

### 3.3 Command buffer lifecycle
- Create one `CommandBuffer` named "Screen Space Fluid" and attach it **once** to the render camera at `CameraEvent.AfterForwardAlpha`.
- Remove **only that buffer** in `OnDisable`/`OnDestroy` with `RemoveCommandBuffer`.
- Re-record it each frame with `cmd.Clear()` plus about 30 commands. That keeps camera matrices current, costs next to nothing, and `Clear` reuses its storage. There must be **zero managed allocations per frame**: property IDs are `static readonly int` from `Shader.PropertyToID`, pass indices are cached with `material.FindPass`, and there's no LINQ, no string property names and no `new` in the frame path.
- Allocate intermediate targets with `cmd.GetTemporaryRT(id, -1, -1, …)` (camera size) or `-2, -2` (half size), and release them at the end of the buffer. Resizing then just works, and there is no RenderTexture lifecycle code to maintain.

### 3.4 Occlusion by rigid bodies, and refraction of the scene
- Bodies in front of the fluid must hide it. The camera depth texture can't be used for this because it lacks bodies (§1).
- Instead, render into the fluid targets with their **own depth buffer**:
  1. Clear it.
  2. Draw the bodies *depth-only* (a new `ColorMask 0` pass in `Custom/Body3D` that reuses its vertex function). Draw them with `CommandBuffer.DrawMeshInstancedIndirect` using the existing args `GraphicsBuffer`s, or `DrawMeshInstancedProcedural`. Check which overload exists in 6000.3 and that `SV_InstanceID` starts at 0.
  3. Draw the particles with `ZTest LEqual`.
- The half-resolution thickness target needs its own half-resolution depth buffer, with bodies drawn into it again (two draw calls). Otherwise fluid behind a body adds absorption to the fluid in front of it.
- This touches existing files (`BodyShader.shader`, `BodyRenderer.cs`), so put it in the plan for approval. A good shape is one public method on `BodyRenderer`, such as `DrawDepth(CommandBuffer cmd)`.
- **Refraction uses the scene behind the fluid.** Copy the camera target to a temporary RT, then composite back to `BuiltinRenderTextureType.CameraTarget`. Offset the scene-colour lookup by the view-space normal's xy, scaled by thickness and a strength setting and clamped. This screen-space refraction shows the tank lines and bodies through the water.
- Pixels with no fluid return the scene colour unchanged.
- Known limitation, acceptable: refraction can pick up a body that is *in front of* the fluid.

### 3.5 Physically meaningful, scale-independent parameters
- **Render radius.** `particleRenderRadius` is its own setting, not `sim.particleRadius`. The default is about 0.6 × rest spacing (about 1.5 units here), and the tooltip should explain why.
- **Thickness.** Accumulate the sphere chord length `2 * nz * radius` per fragment instead of a constant 0.1. Thickness is then in world units, extinction coefficients mean "per world unit", and reasonable defaults can be estimated from the tank size.
- **Bilateral smoothing.**
  - Spatial radius in world units, clamped to `maxPixelRadius`.
  - Range weight `exp(-Δz² / (2σ_r²))`, with `σ_r` in world units (default about the render radius) instead of the reference's unitless `diffStrength`.
  - Loop to a compile-time `MAX_RADIUS` with `[loop]`, and sample with `tex2Dlod`/`SampleLevel`, because gradient-based sampling in a loop triggers warning X3570.
- **Fresnel.** Use Schlick with F0 = 0.02 (water) instead of the full polarised equations. It's cheaper and looks the same.
- **Lighting.** Use the existing global `lightDirection` (direction light travels; `BodyShader.shader:61` shows how it's used), so the fluid and bodies are lit the same way.
- **Reflection.** Use a cheap analytic sky gradient plus a sun highlight. No environment ray tracing.

### 3.6 Optimisations to build in, and to measure rather than assume
- **Particle draw.** Use `cmd.DrawProcedural(Matrix4x4.identity, mat, pass, MeshTopology.Triangles, 6, numParticles)`, building quad corners from `SV_VertexID` and the particle from `SV_InstanceID`. That needs no mesh and no args buffer. The existing billboard mesh is a circle with `particleQuality` segments, which has several times the vertices of a quad plus `discard`.
- **Invalid positions.** In the particle vertex shader, send any particle with a non-finite position outside clip space. One NaN depth pixel spreads through every blur iteration.
- **Normals in the composite.** Reconstruct normals inside the composite pass instead of in a separate full-screen pass and RGBA float target. That saves a pass and a full-resolution 4-channel float texture.
- **Separate smoothing.** Smooth depth at full resolution in an `RGFloat` target (R = smoothed, G = raw depth kept for range weights). Smooth thickness at half resolution in `RHalf` with a plain separable Gaussian, which is 4x fewer pixels than the reference's joint full-resolution RGBA32F smoothing. Composite only reads thickness where depth is valid, so bleed outside the silhouette doesn't show.
- **Early exits.** Return early in blur passes and in the composite where depth is the sentinel. These branches are spatially coherent, so they're cheap.
- **Fluid draw pass.** Skip `ParticleRenerer.DrawParticles` and its colour compute dispatch while SSFR is active.
- **Profiling markers.** Wrap each stage in `cmd.BeginSample`/`EndSample`, so the Frame Debugger and GPU profiler show per-pass cost.
- **Optional, after measuring:** conservative depth output (`SV_DepthGreaterEqual`/`LessEqual`, depending on reversed Z) with the quad pushed toward the camera by the radius, to regain early-Z. Only adopt it if the profiler shows the depth pass matters.

### 3.7 Debug views
Add a `DebugView` enum on the renderer: `Composite, RawDepth, SmoothDepth, Normals, Thickness, SmoothThickness`. The composite shader switches on it with a uniform int. A uniform branch doesn't diverge across pixels, the same reasoning the existing kernels use (`Simulation.compute:208`). Scale depth and thickness displays by settings. These views are how the user verifies each stage.

---

## 4. Code structure

Put all new files in `Assets/3D/RenderCode/ScreenSpace/`, inside the `3DRender` assembly with no new asmdef. Proposed layout (adjust in the plan if you have a reason):

| File | Responsibility |
|---|---|
| `ScreenSpaceFluidRenderer.cs` | MonoBehaviour. `Setup(SimulationManager)` binds buffers and counts. `Draw()` sets per-frame camera globals and material values and re-records the command buffer. Also attaches/detaches the command buffer, and releases resources on destroy and on `AssemblyReloadEvents.beforeAssemblyReload` like the other renderers. Holds a `[Serializable]` settings struct and the `DebugView` enum. Split settings into their own file only if this file passes about 300 lines. |
| `FluidCommon.hlsl` | Shared declarations and helpers, each defined once: camera globals, `FLUID_NO_DEPTH`, eye depth ↔ raw depth, view ray and position from uv, pixels per world unit. |
| `FluidParticles.shader` | Pass `Depth` (sphere impostor, `SV_Depth`) and pass `Thickness` (additive chord length). They share one vertex function. |
| `FluidSmooth.shader` | Passes `BilateralH`, `BilateralV`, `GaussH`, `GaussV`: one filter function each, parameterised by direction. |
| `FluidComposite.shader` | Normals, shading, refraction and reflection, and the debug views. |

Changes to existing files, **each listed in the plan for approval**:
- `RenderManager.cs`: a fluid mode switch (billboards or screen space) and wiring the new renderer into `Setup`/`Draw` through the existing `TrySetup` pattern.
- `BodyShader.shader` and `BodyRenderer.cs`: the depth-only pass and `DrawDepth(CommandBuffer)`. Also fix the stale URP comment.
- Scene/material wiring: which serialized fields the user must assign in the inspector. List them in the final report.

Rules:
- No interfaces with one implementation, no factories, no config for values that never change.
- Constants that must match between C# and HLSL (pass names, the sentinel) are named in both places, with a comment pointing at the other.
- Match surrounding code style: comment density, `[SerializeField] private`, tooltips on non-obvious settings.

---

## 5. Process

### Phase 0: investigate (read-only)
1. Re-verify the facts in §1 that the design depends on: pipeline, buffer names and strides, event order, the `BodyShader` passes, camera settings.
2. Read the reference files listed in §2 end to end.
3. Check the Unity 6000.3 API surface you plan to use: `CommandBuffer.DrawProcedural` instance overload, `DrawMeshInstancedIndirect` with `GraphicsBuffer`, `GetTemporaryRT` with −1/−2, `SetViewProjectionMatrices`. Use the scratch-csproj build from memory `offline-compile-checks` if unsure.
4. Check which formats the target GPU supports: `SystemInfo.SupportsRenderTextureFormat` for RFloat, RGFloat and RHalf, and blending on RHalf.

### Phase 1: plan (use plan mode; wait for approval)
The plan must include:
- The pass-by-pass pipeline: for each pass, its input and output textures with format and resolution, blend/ZTest/ZWrite state, and shader pass name.
- The depth/projection conventions from §3.1–3.2, written as formulas.
- Every existing file you'll edit and why, with line references.
- A table of default parameter values derived from this scene's scale (§1), with the reasoning for each.
- The expected GPU cost per pass at 1920×1080 and 125k particles, as an estimate you'll check against the profiler, not a claim.
- The stage order and what the user checks at each stage.
- The gotchas, each with where it comes from (user convention).

### Phase 2: implement in stages
After each stage, run the offline shader compile (vs_5_0/ps_5_0 per memory `offline-compile-checks`) and the C# build. Then tell the user exactly what to look at in Play mode before moving on.
1. Depth pass and `RawDepth` debug view. Check: orientation correct, fluid silhouette right, bodies occlude fluid.
2. Thickness pass and `Thickness` view.
3. Smoothing and `SmoothDepth`/`SmoothThickness` views.
4. Normals and composite, then `Normals` and `Composite` views.
5. `RenderManager` mode switch, and skipping billboard draws in screen-space mode.
6. Profiling markers, then a pass over the frame path to confirm zero per-frame allocations (check with the Profiler's GC Alloc column).

### Phase 3: report
Tell the user:
- What was built.
- Inspector assignments they must make.
- Which checks passed offline and which need a Play test.
- Measured or estimated costs.
- Known limitations: screen-space refraction artefacts, no foam.

---

## 6. Out of scope (don't build these)
- Foam, spray and bubbles. Adding them would need new simulation kernels (spawn, update, compaction). They couldn't go into `DoubleDensityRelaxation`, which already uses all 8 UAV slots cs_5_0 allows (memory `3d-ro-buffer-aliases`). They would be a separate kernel after `CalculateVelocity`, reading `PositionsRO`/`VelocitiesRO`/grid aliases. That's a separate task needing the user's approval.
- Shadows cast by the fluid, a floor, and environment ray tracing.
- A URP version (unless Phase 0 finds URP active).
- Changes to the simulation's physics.

## 7. Acceptance criteria
- No compile errors or warnings in the new shaders or C#. Existing kernels are unaffected.
- Each debug view shows the expected stage, the right way up, at any window size, including after resizing during Play.
- Rigid bodies correctly hide the fluid behind them and show through the fluid in front of them (refracted).
- Pressing `R` and adding/removing bodies at runtime keeps rendering working, with no errors about released buffers.
- Switching the fluid mode back to billboards restores the old rendering exactly.
- No GC allocations per frame from the new renderer.
- Every setting has a sensible default for this scene, and non-obvious ones have tooltips.
