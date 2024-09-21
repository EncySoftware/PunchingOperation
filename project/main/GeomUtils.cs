using Geometry.VecMatrLib;
using STTypes;

namespace ExtensionUtilityToolPathCalculationNet;

public static class GeomUtils
{
    public static T3DMatrix ToVML(this TST3DMatrix value)
    {
        return new()
        {
            vX = new T3DPoint
            {
                X = value.vX.X,
                Y = value.vX.Y,
                Z = value.vX.Z
            },
            A = value.A,
            vY = new T3DPoint
            {
                X = value.vY.X,
                Y = value.vY.Y,
                Z = value.vY.Z
            },
            B = value.B,
            vZ = new T3DPoint
            {
                X = value.vZ.X,
                Y = value.vZ.Y,
                Z = value.vZ.Z
            },
            C = value.C,
            vT = new T3DPoint
            {
                X = value.vT.X,
                Y = value.vT.Y,
                Z = value.vT.Z
            },
            D = value.D
        };
    }

    public static void FromVML(this TST3DMatrix self, T3DMatrix value)
    {
        self.vX.X = value.vX.X;
        self.vX.Y = value.vX.Y;
        self.vX.Z = value.vX.Z;
        self.A = value.A;
        self.vY.X = value.vY.X;
        self.vY.Y = value.vY.Y;
        self.vY.Z = value.vY.Z;
        self.B = value.B;
        self.vZ.X = value.vZ.X;
        self.vZ.Y = value.vZ.Y;
        self.vZ.Z = value.vZ.Z;
        self.C = value.C;
        self.vT.X = value.vT.X;
        self.vT.Y = value.vT.Y;
        self.vT.Z = value.vT.Z;
        self.D = value.D;
    }
}