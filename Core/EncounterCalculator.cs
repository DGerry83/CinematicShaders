using System;
using UnityEngine;

namespace CinematicShaders.Core
{
    /// <summary>
    /// Which kind of encounter timing the HUD should show (priority order).
    /// </summary>
    public enum EncounterMode
    {
        None,
        ClosestApproach,
        SoiChange,
        Impact
    }

    /// <summary>
    /// Result of an encounter-time calculation for the current target.
    /// Times are seconds until the event; separations are meters.
    /// </summary>
    public struct EncounterInfo
    {
        public EncounterMode Mode;
        public double TimeSeconds;
        public double SeparationMeters;    // ClosestApproach only
        public string BodyName;            // SoiChange only (raw display name, caller sanitizes)
        public bool SoiEntering;           // SoiChange only: true = entering SOI, false = escaping
        public bool HasSecondApproach;     // ClosestApproach only
        public double SecondTimeSeconds;
        public double SecondSeparationMeters;
    }

    /// <summary>
    /// Computes "time to encounter" metrics for the active vessel's current target,
    /// replicating stock map-view behavior (OrbitTargeter / PatchedConics) using public
    /// KSP APIs only. See ReferenceNotes/active/2026-08-18_039Update_PlanContinuation/SECTION5_SPEC.md.
    ///
    /// Mode priority: IMPACT (target body only, current patch) &gt; SOI change &gt; closest approach.
    /// Impact is an estimate: sea-level radius crossing, no terrain elevation or atmosphere.
    /// </summary>
    public static class EncounterCalculator
    {
        // CONIC_PATCH_LIMIT defaults to 3, but users can raise it; cap chain walks so a
        // malformed chain can never loop forever.
        private const int MaxPatchChainLinks = 8;

        // Stock solver parameters (PatchedConics.SolverParameters defaults / OrbitTargeter call site).
        private const double SolverEpsilon = 0.0001;
        private const int MaxGeometryIterations = 20;

        // Window cap for the orbit-to-orbit sweep when the synodic period is undefined
        // (hyperbolic) or the patch never ends (FINAL hyperbolic, EndUT = +infinity).
        private const double OrbitSweepCapSeconds = 365.0 * 24.0 * 3600.0; // 1 Kerbin/Earth year, display estimate only
        private const int CoarseSampleCount = 64;

        /// <summary>
        /// Compute encounter info for the given target. Returns Mode == None when no
        /// valid metric exists (no target, landed, no encounter, guard failures).
        /// </summary>
        public static EncounterInfo Calculate(ITargetable target)
        {
            EncounterInfo none = default(EncounterInfo);
            if (target == null) return none;
            if (!FlightGlobals.ready || FlightGlobals.ActiveVessel == null) return none;

            Vessel activeVessel = FlightGlobals.ActiveVessel;
            if (!IsOrbiting(activeVessel)) return none;

            Orbit vesselOrbit = activeVessel.orbit;
            if (vesselOrbit == null || vesselOrbit.referenceBody == null) return none;

            double now = Planetarium.GetUniversalTime();

            Vessel targetVessel = target.GetVessel();
            CelestialBody targetBody = null;
            if (targetVessel == null)
            {
                targetBody = target as CelestialBody;
                if (targetBody == null) return none; // unknown ITargetable flavor
            }

            // Priority 1: IMPACT — target body only, current patch only (spec Q2/Q3).
            if (targetBody != null && vesselOrbit.referenceBody == targetBody)
            {
                double tImpact;
                if (TryGetImpactTime(vesselOrbit, now, out tImpact))
                {
                    EncounterInfo info = default(EncounterInfo);
                    info.Mode = EncounterMode.Impact;
                    info.TimeSeconds = tImpact - now;
                    return info;
                }
            }

            // Priority 2: SOI change — first pending ENCOUNTER/ESCAPE in our patch chain.
            EncounterInfo soi;
            if (TryGetSoiChange(vesselOrbit, now, out soi)) return soi;

            // Priority 3: closest approach.
            if (targetVessel != null)
                return VesselClosestApproach(vesselOrbit, targetVessel, now);
            return BodyClosestApproach(vesselOrbit, targetBody, now);
        }

