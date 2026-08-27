namespace CivilDynamoTools.Diagnostics.Models;

/// <summary>
/// Plain diagnostic snapshot of one AeccParcelSegmentElement, copied out of COM
/// before any reference-counting or Dynamo formatting happens.
/// </summary>
public sealed class ParcelSegmentElementData
{
    public string ParcelName { get; set; } = string.Empty;
    public string? ParcelNumber { get; set; }

    public int LoopIndex { get; set; }
    public int ElementIndex { get; set; }

    public string ParentSegmentIdentity { get; set; } = string.Empty;

    public double StartX { get; set; }
    public double StartY { get; set; }
    public double EndX { get; set; }
    public double EndY { get; set; }

    public bool IsLine { get; set; }
    public bool IsCurve { get; set; }
    public string ElementTypeName { get; set; } = string.Empty;

    public double Length { get; set; }

    public string MatchingKey { get; set; } = string.Empty;
    public bool WasReversedRelativeToCanonical { get; set; }
    public int ReferenceCount { get; set; }

    public List<string> Warnings { get; } = new();

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["ParcelName"] = ParcelName,
        ["ParcelNumber"] = (object?)ParcelNumber ?? string.Empty,
        ["LoopIndex"] = LoopIndex,
        ["ElementIndex"] = ElementIndex,
        ["ParentSegmentIdentity"] = ParentSegmentIdentity,
        ["StartX"] = StartX,
        ["StartY"] = StartY,
        ["EndX"] = EndX,
        ["EndY"] = EndY,
        ["IsLine"] = IsLine,
        ["IsCurve"] = IsCurve,
        ["ElementTypeName"] = ElementTypeName,
        ["Length"] = Length,
        ["MatchingKey"] = MatchingKey,
        ["WasReversedRelativeToCanonical"] = WasReversedRelativeToCanonical,
        ["ReferenceCount"] = ReferenceCount,
        ["Warnings"] = Warnings.ToArray(),
    };
}
