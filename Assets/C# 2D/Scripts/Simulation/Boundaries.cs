using System;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SimulationLogic
{
    public class Boundaries
    {
        private Particle[] _particles;
        private int _count;
        private float particleRadius;
        private float collisionDamp;
        private Body body;
        private float2 realHalfBoundSizeBody;

        public Boundaries(Particle[] particles, int count, float particleRadius, float collisionDamp, ref Body body, float2 realHalfBoundSizeBody)
        {
            _particles = particles;
            _count = count;
            this.particleRadius = particleRadius;
            this.collisionDamp = collisionDamp;
            this.body = body;
            this.realHalfBoundSizeBody = realHalfBoundSizeBody;
        }

        public void ResolveBoundaries(float2 realHalfBoundSize)
        {
            // Particles
            for (int i = 0; i < _count; i++)
            {
                var pos = _particles[i].position;
                if (Math.Abs(pos.x) >= realHalfBoundSize.x)
                {
                    var sign = Math.Sign(pos.x);
                    pos.x = realHalfBoundSize.x * sign;
                    pos.x += -sign * collisionDamp;
                    _particles[i].position = pos;
                }

                if (Math.Abs(pos.y) >= realHalfBoundSize.y)
                {
                    var sign = Math.Sign(pos.y);
                    pos.y = realHalfBoundSize.y * sign;
                    pos.y += -sign * collisionDamp;
                    _particles[i].position = pos;
                }
            }

            // Bodies
            if (Math.Abs(body.position.x) >= realHalfBoundSizeBody.x)
            {
                body.position.x = realHalfBoundSizeBody.x * Math.Sign(body.position.x);
                body.velocity.x = 0;
            }

            if (Math.Abs(body.position.y) >= realHalfBoundSizeBody.y)
            {
                body.position.y = realHalfBoundSizeBody.y * Math.Sign(body.position.y);
                body.velocity.y = 0;
            }
        }

        private void LineBarrier(float2 start, float2 end)
        {
            if (start.x != end.x && start.y != end.y)
            {
                Debug.LogError("Simulation: can't create bariers not on the axis");
                return;
            }

            // Horizontal
            if (start.y == end.y)
            {
                for (int i = 0; i < _count; i++)
                {
                    // Left-right
                    if (start.x < end.x)
                        if (_particles[i].position.x < start.x || _particles[i].position.x > end.x) continue;

                    // Right-left
                    if (start.x > end.x)
                        if (_particles[i].position.x > start.x || _particles[i].position.x < end.x) continue;

                    var dist = start.y - _particles[i].position.y;
                    var prevDist = start.y - _particles[i].prevPosition.y;

                    if (Math.Abs(dist) >= particleRadius && Math.Sign(dist) == Math.Sign(prevDist)) continue;

                    var sign = Math.Sign(prevDist) != 0 ? Math.Sign(prevDist) : Math.Sign(dist);
                    _particles[i].position.y = start.y + (-sign * particleRadius);
                    _particles[i].position.y += -sign * collisionDamp;
                }
            }

            // Vertical
            else if (start.x == end.x)
            {
                for (int i = 0; i < _count; i++)
                {
                    // Top-down
                    if (start.y > end.y)
                        if (_particles[i].position.y > start.y || _particles[i].position.y < end.y) continue;

                    // Down-top
                    if (start.y < end.y)
                        if (_particles[i].position.y < start.y || _particles[i].position.y > end.y) continue;

                    var dist = start.x - _particles[i].position.x;
                    var prevDist = start.x - _particles[i].prevPosition.x;

                    if (Math.Abs(dist) >= particleRadius && Math.Sign(dist) == Math.Sign(prevDist)) continue;

                    var sign = Math.Sign(prevDist) != 0 ? Math.Sign(prevDist) : Math.Sign(dist);
                    _particles[i].position.x = start.x + (-sign * particleRadius);
                    _particles[i].position.x += -sign * collisionDamp;
                }
            }
        }

        private void RectBarrier(float2 topLeft, float2 bottomRight)
        {
            LineBarrier(new float2(topLeft.x, topLeft.y), new float2(bottomRight.x, topLeft.y));    // top
            LineBarrier(new float2(topLeft.x, bottomRight.y), new float2(bottomRight.x, bottomRight.y)); // bottom
            LineBarrier(new float2(topLeft.x, bottomRight.y), new float2(topLeft.x, topLeft.y));    // left
            LineBarrier(new float2(bottomRight.x, bottomRight.y), new float2(bottomRight.x, topLeft.y));    // right
        }

        private void CircleBarrier(float2 center, float circleRadius)
        {
            var collisionRadius = circleRadius + particleRadius;

            for (int i = 0; i < _count; i++)
            {
                var dist = FluidMath.Distance(center, _particles[i].position);
                var prevDist = FluidMath.Distance(center, _particles[i].prevPosition);

                if (dist >= collisionRadius && prevDist >= collisionRadius) continue;

                var refPos = dist > 0 ? _particles[i].position : _particles[i].prevPosition;
                var outward = FluidMath.UnitVector(center, refPos);

                _particles[i].position = center + outward * (collisionRadius + collisionDamp);
            }
        }
    }
}
