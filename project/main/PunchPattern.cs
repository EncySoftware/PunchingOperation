using Geometry.VecMatrLib;
using STXMLPropTypes;

namespace PunchingOperationExtension;

public enum PunchPatternType
{
    Round,
    Rectangle,
    Star,
    Custom
}

public struct PunchPattern
{
    public PunchPatternType PatternType;
    public int SymmetriesCount;
    public double StartAngleOffset;
    public List<double> SymmetryAngles;

    public static PunchPattern LoadFromXMLProp(IST_XMLPropPointer props)
    {
        var result = default(PunchPattern);
        switch (props.Int["Pattern"])
        {
            case 0:
                result.PatternType = PunchPatternType.Round;
                result.SymmetriesCount = 0;
                break;
            case 1:
                result.PatternType = PunchPatternType.Rectangle;
                result.SymmetriesCount = 4;
                result.SymmetryAngles = [0, Math.PI/2, Math.PI, 3*Math.PI/2];
                break;
            case 2:
                result.PatternType = PunchPatternType.Star;
                result.SymmetriesCount = props.Int["RayCount"];
                result.SymmetryAngles = new();
                for (int i = 0; i < result.SymmetriesCount; i++)
                    result.SymmetryAngles.Add(2*Math.PI/result.SymmetriesCount);
                break;
            case 3:
                result.PatternType = PunchPatternType.Custom;
                var symAngles = props.Str["SymmetryAngles"].Split(';');
                result.SymmetriesCount = symAngles.Length;
                result.SymmetryAngles = new();
                for (int i = 0; i < result.SymmetriesCount; i++)
                {
                    double ang = 0;
                    if (!Double.TryParse(symAngles[i], out ang))
                        ang = 0;
                    result.SymmetryAngles.Add(Math.PI/180 * ang);
                }
                break;
        }
        result.StartAngleOffset = Math.PI/180 * props.Flt["StartAngleOffset"];
        return result;
    }

    public bool Is5D => SymmetriesCount == 0;
}