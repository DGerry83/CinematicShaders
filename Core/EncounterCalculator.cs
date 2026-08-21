using System;
using System.Collections.Generic;
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
    /// Times are seconds until the event; separations/altitudes are meters.
    /// </summary>
    public struct EncounterInfo
    {
        public EncounterMode Mode;
        public double TimeSeconds;
        public double SeparationMeters;      // ClosestApproach only
        public bool SoiEntering;             // SoiChange only: true = entering target SOI, false = leaving it
        public bool HasNewSoiPeriapsis;      // SoiChange only
        public double NewSoiPeriapsisMeters; // SoiChange only: periapsis altitude in the SOI being entered
        public bool HasSecondApproach;       // ClosestApproach only
        public double SecondTimeSeconds;
        public double SecondSeparationMeters;
    }

    /// <summary>
    /// Computes "time to encounter" metrics for the active vessel's current target,
    /// replicating stock map-view behavior (OrbitTargeter / PatchedConics) using public
    /// KSP APIs only. See ReferenceNotes/active/2026-08-18_039Update_PlanContinuation/SECTION5_SPEC.md.
    ///
    /// Mode priority: IMPACT (target body only, current patch) &gt; SOI change (target-relevant
    /// transitions only — this is a target tracker, not a general orbit tracker) &gt; closest approach.
    /// Impact is an estimate: sea-level radius crossing, no terrain elevation or atmosphere.
    ///
    /// Chain hygiene notes (verified against the KSP 1.12 decompilation):
    /// - The solver never nulls Orbit.nextPatch; stale patch objects keep old transitions.
    ///   Only links whose patch was re-solved this frame (activePatch) may be followed.
    /// - patches[0].StartUT is rewritten to "now" every solver frame — a stale value means
    ///   the whole chain is untrustworthy.
    /// - The main orbit chain is always the no-maneuver trajectory; with maneuver nodes the
    ///   maneuver-adjusted prediction lives in PatchedConicSolver.flightPlan.
    /// </summary>
    public static class EncounterCalculator
    {
        // CONIC_PATCH_LIMIT defaults to 3, but users can raise it; cap chain walks so a
        // malformed chain can never loop forever.
        private const int MaxPatchChainLinks = 8;

        // Stock solver parameters (PatchedConics.SolverParameters defaults / OrbitTargeter call site).
        private const double SolverEpsilon = 0.0001;
        private const int MaxGeometryIterations = 20;

        // The solver rewrites patches[0].StartUT every frame it runs. Tolerance must exceed
        // one frame of UT at maximum rails warp (100,000x ≈ 1,700 UT-s per frame at 60 FPS).
        private const double FreshnessToleranceSeconds = 3600.0;

        // Window cap for the orbit-to-orbit sweep when the synodic period is undefined
        // (hyperbolic) or the patch never ends (FINAL hyperbolic, EndUT = +infinity).
        private const double OrbitSweepCapSeconds = 365.0 * 24.0 * 3600.0; // 1 year, display estimate only
        private const int CoarseSampleCount = 64;

        /// <summary>
        /// Compute encounter info for the given target. Returns Mode == None when no
        /// valid metric exists (no target, landed, stale chain, no encounter).
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

            List<Orbit> chain = GetOurPatchChain(activeVessel, vesselOrbit, now);
            if (chain.Count == 0) return none; // stale/invalid chain

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

            // Priority 2: SOI change — target-relevant transitions only.
            EncounterInfo soi;
            if (TryGetSoiChange(chain, targetBody, now, out soi)) return soi;

            // Priority 3: closest approach.
            if (targetVessel != null)
                return VesselClosestApproach(chain, targetVessel, now);
            return BodyClosestApproach(chain, targetBody, now);
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
        /// Build the active vessel's effective patch chain: the maneuver-adjusted flight
        /// plan when maneuver nodes exist, otherwise the live no-maneuver chain. Guards
        /// against stale links (the solver reuses patch objects and never nulls nextPatch;
        /// only activePatch-marked links were re-solved this frame).
        /// </summary>
        private static List<Orbit> GetOurPatchChain(Vessel vessel, Orbit vesselOrbit, double now)
        {
            List<Orbit> chain = new List<Orbit>();

            PatchedConicSolver solver = vessel.patchedConicSolver;
            if (solver == null)
            {
                // Patched conics not unlocked (early career): current orbit only, always fresh.
                chain.Add(vesselOrbit);
                return chain;
            }

            // Freshness gate: StartUT is rewritten to now on every solver frame.
            if (double.IsNaN(vesselOrbit.StartUT) ||
                Math.Abs(now - vesselOrbit.StartUT) > FreshnessToleranceSeconds)
                return chain;

            List<Orbit> flightPlan = solver.flightPlan;
            if (flightPlan != null && flightPlan.Count > 0)
            {
                // flightPlan = pre-node main-chain patches + one post-maneuver patch per node.
                foreach (Orbit p in flightPlan)
                {
                    if (p == null || chain.Contains(p)) continue;
                    chain.Add(p);
                    if (chain.Count >= MaxPatchChainLinks) return chain;
                }
                // Continuation patches after the last flight-plan entry.
                Orbit last = chain[chain.Count - 1];
                for (Orbit p = last.nextPatch;
                     p != null && p.activePatch && chain.Count < MaxPatchChainLinks;
                     p = p.nextPatch)
                {
                    chain.Add(p);
                }
                return chain;
            }

            chain.Add(vesselOrbit);
            for (Orbit p = vesselOrbit.nextPatch;
                 p != null && p.activePatch && chain.Count < MaxPatchChainLinks;
                 p = p.nextPatch)
            {
                chain.Add(p);
            }
            return chain;
        }

        /// <summary>
        /// Walk another vessel's patch chain with the same stale-link guard.
        /// </summary>
        private static List<Orbit> GetFreshChain(Orbit first, int cap)
        {
            List<Orbit> chain = new List<Orbit>();
            if (first == null) return chain;
            chain.Add(first);
            for (Orbit p = first.nextPatch; p != null && p.activePatch && chain.Count < cap; p = p.nextPatch)
                chain.Add(p);
            return chain;
        }

        /// <summary>
        /// Impact = next crossing of the target body's sea-level radius on the current patch.
        /// Stock never terminates patches at the surface (PatchTransitionType.IMPACT is a
        /// dead enum), so this is hand-computed. Estimate only: ignores terrain height.
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
        /// SOI mode is target-relevant only: entering the TARGET body's SOI (ENCOUNTER into
        /// it) or leaving it (ESCAPE from it). The first target-relevant transition in the
        /// chain wins, even if an unrelated transition happens sooner. Also reports the
        /// periapsis altitude in the SOI being transitioned into.
        /// </summary>
        private static bool TryGetSoiChange(List<Orbit> chain, CelestialBody targetBody,
            double now, out EncounterInfo info)
        {
            info = default(EncounterInfo);
            if (targetBody == null) return false; // vessel targets never trigger SOI mode

            for (int i = 0; i < chain.Count; i++)
            {
                Orbit p = chain[i];
                if (double.IsNaN(p.EndUT) || double.IsInfinity(p.EndUT)) continue;

                bool entering = p.patchEndTransition == Orbit.PatchTransitionType.ENCOUNTER &&
                                p.nextPatch != null && p.nextPatch.referenceBody == targetBody;
                bool leaving = p.patchEndTransition == Orbit.PatchTransitionType.ESCAPE &&
                               p.referenceBody == targetBody;
                if (!entering && !leaving) continue;

                info.Mode = EncounterMode.SoiChange;
                info.TimeSeconds = p.EndUT - now;
                info.SoiEntering = entering;

                // Periapsis in the new SOI (the patch transitioned into).
                Orbit np = p.nextPatch;
                if (np != null && np.referenceBody != null)
                {
                    double peA = np.PeA;
                    if (!double.IsNaN(peA) && !double.IsInfinity(peA))
                    {
                        info.HasNewSoiPeriapsis = true;
                        info.NewSoiPeriapsisMeters = peA;
                    }
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Vessel target: replicate stock OrbitTargeter behavior — find the first pair of
        /// patches (ours x target's) sharing a reference body and solve for intercepts on
        /// it. Up to two solutions (rendezvous orbits can cross twice).
        /// </summary>
        private static EncounterInfo VesselClosestApproach(List<Orbit> ourChain, Vessel targetVessel, double now)
        {
            EncounterInfo none = default(EncounterInfo);
            if (!IsOrbiting(targetVessel)) return none;
            if (targetVessel.orbit == null) return none;

            List<Orbit> targetChain = GetFreshChain(targetVessel.orbit, MaxPatchChainLinks);

            for (int i = 0; i < ourChain.Count; i++)
            {
                for (int j = 0; j < targetChain.Count; j++)
                {
                    if (targetChain[j].referenceBody != ourChain[i].referenceBody) continue;

                    // First same-body pair only (matches stock's FindPatch behavior).
                    EncounterInfo found;
                    if (TryVesselInterceptOnPatches(ourChain[i], targetChain[j], now, out found)) return found;
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
                validB = false;
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
            if (ut < now) return false;                     // past solution (e.g. hyperbolic post-Pe)
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
        /// that patch's periapsis. Otherwise (no encounter) sweep each chain patch against
        /// the body's own orbit — stock shows this via approach markers, but its solver is
        /// not frame-safe for cross-SOI inputs, so we sample in the absolute
        /// (getTruePositionAtUT) frame instead.
        /// </summary>
        private static EncounterInfo BodyClosestApproach(List<Orbit> chain, CelestialBody targetBody, double now)
        {
            EncounterInfo none = default(EncounterInfo);
            if (targetBody == null) return none;

            for (int i = 0; i < chain.Count; i++)
            {
                Orbit p = chain[i];
                if (p.referenceBody != targetBody) continue;

                double utPe;
                if (i == 0)
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

            // No encounter with the body in the chain: orbit-vs-orbit sweep.
            // (Kerbol's own GetOrbit() may be null — guard.)
            if (targetBody.GetOrbit() == null) return none;

            double utCa, sepCa;
            if (!TrySweepClosestApproach(chain, targetBody, now, out utCa, out sepCa))
                return none;

            EncounterInfo info = default(EncounterInfo);
            info.Mode = EncounterMode.ClosestApproach;
            info.TimeSeconds = utCa - now;
            info.SeparationMeters = sepCa;
            return info;
        }

        /// <summary>
        /// Per-patch coarse sweep for the global distance minimum over each patch's validity
        /// window, then golden-section refinement inside the winning sample bucket. Distance
        /// is computed in the absolute frame so orbits around different bodies compare
        /// correctly.
        /// </summary>
        private static bool TrySweepClosestApproach(List<Orbit> chain, CelestialBody targetBody,
            double now, out double utCa, out double sepCa)
        {
            utCa = 0.0;
            sepCa = 0.0;

            try
            {
                // Coarse sweep over every chain patch's own window; track the global best.
                Orbit bestPatch = null;
                double bestT = 0.0, bestDist = double.MaxValue, bestStep = 0.0;
                double bestWindowStart = 0.0, bestWindowEnd = 0.0;

                for (int i = 0; i < chain.Count; i++)
                {
                    Orbit p = chain[i];
                    double wStart = Math.Max(p.StartUT, now);
                    double wEnd = p.EndUT;

                    double syn = Orbit.SynodicPeriod(p, targetBody.GetOrbit());
                    if (!double.IsNaN(syn) && !double.IsInfinity(syn) && syn > 0.0)
                        wEnd = Math.Min(wEnd, now + syn);
                    if (double.IsNaN(wEnd) || double.IsInfinity(wEnd) || wEnd > now + OrbitSweepCapSeconds)
                        wEnd = now + OrbitSweepCapSeconds;
                    if (wEnd <= wStart) continue;

                    double step = (wEnd - wStart) / CoarseSampleCount;
                    for (int s = 0; s <= CoarseSampleCount; s++)
                    {
                        double t = wStart + s * step;
                        double d = AbsoluteSeparation(p, targetBody, t);
                        if (double.IsNaN(d) || double.IsInfinity(d)) continue;
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestT = t;
                            bestStep = step;
                            bestPatch = p;
                            bestWindowStart = wStart;
                            bestWindowEnd = wEnd;
                        }
                    }
                }
                if (bestPatch == null) return false;

                // Refine within +/- one sample interval around the coarse minimum.
                double a = Math.Max(bestWindowStart, bestT - bestStep);
                double b = Math.Min(bestWindowEnd, bestT + bestStep);
                const double invPhi = 0.6180339887498949; // 1/phi
                double c = b - invPhi * (b - a);
                double d2 = a + invPhi * (b - a);
                double fc = AbsoluteSeparation(bestPatch, targetBody, c);
                double fd = AbsoluteSeparation(bestPatch, targetBody, d2);
                for (int iter = 0; iter < 40; iter++)
                {
                    if (double.IsNaN(fc) || double.IsNaN(fd)) break;
                    if (fc < fd)
                    {
                        b = d2; d2 = c; fd = fc;
                        c = b - invPhi * (b - a);
                        fc = AbsoluteSeparation(bestPatch, targetBody, c);
                    }
                    else
                    {
                        a = c; c = d2; fc = fd;
                        d2 = a + invPhi * (b - a);
                        fd = AbsoluteSeparation(bestPatch, targetBody, d2);
                    }
                }

                utCa = 0.5 * (a + b);
                sepCa = AbsoluteSeparation(bestPatch, targetBody, utCa);
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
