using System;
using System.Linq;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Captures and calculates atmospheric scattering parameters for starfield rendering.
    /// Used to dim stars based on atmospheric extinction.
    /// </summary>
    public static class AtmosphericScatteringData
    {
        /// <summary>
        /// Raw data captured from KSP
        /// </summary>
        public struct RawData
        {
            public string BodyName;
            public double BodyRadius;
            public double AtmosphereDepth;
            public double ScaleHeight;
            public double CameraAltitudeASL;
            public double StaticPressure; // kPa
            public Vector3 SunDirection;
            public Vector3 UpVector;
            public double SunZenithAngle; // degrees
            public bool IsInAtmosphere;
            public bool IsValid;
        }

        /// <summary>
        /// Calculated values for shader use
        /// </summary>
        public struct CalculatedData
        {
            public float AltitudeFactor; // 0.0 = surface, 1.0 = space
            public float AirmassZenith; // Base optical depth overhead
            public float AirmassHorizon; // Optical depth at horizon
            public float SunDayFactor; // 0.0 = full night, 1.0 = full day
            public float ExtinctionZenith; // Extinction looking straight up (0= invisible, 1=fully visible)
            public float ExtinctionHorizon; // Extinction at horizon (0= invisible, 1=fully visible)
        }

        /// <summary>
        /// Check if we're currently in map view (orbital map)
        /// </summary>
        public static bool IsMapView()
        {
            try
            {
                // Check for MapView static property (KSP 1.12)
                var mapViewType = System.Type.GetType("MapView, Assembly-CSharp");
                if (mapViewType != null)
                {
                    var mapEnabledProp = mapViewType.GetProperty("MapIsEnabled", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (mapEnabledProp != null)
                    {
                        return (bool)mapEnabledProp.GetValue(null);
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Captures raw atmospheric data from KSP
        /// </summary>
        public static RawData CaptureRawData()
        {
            var data = new RawData();

            try
            {
                // Check if we're in a valid flight scene (or space center, or tracking station)
                bool isValidScene = FlightGlobals.ActiveVessel != null || 
                                    HighLogic.LoadedScene == GameScenes.TRACKSTATION ||
                                    HighLogic.LoadedScene == GameScenes.SPACECENTER;
                
                if (!isValidScene)
                {
                    data.IsValid = false;
                    return data;
                }
                
                // Space Center: treat as sea level on Kerbin (or current main body)
                // Hardcoded surface values but calculate real sun position
                if (HighLogic.LoadedScene == GameScenes.SPACECENTER)
                {
                    CelestialBody scBody = FlightGlobals.getMainBody();
                    if (scBody == null)
                    {
                        data.IsValid = false;
                        return data;
                    }
                    
                    data.BodyName = scBody.name;
                    data.BodyRadius = scBody.Radius;
                    data.AtmosphereDepth = scBody.atmosphere ? scBody.atmosphereDepth : 0;
                    data.ScaleHeight = scBody.atmosphere ? scBody.atmosphereDepth / 10.0 : 1000;
                    
                    // Hardcoded sea-level surface values
                    data.CameraAltitudeASL = 100.0; // 100m above sea level
                    data.StaticPressure = 101.325; // Sea level pressure
                    data.IsInAtmosphere = scBody.atmosphere;
                    
                    // Get sun position - use surface normal at KSC location
                    // KSC is at approx lat 0.1°, lon -74.6° on Kerbin
                    Vector3 scBodyCenter = scBody.position;
                    data.UpVector = scBody.GetSurfaceNVector(0.0972, -74.5577).normalized;
                    
                    CelestialBody scSun = FlightGlobals.Bodies?.FirstOrDefault(b => b.name == "Sun");
                    if (scSun != null && scSun != scBody)
                    {
                        Vector3 sunPos = scSun.position;
                        data.SunDirection = (sunPos - scBodyCenter).normalized;
                        double sunDotUp = Vector3.Dot(data.SunDirection, data.UpVector);
                        data.SunZenithAngle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, sunDotUp))) * Mathf.Rad2Deg;
                    }
                    else
                    {
                        data.SunDirection = Vector3.up;
                        data.SunZenithAngle = 0;
                    }
                    
                    data.IsValid = true;
                    return data;
                }
                
                // Map view is effectively space (no atmospheric extinction)
                if (IsMapView())
                {
                    data.IsValid = true;
                    data.IsInAtmosphere = false;
                    data.StaticPressure = 0;
                    data.CameraAltitudeASL = 1000000; // Way above any atmosphere
                    data.BodyName = FlightGlobals.getMainBody()?.name ?? "Unknown";
                    data.BodyRadius = FlightGlobals.getMainBody()?.Radius ?? 600000;
                    data.AtmosphereDepth = FlightGlobals.getMainBody()?.atmosphereDepth ?? 70000;
                    data.ScaleHeight = data.AtmosphereDepth / 10.0;
                    data.SunDirection = Vector3.up;
                    data.UpVector = Vector3.up;
                    data.SunZenithAngle = 90;
                    return data;
                }

                // Get the main body (planet/moon)
                CelestialBody body = FlightGlobals.getMainBody();
                if (body == null)
                {
                    data.IsValid = false;
                    return data;
                }

                data.BodyName = body.name;
                data.BodyRadius = body.Radius;
                data.AtmosphereDepth = body.atmosphere ? body.atmosphereDepth : 0;
                // Scale height approximated from atmosphere depth
                // Typical scale height is about 1/10th of total atmosphere depth
                data.ScaleHeight = body.atmosphere ? body.atmosphereDepth / 10.0 : 1000;

                // Camera altitude
                if (HighLogic.LoadedScene == GameScenes.TRACKSTATION)
                {
                    // In tracking station, we're effectively in space
                    data.CameraAltitudeASL = data.AtmosphereDepth * 2; // Well above atmosphere
                    data.StaticPressure = 0;
                }
                else
                {
                    data.CameraAltitudeASL = FlightGlobals.ActiveVessel?.altitude ?? 0;
                    data.StaticPressure = FlightGlobals.getStaticPressure();
                }

                data.IsInAtmosphere = body.atmosphere && data.CameraAltitudeASL < data.AtmosphereDepth;

                // Get sun direction and up vector
                Vector3 bodyCenter = body.position;
                Vector3 cameraPos = FlightGlobals.ActiveVessel?.transform.position ?? bodyCenter + Vector3.up * (float)(data.BodyRadius + data.CameraAltitudeASL);
                data.UpVector = (cameraPos - bodyCenter).normalized;
                
                // Try to get sun position - Sun is a CelestialBody
                CelestialBody sun = null;
                try
                {
                    // Find the sun from the body's reference frame or use known sun name
                    sun = FlightGlobals.Bodies?.FirstOrDefault(b => b.name == "Sun") ?? 
                          (body.name == "Sun" ? body : null);
                }
                catch { }
                
                if (sun != null && sun != body)
                {
                    Vector3 sunPos = sun.position;
                    data.SunDirection = (sunPos - bodyCenter).normalized;

                    // Calculate sun zenith angle (0 = overhead, 180 = directly opposite)
                    double sunDotUp = Vector3.Dot(data.SunDirection, data.UpVector);
                    data.SunZenithAngle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, sunDotUp))) * Mathf.Rad2Deg;
                }
                else
                {
                    data.SunDirection = Vector3.up;
                    data.SunZenithAngle = 0;
                }

                data.IsValid = true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CinematicShaders] Error capturing atmospheric data: {ex.Message}");
                data.IsValid = false;
            }

            return data;
        }

        /// <summary>
        /// Calculates derived values from raw atmospheric data
        /// </summary>
        public static CalculatedData Calculate(RawData raw)
        {
            var calc = new CalculatedData();

            if (!raw.IsValid)
            {
                // Default to space values (no extinction, full visibility)
                calc.AltitudeFactor = 1.0f;
                calc.AirmassZenith = 0.0f;
                calc.AirmassHorizon = 0.0f;
                calc.SunDayFactor = 0.0f;
                calc.ExtinctionZenith = 1.0f;
                calc.ExtinctionHorizon = 1.0f;
                return calc;
            }

            // Altitude factor: 0 at surface, 1 at/above atmosphere top
            if (raw.AtmosphereDepth > 0)
            {
                calc.AltitudeFactor = Mathf.Clamp01((float)(raw.CameraAltitudeASL / raw.AtmosphereDepth));
            }
            else
            {
                calc.AltitudeFactor = 1.0f;
            }

            // Early out: if not in atmosphere (space or map view), no extinction
            if (!raw.IsInAtmosphere)
            {
                calc.AirmassZenith = 0.0f;
                calc.AirmassHorizon = 0.0f;
                calc.SunDayFactor = 0.0f;
                calc.ExtinctionZenith = 1.0f;
                calc.ExtinctionHorizon = 1.0f;
                return calc;
            }
            
            // Airmass calculation
            // Zenith: 1.0 at sea level, decreases with altitude
            // Using barometric formula: pressure_ratio = exp(-altitude/scale_height)
            double pressureRatio = raw.StaticPressure / 101.325; // Relative to sea level
            calc.AirmassZenith = Mathf.Max(0.0f, (float)pressureRatio);

            // Horizon airmass: approximately 38x zenith at sea level, decreases with altitude
            // Formula from Rozenberg (1966): X(z=90°) = sqrt(2 * pi * R / H)
            // where R is planet radius and H is scale height
            if (raw.AtmosphereDepth > 0)
            {
                double atmosphereHeight = Math.Min(raw.AtmosphereDepth, raw.CameraAltitudeASL + raw.ScaleHeight * 10);
                double horizonAirmassBase = Math.Sqrt(2 * Math.PI * raw.BodyRadius / raw.ScaleHeight);
                // Reduce with altitude (simplified approximation)
                double altitudeFactor = 1.0 - (raw.CameraAltitudeASL / (raw.AtmosphereDepth * 1.5));
                calc.AirmassHorizon = (float)(horizonAirmassBase * Math.Max(0.0, altitudeFactor) * calc.AirmassZenith);
            }
            else
            {
                calc.AirmassHorizon = 0.0f;
            }

            // Sun day factor: 0.0 = night (sun below horizon), 1.0 = day (sun at zenith)
            // This drives the blended scattering coefficient
            double sunZenithClamped = Math.Max(0, Math.Min(180, raw.SunZenithAngle));
            if (sunZenithClamped < 90)
            {
                // Sun above horizon: full daytime scattering
                // Linear ramp from 0 at horizon to 1 at zenith
                calc.SunDayFactor = (float)(1.0 - sunZenithClamped / 90.0); // 1.0 at zenith, 0.0 at horizon
            }
            else
            {
                // Sun below horizon: minimal scattering (night)
                calc.SunDayFactor = 0.0f;
            }

            // Extinction calculations using Beer-Lambert law: I = I0 * exp(-tau * m)
            // where tau is optical depth and m is airmass
            // extinction = how much starlight gets THROUGH (0.0 = invisible, 1.0 = fully visible)
            
            float dayScattering = 12.0f;    // Strong scattering during day (stars invisible)
            float nightScattering = 0.05f;   // Weak scattering at night (stars visible)
            
            // Blend scattering coefficient based on sun position
            float blendedScattering = Mathf.Lerp(nightScattering, dayScattering, calc.SunDayFactor);
            
            // Calculate extinction: how much starlight penetrates the atmosphere
            calc.ExtinctionZenith = Mathf.Exp(-calc.AirmassZenith * blendedScattering);
            calc.ExtinctionHorizon = Mathf.Exp(-calc.AirmassHorizon * blendedScattering);

            return calc;
        }

        /// <summary>
        /// Logs atmospheric data to the KSP log for debugging
        /// </summary>
        public static void LogDebugDump()
        {
            var raw = CaptureRawData();
            var calc = Calculate(raw);

            Debug.Log("[CinematicShaders] ========== ATMOSPHERE DEBUG DUMP ==========");

            if (!raw.IsValid)
            {
                Debug.Log("[CinematicShaders] Data capture failed - not in valid flight scene");
                Debug.Log("[CinematicShaders] ============================================");
                return;
            }

            // Raw data
            Debug.Log($"[CinematicShaders] RAW DATA:");
            Debug.Log($"  Body: {raw.BodyName}");
            Debug.Log($"  Body Radius: {raw.BodyRadius:N0} m");
            Debug.Log($"  Atmosphere Depth: {raw.AtmosphereDepth:N0} m");
            Debug.Log($"  Scale Height: {raw.ScaleHeight:N0} m");
            Debug.Log($"  Camera Altitude ASL: {raw.CameraAltitudeASL:N0} m");
            Debug.Log($"  Static Pressure: {raw.StaticPressure:F2} kPa ({(raw.StaticPressure/101.325)*100:F1}% sea level)");
            Debug.Log($"  Is In Atmosphere: {raw.IsInAtmosphere}");

            Debug.Log($"[CinematicShaders] SUN POSITION:");
            Debug.Log($"  Sun Direction: ({raw.SunDirection.x:F4}, {raw.SunDirection.y:F4}, {raw.SunDirection.z:F4})");
            Debug.Log($"  Up Vector: ({raw.UpVector.x:F4}, {raw.UpVector.y:F4}, {raw.UpVector.z:F4})");
            Debug.Log($"  Sun Zenith Angle: {raw.SunZenithAngle:F1}° ({GetDayNightDescription(raw.SunZenithAngle)})");

            Debug.Log($"[CinematicShaders] CALCULATED VALUES:");
            Debug.Log($"  Altitude Factor: {calc.AltitudeFactor:F3} (0=surface, 1=space)");
            Debug.Log($"  Airmass (zenith): {calc.AirmassZenith:F3}");
            Debug.Log($"  Airmass (horizon): {calc.AirmassHorizon:F2}");
            Debug.Log($"  Sun Day Factor: {calc.SunDayFactor:F3} (0=night, 1=noon)");
            Debug.Log($"  Blended Scattering: {Mathf.Lerp(0.05f, 8.0f, calc.SunDayFactor):F3}");

            Debug.Log($"[CinematicShaders] STAR VISIBILITY (EXTINCTION):");
            Debug.Log($"  Looking UP (zenith):   {calc.ExtinctionZenith:F4}   ({calc.ExtinctionZenith*100:F1}% visible)");
            Debug.Log($"  Looking OUT (horizon): {calc.ExtinctionHorizon:F4}   ({calc.ExtinctionHorizon*100:F1}% visible)");
            Debug.Log($"  Note: Lower = more atmospheric dimming");

            Debug.Log($"[CinematicShaders] SHADER PASS DATA:");
            Debug.Log($"  extinctionZenith: {calc.ExtinctionZenith:F6}f");
            Debug.Log($"  extinctionHorizon: {calc.ExtinctionHorizon:F6}f");
            Debug.Log($"  sunDayFactor: {calc.SunDayFactor:F6}f");
            Debug.Log($"  altitudeFactor: {calc.AltitudeFactor:F6}f");

            Debug.Log("[CinematicShaders] ============================================");
        }

        private static string GetDayNightDescription(double sunZenithAngle)
        {
            if (sunZenithAngle < 80) return "daytime";
            if (sunZenithAngle < 90) return "sunset/sunrise";
            if (sunZenithAngle < 100) return "twilight";
            return "nighttime";
        }
    }
}
