# CivilDynamoTools.Diagnostics

## Purpose

A minimal C# Dynamo package for Civil 3D 2026 whose only job right now is to prove we
can read **native Civil 3D parcel-loop topology** through the legacy COM API, as a
diagnostic step before ever attempting to reconstruct parcel geometry in Dynamo.

This is a prototype/exploration phase, not the final feature. The eventual goal
(**not yet started**) is parcel-angle labeling, currently implemented as a fragile
Dynamo graph that rebuilds parcel geometry into `PolyCurve`s. That approach breaks on
real Civil 3D parcel topology because parcels can contain: split segment elements,
very short elements, nearly coincident vertices, multiple topological elements along
what looks like one straight line, curves, and intersections caused by geometry on the
opposite side of a boundary. Before trying to fix that graph, we want to inspect Civil
3D's native topology directly.

## Naming note - do not rename back to `CivilDynamoTools`

There is already a separate, existing installed Dynamo package called
`CivilDynamoTools` (the one the legacy angle-labeling graph depends on - it supplies
`Site.ByIndex` / `Site.Parcels` wrapper nodes). This new project is intentionally named
**`CivilDynamoTools.Diagnostics`** to avoid a package-name collision in Dynamo, and it
must **not** reference or depend on the old `CivilDynamoTools` package or its wrapper
types. This new package wraps/retrieves Civil 3D parcels itself, using the Civil 3D
APIs directly.

## The hypothesis being tested

Autodesk's managed API (`Autodesk.Civil.DatabaseServices.Parcel` /
`Autodesk.Civil.DatabaseServices.ParcelSegment`) does not expose parcel-loop topology
in enough detail. The legacy COM API does, via:

- `AeccParcel.ParcelLoops` - collection of parcel segment elements making up a parcel
- `AeccParcelSegmentElement` - one topological element (parent `ParcelSegment`,
  `StartX/Y`, `EndX/Y`, line-vs-curve)
- `AeccParcelSegmentLine` / `AeccParcelSegmentCurve` - the two concrete element kinds

Hypothesis to test (**not confirmed Civil 3D behavior yet** - that's the point of this
prototype): counting, across every parcel in a site, how many parcels reference the
same underlying segment element -

- `ReferenceCount == 1` → likely an exterior / site-boundary segment
- `ReferenceCount == 2` → likely an interior / shared-between-parcels segment

The diagnostic node reports the count per element; it does not encode or act on the
hypothesis. Validation is manual: run it against a real plat where the exterior
boundary has a yellow Boundary/site parcel style, and visually compare.

## Architecture decisions

```
Dynamo input (object)
    -> ParcelUnwrapper.ToParcelObjectId       (Interop/, plain .NET + reflection)
    -> Autodesk.Civil.DatabaseServices.Parcel (managed API, opened via Transaction)
    -> parcel.AcadObject as dynamic           (bridge into COM)
    -> AeccParcel.ParcelLoops                 (COM, isolated to ParcelTopologyReader)
    -> ParcelSegmentElementData               (plain C# data - COM stops here)
    -> reference counting (Dictionary<SegmentElementKey, HashSet<ObjectId>>)
    -> List<Dictionary<string,object>>        (Dynamo/Watch-friendly output)
```

Key decisions:

- **COM is isolated to `Interop/ParcelTopologyReader.cs`.** Nothing outside that class
  ever touches a `dynamic`/COM value. Everything else operates on the plain
  `ParcelSegmentElementData` model.
- **No Autodesk COM interop assembly / PIA reference.** Two techniques avoid needing
  one:
  - `dynamic aeccParcel = parcel.AcadObject;` for late-bound (IDispatch) member access.
  - `Interop/ComTypeInspector.cs` uses `System.Runtime.InteropServices.Marshal
    .GetTypeInfoFromObject` (BCL only) to read the COM object's real interface/coclass
    name (e.g. `"AeccParcelSegmentLine"` vs `"AeccParcelSegmentCurve"`) instead of
    guessing at a `SegmentType`-style enum property. This is how line-vs-curve
    classification is implemented; the raw name is also surfaced in output as
    `ElementTypeName` so misclassification is visible rather than hidden.
- **Defensive, self-diagnosing input unwrapping** (`Interop/ParcelUnwrapper.cs`). The
  node's public parameter type is `object`, not a specific wrapper class, because the
  exact type Civil 3D 2026's Dynamo integration passes for a selected parcel has not
  been confirmed (see verification item 5 below). It handles, in order: a native
  `Autodesk.Civil.DatabaseServices.Parcel`, an `ObjectId`, a `DBObject`, or any object
  exposing an `ObjectId` / `InternalObjectId` / `InternalDBObject` property via
  reflection. Anything else throws `InvalidOperationException` naming the runtime type
  and listing its public properties - that message is meant to be pasted back into a
  future session to add the real shape.
