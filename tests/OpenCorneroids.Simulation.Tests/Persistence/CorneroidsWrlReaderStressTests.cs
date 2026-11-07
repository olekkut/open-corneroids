using System.IO.Compression;
using System.Numerics;
using Microsoft.Data.Sqlite;
using OpenCorneroids.Simulation.Persistence;
using OpenCorneroids.Simulation.Shapes;
using Xunit;

namespace OpenCorneroids.Simulation.Tests.Persistence;

/// <summary>
/// Empirical Challenger Stress & Combinatorial Fuzzing Test Suite for Milestone 1 (CorneroidsWrlReader).
/// Rigorously verifies:
/// 1. Fixed-point coordinate scaling accuracy across extreme values (-8388608000L to +8388608000L) and powers of 2.
/// 2. Combinatorial quarter-turn rotation matrix matching for all 64 possible combinations of (yaw, pitch, roll).
/// 3. Large multi-sector entities spanning complex bounding boxes (positive and negative coordinates).
/// 4. Preserved world positions of voxels and control consoles across entity grid shifts.
/// </summary>
public sealed class CorneroidsWrlReaderStressTests
{
    // =========================================================================
    // 1. FIXED-POINT COORDINATE SCALING ACCURACY
    // =========================================================================

    [Theory]
    [InlineData(-8388608000L, -1000f)]
    [InlineData(8388608000L, 1000f)]
    [InlineData(-83886080000L, -10000f)]
    [InlineData(83886080000L, 10000f)]
    [InlineData(0L, 0f)]
    [InlineData(8388608L, 1f)]
    [InlineData(-8388608L, -1f)]
    [InlineData(4194304L, 0.5f)]
    [InlineData(-4194304L, -0.5f)]
    [InlineData(12582912L, 1.5f)]
    [InlineData(2097152L, 0.25f)]
    [InlineData(1048576L, 0.125f)]
    [InlineData(524288L, 0.0625f)]
    public void FixedPoint_ExtremeAndFractionalValues_DecodeAccurately(long raw, float expected)
    {
        float decoded = CorneroidsWrlReader.DecodeCoord(raw);
        Assert.Equal(expected, decoded, 0.00001f);
    }

    [Fact]
    public void FixedPoint_SubMillimeterFineGrain_PrecisionPreserved()
    {
        // 1 raw tick = 1 / 8388608.0 = 2^-23 meters (~0.1192 micro-meters)
        float oneTick = CorneroidsWrlReader.DecodeCoord(1L);
        float minusOneTick = CorneroidsWrlReader.DecodeCoord(-1L);

        Assert.Equal(1f / 8388608f, oneTick);
        Assert.Equal(-1f / 8388608f, minusOneTick);
        Assert.NotEqual(0f, oneTick);
        Assert.NotEqual(0f, minusOneTick);
    }

    [Fact]
    public void FixedPoint_ExtremeRangeLimits_DecodeWithoutOverflowOrNan()
    {
        float maxLong = CorneroidsWrlReader.DecodeCoord(long.MaxValue);
        float minLong = CorneroidsWrlReader.DecodeCoord(long.MinValue);

        Assert.True(float.IsFinite(maxLong));
        Assert.True(float.IsFinite(minLong));
        Assert.True(maxLong > 0);
        Assert.True(minLong < 0);
    }

    [Fact]
    public void FixedPoint_FuzzRandomValuesAcrossExtremeRange_MatchesDoublePrecisionDivision()
    {
        var rng = new Random(424242);
        for (int i = 0; i < 5000; i++)
        {
            // Range: -8388608000L to +8388608000L (-1000m to +1000m)
            long raw = rng.NextInt64(-8388608000L, 8388608001L);

            double expectedDouble = raw / 8388608.0;
            float expectedFloat = (float)expectedDouble;
            float actual = CorneroidsWrlReader.DecodeCoord(raw);

            Assert.Equal(expectedFloat, actual);
            Assert.True(float.IsFinite(actual));
        }
    }

    [Fact]
    public void FixedPoint_InDatabaseEntitiesAndPlayer_DecodesExtremeCoordinatesInSnapshot()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();

