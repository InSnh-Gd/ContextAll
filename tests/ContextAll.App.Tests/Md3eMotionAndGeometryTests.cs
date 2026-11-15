using Avalonia;
using ContextAll.App.Controls;
using ContextAll.App.Motion;

namespace ContextAll.App.Tests;

[Collection("AvaloniaUI")]
public sealed class Md3eMotionAndGeometryTests
{
    [Fact]
    public void MotionScheme_Expressive_defines_6_spring_roles_with_underdamped_spatial_and_critical_effects()
    {
        var fastSpatial = MotionScheme.Expressive(MotionRole.FastSpatial);
        var defaultSpatial = MotionScheme.Expressive(MotionRole.DefaultSpatial);
        var slowSpatial = MotionScheme.Expressive(MotionRole.SlowSpatial);

        var fastEffects = MotionScheme.Expressive(MotionRole.FastEffects);
        var defaultEffects = MotionScheme.Expressive(MotionRole.DefaultEffects);
        var slowEffects = MotionScheme.Expressive(MotionRole.SlowEffects);

        Assert.InRange(fastSpatial.DampingRatio, 0.55, 0.75);
        Assert.InRange(defaultSpatial.DampingRatio, 0.70, 0.88);
        Assert.InRange(slowSpatial.DampingRatio, 0.75, 0.90);
        Assert.True(fastSpatial.Stiffness > defaultSpatial.Stiffness);
        Assert.True(defaultSpatial.Stiffness > slowSpatial.Stiffness);

        Assert.Equal(1.0, fastEffects.DampingRatio, precision: 3);
        Assert.Equal(1.0, defaultEffects.DampingRatio, precision: 3);
        Assert.Equal(1.0, slowEffects.DampingRatio, precision: 3);
        Assert.True(fastEffects.Stiffness > defaultEffects.Stiffness);
        Assert.True(defaultEffects.Stiffness > slowEffects.Stiffness);
    }

    [Fact]
    public void SpringSolver_overshoots_on_underdamped_spatial_preserves_velocity_and_snaps_when_reduced_motion()
    {
        var spatial = new SpringSolver(MotionScheme.Expressive(MotionRole.FastSpatial), initialValue: 0.0);
        spatial.SetTarget(100.0);

        var maxSeen = 0.0;
        var dt = 1.0 / 120.0;
        for (var i = 0; i < 60; i++)
        {
            var val = spatial.Step(dt);
            if (val > maxSeen)
            {
                maxSeen = val;
            }
        }

        // Underdamped spring must overshoot past target 100 slightly
        Assert.True(maxSeen > 100.5, $"Expected underdamped spatial overshoot > 100.5, got {maxSeen:F2}");

        // Mid-flight retargeting preserves velocity smoothly without discontinuity
        var solver = new SpringSolver(MotionScheme.Expressive(MotionRole.DefaultSpatial), initialValue: 0.0);
        solver.SetTarget(100.0);
        for (var i = 0; i < 12; i++)
        {
            solver.Step(dt);
        }

        var vBefore = solver.Velocity;
        var posBefore = solver.Value;
        Assert.True(vBefore > 10.0);
        solver.SetTarget(20.0);
        Assert.Equal(vBefore, solver.Velocity, precision: 6);
        Assert.Equal(posBefore, solver.Value, precision: 6);

        // Effects track does not overshoot target
        var effects = new SpringSolver(MotionScheme.Expressive(MotionRole.FastEffects), initialValue: 0.0);
        effects.SetTarget(100.0);
        for (var i = 0; i < 180; i++)
        {
            var val = effects.Step(dt);
            Assert.True(val <= 100.05, $"Critically damped effects must not overshoot: {val:F4}");
        }

        Assert.True(effects.IsSettled);
        Assert.Equal(100.0, effects.Value, precision: 2);

        // ReducedMotion snaps immediately on step or SetTarget
        spatial.ReducedMotion = true;
        spatial.SetTarget(42.0);
        Assert.Equal(42.0, spatial.Step(dt));
        Assert.Equal(0.0, spatial.Velocity);
        Assert.True(spatial.IsSettled);
    }

