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
        /// Apply catalog rotation to a star direction vector.
        /// Matches the rotation applied in the starfield shader for HYG catalogs.
        /// HLSL rotate3D applies X, then Y, then Z.
        /// </summary>
        public static Vector3 ApplyCatalogRotation(Vector3 direction, float rotX, float rotY, float rotZ)
        {
            // Convert degrees to radians
            float radX = rotX * Mathf.Deg2Rad;
            float radY = rotY * Mathf.Deg2Rad;
            float radZ = rotZ * Mathf.Deg2Rad;

            float cosX = Mathf.Cos(radX);
            float sinX = Mathf.Sin(radX);
            float cosY = Mathf.Cos(radY);
            float sinY = Mathf.Sin(radY);
            float cosZ = Mathf.Cos(radZ);
            float sinZ = Mathf.Sin(radZ);

            // Rotation around X axis
            Vector3 v1 = new Vector3(
                direction.x,
                direction.y * cosX - direction.z * sinX,
                direction.y * sinX + direction.z * cosX
            );

            // Rotation around Y axis
            Vector3 v2 = new Vector3(
                v1.x * cosY + v1.z * sinY,
                v1.y,
                -v1.x * sinY + v1.z * cosY
            );

            // Rotation around Z axis
            Vector3 v3 = new Vector3(
                v2.x * cosZ - v2.y * sinZ,
                v2.x * sinZ + v2.y * cosZ,
                v2.z
            );

            return v3.normalized;
        }

        /// <summary>
        /// Projects a world-space direction vector to screen UV coordinates (0-1 range).
        /// Returns (-1, -1) if the star is behind the camera.
        /// </summary>
        public static Vector2 WorldDirectionToScreenUV(
            Vector3 worldDir,
            Vector3 cameraRight,
            Vector3 cameraUp,
            Vector3 cameraForward,
            float aspect,
            float verticalFOV)
        {
            // Inverse of ViewToWorld from KartographerPS.hlsl:
            // ViewToWorld: world = v.x * right + v.y * up + v.z * forward
            // So view.x = dot(world, right)
            //     view.y = dot(world, up)
            //     view.z = dot(world, forward)

            float vx = Vector3.Dot(worldDir, cameraRight);
            float vy = Vector3.Dot(worldDir, cameraUp);
            float vz = Vector3.Dot(worldDir, cameraForward);

            // Behind camera check
            if (vz <= 0.001f)
                return new Vector2(-1f, -1f);

            // Perspective projection to NDC
            // Shader: ray = normalize(float3(uv.x, uv.y, focalLength))
            // So: ndcX = (vx / vz) * focalLength
            //     ndcY = (vy / vz) * focalLength
            float focalLength = 1.0f / Mathf.Tan(verticalFOV * 0.5f);
            float ndcX = (vx / vz) * focalLength;
            float ndcY = (vy / vz) * focalLength;

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
