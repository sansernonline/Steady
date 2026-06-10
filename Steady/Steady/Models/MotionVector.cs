namespace Steady.Models;

public sealed record MotionVector(double X, double Y, double Z)
{
    public static readonly MotionVector Zero = new(0, 0, 0);

    public MotionVector Lerp(MotionVector target, double factor) => new(
        X + (target.X - X) * factor,
        Y + (target.Y - Y) * factor,
        Z + (target.Z - Z) * factor);

    public MotionVector Scale(double factor) => new(X * factor, Y * factor, Z * factor);

    public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);
}
