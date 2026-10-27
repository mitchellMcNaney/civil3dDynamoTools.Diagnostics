# CivilDynamoTools.Diagnostics

**Civil 3D parcel topology analysis and automated angle annotation for Dynamo**

CivilDynamoTools.Diagnostics is a C#/.NET package that extends Dynamo for Autodesk Civil 3D with tools for inspecting native parcel topology, analyzing parcel geometry, and generating angle annotations. It was developed to handle parcel boundaries that are difficult to process reliably using reconstructed Dynamo `PolyCurve` geometry, including shared boundaries, split segments, short elements, and curves.

## Features

- **Native parcel topology inspection:** Reads parcel loops and segment elements through the Civil 3D managed and COM APIs without rebuilding the source geometry as Dynamo curves.
- **Shared-boundary diagnostics:** Counts how many parcels in a site reference a segment element, helping distinguish exterior and shared boundaries.
- **Segment-order analysis:** Checks whether parcel segments form connected loops and reports orientation, gaps, and reversed segments.
- **Corner analysis:** Classifies vertices, calculates interior angles, and flags cases that cannot be evaluated reliably, such as curve-adjacent vertices.
- **Single-parcel angle annotations:** Creates formatted angle labels for eligible line-to-line corners, with placement along the interior angle bisector.
- **Site-wide angle annotations:** Processes an entire site while applying rules for right-of-way parcels, exterior-boundary corners, supplementary angles at shared junctions, and optional square-chamfer exclusions.
- **Repeatable labeling:** Re-running the annotation nodes replaces the package's previously generated labels for the affected parcels rather than accumulating duplicates.

## Technology

- C# and .NET
- Autodesk Civil 3D managed API and legacy COM API
- Dynamo for Civil 3D
- AutoCAD database transactions and annotative `MText`
- Unit-tested, Autodesk-independent geometry logic

## Architecture

The project separates Civil 3D integration from geometry calculations and annotation output:

```text
Dynamo nodes
    |
    +--> Civil 3D parcel input and API interoperability
    |        |
    |        +--> Native parcel loops and segment data
    |                    |
    +--> Geometry and topology analysis (plain C#)
    |        |
    |        +--> Segment order, corner angles, label planning
    |                    |
    +--> Dynamo diagnostic results / AutoCAD MText annotations
```

COM interaction is isolated within the interoperability layer. Geometry analysis and label planning are implemented separately from AutoCAD object creation, making core logic easier to test and maintain.

## Dynamo nodes

| Node | Purpose |
| --- | --- |
| `Parcel.SegmentReferenceCounts` | Reports parcel segment elements and their reference counts across a site. |
| `Parcel.SegmentOrder` | Inspects loop connectivity, orientation, and segment direction. |
| `Parcel.Corners` | Reports corner classifications and calculated angles without creating labels. |
| `Parcel.LabelAngles` | Creates angle annotations for an individual parcel. |
| `Parcel.LabelSiteAngles` | Creates angle annotations across a site with additional filtering rules. |

## Angle-labeling behavior

The labeling tools use degrees, minutes, and seconds (DMS) and place text along the interior angle bisector. They skip right angles, support existing text styles, and create annotative `MText` using the current annotation scale.

Site-wide labeling can also skip right-of-way parcels and corners formed by two exterior-boundary lines, resolve selected supplementary-angle duplicates at shared junctions, and optionally omit matching 135° square chamfers. Labels are ordinary AutoCAD `MText`, **not** Civil 3D label-style objects.

## Compatibility and setup

The package has been tested in Autodesk Civil 3D 2026 and 2027 on the development environments used for this project. The current build targets .NET 10 and depends on the Civil 3D and AutoCAD assemblies provided by the host installation. Compatibility with other builds is not guaranteed.

To build and load the package:

1. Install a compatible version of Civil 3D with Dynamo and the .NET SDK required by the project.
2. Clone this repository and configure the local Autodesk assembly paths through the project's build properties or supported environment variables.
3. Build the solution and run the geometry unit tests:

   ```powershell
   dotnet build civil3dNodes.slnx -c Release
   dotnet test civil3dNodes.slnx
   ```

4. Run `package/Build-Package.ps1` to assemble the Dynamo package under `dist/`.
5. In Dynamo, add the generated package folder under **Preferences > Package Settings > Node and Package Paths**.
6. Restart Dynamo if necessary, then locate the package's parcel nodes in the library.

> Build configuration and installation paths depend on the installed Civil 3D version. Review `Directory.Build.props` before building. This project does not redistribute Autodesk assemblies.

## Testing and current limitations

The geometry analysis and label-planning components have unit tests. Parcel topology inspection, corner calculations, and annotation behavior have also been exercised in Civil 3D 2026; the assembled package has been tested in Civil 3D 2026 and 2027.

Known limitations include:

- Curve-adjacent vertices are reported but do not currently receive angle labels.
- Multi-loop parcel classification (outer boundary versus holes) requires additional real-world validation.
- Site-wide right-of-way detection uses parcel names or style names rather than a dedicated topology classification.
- Certain boundary-reference and shared-junction cases may require further geometric validation on complex drawings.
- The tool generates annotative `MText`, not associative Civil 3D parcel labels.

## Project status

**Active development.** The package has progressed from parcel-topology diagnostics to single-parcel and site-wide angle labeling. Further work focuses on edge-case validation, robustness, and usability.

## Disclaimer

This is an independent development project and is not affiliated with or endorsed by Autodesk. Autodesk, AutoCAD, Civil 3D, and Dynamo are trademarks of their respective owners. Verify generated annotations against applicable drafting standards and project requirements before relying on them in production drawings.