- **Identity key** (`Topology/SegmentElementKey.cs`): `ParentSegmentIdentity` (COM
  `Handle`, falling back to `Name` with a warning, falling back to a synthetic GUID
  with a loud warning if neither is available) combined with direction-normalized,
  6-decimal-rounded `(StartX,StartY)-(EndX,EndY)`. Normalization treats A→B and B→A as
  the same element (`WasReversedRelativeToCanonical` records which one this element
  was). Rounding is the numeric tolerance; it never merges elements that have
  different parent-segment identities, even if their coordinates are close.
- **Fail loud, never skip silently.** Every COM/API call that doesn't behave as
  expected throws immediately with parcel/loop/element context (name, index, the
  underlying exception message). This is diagnostic code - an informative crash is
  more useful than a quietly incomplete result.
- **All geometry is preserved.** No filtering, merging, or "cleanup" of short
  segments, near-duplicate points, curves, or reversed directions - the whole point of
  this prototype is to reveal weird topology, not correct it.
- **Reference counting scope**: sibling parcels are all parcels in the *same Site* as
  the selected parcel (`Parcel.SiteId` → `Site.GetParcelIds()`). Output rows are only
  the selected parcel's own elements, each annotated with its computed
  `ReferenceCount` across the whole site.

### Next milestone: `Parcel.Corners` (in progress)

The reference-count diagnostic has been validated in Dynamo (exterior=1 / shared=2
holds). The next node, `Parcel.Corners`, is a general-purpose geometry node: retrieve
parcel loops, identify corners, compute interior angles, return geometric/topology data.
It keeps **every** vertex and classifies it (`Corner`, `CollinearJunction`,
`CurveAdjacent`, `UnknownAdjacent`, `Degenerate`); curve-adjacent vertices are output
but get no angle (for now).

