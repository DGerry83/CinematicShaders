using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Shared projection math for converting world-space star directions to screen UV coordinates.
    /// Must match the inverse of the shader's ViewToWorld transform exactly.
    /// </summary>
    public static class KartographerMath
    {
        // Must match KartographerPS.hlsl focalLength exactly
        private const float FocalLength = 1.732f;

        /// <summary>
        /// Projects a world-space direction vector to screen UV coordinates (0-1 range).
        /// Returns (-1, -1) if the star is behind the camera.
        /// </summary>
        public static Vector2 WorldDirectionToScreenUV(
            Vector3 worldDir,
            Vector3 cameraRight,
            Vector3 cameraUp,
            Vector3 cameraForward,
            float aspect)
        {
            // Inverse of ViewToWorld from shader:
            // ViewToWorld: world = v.x * right - v.y * up + v.z * forward
            // So view.x = dot(world, right)
            //     view.y = -dot(world, up)  [note the negative]
            //     view.z = dot(world, forward)

            float vx = Vector3.Dot(worldDir, cameraRight);
            float vy = -Vector3.Dot(worldDir, cameraUp);
            float vz = Vector3.Dot(worldDir, cameraForward);

            // Behind camera check
            if (vz <= 0.001f)
                return new Vector2(-1f, -1f);

            // Perspective projection to NDC
            // Shader: ray = normalize(float3(uv.x, uv.y, focalLength))
            // So: ndcX = (vx / vz) * focalLength
            //     ndcY = (vy / vz) * focalLength
            float ndcX = (vx / vz) * FocalLength;
            float ndcY = (vy / vz) * FocalLength;

            // NDC (-1 to 1) to UV (0 to 1)
            // Shader UV construction:
            //   uv.x = (input.uv.x - 0.5) * 2.0 * aspect  →  input.uv.x = uv.x / (2*aspect) + 0.5
            //   uv.y = (input.uv.y - 0.5) * 2.0          →  input.uv.y = uv.y / 2 + 0.5
            float u = ndcX / (2f * aspect) + 0.5f;
            float v = ndcY / 2f + 0.5f;

            return new Vector2(u, v);
        }

        /// <summary>
        /// Checks if a screen UV coordinate is within the valid 0-1 range (on screen).
        /// </summary>
        public static bool IsOnScreen(Vector2 uv, float margin = 0.1f)
        {
            return uv.x >= -margin && uv.x <= 1f + margin &&
                   uv.y >= -margin && uv.y <= 1f + margin;
        }
    }
}