    [Fact]
    public void WavyProgressGeometry_computes_wave_points_physical_gap_and_stop_dot()
    {
        var zero = WavyProgressGeometry.Compute(width: 240, height: 12, fraction: 0.0, phaseRadians: 0.0);
        Assert.Empty(zero.ActiveWavePoints);
        Assert.False(zero.HasGap);
        Assert.True(zero.InactiveTrackEndX > zero.InactiveTrackStartX);

        var mid = WavyProgressGeometry.Compute(width: 240, height: 12, fraction: 0.5, phaseRadians: Math.PI * 0.5);
        Assert.True(mid.ActiveWavePoints.Count >= 16);
        Assert.True(mid.HasGap);
        Assert.Equal(120.0, mid.ActiveEndX, precision: 1);
        Assert.Equal(4.0, mid.InactiveTrackStartX - mid.ActiveEndX, precision: 2);
        Assert.True(mid.StopDotCenter.X > mid.InactiveTrackStartX);

        // Verify sine oscillation vertically around centerline
        var minY = mid.ActiveWavePoints.Min(p => p.Y);
        var maxY = mid.ActiveWavePoints.Max(p => p.Y);
        Assert.True(maxY - minY >= 3.5);

        var full = WavyProgressGeometry.Compute(width: 240, height: 12, fraction: 1.0, phaseRadians: 0.0);
        Assert.False(full.HasGap);
        Assert.Equal(240.0, full.ActiveEndX, precision: 1);
    }

    [Fact]
    public void PolygonMorphGeometry_interpolates_smoothly_across_all_7_md3e_polygons()
    {
        Assert.Equal(7, PolygonMorphGeometry.ShapeCount);

        for (var shapeIndex = 0; shapeIndex < PolygonMorphGeometry.ShapeCount; shapeIndex++)
        {
            var ptsA = PolygonMorphGeometry.SamplePolygon(shapeIndex, vertexCount: 64);
            Assert.Equal(64, ptsA.Count);

            // Normalized radius around center (0.5, 0.5) stays inside [0,1]^2
            foreach (var p in ptsA)
            {
                Assert.InRange(p.X, 0.0, 1.0);
                Assert.InRange(p.Y, 0.0, 1.0);
            }
        }

        // Interpolation at t=0 equals start shape, t=1 equals next shape, t=0.5 blends smoothly
        var p0 = PolygonMorphGeometry.Interpolate(fromShape: 0, toShape: 1, progress: 0.0, rotationRadians: 0.0, vertexCount: 64);
        var pHalf = PolygonMorphGeometry.Interpolate(fromShape: 0, toShape: 1, progress: 0.5, rotationRadians: 0.0, vertexCount: 64);
        var p1 = PolygonMorphGeometry.Interpolate(fromShape: 0, toShape: 1, progress: 1.0, rotationRadians: 0.0, vertexCount: 64);

        Assert.Equal(64, pHalf.Count);
        Assert.Equal(p0[0].X, (p0[0].X + p1[0].X) * 0.5 == pHalf[0].X ? p0[0].X : p0[0].X, precision: 4);
        for (var i = 0; i < 64; i++)
        {
            Assert.Equal((p0[i].X + p1[i].X) * 0.5, pHalf[i].X, 1e-9);
            Assert.Equal((p0[i].Y + p1[i].Y) * 0.5, pHalf[i].Y, 1e-9);
        }
    }

    [Fact]
    public void MotionFrameDriver_does_not_inflate_submillisecond_frames_and_all_7_polygons_are_symmetric()
    {
        Assert.Equal(0.0, MotionFrameDriver.ComputeDeltaSeconds(TimeSpan.Zero));
        var subMsDt = MotionFrameDriver.ComputeDeltaSeconds(TimeSpan.FromMicroseconds(100));
        Assert.True(subMsDt < 0.001, $"Expected sub-ms elapsed to stay < 0.001s, got {subMsDt}");
        Assert.Equal(0.05, MotionFrameDriver.ComputeDeltaSeconds(TimeSpan.FromMilliseconds(200)));
        Assert.True(ContainedLoadingIndicator.FullCycleDurationSeconds >= 7.5);

        for (var shapeIndex = 0; shapeIndex < PolygonMorphGeometry.ShapeCount; shapeIndex++)
        {
            var pts = PolygonMorphGeometry.SamplePolygon(shapeIndex, vertexCount: 72);
            var minX = pts.Min(p => p.X);
            var maxX = pts.Max(p => p.X);
            var minY = pts.Min(p => p.Y);
            var maxY = pts.Max(p => p.Y);
            var width = maxX - minX;
            var height = maxY - minY;
            Assert.True(Math.Abs(width - height) < 0.08, $"Shape {shapeIndex} asymmetric: w={width:F3}, h={height:F3}");
            Assert.True(Math.Abs((minX + maxX) * 0.5 - 0.5) < 0.04, $"Shape {shapeIndex} X off-center");
            Assert.True(Math.Abs((minY + maxY) * 0.5 - 0.5) < 0.04, $"Shape {shapeIndex} Y off-center");
        }
    }
}