        /// <summary>
        /// Landed/prelaunch vessels have OrbitDriver idle and no valid patch chain
        /// (stock PatchedConicSolver early-outs on the same condition).
        /// </summary>
        private static bool IsOrbiting(Vessel v)
        {
            return v != null && v.orbitDriver != null &&
                   v.orbitDriver.updateMode != OrbitDriver.UpdateMode.IDLE;
        }

        /// <summary>
        /// Impact = next crossing of the body's sea-level radius on the current patch.
        /// Stock never terminates patches at the surface (PatchTransitionType.IMPACT is
        /// a dead enum), so this is hand-computed. Estimate only: ignores terrain height.
        /// </summary>
        private static bool TryGetImpactTime(Orbit orbit, double now, out double tImpact)
        {
            tImpact = 0.0;
            CelestialBody body = orbit.referenceBody;
            if (body == null) return false;
            if (orbit.PeR >= body.Radius) return false;

            double t = orbit.GetNextTimeOfRadius(now, body.Radius);
            // Sentinel: GetNextTimeOfRadius returns the input UT unchanged when unsolvable.
            if (double.IsNaN(t) || double.IsInfinity(t)) return false;
            if (t <= now) return false;
            if (t > orbit.EndUT) return false;

            tImpact = t;
            return true;
        }

