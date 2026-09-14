using System;
using Unity.Mathematics;

// TODO: Find a relation/conversation between the simulation values and real world values
public class SimulationAPI : ISImulationAPI
{
    private const float BodySampleDensity = 1;

    private SimulationManager manager;

    public SimulationAPI(SimulationManager _manager)
    {
        manager = _manager;
    }

    public float GetGravity()
    {
        return manager.settings.gravity;
    }

    public void SetGravity(float gravity)
    {
        if (manager.Running)
            throw new InvalidOperationException("Cannot change simulation properties while the simulation is running.");

        else
            manager.settings.gravity = gravity;
    }

    public float GetDensity()
    {
        return manager.settings.restDensity;
    }

    public void SetDensity(float density)
    {
        if (manager.Running)
            throw new InvalidOperationException("Cannot change simulation properties while the simulation is running.");

        else
            manager.settings.restDensity = density;
    }

    public float GetViscosity()
    {
        return manager.settings.lowViscosity;
    }

    public void SetViscosity(float viscosity)
    {
        if (manager.Running)
            throw new InvalidOperationException("Cannot change simulation properties while the simulation is running.");

        else
            manager.settings.lowViscosity = viscosity;
    }

    public int AddBody(BodyShape3D shape, float3 position, float radius, float mass)
    {
        if (manager.Running)
            throw new InvalidOperationException("Cannot change simulation properties while the simulation is running.");

        if (radius <= 0)
            throw new ArgumentOutOfRangeException(nameof(radius), radius, "The radius has to be above 0.");

        if (mass <= 0)
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "The mass has to be above 0.");

        // The box isn't drawn, and a body placed partly outside it gets pushed back in instead of staying where it was put
        float3 halfBoundSize = manager.RealHalfBoundSize;
        if (math.any(math.abs(position) + radius > halfBoundSize))
            throw new ArgumentOutOfRangeException(nameof(position), position, $"The body has to fit inside the simulation box, which spans x ±{halfBoundSize.x}, y ±{halfBoundSize.y}, z ±{halfBoundSize.z}.");

        return manager.AddBody(new RigidBodySettings3D
        {
            shape = shape,
            bodyRadius = radius,
            sampleDensity = BodySampleDensity,
            bodyPosition = position,
            boundaryBodyMass = mass
        });
    }

    public void RemoveBody(int id)
    {
        if (manager.Running)
            throw new InvalidOperationException("Cannot change simulation properties while the simulation is running.");

        manager.RemoveBody(id);
    }
}