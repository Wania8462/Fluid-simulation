using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions.Must;

public class Render : MonoBehaviour
{
    [SerializeField] private int resolution;
    [SerializeField] private Material mat;

    private List<Transform> transforms = new();
    private List<MeshRenderer> meshRenderers = new();

    public void CreateParticles(Vector2[] positions, Vector2[] velocities)
    {
        Mesh mesh = MeshGenerator.Sphere(0.5f, resolution);

        for(int i = 0; i < positions.Length; i++)
        {
            GameObject gameObject = new("Particle", typeof(MeshFilter), typeof(MeshRenderer));
            gameObject.transform.localScale = new(1, 1, 1);
            gameObject.transform.position = (Vector3)positions[i];

            transforms.Add(gameObject.transform);
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
        if (positions.Length > transforms.Count)
        {
            Debug.LogError("Render: Trying to update more positions than there is particles. Particles: " + transforms.Count + ", Positions: " + positions.Length);
            return;
        }

        if (positions.Length < transforms.Count)
            Debug.LogWarning("Render: There are less positions than particles. Particles: " + transforms.Count + ", Positions: " + positions.Length);

        for (int i = 0; i < positions.Length; i++)
        {
            transforms[i].position = (Vector3)positions[i];
            MaterialPropertyBlock mpb = new();
            mpb.SetColor("_Color", GetColor(velocities[i]));
            meshRenderers[i].SetPropertyBlock(mpb);;
        }
    }

    public void DeleteParticles()
    {
        foreach (Transform p in transforms)
            Destroy(p.gameObject);

        transforms.Clear();
        meshRenderers.Clear();
    }

    private Color GetColor(Vector2 velocity)
    {
        float brightness = Mathf.Clamp01((Mathf.Abs(velocity.x) + Mathf.Abs(velocity.y)) / 40f);
        return new Color(brightness, 0, 1 - brightness);
    }
}