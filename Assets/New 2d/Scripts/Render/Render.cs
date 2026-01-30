using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions.Must;

public class Render : MonoBehaviour
{
    [SerializeField] private int resolution;
    [SerializeField] private Material mat;

    private List<Transform> particles = new();
    private List<Transform> bodies = new();
    private List<MeshRenderer> meshRenderers = new();

    #region Particles

    public void CreateParticles(Vector2[] positions, Vector2[] velocities)
    {
        Mesh mesh = MeshGenerator.Sphere(0.5f, resolution);

        for (int i = 0; i < positions.Length; i++)
        {
            GameObject gameObject = new("Particle", typeof(MeshFilter), typeof(MeshRenderer));
            gameObject.transform.localScale = new(1, 1, 1);
            gameObject.transform.position = (Vector3)positions[i];

            particles.Add(gameObject.transform);
            meshRenderers.Add(gameObject.GetComponent<MeshRenderer>());
            gameObject.GetComponent<MeshFilter>().mesh = mesh;

            meshRenderers[i].material = mat;
            MaterialPropertyBlock mpb = new();
            mpb.SetColor("_Color", GetColor(velocities[i]));
            meshRenderers[i].SetPropertyBlock(mpb);
        }
    }

    public void UpdatePositions(Vector2[] positions, Vector2[] velocities)
    {
        if (positions.Length > particles.Count)
        {
            Debug.LogError("Render: Trying to update more positions than there is particles. Particles: " + particles.Count + ", positions: " + positions.Length);
            return;
        }

        if (positions.Length < particles.Count)
            Debug.LogWarning("Render: There are less positions than particles. Particles: " + particles.Count + ", positions: " + positions.Length);

        for (int i = 0; i < positions.Length; i++)
        {
            particles[i].position = (Vector3)positions[i];
            MaterialPropertyBlock mpb = new();
            mpb.SetColor("_Color", GetColor(velocities[i]));
            meshRenderers[i].SetPropertyBlock(mpb); ;
        }
    }

    public void DeleteParticles()
    {
        foreach (Transform p in particles)
            Destroy(p.gameObject);

        particles.Clear();
        meshRenderers.Clear();
    }

    #endregion

    #region Bodies

    public void DrawCircle(Vector2 centre, float radius)
    {
        GameObject gameObject = new("Circle", typeof(MeshFilter), typeof(MeshRenderer));
        gameObject.transform.localScale = new(1, 1, 1);
        gameObject.transform.position = (Vector3)centre;
        bodies.Add(gameObject.transform);

        Mesh mesh = MeshGenerator.Sphere(radius, resolution);

        gameObject.GetComponent<MeshFilter>().mesh = mesh;
        MeshRenderer meshRenderer = gameObject.GetComponent<MeshRenderer>();
        meshRenderer.material = mat;

        MaterialPropertyBlock mpb = new();
        mpb.SetColor("_Color", Color.blue);
        meshRenderer.SetPropertyBlock(mpb);
    }

    public void UpdateBodyPosition(Vector2 position, int index)
    {
        if (index >= bodies.Count)
        {
            Debug.LogError("Render: The body is outside of the range. Bodies count: " + bodies.Count + ", body index" + index);
            return;
        }

        bodies[index].position = (Vector3)position;
    }

    public void DeleteBody(int index)
    {
        Destroy(bodies[index].gameObject);
        bodies.Remove(bodies[index]);
    }

    public void DeleteAllBodies()
    {
        foreach (Transform body in bodies)
            Destroy(body.gameObject);

        bodies.Clear();
    }

    #endregion

    private Color GetColor(Vector2 velocity)
    {
        float brightness = Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / 40f);
        return new Color(brightness, 0, 1 - brightness);
    }
}