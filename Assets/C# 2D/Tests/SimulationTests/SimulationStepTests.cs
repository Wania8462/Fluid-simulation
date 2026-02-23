using NUnit.Framework;
using SimulationLogic;

public class SimulationStepTests
{
    // private static SpatialPartitioning Create3x3Partitioning()
    // {
    //     var sp = new SpatialPartitioning(float2.zero, new float2(30, 30), 10);
    //     float2[] positions =
    //     {
    //         new(1, 1),
    //         new(11, 1),
    //         new(21, 1),
    //         new(1, 11),
    //         new(11, 11),
    //         new(21, 11),
    //         new(1, 21),
    //         new(11, 21),
    //         new(21, 21)
    //     };

    //     sp.Init(positions);

    //     return sp;
    // }

    // [Test]
    // public void SimulateOneStep()
    // {
    //     var settings = GetSettings();
    //     var spawn = new SpawnParticles();
    //     var sim = new Simulation(settings, spawn);
    //     sim.SetScene();
    //     sim.SimulationStep(new float2(10, 10), 1/60f);
    // }

    // private static SimulationSettings GetSettings()
    // {
    //     var settings = new SimulationSettings()
    //     {
    //         particleSize = 0.5f,
    //         interactionRadius = 6,
    //         gravity = -20,
    //         mouseAttractiveness = 1, 
    //         mouseRadius = 1000,
    //         stiffness = 200,
    //         nearStiffness = 20,
    //         restDensity = 5,
    //         springInteractionRadius = 2, 
    //         springRadius = 1,
    //         springStiffness = 0.05f, 
    //         springDeformationLimit = 0, 
    //         plasticity = 0.04f,
    //         highViscosity = 0,
    //         lowViscosity = 0.2f
    //     };
    //     return settings;
    // }
}