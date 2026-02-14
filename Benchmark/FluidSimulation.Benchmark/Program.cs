// See https://aka.ms/new-console-template for more information

using SimulationLogic;
using UnityEngine;

var settings = GetSettings();
var spawn = new SpawnParticles();
var sim = new Simulation(settings, spawn);
sim.SetScene();
sim.SimulationStep(new Vector2(10, 10));

static SimulationSettings GetSettings()
{
    var settings = new SimulationSettings()
    {
        particleSize = 0.5f,
        interactionRadius = 6,
        gravity = -20,
        mouseAttractiveness = 1, 
        mouseRadius = 1000,
        stiffness = 200,
        nearStiffness = 20,
        restDensity = 5,
        springInteractionRadius = 2, 
        springRadius = 1,
        springStiffness = 0.05f, 
        springDeformationLimit = 0, 
        plasticity = 0.04f,
        highViscosity = 0,
        lowViscosity = 0.2f
    };
    return settings;
}
Console.WriteLine("Hello, World!");