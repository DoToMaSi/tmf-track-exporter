using GBX.NET;

namespace TmfTrackConverter.Core;

public static class BlockPlacement
{
    public static Vec3 CellOrigin(Int3 coord, Int3 blockSize)
    {
        return new Vec3(coord.X * blockSize.X, coord.Y * blockSize.Y, coord.Z * blockSize.Z);
    }

    public static Vec3 CornerCompensation(Direction direction, Int3 blockSize)
    {
        var sx = blockSize.X;
        var sz = blockSize.Z;
        return direction switch
        {
            Direction.East => new Vec3(sx, 0, 0),
            Direction.South => new Vec3(sx, 0, sz),
            Direction.West => new Vec3(0, 0, sz),
            _ => Vec3.Zero
        };
    }

    public static Vec3 RotateY(Vec3 value, float yawDegrees)
    {
        var radians = yawDegrees * MathF.PI / 180f;
        return RotateY(value, MathF.Cos(radians), MathF.Sin(radians));
    }

    public static Vec3 RotateY(Vec3 value, float cos, float sin)
    {
        return new Vec3(
            value.X * cos - value.Z * sin,
            value.Y,
            value.X * sin + value.Z * cos);
    }

    public static float YawDegrees(Direction direction) => (int)direction * 90f;

    public static Vec3 InstancePosition(Int3 coord, Int3 blockSize, Direction direction, Vec3 localOrigin)
    {
        var yaw = YawDegrees(direction);
        return CellOrigin(coord, blockSize)
               + CornerCompensation(direction, blockSize)
               + RotateY(localOrigin, yaw);
    }

    public static Int3 RotateOffset(Int3 offset, Direction direction)
    {
        return direction switch
        {
            Direction.East => new Int3(-offset.Z, offset.Y, offset.X),
            Direction.South => new Int3(-offset.X, offset.Y, -offset.Z),
            Direction.West => new Int3(offset.Z, offset.Y, -offset.X),
            _ => offset
        };
    }

    public static IReadOnlyList<Int3> OccupiedCells(Int3 coord, Direction direction, IReadOnlyList<Int3> unitOffsets)
    {
        if (unitOffsets.Count == 0)
        {
            return [coord];
        }

        var cells = new Int3[unitOffsets.Count];
        for (var i = 0; i < unitOffsets.Count; i++)
        {
            var rotated = RotateOffset(unitOffsets[i], direction);
            cells[i] = new Int3(coord.X + rotated.X, coord.Y + rotated.Y, coord.Z + rotated.Z);
        }

        return cells;
    }
}