        /// <summary>
        /// First patch in the chain ending in an SOI transition wins. ENCOUNTER means
        /// entering nextPatch's body; ESCAPE means leaving this patch's body.
        /// </summary>
        private static bool TryGetSoiChange(Orbit startPatch, double now, out EncounterInfo info)
        {
            info = default(EncounterInfo);
            Orbit p = startPatch;
            for (int i = 0; p != null && i < MaxPatchChainLinks; i++, p = p.nextPatch)
            {
                if (double.IsNaN(p.EndUT) || double.IsInfinity(p.EndUT)) continue;

                if (p.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER &&
                    p.nextPatch != null && p.nextPatch.referenceBody != null)
                {
                    info.Mode = EncounterMode.SoiChange;
                    info.TimeSeconds = p.EndUT - now;
                    info.SoiEntering = true;
                    info.BodyName = p.nextPatch.referenceBody.bodyDisplayName;
                    return true;
                }

                if (p.patchEndTransition == Orbit.PatchTransitionType.ESCAPE &&
                    p.referenceBody != null)
                {
                    info.Mode = EncounterMode.SoiChange;
                    info.TimeSeconds = p.EndUT - now;
                    info.SoiEntering = false;
                    info.BodyName = p.referenceBody.bodyDisplayName;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Vessel target: replicate stock OrbitTargeter behavior — find the first pair of
        /// patches (ours x target's) sharing a reference body and solve for intercepts on
        /// it. Up to two solutions (rendezvous orbits can cross twice).
        /// </summary>
        private static EncounterInfo VesselClosestApproach(Orbit vesselOrbit, Vessel targetVessel, double now)
        {
            EncounterInfo none = default(EncounterInfo);
            if (!IsOrbiting(targetVessel)) return none;
            Orbit targetOrbit = targetVessel.orbit;
            if (targetOrbit == null) return none;

            Orbit p = vesselOrbit;
            for (int i = 0; p != null && i < MaxPatchChainLinks; i++, p = p.nextPatch)
            {
                Orbit t = targetOrbit;
                for (int j = 0; t != null && j < MaxPatchChainLinks; j++, t = t.nextPatch)
                {
                    if (t.referenceBody != p.referenceBody) continue;

                    // First same-body pair only (matches stock's FindPatch behavior).
                    EncounterInfo found;
                    if (TryVesselInterceptOnPatches(p, t, now, out found)) return found;
                    return none;
                }
            }
            return none;
        }

        /// <summary>
        /// Stock call shape from OrbitTargeter: PeApIntersects precheck, FindClosestPoints
        /// (Targeting.Intercepts numeric sampler), true anomalies converted to UT via
        /// patch.StartUT + GetDTforTrueAnomaly(tA, 0), ascending sort with swaps, and
        /// per-patch TA/UT bounds checks.
        /// </summary>
        private static bool TryVesselInterceptOnPatches(Orbit p, Orbit s, double now, out EncounterInfo info)
        {
            info = default(EncounterInfo);
            if (!Orbit.PeApIntersects(p, s, 10000.0)) return false;

            double cd = 0.0, ccd = 0.0;
            double fFp = 0.0, fFs = 0.0, sFp = 0.0, sFs = 0.0;
            int iterations = 0;
            int solutions;
            try
            {
                solutions = Orbit.FindClosestPoints(p, s, ref cd, ref ccd,
                    ref fFp, ref fFs, ref sFp, ref sFs, SolverEpsilon, MaxGeometryIterations, ref iterations);
            }
            catch (Exception)
            {
                return false;
            }
            if (solutions <= 0) return false;

            double utA = p.StartUT + p.GetDTforTrueAnomaly(fFp, 0.0);
            double utB = solutions > 1 ? p.StartUT + p.GetDTforTrueAnomaly(sFp, 0.0) : double.NaN;

            // Stock sorts ascending and swaps both true-anomaly pairs along with the UTs.
            if (solutions > 1 && utA > utB)
            {
                double tmp;
                tmp = utA; utA = utB; utB = tmp;
                tmp = fFp; fFp = sFp; sFp = tmp;
                tmp = fFs; fFs = sFs; sFs = tmp;
            }

            double sepA = 0.0, sepB = 0.0;
            bool validA = IsSolutionValid(p, s, fFp, fFs, utA, now, out sepA);
            bool validB = solutions > 1 && IsSolutionValid(p, s, sFp, sFs, utB, now, out sepB);
            if (!validA && !validB) return false;

            // Promote B if it is the only valid solution.
            if (!validA)
            {
                validA = true; validB = false;
                utA = utB; sepA = sepB;
            }

            info.Mode = EncounterMode.ClosestApproach;
            info.TimeSeconds = utA - now;
            info.SeparationMeters = sepA;
            if (validB)
            {
                info.HasSecondApproach = true;
                info.SecondTimeSeconds = utB - now;
                info.SecondSeparationMeters = sepB;
            }
            return true;
        }

        private static bool IsSolutionValid(Orbit p, Orbit s, double taP, double taS, double ut,
            double now, out double separationMeters)
        {
            separationMeters = 0.0;
            if (double.IsNaN(ut) || double.IsInfinity(ut)) return false;
            if (ut < now) return false;                    // past solution (e.g. hyperbolic post-Pe)
            if (ut > p.EndUT || ut > s.EndUT) return false; // beyond either patch
            if (!PatchedConics.TAIsWithinPatchBounds(taP, p)) return false;
            if (!PatchedConics.TAIsWithinPatchBounds(taS, s)) return false;

            // Both patches share a reference body, so relative positions share a frame.
            double sep;
            try
            {
                Vector3d rp = p.getRelativePositionAtUT(ut);
                Vector3d rs = s.getRelativePositionAtUT(ut);
                sep = (rp - rs).magnitude;
            }
            catch (Exception)
            {
                return false;
            }
            if (double.IsNaN(sep) || double.IsInfinity(sep)) return false;

            separationMeters = sep;
            return true;
        }

        /// <summary>
        /// Body target: if any patch in our chain is around that body, closest approach is
        /// that patch's periapsis. Otherwise (no encounter) sweep the vessel orbit against
        /// the body's own orbit and refine the minimum — stock shows the same thing via its
        /// approach markers, but its solver is not frame-safe for cross-SOI inputs, so we
        /// sample in the absolute (getTruePositionAtUT) frame instead.
        /// </summary>
        private static EncounterInfo BodyClosestApproach(Orbit vesselOrbit, CelestialBody targetBody, double now)
        {
            EncounterInfo none = default(EncounterInfo);
            if (targetBody == null) return none;

            Orbit p = vesselOrbit;
            for (int i = 0; p != null && i < MaxPatchChainLinks; i++, p = p.nextPatch)
            {
                if (p.referenceBody != targetBody) continue;

                double utPe;
                if (p == vesselOrbit)
                {
                    // Live patch: timeToPe is maintained by the game. Negative = hyperbolic
                    // trajectory already past periapsis (no future approach).
                    if (double.IsNaN(p.timeToPe) || double.IsInfinity(p.timeToPe)) return none;
                    if (p.timeToPe < 0.0) return none;
                    utPe = now + p.timeToPe;
                }
                else
                {
                    // Future patch: periapsis is true anomaly 0.
                    double dT = p.GetDTforTrueAnomaly(0.0, 0.0);
                    if (double.IsNaN(dT) || double.IsInfinity(dT)) return none;
                    utPe = p.StartUT + dT;
                }
                if (double.IsNaN(utPe) || utPe < now || utPe > p.EndUT) return none;

                EncounterInfo peInfo = default(EncounterInfo);
                peInfo.Mode = EncounterMode.ClosestApproach;
                peInfo.TimeSeconds = utPe - now;
                peInfo.SeparationMeters = p.PeA;
                return peInfo;
            }

            // No encounter with the body in the patch chain: orbit-vs-orbit sweep.
            // (Kerbol's own GetOrbit() may be null — guard.)
            Orbit bodyOrbit = targetBody.GetOrbit();
            if (bodyOrbit == null) return none;

            double maxUT = vesselOrbit.EndUT;
            double synodic = Orbit.SynodicPeriod(vesselOrbit, bodyOrbit);
            if (!double.IsNaN(synodic) && !double.IsInfinity(synodic) && synodic > 0.0)
                maxUT = Math.Min(maxUT, now + synodic);
            if (double.IsInfinity(maxUT) || double.IsNaN(maxUT) || maxUT > now + OrbitSweepCapSeconds)
                maxUT = now + OrbitSweepCapSeconds;
            if (maxUT <= now) return none;

            double utCa, sepCa;
            if (!TrySweepClosestApproach(vesselOrbit, targetBody, now, maxUT, out utCa, out sepCa))
                return none;

            EncounterInfo info = default(EncounterInfo);
            info.Mode = EncounterMode.ClosestApproach;
            info.TimeSeconds = utCa - now;
            info.SeparationMeters = sepCa;
            return info;
        }

        /// <summary>
        /// Coarse sweep for the global minimum over [minUT, maxUT], then golden-section
        /// refinement inside the winning sample bucket. Distance is computed in the
        /// absolute frame so orbits around different bodies compare correctly.
        /// </summary>
        private static bool TrySweepClosestApproach(Orbit vesselOrbit, CelestialBody targetBody,
            double minUT, double maxUT, out double utCa, out double sepCa)
        {
            utCa = 0.0;
            sepCa = 0.0;

            try
            {
                double step = (maxUT - minUT) / CoarseSampleCount;
                if (step <= 0.0) return false;

                int best = -1;
                double bestDist = double.MaxValue;
                for (int i = 0; i <= CoarseSampleCount; i++)
                {
                    double t = minUT + i * step;
                    double d = AbsoluteSeparation(vesselOrbit, targetBody, t);
                    if (double.IsNaN(d) || double.IsInfinity(d)) continue;
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                if (best < 0) return false;

                // Refine within +/- one sample interval around the coarse minimum.
                double a = Math.Max(minUT, minUT + (best - 1) * step);
                double b = Math.Min(maxUT, minUT + (best + 1) * step);
                const double invPhi = 0.6180339887498949; // 1/phi
                double c = b - invPhi * (b - a);
                double d2 = a + invPhi * (b - a);
                double fc = AbsoluteSeparation(vesselOrbit, targetBody, c);
                double fd = AbsoluteSeparation(vesselOrbit, targetBody, d2);
                for (int iter = 0; iter < 40; iter++)
                {
                    if (double.IsNaN(fc) || double.IsNaN(fd)) break;
                    if (fc < fd)
                    {
                        b = d2; d2 = c; fd = fc;
                        c = b - invPhi * (b - a);
                        fc = AbsoluteSeparation(vesselOrbit, targetBody, c);
                    }
                    else
                    {
                        a = c; c = d2; fc = fd;
                        d2 = a + invPhi * (b - a);
                        fd = AbsoluteSeparation(vesselOrbit, targetBody, d2);
                    }
                }

                utCa = 0.5 * (a + b);
                sepCa = AbsoluteSeparation(vesselOrbit, targetBody, utCa);
                if (double.IsNaN(utCa) || double.IsNaN(sepCa) || double.IsInfinity(sepCa)) return false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static double AbsoluteSeparation(Orbit vesselOrbit, CelestialBody targetBody, double ut)
        {
            Vector3d vesselPos = vesselOrbit.getTruePositionAtUT(ut);
            Vector3d bodyPos = targetBody.getTruePositionAtUT(ut);
            return (vesselPos - bodyPos).magnitude;
        }
    }
}
