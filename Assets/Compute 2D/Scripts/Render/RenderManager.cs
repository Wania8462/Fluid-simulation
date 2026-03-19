using UnityEngine;

public class RenderManager : MonoBehaviour
{
    [SerializeField] private bool useMarchingCubes;

    [SerializeField] private GPUSimulationManager sim;
    [SerializeField] private ParticleRender particleRender;
    [SerializeField] private MarchingCubes marchingCubes;
    [SerializeField] private Material material;

    public void Setup()
    {
        if (!useMarchingCubes)
            particleRender.Setup(material, sim);

        // else
        //     SetupMarchingCubes();
    }

    public void Draw()
    {
        if (!useMarchingCubes)
            particleRender.DrawParticles();

        // else
    }
}