        long extremeX = 8388608000L;  // +1000.0f
        long extremeY = -8388608000L; // -1000.0f
        long extremeZ = 4194304000L;  // +500.0f

        fixture.WithPlayer(1, "DeepSpaceVoyager", extremeX, extremeY, extremeZ);
        fixture.WithEntity(1, -extremeX, -extremeY, -extremeZ);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);

        Assert.Equal(1000f, snapshot.Player.Position.X, 0.001f);
        Assert.Equal(-1000f, snapshot.Player.Position.Y, 0.001f);
        Assert.Equal(500f, snapshot.Player.Position.Z, 0.001f);

        Assert.Single(snapshot.Ships);
        Assert.Equal(-1000f, snapshot.Ships[0].Position.X, 0.001f);
        Assert.Equal(1000f, snapshot.Ships[0].Position.Y, 0.001f);
        Assert.Equal(-500f, snapshot.Ships[0].Position.Z, 0.001f);
    }

    // =========================================================================
    // 2. COMBINATORIAL QUARTER-TURN ROTATION MATRIX MATCHING (ALL 64 COMBINATIONS)
    // =========================================================================

    [Fact]
    public void QuarterTurn_All64Combinations_MatchExactRotationMatrixOracle()
    {
        var visitedOrientations = new HashSet<int>();

        for (int yaw = 0; yaw < 4; yaw++)
        {
            for (int pitch = 0; pitch < 4; pitch++)
            {
                for (int roll = 0; roll < 4; roll++)
                {
                    byte b = (byte)(yaw | (pitch << 2) | (roll << 4));

                    // Independent mathematical oracle: R = R_y * R_x * R_z
                    float[,] expectedMatrix = ComputeExpectedRotationMatrix(yaw, pitch, roll);

                    int orientIndex = CorneroidsOrientation.FromByte(b);
                    Assert.InRange(orientIndex, 0, Orientation.Count - 1);
                    visitedOrientations.Add(orientIndex);

                    var orientation = Orientation.FromIndex(orientIndex);
                    int[,] actualMatrix = orientation.ToMatrix();

                    for (int r = 0; r < 3; r++)
                    {
                        for (int c = 0; c < 3; c++)
                        {
                            int expVal = (int)MathF.Round(expectedMatrix[r, c]);
                            int actVal = actualMatrix[r, c];
                            Assert.True(expVal == actVal,
                                $"Mismatch for (yaw={yaw}, pitch={pitch}, roll={roll}, byte={b}) at [{r}, {c}]: expected {expVal}, got {actVal}");
                        }
                    }
                }
            }
        }

        // All 24 proper rotations of a cube are covered by the 64 Euler angle combinations
        Assert.Equal(24, visitedOrientations.Count);
    }

    [Fact]
    public void QuarterTurn_UpperBitsMasking_EquivalenceAcrossAll256ByteValues()
    {
        for (int b = 0; b < 256; b++)
        {
            byte raw = (byte)b;
            byte masked = (byte)(b & 0x3F);

            int o1 = CorneroidsOrientation.FromByte(raw);
            int o2 = CorneroidsOrientation.FromByte(masked);

            Assert.Equal(o2, o1);
            Assert.InRange(o1, 0, 23);
        }
    }

    [Fact]
    public void QuarterTurn_InWorldImport_All64CombinationsCorrectlyTurnedOrOmitted()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);

        // Put 64 blocks along an 8x8 slice in sector 0, each having orientation byte 0..63
        var coords = new List<(int x, int y, int z)>();
        var blockIds = new List<byte>();
        var orientations = new List<byte>();

        for (int z = 0; z < 8; z++)
        {
            for (int y = 0; y < 8; y++)
            {
                coords.Add((0, y, z));
                blockIds.Add(42);
                orientations.Add((byte)(z * 8 + y));
            }
        }

        fixture.WithMultiBlockSector(1, 0, 0, 0, coords, [.. blockIds], [.. orientations]);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var ship = snapshot.Ships[0];

        // Ensure TurnedCell contains only non-zero orientations
        foreach (var turned in ship.Turned)
        {
            Assert.InRange(turned.Orientation, 1, 23);
        }

        // Check each block individually
        for (int z = 0; z < 8; z++)
        {
            for (int y = 0; y < 8; y++)
            {
                byte orientByte = (byte)(z * 8 + y);
                int expectedOrient = CorneroidsOrientation.FromByte(orientByte);
                var cell = new Int3(0, y, z);

                var turnedMatch = ship.Turned.FirstOrDefault(t => t.Cell == cell);
                if (expectedOrient == 0)
                {
                    Assert.Null(turnedMatch);
                }
                else
                {
                    Assert.NotNull(turnedMatch);
                    Assert.Equal(expectedOrient, turnedMatch.Orientation);
                }
            }
        }

        // Verify full round-trip through OpenCorneroids native .ocworld serializer
        using var ms = new MemoryStream();
        WorldSaveFormat.Write(ms, snapshot);
        ms.Position = 0;
        var reloaded = WorldSaveFormat.Read(ms);

        Assert.Equal(snapshot.Ships[0].Turned.Count, reloaded.Ships[0].Turned.Count);
    }

    // =========================================================================
    // 3. LARGE MULTI-SECTOR ENTITIES SPANNING COMPLEX BOUNDING BOXES
    // =========================================================================

    [Fact]
    public void MultiSector_EntitySpanningPositiveAndNegativeSectors_ShiftsAndSavesCleanly()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);

        // Sector A: (-2, -2, -2) -> min corner: gx = -32, gy = -32, gz = -32
        // Sector B: (+1, +2, +1) -> max corner: gx = 31, gy = 47, gz = 31
        // Total span: dimX = 31 - (-32) + 1 = 64
        //             dimY = 47 - (-32) + 1 = 80
        //             dimZ = 31 - (-32) + 1 = 64
        // Max dimension = 80 -> ship.Size must be 128 (next power of 2 >= 80)
        fixture.WithSingleBlockSector(1, -2, -2, -2, 0, 0, 0, blockId: 10, orientationByte: 0);
        fixture.WithSingleBlockSector(1, 1, 2, 1, 15, 15, 15, blockId: 20, orientationByte: 0);
        fixture.WithSingleBlockSector(1, 0, 0, 0, 5, 5, 5, blockId: 30, orientationByte: 0);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        Assert.Single(snapshot.Ships);
        var ship = snapshot.Ships[0];

        Assert.Equal(128, ship.Size);

        // Min corner (-32, -32, -32) shifted by -(-32, -32, -32) = (0, 0, 0)
        int minIdx = WorldSnapshot.CellIndex(ship.Size, 0, 0, 0);
        Assert.Equal(10, ship.Palette[ship.Cells[minIdx]]);

        // Max corner (31, 47, 31) shifted by -(-32, -32, -32) = (63, 79, 63)
        int maxIdx = WorldSnapshot.CellIndex(ship.Size, 63, 79, 63);
        Assert.Equal(20, ship.Palette[ship.Cells[maxIdx]]);

        // Center (5, 5, 5) shifted by -(-32, -32, -32) = (37, 37, 37)
        int centerIdx = WorldSnapshot.CellIndex(ship.Size, 37, 37, 37);
        Assert.Equal(30, ship.Palette[ship.Cells[centerIdx]]);

        // Verify serializability
        using var ms = new MemoryStream();
        WorldSaveFormat.Write(ms, snapshot);
        ms.Position = 0;
        var reloaded = WorldSaveFormat.Read(ms);
        Assert.Equal(128, reloaded.Ships[0].Size);
    }

    [Fact]
    public void MultiSector_ExtremeCorners128Span_SurvivesWithinMaxShipSize()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);

        // Sector (-4, -4, -4) local (0, 0, 0) -> gx = -64, gy = -64, gz = -64
        // Sector (+3, +3, +3) local (15, 15, 15) -> gx = 63, gy = 63, gz = 63
        // Span = 63 - (-64) + 1 = 128 in all 3 dimensions
        fixture.WithSingleBlockSector(1, -4, -4, -4, 0, 0, 0, blockId: 11, orientationByte: 0);
        fixture.WithSingleBlockSector(1, 3, 3, 3, 15, 15, 15, blockId: 22, orientationByte: 0);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var ship = snapshot.Ships[0];

        Assert.Equal(128, ship.Size);

        int minIdx = WorldSnapshot.CellIndex(ship.Size, 0, 0, 0);
        int maxIdx = WorldSnapshot.CellIndex(ship.Size, 127, 127, 127);

        Assert.Equal(11, ship.Palette[ship.Cells[minIdx]]);
        Assert.Equal(22, ship.Palette[ship.Cells[maxIdx]]);

        // Verify format compatibility with native save cap (MaxShipSize = 128)
        using var ms = new MemoryStream();
        WorldSaveFormat.Write(ms, snapshot);
        ms.Position = 0;
        var restored = WorldSaveFormat.Read(ms);
        Assert.Equal(128, restored.Ships[0].Size);
    }

    [Fact]
    public void MultiSector_DenseSectorWith4096Blocks_DecompressesAndMapsCorrectly()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();
        fixture.WithEntity(1, 0L, 0L, 0L);

        // All 4096 bits set (512 bytes of 0xFF)
        byte[] denseFormation = new byte[512];
        Array.Fill(denseFormation, (byte)0xFF);

        byte[] blockIds = new byte[4096];
        Array.Fill(blockIds, (byte)77);

        byte[] orientations = new byte[4096];
        for (int i = 0; i < 4096; i++)
            orientations[i] = (byte)(i % 64);

        byte[] formationCompressed = CorneroidsWrlFixtureBuilder.GzipCompress(denseFormation);
        byte[] blockIdsCompressed = CorneroidsWrlFixtureBuilder.GzipCompress(blockIds);
        byte[] orientationsCompressed = CorneroidsWrlFixtureBuilder.GzipCompress(orientations);

        fixture.WithRawSector(1, 0, 0, 0, formationCompressed, blockIdsCompressed, orientationsCompressed);

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var ship = snapshot.Ships[0];

        Assert.Equal(16, ship.Size);

        int nonAirCount = 0;
        foreach (var c in ship.Cells)
        {
            if (c != WorldSnapshot.Air) nonAirCount++;
        }
        Assert.Equal(4096, nonAirCount);
    }

    // =========================================================================
    // 4. PRESERVED WORLD POSITIONS OF VOXELS ACROSS GRID SHIFTING
    // =========================================================================

    [Fact]
    public void GridShift_ArbitraryPositionAndOrientation_VoxelWorldPositionsStrictlyInvariant()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();

        // Arbitrary entity position: P = (123.456, -789.012, 345.678)
        long rawX = (long)(123.456 * CorneroidsWrlReader.PositionScale);
        long rawY = (long)(-789.012 * CorneroidsWrlReader.PositionScale);
        long rawZ = (long)(345.678 * CorneroidsWrlReader.PositionScale);
        Vector3 initialEntityPos = CorneroidsWrlReader.DecodePosition(rawX, rawY, rawZ);

        // Arbitrary non-axial orientation: Yaw = 33 deg, Pitch = -21 deg, Roll = 55 deg
        var qYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 33f * MathF.PI / 180f);
        var qPitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -21f * MathF.PI / 180f);
        var qRoll = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 55f * MathF.PI / 180f);
        var rot = Quaternion.Normalize(qYaw * qPitch * qRoll);

        fixture.WithEntity(1, rawX, rawY, rawZ, rot.X, rot.Y, rot.Z, rot.W);

        // Place voxels across 3 distinct sectors:
        // Sector (-2, 0, 1): 2 voxels at (0, 0, 0) and (15, 10, 5)
        fixture.WithMultiBlockSector(1, -2, 0, 1,
            solidCoords: [(0, 0, 0), (15, 10, 5)],
            blockIds: [1, 2],
            orientations: [0, 0]);

        // Sector (0, 0, 0): 1 voxel at (7, 7, 7)
        fixture.WithSingleBlockSector(1, 0, 0, 0, 7, 7, 7, blockId: 3, orientationByte: 0);

        // Sector (1, -1, 0): 2 voxels at (2, 4, 8) and (15, 15, 15)
        fixture.WithMultiBlockSector(1, 1, -1, 0,
            solidCoords: [(2, 4, 8), (15, 15, 15)],
            blockIds: [4, 5],
            orientations: [0, 0]);

        var testVoxels = new (int gx, int gy, int gz, byte blockId)[]
        {
            (-2 * 16 + 0, 0 * 16 + 0, 1 * 16 + 0, 1),
            (-2 * 16 + 15, 0 * 16 + 10, 1 * 16 + 5, 2),
            (0 * 16 + 7, 0 * 16 + 7, 0 * 16 + 7, 3),
            (1 * 16 + 2, -1 * 16 + 4, 0 * 16 + 8, 4),
            (1 * 16 + 15, -1 * 16 + 15, 0 * 16 + 15, 5),
        };

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var ship = snapshot.Ships[0];

        // For each voxel, compute ground-truth world position before import:
        // W_orig = P + Rotate(g, rot)
        foreach (var v in testVoxels)
        {
            var originalGridPos = new Vector3(v.gx, v.gy, v.gz);
            Vector3 expectedWorldPos = initialEntityPos + Vector3.Transform(originalGridPos, rot);

            // In reconstructed ship:
            // Find which cell holds this blockId
            int palIdx = -1;
            for (int pIdx = 0; pIdx < ship.Palette.Count; pIdx++)
            {
                if (ship.Palette[pIdx] == v.blockId)
                {
                    palIdx = pIdx;
                    break;
                }
            }
            Assert.True(palIdx > 0, $"BlockId {v.blockId} not found in ship palette");

            // Find cell coordinate in ship grid
            bool found = false;
            for (int z = 0; z < ship.Size && !found; z++)
            {
                for (int y = 0; y < ship.Size && !found; y++)
                {
                    for (int x = 0; x < ship.Size && !found; x++)
                    {
                        if (ship.Cells[WorldSnapshot.CellIndex(ship.Size, x, y, z)] == palIdx)
                        {
                            var cellPos = new Vector3(x, y, z);
                            Vector3 reconstructedWorldPos = ship.Position + Vector3.Transform(cellPos, ship.Rotation);

                            float dist = Vector3.Distance(expectedWorldPos, reconstructedWorldPos);
                            Assert.True(dist < 0.001f,
                                $"World position drift for block {v.blockId}: expected {expectedWorldPos}, got {reconstructedWorldPos}, dist={dist}");
                            found = true;
                        }
                    }
                }
            }
            Assert.True(found, $"Cell for blockId {v.blockId} was not located in ship");
        }
    }

    [Fact]
    public void GridShift_ConsolesTargetsAndCamera_PreservedWorldPositions()
    {
        using var fixture = new CorneroidsWrlFixtureBuilder();
        fixture.WithWorldInfo();

        long rawX = 838860800L; // 100.0f
        long rawY = 0L;
        long rawZ = 0L;
        Vector3 initialEntityPos = new(100f, 0f, 0f);

        // 90 deg Yaw about Y
        float s = MathF.Sin(MathF.PI / 4f);
        float c = MathF.Cos(MathF.PI / 4f);
        var rot = new Quaternion(0f, s, 0f, c);

        fixture.WithEntity(1, rawX, rawY, rawZ, 0f, s, 0f, c);

        // Voxel at (-16, -16, -16) forces shift to (-16, -16, -16)
        fixture.WithSingleBlockSector(1, -1, -1, -1, 0, 0, 0, blockId: 1, orientationByte: 0);

        // Console at (-10, -10, -10) -> shifted cell is (6, 6, 6) in 16-cell grid
        fixture.WithConsole(id: 10, ownerEntityId: 1, posX: -10f, posY: -10f, posZ: -10f, bindingId: 99);

        // Controlled block at (-8, -12, -6) -> shifted cell is (8, 4, 10) in 16-cell grid
        fixture.WithControlledBlock(id: 100, boundTo: 10, bindingColor: 2, ownerEntityId: 1, posX: -8f, posY: -12f, posZ: -6f);

        // Camera block at (-12, -8, -14) -> shifted cell is (4, 8, 2) in 16-cell grid with id 99
        fixture.WithControlledBlock(id: 99, boundTo: 10, bindingColor: 0, ownerEntityId: 1, posX: -12f, posY: -8f, posZ: -14f);

        fixture.WithKeyMapping(1, 10, 2, 32); // Space

        var snapshot = CorneroidsWrlReader.ReadFromConnection(fixture.Connection);
        var ship = snapshot.Ships[0];

        Assert.Single(ship.Consoles);
        var console = ship.Consoles[0];

        // Console world position invariance
        Vector3 expectedConsoleWorld = initialEntityPos + Vector3.Transform(new Vector3(-10f, -10f, -10f), rot);
        Vector3 actualConsoleWorld = ship.Position + Vector3.Transform(console.Cell.ToVector3(), ship.Rotation);
        Assert.True(Vector3.Distance(expectedConsoleWorld, actualConsoleWorld) < 0.001f);

        // Camera world position invariance
        Assert.NotNull(console.Camera);
        Vector3 expectedCameraWorld = initialEntityPos + Vector3.Transform(new Vector3(-12f, -8f, -14f), rot);
        Vector3 actualCameraWorld = ship.Position + Vector3.Transform(console.Camera.Value.ToVector3(), ship.Rotation);
        Assert.True(Vector3.Distance(expectedCameraWorld, actualCameraWorld) < 0.001f);

        // Controlled target world position invariance
        var group = console.Groups.FirstOrDefault(g => g.Index == 2);
        Assert.NotNull(group);
        Assert.Single(group.Targets);
        Vector3 expectedTargetWorld = initialEntityPos + Vector3.Transform(new Vector3(-8f, -12f, -6f), rot);
        Vector3 actualTargetWorld = ship.Position + Vector3.Transform(group.Targets[0].ToVector3(), ship.Rotation);
        Assert.True(Vector3.Distance(expectedTargetWorld, actualTargetWorld) < 0.001f);
    }

    // =========================================================================
    // INDEPENDENT MATHEMATICAL ORACLE HELPERS
    // =========================================================================

    private static float[,] ComputeExpectedRotationMatrix(int yawQuarters, int pitchQuarters, int rollQuarters)
    {
        // R = R_y(yaw * 90) * R_x(pitch * 90) * R_z(roll * 90)
        float[,] rz = RotationMatrixZ(rollQuarters * MathF.PI / 2f);
        float[,] rx = RotationMatrixX(pitchQuarters * MathF.PI / 2f);
        float[,] ry = RotationMatrixY(yawQuarters * MathF.PI / 2f);

        return Multiply3x3(ry, Multiply3x3(rx, rz));
    }

    private static float[,] RotationMatrixZ(float theta)
    {
        float c = MathF.Cos(theta);
        float s = MathF.Sin(theta);
        return new float[3, 3]
        {
            { c, -s, 0 },
            { s,  c, 0 },
            { 0,  0, 1 }
        };
    }

    private static float[,] RotationMatrixX(float theta)
    {
        float c = MathF.Cos(theta);
        float s = MathF.Sin(theta);
        return new float[3, 3]
        {
            { 1, 0,  0 },
            { 0, c, -s },
            { 0, s,  c }
        };
    }

    private static float[,] RotationMatrixY(float theta)
    {
        float c = MathF.Cos(theta);
        float s = MathF.Sin(theta);
        return new float[3, 3]
        {
            {  c, 0, s },
            {  0, 1, 0 },
            { -s, 0, c }
        };
    }

    private static float[,] Multiply3x3(float[,] a, float[,] b)
    {
        var result = new float[3, 3];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                result[r, c] = a[r, 0] * b[0, c] + a[r, 1] * b[1, c] + a[r, 2] * b[2, c];
            }
        }
        return result;
    }
}