Done so far:
- `src\CivilDynamoTools.Diagnostics.Geometry\` - Autodesk-free geometry layer
  (`SegmentOrderAnalyzer`: joint/gap analysis + chaining by flipping, never reordering;
  `CornerAnalyzer`: classification, signed deflection, interior angle). Unit-tested in
  `tests\CivilDynamoTools.Diagnostics.Geometry.Tests\` (`dotnet test civil3dNodes.slnx`).
- `Parcel.SegmentOrder` diagnostic node - reports whether COM `ParcelLoops` element
  order is a continuous chain and which elements need flipping. Validated on three real
  parcels (4 lines; 3 lines + curve; 5 lines with a split segment): all single-loop,
  clockwise, connected as given, no reversals.
- `Parcel.Corners` node, backed by `ParcelCornerAnalyzer`: validates each loop and flags
  rather than repairs (`IsValid`, `Issues`). Multi-loop parcels: largest-area loop is
  inferred Outer, others Hole - flagged as unverified, since no real multi-loop parcel
  has been tested yet.

`Parcel.Corners` validated in Dynamo (2026-10-21): angles match the plat and
curve-adjacent vertices come through as null. Note: wire it from the parcel selection,
not from `SegmentOrder`'s output (that yields a "DesignScript.Builtin.Dictionary" unwrap
error).

Pending: find a multi-loop parcel to check the outer/hole inference.

### Labeler 0.2: `Parcel.LabelAngles` (branch `Angle-labeler-0.2`, validated in Civil 3D 2026-10-21)

User-chosen behavior: DMS text (whole seconds, rounding carries); label every line-line
Corner except those that display as 90°00'00"/270°00'00"; text laid along the interior
bisector, vertex-side end at `offset` from the vertex (MiddleLeft, reading away), flipped
to MiddleRight where reading away would be upside-down (rotation kept in (-90°, 90°]);
gap auto-widened for narrow angles (0.75 × text height clear of each line). Model-space
MText. Angles/exclusions validated in Civil 3D; rotated placement requested by user
after seeing horizontal labels. Re-run
erases MText tagged with XData app `CDT_DIAGNOSTICS` ("ParcelAngleLabel", parcel handle)
for that parcel before creating new ones. Planning (`AngleLabelPlanner`, `AngleFormatter`)
is in the geometry layer and unit-tested; `Annotation/AngleLabelWriter.cs` only writes.
`Parcel.Corners` stays a pure geometry node.

### Explicitly out of scope

Civil 3D label styles/objects (labels are plain MText), polyline/PolyCurve
reconstruction, Dynamo `Line`/`Arc`/`PolyCurve` geometry objects, parcel geometry or
style modification, and porting the legacy angle-labeling graph.

## Files created so far

```
C:\dev\civil3dNodes\
  Directory.Build.props
      Defines AutoCAD2026Root / Civil3DRoot MSBuild properties, overridable via
      AUTOCAD2026_ROOT / CIVIL3D_ROOT environment variables or -p: command-line
      properties. Default is this machine's install: H:\Program Files (x86)\AutoCAD 2026
      (C3D\ and ACA\ subfolders hold AeccDbMgd.dll / AecBaseMgd.dll). C:\Program Files\
      Autodesk only has Object Enablers here - never build against those.

  package\pkg.json
      Dynamo package manifest skeleton. `engine_version` is a literal placeholder
      string ("REPLACE_WITH_INSTALLED_DYNAMO_VERSION") - not yet filled in.
      `bin\` under this folder does not exist yet; nothing has been built or copied
      there. For fast iteration, prefer Dynamo's "Import Library..." pointed directly
      at the built DLL under src\...\bin\ instead of loading this package folder.

  src\CivilDynamoTools.Diagnostics\
    CivilDynamoTools.Diagnostics.csproj
        SDK-style project. TargetFramework net8.0-windows, Platforms/PlatformTarget
        x64. References AcCoreMgd, AcDbMgd, AcMgd (from $(AutoCAD2026Root)) and
        AeccDbMgd (from $(Civil3DRoot)), all with <Private>false</Private> (Copy
        Local = false) since the host process supplies them at runtime.

    Nodes\Parcel.cs
        Public static class `Parcel` with one method, `SegmentReferenceCounts(object
        parcel)`. This is the Dynamo-facing node; Dynamo will list it as
        "Parcel.SegmentReferenceCounts". Delegates to ParcelUnwrapper +
        ParcelTopologyReader and converts results to
        List<Dictionary<string,object>> for Watch-node readability.

    Interop\ParcelUnwrapper.cs
        object -> Autodesk.AutoCAD.DatabaseServices.ObjectId. See "Defensive,
        self-diagnosing input unwrapping" above.

    Interop\ParcelTopologyReader.cs
        The only class that touches COM/dynamic. Opens the selected parcel, resolves
        its Site, enumerates every parcel ObjectId in that site, and for each: opens
        it, gets `parcel.AcadObject` as dynamic, casts `ParcelLoops` to IEnumerable,
        walks loops and elements, reads each element into a ParcelSegmentElementData,
        computes its SegmentElementKey, and tracks which parcel ObjectIds reference
        each key. After the full pass, fills in ReferenceCount on the selected
        parcel's own elements and returns those.
        NOTE: written against documented/assumed member names that have not been
        compiled or run against real assemblies - see verification items 1-4, 6 below.

    Interop\ComTypeInspector.cs
        `GetComTypeName(object comObject)` - reads the COM interface/coclass name via
        `Marshal.GetTypeInfoFromObject(...).GetDocumentation(-1, ...)`. Used to
        classify an element as line vs. curve by checking whether the returned name
        contains "Line" or "Curve" (case-insensitive). If neither matches, a warning
        is recorded on that element rather than guessing.

    Topology\SegmentElementKey.cs
        `readonly record struct` identity key: ParentSegmentIdentity + normalized,
        6-decimal-rounded endpoint pair. `Create(...)` normalizes direction and
        reports whether it reversed the input order via an `out bool reversed`.

    Models\ParcelSegmentElementData.cs
        Plain diagnostic record: ParcelName, ParcelNumber, LoopIndex, ElementIndex,
        ParentSegmentIdentity, StartX/Y, EndX/Y, IsLine, IsCurve, ElementTypeName,
        Length, MatchingKey, WasReversedRelativeToCanonical, ReferenceCount, and a
        Warnings list. `ToDictionary()` converts to Dynamo/Watch-friendly output.

  .gitignore
      bin/, obj/, *.user, .vs/
```

No git repository has been initialized in this folder yet.

## Civil 3D 2026 assembly paths - user-confirmed, not independently verified by Claude

The development machine used to write this code does **not** have Civil 3D 2026 or
Dynamo installed, so none of this could be inspected directly or compiled. The user
confirmed the following on the actual Civil 3D 2026 machine:

