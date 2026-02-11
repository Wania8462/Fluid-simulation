using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rendering
{
    public class Render : MonoBehaviour
    {
        [SerializeField] private int resolution;
        [SerializeField] private Material mat;

        private List<Transform> particles = new();
        private List<Transform> bodies = new();
        private List<MeshRenderer> meshRenderers = new();
        
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        #region Particles

        public void CreateParticles(Vector2[] positions, Vector2[] velocities, float radius)
        {
            Mesh mesh = MeshGenerator.Sphere(radius, resolution);

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
                mpb.SetColor(ColorProp, GetColor(velocities[i]));
                meshRenderers[i].SetPropertyBlock(mpb);
            }
        }

        public void DrawParticles(Vector2[] positions, Vector2[] velocities, List<int> select = null)
        {
            if (positions.Length > particles.Count)
            {
                Debug.LogError($"Render: Trying to update more positions than there is particles. Particles: {particles.Count}, positions: {positions.Length}");
                return;
            }

            if (positions.Length < particles.Count)
                Debug.LogWarning($"Render: There are less positions than particles. Particles: {particles.Count}, positions: {positions.Length}");

            if (select == null)
            {
                for (var i = 0; i < positions.Length; i++)
                {
                    particles[i].position = (Vector3)positions[i];
                    MaterialPropertyBlock mpb = new();
                    mpb.SetColor(ColorProp, GetColor(velocities[i]));
                    meshRenderers[i].SetPropertyBlock(mpb);
                }
            }

            else
            {
                for (var i = 0; i < positions.Length; i++)
                {
                    particles[i].position = (Vector3)positions[i];
                    MaterialPropertyBlock mpb = new();
                    mpb.SetColor(ColorProp, select.Contains(i) ? Color.green : GetColor(velocities[i]));
                    meshRenderers[i].SetPropertyBlock(mpb);
                }
            }
        }

        public void DeleteParticles()
        {
            foreach (Transform p in particles)
                Destroy(p.gameObject);

            particles.Clear();
            meshRenderers.Clear();
        }

        public void CreateDebugParticles(Vector2[] positions)
        {
            Mesh mesh = MeshGenerator.Sphere(0.5f, resolution);

            for (int i = 0; i < positions.Length; i++)
            {
                GameObject gameObject = new("Particle", typeof(MeshFilter), typeof(MeshRenderer));
                gameObject.transform.localScale = new(1, 1, 1);
                gameObject.transform.position = (Vector3)positions[i];

                meshRenderers.Add(gameObject.GetComponent<MeshRenderer>());
                gameObject.GetComponent<MeshFilter>().mesh = mesh;

                meshRenderers[i].material = mat;
                MaterialPropertyBlock mpb = new();
                mpb.SetColor(ColorProp, Color.gray);
                meshRenderers[i].SetPropertyBlock(mpb);
            }
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
            mpb.SetColor(ColorProp, Color.blue);
            meshRenderer.SetPropertyBlock(mpb);
        }

        public void UpdateBodyPosition(Vector2 position, int index)
        {
            if (index < bodies.Count)
                bodies[index].position = (Vector3)position;

            else
                Debug.LogError($"Render: The body is outside of the range. Bodies count: {bodies.Count}, body index: {index}");
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
}