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
        return direction switch
        {
            Direction.East => new Vec3(0, 0, blockSize.Z),
            Direction.South => new Vec3(blockSize.X, 0, blockSize.Z),
            Direction.West => new Vec3(blockSize.X, 0, 0),
            _ => Vec3.Zero
        };
    }

    public static Vec3 RotateY(Vec3 value, float yawDegrees)
    {
        var radians = yawDegrees * MathF.PI / 180f;
        var cos = MathF.Cos(radians);
        var sin = MathF.Sin(radians);
        return new Vec3(
            value.X * cos + value.Z * sin,
            value.Y,
            -value.X * sin + value.Z * cos);
    }

    public static Vec3 InstancePosition(Int3 coord, Int3 blockSize, Direction direction, Vec3 localOrigin)
    {
        var yaw = (int)direction * 90f;
        return CellOrigin(coord, blockSize)
               + CornerCompensation(direction, blockSize)
               + RotateY(localOrigin, yaw);
    }
}