- Target framework: **`net8.0-windows`**
- AutoCAD managed assemblies (`AcCoreMgd.dll`, `AcDbMgd.dll`, `AcMgd.dll`) live under:
  `C:\Program Files\Autodesk\AutoCAD 2026\`
- Civil 3D managed assembly (`AeccDbMgd.dll`) lives under:
  `C:\Program Files\Autodesk\AutoCAD 2026\C3D\`
- Important: use the **actual Civil 3D installation copies** of these DLLs, not any
  **Object Enabler** copies that may exist elsewhere with the same file names (Object
  Enabler copies are lighter/view-only and will not have the full API surface this
  package needs).

These paths are wired into `Directory.Build.props` as defaults, overridable via
`AUTOCAD2026_ROOT` / `CIVIL3D_ROOT` environment variables.

**Not yet confirmed**: whether `AeccDbMgd.dll` sits directly in that `C3D` folder or in
a nested subfolder, and whether it's the only Civil 3D managed assembly this project
will end up needing.

## What needs verifying on the Civil 3D 2026 machine

These six items are the concrete next steps for whoever (human or a fresh Claude Code
session) picks this up next. None of them could be checked from the development
machine.

1. **Compilation against real assemblies.** The code was written against documented/
   assumed member names that have never been compiled against the real
   `AeccDbMgd.dll`/`AcDbMgd.dll`. Specifically at risk:
   - `Autodesk.Civil.DatabaseServices.Parcel.SiteId`
   - `Autodesk.Civil.DatabaseServices.Site.GetParcelIds()`
   - `Autodesk.Civil.DatabaseServices.Parcel.AcadObject` (inherited from `DBObject`/
     `Entity`)
   - `Autodesk.Civil.DatabaseServices.Parcel.Number`
   - COM members on `AeccParcel`/`AeccParcelSegmentElement`/`AeccParcelSegment`:
     `ParcelLoops`, `StartX`/`StartY`/`EndX`/`EndY`, `Length`, `ParcelSegment`,
     `Handle`, `Name`.
   Expect the first build on the real machine to surface renamed/missing members;
   fixes should be localized to `ParcelTopologyReader.cs` and should not require
   restructuring.

2. **`AeccDbMgd.dll` location precision.** Confirm it sits directly under
   `C:\Program Files\Autodesk\AutoCAD 2026\C3D\` (not a nested version folder), and
   confirm it's the real Civil 3D copy rather than an Object Enabler copy (see above).

3. **`dynamic` + `AcadObject` under the .NET 8 host runtime.** Confirm that
   `parcel.AcadObject` returns a usable COM object and that late-bound (IDispatch)
   member access on it (`aeccParcel.ParcelLoops`, `element.StartX`, etc.) works
   correctly when hosted inside Civil 3D 2026's .NET 8 CLR. This behavior is assumed
   to carry over from .NET Framework-era Civil 3D plugins but has not been tested
   post-.NET-8-migration.

4. **COM collection enumeration.** Confirm
   `foreach (dynamic loop in (System.Collections.IEnumerable)aeccParcel.ParcelLoops)`
   (and the equivalent for elements within a loop) actually bridges the COM
   automation collection to `IEnumerable` correctly. This is a known trouble spot: OLE
   Automation collections implement `_NewEnum`/`IEnumVARIANT`, and whether that
   surfaces as `IEnumerable` through the runtime-generated RCW needs an actual test.

5. **The real Dynamo input wrapper shape.** Wire an actual selected parcel into
   `Parcel.SegmentReferenceCounts` in a live Dynamo graph. If `ParcelUnwrapper` throws,
   its exception message will name the runtime type and its public properties -
   report that back so `ParcelUnwrapper.ToParcelObjectId` can be extended with the
   real shape (rather than guessing at it now).

6. **`engine_version` in `package/pkg.json`.** Currently a placeholder string. Needs
   the actual installed Dynamo version string from the Civil 3D 2026 machine before
   `package/` can be loaded as a real Dynamo package (not required for the
   "Import Library..." fast-iteration path).

## Recommended workflow for continuing this work

1. Build `src\CivilDynamoTools.Diagnostics\CivilDynamoTools.Diagnostics.csproj` on the
   Civil 3D 2026 machine; fix any compile errors from verification item 1.
2. In Dynamo, use **Import Library...** pointed at
   `src\CivilDynamoTools.Diagnostics\bin\<Debug|Release>\CivilDynamoTools.Diagnostics.dll`
   (do not use the `package\` folder yet - its `bin\` is empty and `engine_version` is
   a placeholder).
3. Wire a real parcel selection into `Parcel.SegmentReferenceCounts` and see what
   happens - a thrown `ParcelUnwrapper` exception is expected and useful the first
   time; report its message back.
4. Once it runs, compare `ReferenceCount` values against the plat with the yellow
   Boundary/site parcel style to check the exterior=1 / interior=2 hypothesis.
5. Only after that validation should scope expand beyond this diagnostic node.
