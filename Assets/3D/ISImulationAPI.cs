using Unity.Mathematics;

public interface ISImulationAPI
{
    // General
    public float GetGravity();
    public void SetGravity(float gravity);

    // Fluid
    public float GetDensity();
    public void SetDensity(float density);
    public float GetViscosity();
    public void SetViscosity(float viscosity);

    // Bodies
    public int AddBody(BodyShape3D shape, float3 position, float radius, float mass);
    public void RemoveBody(int id);
}