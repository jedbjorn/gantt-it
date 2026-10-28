using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;

namespace GanttIt.Spike;

/// <summary>V3, V4, V5, V6, V8, V9 in code. Every step is isolated (exceptions recorded, run continues).</summary>
internal static class Spike
{
    private class Ctx
    {
        public UIApplication UiApp; public Document Doc; public JsonArray Fails = new(); public string Stamp;
        public View Visual, V9, V9s50, V6, V8;
        public ElementId SolidFillId, InvisibleLinesId, FillTypeId;
        public GraphicsStyle ThinLines, WideLines;
        public Dictionary<string, TextNoteType> Types = new();
        public TextNoteType BaseTextType;
    }

    public static JsonObject Run(UIApplication uiapp, JsonObject args)
    {
        var doc = uiapp.ActiveUIDocument.Document;
        var c = new Ctx { UiApp = uiapp, Doc = doc, Stamp = DateTime.Now.ToString("HHmmss") };
        var R = new JsonObject
        {
            ["meta"] = new JsonObject
            {
                ["at"] = DateTime.Now.ToString("o"), ["doc"] = doc.PathName, ["title"] = doc.Title,
                ["revit"] = uiapp.Application.VersionNumber + " " + uiapp.Application.VersionBuild,
                ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ["displayUnitsLength"] = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId().TypeId,
                ["shortCurveToleranceFt"] = uiapp.Application.ShortCurveTolerance,
                ["shortCurveToleranceMm"] = Util.N(Util.Mm(uiapp.Application.ShortCurveTolerance)),
            }
        };
        R["env"] = Util.Step("env", () => Env(c));
        R["V3"] = Util.Step("V3", () => V3(c));
        R["V5"] = Util.Step("V5", () => V5(c));
        R["V4"] = Util.Step("V4", () => V4(c));
        R["V9"] = Util.Step("V9", () => V9(c, args));
        R["V9visual"] = Util.Step("V9visual", () => V9Visual(c));
        R["V6"] = Util.Step("V6", () => V6(c));
        R["V6background"] = Util.Step("V6bg", () => V6Background(c, args));
        R["V8"] = Util.Step("V8", () => V8(c));
        R["export"] = Util.Step("export", () => Export(c));
        if (args["save"]?.GetValue<bool>() != false) R["save"] = Util.Step("save", () => Save(doc));
        R["activate"] = Util.Step("activate", () => Activate(c));
        R["failures"] = c.Fails;
        return R;
    }

    // ------------------------------------------------------------------ env
    private static JsonObject Env(Ctx c)
    {
        var doc = c.Doc;
        var o = new JsonObject();
        o["legends"] = new JsonArray(Util.Legends(doc).Select(v => (JsonNode)new JsonObject
        {
            ["id"] = Util.Id(v.Id), ["name"] = v.Name, ["scale"] = v.Scale, ["owned"] = Util.OwnedBy(doc, v.Id).Count,
        }).ToArray());
        var def = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        o["defaultTextNoteTypeId"] = Util.Id(def);
        o["textNoteTypes"] = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
            .Select(t => (JsonNode)TypeSummary(t)).ToArray());
        var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        var ls = new JsonArray();
        foreach (Category sc in lines.SubCategories)
        {
            var gs = sc.GetGraphicsStyle(GraphicsStyleType.Projection);
            ls.Add(new JsonObject
            {
                ["name"] = sc.Name, ["categoryId"] = Util.Id(sc.Id), ["builtIn"] = sc.BuiltInCategory.ToString(),
                ["graphicsStyleId"] = Util.Id(gs?.Id), ["lineWeight"] = sc.GetLineWeight(GraphicsStyleType.Projection),
            });
            if (sc.BuiltInCategory == BuiltInCategory.OST_CurvesThinLines) c.ThinLines = gs;
            if (sc.BuiltInCategory == BuiltInCategory.OST_CurvesWideLines) c.WideLines = gs;
        }
        o["lineStyles"] = ls;
        c.BaseTextType = doc.GetElement(def) as TextNoteType;
        o["baseTextTypeParams"] = DumpParams(c.BaseTextType);
        o["fontsInstalled"] = WpfMeasure.InstalledFonts();
        return o;
    }

    private static JsonObject TypeSummary(TextNoteType t) => new()
    {
        ["id"] = Util.Id(t.Id), ["name"] = t.Name,
        ["font"] = t.get_Parameter(BuiltInParameter.TEXT_FONT)?.AsString(),
        ["sizeMm"] = Util.N(Util.Mm(t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? double.NaN)),
        ["widthFactor"] = Util.N(t.get_Parameter(BuiltInParameter.TEXT_WIDTH_SCALE)?.AsDouble() ?? double.NaN),
        ["bold"] = t.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD)?.AsInteger(),
        ["italic"] = t.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC)?.AsInteger(),
        ["leaderBorderOffsetMm"] = Util.N(Util.Mm(t.get_Parameter(BuiltInParameter.LEADER_OFFSET_SHEET)?.AsDouble() ?? double.NaN)),
        ["background"] = t.get_Parameter(BuiltInParameter.TEXT_BACKGROUND)?.AsInteger(),
        ["showBorder"] = t.get_Parameter(BuiltInParameter.TEXT_BOX_VISIBILITY)?.AsInteger(),
    };

    private static JsonArray DumpParams(Element e)
    {
        var arr = new JsonArray();
        if (e == null) return arr;
        foreach (Parameter p in e.Parameters)
        {
            var o = new JsonObject
            {
                ["name"] = p.Definition?.Name,
                ["builtIn"] = (p.Definition as InternalDefinition)?.BuiltInParameter.ToString(),
                ["storage"] = p.StorageType.ToString(), ["readOnly"] = p.IsReadOnly, ["valueString"] = p.AsValueString(),
            };
            if (p.StorageType == StorageType.Double) o["double"] = Util.N(p.AsDouble(), 9);
            if (p.StorageType == StorageType.Integer) o["int"] = p.AsInteger();
            if (p.StorageType == StorageType.String) o["string"] = p.AsString();
            arr.Add(o);
        }
        return arr;
    }

    // ------------------------------------------------------------------ V3
    private static JsonObject V3(Ctx c)
    {
        var doc = c.Doc;
        var src = Util.Legends(doc).OrderBy(v => v.Name == "Legend 1" ? 0 : 1).First();
        var o = new JsonObject
        {
            ["source"] = new JsonObject { ["name"] = src.Name, ["id"] = Util.Id(src.Id), ["scale"] = src.Scale, ["ownedBefore"] = Util.OwnedBy(doc, src.Id).Count },
            ["canDuplicate"] = new JsonObject
            {
                ["Duplicate"] = src.CanViewBeDuplicated(ViewDuplicateOption.Duplicate),
                ["WithDetailing"] = src.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing),
                ["AsDependent"] = src.CanViewBeDuplicated(ViewDuplicateOption.AsDependent),
            },
        };
        var made = new JsonArray();
        View Dup(string name, bool scale1)
        {
            var id = src.Duplicate(ViewDuplicateOption.Duplicate);
            var v = (View)doc.GetElement(id);
            var rec = new JsonObject { ["autoName"] = v.Name, ["scaleBefore"] = v.Scale };
            v.Name = $"{name} {c.Stamp}";
            if (scale1) v.Scale = 1;
            rec["id"] = Util.Id(id); rec["name"] = v.Name; rec["scaleAfterSet"] = v.Scale;
            made.Add(rec);
            return v;
        }
        var st = Util.Tx(doc, "Gantt-It: V3 duplicate legends", () =>
        {
            c.Visual = Dup("GI Spike Visual", true);
            c.V9 = Dup("GI Spike V9", true);
            c.V9s50 = Dup("GI Spike V9 s50", false);
            c.V6 = Dup("GI Spike V6 chart", true);
            c.V8 = Dup("GI Spike V8", true);
        }, c.Fails);
        o["txStatus"] = st.ToString();
        o["created"] = made;
        var v = c.Visual;
        o["result"] = new JsonObject
        {
            ["viewType"] = v.ViewType.ToString(), ["scale"] = v.Scale, ["name"] = v.Name, ["isTemplate"] = v.IsTemplate,
            ["ownedElements"] = Util.Describe(Util.OwnedBy(doc, v.Id)),
            ["elementsInViewCollector"] = Util.Describe(new FilteredElementCollector(doc, v.Id).WhereElementIsNotElementType().ToElements()),
            ["scaleParam"] = v.get_Parameter(BuiltInParameter.VIEW_SCALE)?.AsInteger(),
        };
        o["intrinsicOwnedOnly"] = Util.OwnedBy(doc, v.Id).All(e => e is SketchPlane || e.GetType() == typeof(Element));
        o["pass"] = v.ViewType == ViewType.Legend && v.Scale == 1 && o["intrinsicOwnedOnly"].GetValue<bool>();
        return o;
    }

    // ------------------------------------------------------------------ V5
    private static JsonObject V5(Ctx c)
    {
        var doc = c.Doc; var view = c.Visual;
        var def = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        var chosen = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
            .OrderBy(t => t.Id == def ? 1 : 0).First();
        var o = new JsonObject { ["defaultTypeId"] = Util.Id(def), ["chosenType"] = TypeSummary(chosen) };
        o["chosenLineStyles"] = new JsonObject { ["wide"] = c.WideLines?.Name, ["wideId"] = Util.Id(c.WideLines?.Id), ["thin"] = c.ThinLines?.Name, ["thinId"] = Util.Id(c.ThinLines?.Id) };
        TextNote note = null; DetailCurve dc1 = null, dc2 = null; string dc1Default = null;
        var st = Util.Tx(doc, "Gantt-It: V5 text + lines", () =>
        {
            note = TextNote.Create(doc, view.Id, new XYZ(0, Util.Ft(60), 0), "V5 TextNote in a chosen TextNoteType",
                new TextNoteOptions(chosen.Id) { HorizontalAlignment = HorizontalTextAlignment.Left, VerticalAlignment = VerticalTextAlignment.Top });
            dc1 = doc.Create.NewDetailCurve(view, Line.CreateBound(new XYZ(0, Util.Ft(50), 0), new XYZ(Util.Ft(100), Util.Ft(50), 0)));
            dc1Default = (dc1.LineStyle as GraphicsStyle)?.Name;
            dc1.LineStyle = c.WideLines;
            dc2 = doc.Create.NewDetailCurve(view, Line.CreateBound(new XYZ(0, Util.Ft(45), 0), new XYZ(Util.Ft(100), Util.Ft(45), 0)));
            dc2.LineStyle = c.ThinLines;
        }, c.Fails);
        o["txStatus"] = st.ToString();
        o["note"] = new JsonObject { ["id"] = Util.Id(note.Id), ["typeId"] = Util.Id(note.GetTypeId()), ["typeName"] = doc.GetElement(note.GetTypeId()).Name, ["ownerView"] = Util.Id(note.OwnerViewId) };
        o["detailCurve1"] = new JsonObject { ["id"] = Util.Id(dc1.Id), ["defaultStyleOnCreate"] = dc1Default, ["styleId"] = Util.Id(dc1.LineStyle.Id), ["styleName"] = dc1.LineStyle.Name, ["ownerView"] = Util.Id(dc1.OwnerViewId) };
        o["detailCurve2"] = new JsonObject { ["id"] = Util.Id(dc2.Id), ["styleId"] = Util.Id(dc2.LineStyle.Id), ["styleName"] = dc2.LineStyle.Name };
        // Short-curve tolerance behaviour (a gotcha for the grid emitter).
        var tol = c.UiApp.Application.ShortCurveTolerance;
        var shortCurve = new JsonObject { ["toleranceMm"] = Util.N(Util.Mm(tol)) };
        try
        {
            Util.Tx(doc, "Gantt-It: V5 short curve", () =>
            {
                doc.Create.NewDetailCurve(view, Line.CreateBound(new XYZ(0, Util.Ft(-300), 0), new XYZ(tol * 0.5, Util.Ft(-300), 0)));
                throw new OperationCanceledException("rollback on purpose");
            }, c.Fails);
        }
        catch (Exception ex) { shortCurve["halfToleranceLine"] = ex.GetType().Name + ": " + ex.Message; }
        o["shortCurve"] = shortCurve;
        o["pass"] = note.GetTypeId() == chosen.Id && dc1.LineStyle.Id == c.WideLines.Id && dc2.LineStyle.Id == c.ThinLines.Id;
        return o;
    }

    // ------------------------------------------------------------------ V4
    private static JsonObject V4(Ctx c)
    {
        var doc = c.Doc; var view = c.Visual;
        var o = new JsonObject();
        var byName = FillPatternElement.GetFillPatternElementByName(doc, FillPatternTarget.Drafting, "<Solid fill>");
        var solids = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .Where(f => f.GetFillPattern().IsSolidFill).ToList();
        o["solidFill"] = new JsonObject
        {
            ["byNameDrafting"] = byName == null ? null : new JsonObject { ["id"] = Util.Id(byName.Id), ["name"] = byName.Name },
            ["isSolidFillScan"] = new JsonArray(solids.Select(f => (JsonNode)new JsonObject { ["id"] = Util.Id(f.Id), ["name"] = f.Name, ["target"] = f.GetFillPattern().Target.ToString() }).ToArray()),
        };
        var solid = solids.First(f => f.GetFillPattern().Target == FillPatternTarget.Drafting);
        c.SolidFillId = solid.Id;

        // <Invisible lines>: three ways.
        var lines = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        GraphicsStyle invByName = null;
        foreach (Category sc in lines.SubCategories) if (sc.Name == "<Invisible lines>") invByName = sc.GetGraphicsStyle(GraphicsStyleType.Projection);
        var invCat = Category.GetCategory(doc, BuiltInCategory.OST_InvisibleLines);
        var invByBic = invCat?.GetGraphicsStyle(GraphicsStyleType.Projection);
        var valid = FilledRegion.GetValidLineStyleIdsForFilledRegion(doc);
        o["invisibleLines"] = new JsonObject
        {
            ["bySubcategoryName"] = Util.Id(invByName?.Id),
            ["byBuiltInCategory_OST_InvisibleLines"] = Util.Id(invByBic?.Id), ["bicCategoryName"] = invCat?.Name,
            ["validForFilledRegion"] = new JsonArray(valid.Select(i => (JsonNode)new JsonObject { ["id"] = Util.Id(i), ["name"] = doc.GetElement(i)?.Name }).ToArray()),
        };
        var invByValid = valid.Select(i => doc.GetElement(i) as GraphicsStyle)
            .FirstOrDefault(g => g != null && (g.GraphicsStyleCategory?.BuiltInCategory == BuiltInCategory.OST_InvisibleLines || (g.Name ?? "").Contains("Invisible")));
        o["invisibleLines"]["byValidFilledRegionStyles"] = invByValid == null ? null : new JsonObject
        {
            ["id"] = Util.Id(invByValid.Id), ["name"] = invByValid.Name,
            ["categoryBuiltIn"] = invByValid.GraphicsStyleCategory?.BuiltInCategory.ToString(), ["categoryName"] = invByValid.GraphicsStyleCategory?.Name,
            ["categoryParent"] = invByValid.GraphicsStyleCategory?.Parent?.Name,
        };
        o["invisibleLines"]["validDetail"] = new JsonArray(valid.Select(i => doc.GetElement(i) as GraphicsStyle).Where(g => g != null)
            .Select(g => (JsonNode)$"{Util.Id(g.Id)}|{g.Name}|{g.GraphicsStyleCategory?.BuiltInCategory}").ToArray());
        var inv = invByBic ?? invByName ?? invByValid;
        if (inv == null) { o["fatal"] = "no <Invisible lines> graphics style found"; return o; }
        c.InvisibleLinesId = inv.Id;
        o["invisibleIsValidForFilledRegion"] = FilledRegion.IsValidLineStyleIdForFilledRegion(doc, inv.Id);

        var baseType = new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().First();
        o["baseType"] = FrtSummary(baseType);
        FilledRegionType frt = null; FilledRegion fr = null, frDefault = null;
        var st = Util.Tx(doc, "Gantt-It: V4 filled region", () =>
        {
            frt = (FilledRegionType)baseType.Duplicate($"Gantt-It Fill #3A7BD5 {c.Stamp}");
            frt.ForegroundPatternId = solid.Id;
            frt.ForegroundPatternColor = new Color(0x3A, 0x7B, 0xD5);
            frt.BackgroundPatternId = ElementId.InvalidElementId;
            frt.IsMasking = false;
            fr = FilledRegion.Create(doc, frt.Id, view.Id, new List<CurveLoop> { Rect(0, 20, 60, 5) });
            fr.SetLineStyleId(inv.Id);
            frDefault = FilledRegion.Create(doc, frt.Id, view.Id, new List<CurveLoop> { Rect(0, 10, 60, 5) });
        }, c.Fails);
        c.FillTypeId = frt.Id;
        o["txStatus"] = st.ToString();
        o["newType"] = FrtSummary(frt);
        o["region"] = RegionSummary(doc, fr);
        o["regionDefaultBoundary"] = RegionSummary(doc, frDefault);
        o["pass"] = frt.ForegroundPatternId == solid.Id && frt.BackgroundPatternId == ElementId.InvalidElementId && !frt.IsMasking
            && RegionSummary(doc, fr)["boundaryStyleIds"].AsArray().All(n => n.GetValue<long>() == Util.Id(inv.Id));
        return o;
    }

    private static CurveLoop Rect(double xMm, double yMm, double wMm, double hMm)
    {
        var a = new XYZ(Util.Ft(xMm), Util.Ft(yMm), 0); var b = new XYZ(Util.Ft(xMm + wMm), Util.Ft(yMm), 0);
        var cc = new XYZ(Util.Ft(xMm + wMm), Util.Ft(yMm + hMm), 0); var d = new XYZ(Util.Ft(xMm), Util.Ft(yMm + hMm), 0);
        return CurveLoop.Create(new List<Curve> { Line.CreateBound(a, b), Line.CreateBound(b, cc), Line.CreateBound(cc, d), Line.CreateBound(d, a) });
    }

    private static JsonObject FrtSummary(FilledRegionType t) => new()
    {
        ["id"] = Util.Id(t.Id), ["name"] = t.Name,
        ["foregroundPatternId"] = Util.Id(t.ForegroundPatternId), ["foregroundColor"] = $"#{t.ForegroundPatternColor.Red:X2}{t.ForegroundPatternColor.Green:X2}{t.ForegroundPatternColor.Blue:X2}",
        ["backgroundPatternId"] = Util.Id(t.BackgroundPatternId), ["isMasking"] = t.IsMasking, ["lineWeight"] = t.LineWeight,
    };

    private static JsonObject RegionSummary(Document doc, FilledRegion fr)
    {
        var curves = fr.GetDependentElements(new ElementClassFilter(typeof(CurveElement))).Select(doc.GetElement).OfType<CurveElement>().ToList();
        return new JsonObject
        {
            ["id"] = Util.Id(fr.Id), ["typeId"] = Util.Id(fr.GetTypeId()), ["isMasking"] = fr.IsMasking,
            ["boundaryCurves"] = curves.Count,
            ["boundaryStyleIds"] = new JsonArray(curves.Select(cv => (JsonNode)Util.Id(cv.LineStyle?.Id)).ToArray()),
            ["boundaryStyleNames"] = new JsonArray(curves.Select(cv => (JsonNode)cv.LineStyle?.Name).ToArray()),
            ["bbView"] = Util.Bb(fr.get_BoundingBox(doc.GetElement(fr.OwnerViewId) as View)),
        };
    }

    // ------------------------------------------------------------------ V9
    private static readonly string[] Strings =
    {
        "Design", "Permitting & Approvals", "2026-10-01", "46", "Duration", "M", "MMMM", "MMMMMMMMMMMMMMMM",
        "iiii", "iiiiiiiiiiiiiiii", "Wg|Éy", "Jan",
    };
    private const string LongText = "Permitting and approvals for the north wing extension, phase two";
    private const string MultiLine = "Line one\rLine two\rLine three";

    private record Case(string Group, string Font, double SizeMm, bool Bold, bool Italic, double Wf, double? OffsetMm, int Box,
        string Text, HorizontalTextAlignment H, VerticalTextAlignment V, double? WidthMm);

    private static TextNoteType TypeFor(Ctx c, string font, double sizeMm, bool bold, bool italic, double wf, double? offsetMm, int box)
    {
        var key = $"GIS {font} {sizeMm} {(bold ? "B" : "")}{(italic ? "I" : "")} wf{wf} o{(offsetMm?.ToString() ?? "d")} b{box} {c.Stamp}";
        if (c.Types.TryGetValue(key, out var t)) return t;
        t = (TextNoteType)c.BaseTextType.Duplicate(key);
        t.get_Parameter(BuiltInParameter.TEXT_FONT).Set(font);
        t.get_Parameter(BuiltInParameter.TEXT_SIZE).Set(Util.Ft(sizeMm));
        t.get_Parameter(BuiltInParameter.TEXT_WIDTH_SCALE).Set(wf);
        t.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD).Set(bold ? 1 : 0);
        t.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC).Set(italic ? 1 : 0);
        t.get_Parameter(BuiltInParameter.TEXT_STYLE_UNDERLINE).Set(0);
        t.get_Parameter(BuiltInParameter.TEXT_BACKGROUND).Set(1);
        t.get_Parameter(BuiltInParameter.TEXT_BOX_VISIBILITY).Set(box);
        if (offsetMm.HasValue) t.get_Parameter(BuiltInParameter.LEADER_OFFSET_SHEET).Set(Util.Ft(offsetMm.Value));
        c.Types[key] = t;
        return t;
    }

    private static List<Case> Cases(string[] fonts)
    {
        var L = HorizontalTextAlignment.Left; var T = VerticalTextAlignment.Top;
        var list = new List<Case>();
        foreach (var f in fonts) foreach (var s in new[] { 2.0, 2.5, 3.5 }) foreach (var t in Strings)
            list.Add(new Case("core", f, s, false, false, 1, null, 0, t, L, T, null));
        foreach (var f in fonts) foreach (var (b, i) in new[] { (true, false), (false, true), (true, true) }) foreach (var t in Strings)
            list.Add(new Case("style", f, 2.5, b, i, 1, null, 0, t, L, T, null));
        foreach (var f in fonts) foreach (var wf in new[] { 0.7, 1.3 }) foreach (var t in Strings)
            list.Add(new Case("wf", f, 2.5, false, false, wf, null, 0, t, L, T, null));
        foreach (var t in new[] { "Design", "Permitting & Approvals", "46" })
            foreach (var h in new[] { HorizontalTextAlignment.Left, HorizontalTextAlignment.Center, HorizontalTextAlignment.Right })
                foreach (var v in new[] { VerticalTextAlignment.Top, VerticalTextAlignment.Middle, VerticalTextAlignment.Bottom })
                    list.Add(new Case("align", fonts[0], 2.5, false, false, 1, null, 0, t, h, v, null));
        foreach (var off in new double?[] { null, 0, 1, 4 }) foreach (var box in new[] { 0, 1 }) foreach (var t in new[] { "Design", "46", "Permitting & Approvals" })
            list.Add(new Case("offset", fonts[0], 2.5, false, false, 1, off, box, t, L, T, null));
        foreach (var f in fonts)
        {
            foreach (var w in new[] { 15.0, 30.0, 60.0 }) list.Add(new Case("wrap", f, 2.5, false, false, 1, null, 0, LongText, L, T, w));
            list.Add(new Case("wrapSingle", f, 2.5, false, false, 1, null, 0, "Design", L, T, 60));
            foreach (var s in new[] { 2.5, 3.5 }) list.Add(new Case("multiline", f, s, false, false, 1, null, 0, MultiLine, L, T, null));
            list.Add(new Case("wrapWf", f, 2.5, false, false, 0.7, null, 0, LongText, L, T, 30));
        }
        list.Add(new Case("wrapAlign", fonts[0], 2.5, false, false, 1, null, 0, LongText, HorizontalTextAlignment.Right, T, 30));
        list.Add(new Case("wrapAlign", fonts[0], 2.5, false, false, 1, null, 0, LongText, L, VerticalTextAlignment.Bottom, 30));
        list.Add(new Case("wrapAlign", fonts[0], 2.5, false, false, 1, null, 0, LongText, HorizontalTextAlignment.Center, VerticalTextAlignment.Middle, 30));
        return list;
    }

    private static JsonObject ReadNote(TextNote n, View v)
    {
        var bb = n.get_BoundingBox(v); var bbn = n.get_BoundingBox(null); var co = n.Coord;
        var o = new JsonObject
        {
            ["coordMm"] = Util.P(co), ["bbView"] = Util.Bb(bb), ["bbNull"] = Util.Bb(bbn),
            ["widthMm"] = Util.N(Util.Mm(n.Width)), ["heightMm"] = Util.N(Util.Mm(n.Height)),
            ["wrapping"] = n.IsTextWrappingActive,
            ["minWidthMm"] = Util.N(Util.Mm(n.GetMinimumAllowedWidth())), ["maxWidthMm"] = Util.N(Util.Mm(n.GetMaximumAllowedWidth())),
            ["upDir"] = Util.Pft(n.UpDirection), ["baseDir"] = Util.Pft(n.BaseDirection),
        };
        if (bb != null)
        {
            o["minRelCoordMm"] = Util.P(bb.Min - co); o["maxRelCoordMm"] = Util.P(bb.Max - co);
        }
        foreach (var flag in new[] { true, false })
        {
            try
            {
                var res = typeof(TextElement).GetMethod("GetBoxCorners")!.Invoke(n, new object[] { flag }) as System.Collections.IEnumerable;
                o[$"boxCorners_{flag}"] = new JsonArray(res!.Cast<XYZ>().Select(p => (JsonNode)Util.P(p - co)).ToArray());
            }
            catch (Exception ex) { o[$"boxCorners_{flag}"] = (ex.InnerException ?? ex).Message; }
        }
        return o;
    }

    private static JsonObject V9(Ctx c, JsonObject args)
    {
        var doc = c.Doc;
        var fonts = (args["fonts"] as JsonArray)?.Select(n => n.GetValue<string>()).ToArray() ?? new[] { "Arial", "Times New Roman", "Segoe UI" };
        var cases = Cases(fonts);
        var o = new JsonObject { ["fonts"] = new JsonArray(fonts.Select(f => (JsonNode)WpfMeasure.FontInfo(f, false, false)).ToArray()) };
        var rows = new List<JsonObject>();
        var notes = new List<TextNote>();
        var moveD = new XYZ(0.5, -0.3, 0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long tCreate = 0, tRegen1 = 0, tRead = 0, tMove = 0;

        var st = Util.Tx(doc, "Gantt-It: V9 measure", () =>
        {
            for (var i = 0; i < cases.Count; i++)
            {
                var k = cases[i];
                var row = new JsonObject
                {
                    ["i"] = i, ["group"] = k.Group, ["font"] = k.Font, ["sizeMm"] = k.SizeMm, ["bold"] = k.Bold, ["italic"] = k.Italic,
                    ["wf"] = k.Wf, ["offsetMm"] = k.OffsetMm, ["box"] = k.Box, ["text"] = k.Text, ["h"] = k.H.ToString(), ["v"] = k.V.ToString(), ["setWidthMm"] = k.WidthMm,
                };
                rows.Add(row);
                try
                {
                    var t = TypeFor(c, k.Font, k.SizeMm, k.Bold, k.Italic, k.Wf, k.OffsetMm, k.Box);
                    var opts = new TextNoteOptions(t.Id) { HorizontalAlignment = k.H, VerticalAlignment = k.V };
                    var n = k.WidthMm.HasValue
                        ? TextNote.Create(doc, c.V9.Id, XYZ.Zero, Util.Ft(k.WidthMm.Value), k.Text, opts)
                        : TextNote.Create(doc, c.V9.Id, XYZ.Zero, k.Text, opts);
                    notes.Add(n); row["noteId"] = Util.Id(n.Id); row["typeId"] = Util.Id(t.Id);
                    row["typeLeaderOffsetMm"] = Util.N(Util.Mm(t.get_Parameter(BuiltInParameter.LEADER_OFFSET_SHEET).AsDouble()));
                }
                catch (Exception ex) { notes.Add(null); row["createError"] = Util.Err(ex); }
            }
            tCreate = sw.ElapsedMilliseconds;
            doc.Regenerate();
            tRegen1 = sw.ElapsedMilliseconds;
            for (var i = 0; i < notes.Count; i++) if (notes[i] != null) rows[i]["atOrigin"] = ReadNote(notes[i], c.V9);
            tRead = sw.ElapsedMilliseconds;
            for (var i = 0; i < notes.Count; i++)
            {
                if (notes[i] == null) continue;
                if (i % 2 == 0) { ElementTransformUtils.MoveElement(doc, notes[i].Id, moveD); rows[i]["moveMethod"] = "MoveElement"; }
                else { notes[i].Coord = notes[i].Coord + moveD; rows[i]["moveMethod"] = "Coord"; }
            }
            doc.Regenerate();
            tMove = sw.ElapsedMilliseconds;
            for (var i = 0; i < notes.Count; i++) if (notes[i] != null) rows[i]["afterMove"] = ReadNote(notes[i], c.V9);
        }, c.Fails);
        // Re-read after commit (does commit change anything?)
        for (var i = 0; i < notes.Count; i++) if (notes[i] != null) rows[i]["afterCommit"] = ReadNote(notes[i], c.V9);
        o["txStatus"] = st.ToString();
        o["timingsMs"] = new JsonObject { ["create"] = tCreate, ["regen1"] = tRegen1 - tCreate, ["read"] = tRead - tRegen1, ["moveAndRegen"] = tMove - tRead, ["notes"] = notes.Count(n => n != null) };
        o["moveVectorMm"] = Util.P(moveD);

        // Move error per note (mm): (after - before) - d, on bbox min/max and coord.
        double maxMoveErr = 0, maxCommitDiff = 0;
        foreach (var r in rows)
        {
            if (r["atOrigin"] == null) continue;
            var a = r["atOrigin"]["bbView"]; var b = r["afterMove"]["bbView"]; var cm = r["afterCommit"]["bbView"];
            double E(JsonNode x, JsonNode y, int k, double d) => Math.Abs(y[k]!.GetValue<double>() - x[k]!.GetValue<double>() - d);
            var dm = new[] { Util.Mm(moveD.X), Util.Mm(moveD.Y), 0 };
            double err = 0;
            for (var k = 0; k < 2; k++)
            {
                err = Math.Max(err, E(a["minMm"], b["minMm"], k, dm[k])); err = Math.Max(err, E(a["maxMm"], b["maxMm"], k, dm[k]));
                err = Math.Max(err, E(r["atOrigin"]["coordMm"], r["afterMove"]["coordMm"], k, dm[k]));
                maxCommitDiff = Math.Max(maxCommitDiff, E(b["minMm"], cm["minMm"], k, 0)); maxCommitDiff = Math.Max(maxCommitDiff, E(b["maxMm"], cm["maxMm"], k, 0));
            }
            r["moveErrMm"] = Util.N(err, 6);
            maxMoveErr = Math.Max(maxMoveErr, err);
        }
        o["maxMoveErrMm"] = Util.N(maxMoveErr, 6);
        o["maxCommitDiffMm"] = Util.N(maxCommitDiff, 6);

        // WPF raw measurements (em = 100 DIP) for every case, unwrapped.
        foreach (var r in rows)
        {
            try { r["wpf"] = WpfMeasure.Measure(r["text"].GetValue<string>(), r["font"].GetValue<string>(), r["bold"].GetValue<bool>(), r["italic"].GetValue<bool>()); }
            catch (Exception ex) { r["wpfError"] = ex.Message; }
        }
        // Round 2: Display-mode WPF and GDI at candidate integer em sizes on the 384-dpi grid.
        foreach (var r in rows)
        {
            if (r["setWidthMm"] != null) continue;
            try
            {
                var f = r["font"].GetValue<string>(); var b = r["bold"].GetValue<bool>(); var it = r["italic"].GetValue<bool>();
                WpfMeasure.Face(f, b, it).TryGetGlyphTypeface(out var gt);
                r["capsHeightFace"] = Util.N(gt?.CapsHeight ?? double.NaN, 5);
                r["cand"] = GdiMeasure.Candidates(r["text"].GetValue<string>(), f, b, it, r["sizeMm"].GetValue<double>(), gt?.CapsHeight ?? 0.716);
            }
            catch (Exception ex) { r["candError"] = ex.Message; }
        }
        // Per-font scale fit from the M-series (slope removes any constant margin): k = dRevitWidth / (dWpfWidth/100 * size).
        var fit = new JsonObject();
        foreach (var f in fonts)
        {
            foreach (var s in new[] { 2.0, 2.5, 3.5 })
            {
                JsonObject Row(string t) => rows.FirstOrDefault(r => r["group"].GetValue<string>() == "core" && r["font"].GetValue<string>() == f && r["sizeMm"].GetValue<double>() == s && r["text"].GetValue<string>() == t);
                var r1 = Row("M"); var r16 = Row("MMMMMMMMMMMMMMMM");
                if (r1?["atOrigin"] == null || r16?["atOrigin"] == null) continue;
                double RW(JsonObject r) => r["atOrigin"]["widthMm"].GetValue<double>();
                double BW(JsonObject r) => r["atOrigin"]["bbView"]["wMm"].GetValue<double>();
                double WW(JsonObject r) => r["wpf"]["width"].GetValue<double>() / 100.0 * s;
                var kW = (RW(r16) - RW(r1)) / (WW(r16) - WW(r1));
                var kB = (BW(r16) - BW(r1)) / (WW(r16) - WW(r1));
                fit[$"{f}|{s}"] = new JsonObject
                {
                    ["kWidthProp"] = Util.N(kW, 6), ["interceptWidthPropMm"] = Util.N(RW(r1) - kW * WW(r1), 5),
                    ["kBbox"] = Util.N(kB, 6), ["interceptBboxMm"] = Util.N(BW(r1) - kB * WW(r1), 5),
                };
            }
        }
        o["scaleFit"] = fit;
        // Wrapped WPF measurements using the fitted k (per font at 2.5 mm), so line breaks can be compared.
        foreach (var r in rows.Where(r => r["setWidthMm"] != null && r["atOrigin"] != null))
        {
            try
            {
                var f = r["font"].GetValue<string>(); var s = r["sizeMm"].GetValue<double>(); var wf = r["wf"].GetValue<double>();
                var k = fit[$"{f}|2.5"]?["kWidthProp"]?.GetValue<double>() ?? 1.0;
                var maxWem100 = r["setWidthMm"].GetValue<double>() / (k * s * wf) * 100.0;
                r["wpfWrapped"] = WpfMeasure.Measure(r["text"].GetValue<string>(), f, r["bold"].GetValue<bool>(), r["italic"].GetValue<bool>(), maxWem100);
                r["wpfWrapped"]["kUsed"] = Util.N(k, 6);
            }
            catch (Exception ex) { r["wpfWrappedError"] = ex.Message; }
        }

        // Minimum width per font type, and creating below it.
        var minw = new JsonArray();
        foreach (var f in fonts)
        {
            var t = TypeFor(c, f, 2.5, false, false, 1, null, 0);
            var m = new JsonObject { ["font"] = f, ["typeMinWidthMm"] = Util.N(Util.Mm(TextElement.GetMinimumAllowedWidth(doc, t.Id))), ["typeMaxWidthMm"] = Util.N(Util.Mm(TextElement.GetMaximumAllowedWidth(doc, t.Id))) };
            try
            {
                Util.Tx(doc, "Gantt-It: V9 min width", () =>
                {
                    var min = TextElement.GetMinimumAllowedWidth(doc, t.Id);
                    var n = TextNote.Create(doc, c.V9.Id, XYZ.Zero, min, "Design", new TextNoteOptions(t.Id));
                    doc.Regenerate();
                    m["atMin"] = ReadNote(n, c.V9);
                    try { TextNote.Create(doc, c.V9.Id, XYZ.Zero, min * 0.5, "Design", new TextNoteOptions(t.Id)); m["halfMin"] = "created (no exception)"; }
                    catch (Exception ex) { m["halfMin"] = ex.GetType().Name + ": " + ex.Message; }
                    throw new OperationCanceledException("rollback on purpose");
                }, c.Fails);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { m["error"] = ex.Message; }
            minw.Add(m);
        }
        o["minWidth"] = minw;

        // Scale-50 legend: same notes, compare model-space bbox with the 1:1 legend.
        var s50 = new JsonArray();
        Util.Tx(doc, "Gantt-It: V9 scale 50", () =>
        {
            foreach (var txt in new[] { "Design", "Permitting & Approvals", "46" })
            {
                var t = TypeFor(c, fonts[0], 2.5, false, false, 1, null, 0);
                var n = TextNote.Create(doc, c.V9s50.Id, XYZ.Zero, txt, new TextNoteOptions(t.Id));
                doc.Regenerate();
                s50.Add(new JsonObject { ["text"] = txt, ["viewScale"] = c.V9s50.Scale, ["read"] = ReadNote(n, c.V9s50) });
            }
        }, c.Fails);
        o["scale50"] = s50;
        o["typeParamsSample"] = DumpParams(c.Types.Values.First());
        o["cases"] = new JsonArray(rows.Select(r => (JsonNode)r).ToArray());
        return o;
    }

    // ------------------------------------------------------------------ V9 visual: notes with their bbox drawn as detail lines
    private static JsonObject V9Visual(Ctx c)
    {
        var doc = c.Doc; var v = c.Visual;
        var fonts = new[] { "Arial", "Times New Roman", "Segoe UI" };
        var items = new JsonArray();
        var st = Util.Tx(doc, "Gantt-It: V9 visual", () =>
        {
            var placed = new List<(TextNote n, string label)>();
            var y = -20.0;
            foreach (var f in fonts)
            {
                foreach (var s in new[] { 2.5, 3.5 })
                {
                    var x = 0.0;
                    foreach (var txt in new[] { "Design", "Permitting & Approvals", "2026-10-01", "Wg|Éy" })
                    {
                        var t = TypeFor(c, f, s, false, false, 1, null, 0);
                        placed.Add((TextNote.Create(doc, v.Id, new XYZ(Util.Ft(x), Util.Ft(y), 0), txt, new TextNoteOptions(t.Id)), $"{f} {s}"));
                        x += 55;
                    }
                    var tw = TypeFor(c, f, s, false, false, 1, null, 0);
                    placed.Add((TextNote.Create(doc, v.Id, new XYZ(Util.Ft(x), Util.Ft(y), 0), Util.Ft(30), LongText, new TextNoteOptions(tw.Id)), $"{f} {s} wrapped 30mm"));
                    y -= s * 2 + 4 + (s * 6);
                }
            }
            doc.Regenerate();
            foreach (var (n, label) in placed)
            {
                var bb = n.get_BoundingBox(v);
                var a = new XYZ(bb.Min.X, bb.Min.Y, 0); var b = new XYZ(bb.Max.X, bb.Min.Y, 0); var cc = new XYZ(bb.Max.X, bb.Max.Y, 0); var d = new XYZ(bb.Min.X, bb.Max.Y, 0);
                foreach (var (p, q) in new[] { (a, b), (b, cc), (cc, d), (d, a) })
                {
                    var dc = doc.Create.NewDetailCurve(v, Line.CreateBound(p, q)); dc.LineStyle = c.ThinLines;
                }
                // 3 mm tick marking Coord (anchor) on the left, outside the box.
                var co = n.Coord;
                var tick = doc.Create.NewDetailCurve(v, Line.CreateBound(co + new XYZ(Util.Ft(-4), 0, 0), co + new XYZ(Util.Ft(-1), 0, 0))); tick.LineStyle = c.WideLines;
                items.Add(new JsonObject { ["label"] = label, ["text"] = n.Text, ["read"] = ReadNote(n, v) });
            }
        }, c.Fails);
        return new JsonObject { ["txStatus"] = st.ToString(), ["view"] = v.Name, ["items"] = items };
    }

    // ------------------------------------------------------------------ V6
    public static readonly Guid ChartSchemaGuid = new("6a7e2c51-0f00-4d1e-9a00-47414e540001");

    public static Schema ChartSchema()
    {
        var s = Schema.Lookup(ChartSchemaGuid);
        if (s != null) return s;
        var sb = new SchemaBuilder(ChartSchemaGuid);
        sb.SetSchemaName("GanttItSpikeChart");
        sb.SetVendorId("DSOS");
        sb.SetReadAccessLevel(AccessLevel.Vendor);
        sb.SetWriteAccessLevel(AccessLevel.Vendor);
        sb.SetDocumentation("Gantt-It Phase 0 spike chart entity");
        sb.AddSimpleField("ChartJson", typeof(string));
        sb.AddArrayField("GeneratedIds", typeof(string));
        sb.AddSimpleField("AppVersion", typeof(string));
        return sb.Finish();
    }

    public static JsonObject ReadEntity(Element e)
    {
        var s = Schema.Lookup(ChartSchemaGuid);
        if (s == null) return new JsonObject { ["schema"] = "not found" };
        var en = e.GetEntity(s);
        if (en == null || !en.IsValid()) return new JsonObject { ["valid"] = false };
        return new JsonObject
        {
            ["valid"] = true, ["ChartJson"] = en.Get<string>("ChartJson"), ["AppVersion"] = en.Get<string>("AppVersion"),
            ["GeneratedIds"] = new JsonArray(en.Get<IList<string>>("GeneratedIds").Select(x => (JsonNode)x).ToArray()),
        };
    }

    private static List<ElementId> AddContent(Ctx c, Document doc, View v, string label)
    {
        var ids = new List<ElementId>();
        ids.Add(TextNote.Create(doc, v.Id, new XYZ(0, Util.Ft(10), 0), label, doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)).Id);
        ids.Add(doc.Create.NewDetailCurve(v, Line.CreateBound(XYZ.Zero, new XYZ(Util.Ft(50), 0, 0))).Id);
        var frtId = c?.FillTypeId ?? new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).FirstElementId();
        ids.Add(FilledRegion.Create(doc, frtId, v.Id, new List<CurveLoop> { Rect(0, -10, 40, 4) }).Id);
        return ids;
    }

    private static JsonObject V6(Ctx c)
    {
        var doc = c.Doc; var v = c.V6;
        var schema = ChartSchema();
        var o = new JsonObject
        {
            ["schema"] = new JsonObject
            {
                ["guid"] = schema.GUID.ToString(), ["vendorId"] = schema.VendorId, ["read"] = schema.ReadAccessLevel.ToString(), ["write"] = schema.WriteAccessLevel.ToString(),
                ["readGranted"] = schema.ReadAccessGranted(), ["writeGranted"] = schema.WriteAccessGranted(),
            },
        };
        var chartJson = $"{{\"Id\":\"{Guid.NewGuid()}\",\"Name\":\"{v.Name}\",\"nonce\":\"{c.Stamp}\"}}";
        List<ElementId> gen = null;
        var st = Util.Tx(doc, "Gantt-It: V6 write", () =>
        {
            gen = AddContent(c, doc, v, "V6 chart content");
            var en = new Entity(schema);
            en.Set("ChartJson", chartJson);
            en.Set<IList<string>>("GeneratedIds", gen.Select(i => doc.GetElement(i).UniqueId).ToList());
            en.Set("AppVersion", "spike-0");
            v.SetEntity(en);
        }, c.Fails);
        o["writeTx"] = st.ToString();
        o["source"] = new JsonObject { ["id"] = Util.Id(v.Id), ["uniqueId"] = v.UniqueId, ["name"] = v.Name, ["entity"] = ReadEntity(v), ["owned"] = Util.Describe(Util.OwnedBy(doc, v.Id)) };
        o["chartJsonWritten"] = chartJson;

        var dups = new JsonObject();
        foreach (var opt in new[] { ViewDuplicateOption.Duplicate, ViewDuplicateOption.WithDetailing })
        {
            var rec = new JsonObject { ["canDuplicate"] = v.CanViewBeDuplicated(opt) };
            try
            {
                ElementId nid = null;
                rec["tx"] = Util.Tx(doc, $"Gantt-It: V6 duplicate {opt}", () => { nid = v.Duplicate(opt); }, c.Fails).ToString();
                var nv = (View)doc.GetElement(nid);
                var owned = Util.OwnedBy(doc, nid);
                rec["id"] = Util.Id(nid); rec["name"] = nv.Name; rec["scale"] = nv.Scale;
                rec["entity"] = ReadEntity(nv);
                rec["entityChartJsonIdentical"] = rec["entity"]["ChartJson"]?.GetValue<string>() == chartJson;
                rec["owned"] = Util.Describe(owned);
                var srcUids = gen.Select(i => doc.GetElement(i).UniqueId).ToHashSet();
                rec["ownedUniqueIdsMatchSourceGeneratedIds"] = owned.Count(e => srcUids.Contains(e.UniqueId));
            }
            catch (Exception ex) { rec["exception"] = Util.Err(ex); }
            dups[opt.ToString()] = rec;
        }
        o["duplicates"] = dups;
        o["asDependentAllowed"] = v.CanViewBeDuplicated(ViewDuplicateOption.AsDependent);
        return o;
    }

    /// <summary>Save / close / reopen in-process on a second (background) document.</summary>
    private static JsonObject V6Background(Ctx c, JsonObject args)
    {
        var path = args["v6bg"]?.GetValue<string>() ?? @"C:\GanttIt\work\v6-bg.rvt";
        if (!File.Exists(path)) return new JsonObject { ["skipped"] = "no file at " + path };
        var app = c.UiApp.Application;
        var o = new JsonObject { ["path"] = path };
        var schema = ChartSchema();
        var d2 = app.OpenDocumentFile(path);
        o["openedVersion"] = d2.Application.VersionNumber;
        var leg = Util.Legends(d2).First();
        string uid = null; var nonce = Guid.NewGuid().ToString();
        o["tx"] = Util.Tx(d2, "Gantt-It: V6 bg", () =>
        {
            var v = (View)d2.GetElement(leg.Duplicate(ViewDuplicateOption.Duplicate));
            v.Name = "GI V6 BG " + c.Stamp; v.Scale = 1;
            AddContent(null, d2, v, "V6 background content");
            var en = new Entity(schema);
            en.Set("ChartJson", "{\"nonce\":\"" + nonce + "\"}");
            en.Set<IList<string>>("GeneratedIds", new List<string> { "a", "b" });
            en.Set("AppVersion", "spike-0");
            v.SetEntity(en);
            uid = v.UniqueId;
        }, c.Fails).ToString();
        try { d2.Save(); o["save"] = "Save() ok"; }
        catch (Exception ex)
        {
            o["save"] = "Save() failed: " + ex.Message;
            d2.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true }); o["saveAs"] = "SaveAs(overwrite) ok";
        }
        o["close"] = d2.Close(false);
        var d3 = app.OpenDocumentFile(path);
        var v3 = d3.GetElement(uid) as View;
        o["reopened"] = new JsonObject
        {
            ["viewFound"] = v3 != null, ["name"] = v3?.Name, ["scale"] = v3?.Scale,
            ["entity"] = v3 == null ? null : ReadEntity(v3), ["owned"] = v3 == null ? null : Util.OwnedBy(d3, v3.Id).Count,
        };
        o["pass"] = v3 != null && o["reopened"]["entity"]["ChartJson"]?.GetValue<string>()?.Contains(nonce) == true;
        d3.Close(false);
        return o;
    }

    // ------------------------------------------------------------------ V8
    private static JsonObject V8(Ctx c)
    {
        var doc = c.Doc; var v = c.V8;
        var o = new JsonObject();
        var tb = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().FirstElementId();
        o["titleBlockTypeId"] = Util.Id(tb);
        ViewSheet s1 = null, s2 = null; Viewport vp1 = null, vp2 = null;
        o["placeTx"] = Util.Tx(doc, "Gantt-It: V8 place", () =>
        {
            AddContent(c, doc, v, "V8 legend content");
            s1 = ViewSheet.Create(doc, tb ?? ElementId.InvalidElementId); s1.Name = "GI V8 sheet A " + c.Stamp;
            s2 = ViewSheet.Create(doc, tb ?? ElementId.InvalidElementId); s2.Name = "GI V8 sheet B " + c.Stamp;
            o["canAdd1"] = Viewport.CanAddViewToSheet(doc, s1.Id, v.Id);
            vp1 = Viewport.Create(doc, s1.Id, v.Id, new XYZ(0.5, 0.5, 0));
            o["canAdd2"] = Viewport.CanAddViewToSheet(doc, s2.Id, v.Id);
            vp2 = Viewport.Create(doc, s2.Id, v.Id, new XYZ(0.5, 0.5, 0));
        }, c.Fails).ToString();
        o["sheets"] = new JsonArray(new[] { s1, s2 }.Select(s => (JsonNode)new JsonObject { ["id"] = Util.Id(s.Id), ["number"] = s.SheetNumber, ["name"] = s.Name }).ToArray());
        o["viewports"] = new JsonArray(new[] { vp1, vp2 }.Select(p => (JsonNode)Util.Id(p.Id)).ToArray());
        // How to enumerate the sheets a legend is on.
        o["enumerate_viewportCollector"] = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
            .Where(p => p.ViewId == v.Id).Select(p => (JsonNode)((ViewSheet)doc.GetElement(p.SheetId)).SheetNumber).ToArray());
        o["enumerate_sheetPlacedViews"] = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => s.GetAllPlacedViews().Contains(v.Id)).Select(s => (JsonNode)s.SheetNumber).ToArray());
        o["placementStatus"] = v.GetPlacementOnSheetStatus().ToString();
        o["viewSheetNumberParam"] = v.get_Parameter(BuiltInParameter.VIEWPORT_SHEET_NUMBER)?.AsString();
        var vid = v.Id; var vp1Id = vp1.Id; var vp2Id = vp2.Id; var s1Id = s1.Id; var s2Id = s2.Id;
        ICollection<ElementId> deleted = null;
        var failsBefore = c.Fails.Count;
        o["deleteTx"] = Util.Tx(doc, "Gantt-It: V8 delete", () => { deleted = doc.Delete(vid); }, c.Fails).ToString();
        o["deletedCount"] = deleted?.Count;
        o["deletedIncludesViewports"] = deleted != null && deleted.Contains(vp1Id) && deleted.Contains(vp2Id);
        o["viewExistsAfter"] = doc.GetElement(vid) != null;
        o["viewportsExistAfter"] = new JsonArray(doc.GetElement(vp1Id) != null, doc.GetElement(vp2Id) != null);
        o["sheetsExistAfter"] = new JsonArray(doc.GetElement(s1Id) != null, doc.GetElement(s2Id) != null);
        o["failuresDuringDelete"] = c.Fails.Count - failsBefore;
        o["pass"] = o["deleteTx"].GetValue<string>() == "Committed" && !o["viewExistsAfter"].GetValue<bool>()
            && doc.GetElement(vp1Id) == null && doc.GetElement(vp2Id) == null && c.Fails.Count == failsBefore;
        return o;
    }

    // ------------------------------------------------------------------ export / save / activate
    private static JsonObject Export(Ctx c)
    {
        var doc = c.Doc;
        var year = c.UiApp.Application.VersionNumber;
        var o = new JsonObject();
        foreach (var px in new[] { 6000 })
        {
            var opt = new ImageExportOptions
            {
                ExportRange = ExportRange.SetOfViews, FilePath = Path.Combine(SpikeApp.OutDir, $"visual-{year}"),
                HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG,
                ImageResolution = ImageResolution.DPI_600, ZoomType = ZoomFitType.FitToPage, PixelSize = px, FitDirection = FitDirectionType.Horizontal,
            };
            opt.SetViewsAndSheets(new List<ElementId> { c.Visual.Id });
            doc.ExportImage(opt);
        }
        o["files"] = new JsonArray(Directory.GetFiles(SpikeApp.OutDir, $"visual-{year}*").Select(f => (JsonNode)f).ToArray());
        return o;
    }

    private static JsonObject Save(Document doc)
    {
        var o = new JsonObject { ["path"] = doc.PathName };
        try { doc.Save(); o["save"] = "Save() ok"; }
        catch (Exception ex)
        {
            o["save"] = "Save() failed: " + ex.Message;
            try { doc.SaveAs(doc.PathName, new SaveAsOptions { OverwriteExistingFile = true }); o["saveAs"] = "ok"; }
            catch (Exception ex2) { o["saveAs"] = ex2.Message; }
        }
        return o;
    }

    private static JsonObject Activate(Ctx c)
    {
        var uidoc = c.UiApp.ActiveUIDocument;
        // UIDocument.ActiveView cannot be set from Idling ("Setting active view is temporarily disabled");
        // RequestViewChange is the asynchronous alternative.
        uidoc.RequestViewChange(c.Visual);
        return new JsonObject { ["requested"] = c.Visual.Name };
    }
}

/// <summary>Reads back what a previous run left in the model (cross-launch persistence check for V6).</summary>
internal static class Verify
{
    public static JsonObject Run(UIApplication uiapp, JsonObject args)
    {
        var doc = uiapp.ActiveUIDocument.Document;
        var o = new JsonObject { ["doc"] = doc.PathName, ["revit"] = uiapp.Application.VersionNumber, ["at"] = DateTime.Now.ToString("o") };
        o["schemaFound"] = Schema.Lookup(Spike.ChartSchemaGuid) != null;
        o["legends"] = new JsonArray(Util.Legends(doc).Select(v => (JsonNode)new JsonObject
        {
            ["id"] = Util.Id(v.Id), ["uniqueId"] = v.UniqueId, ["name"] = v.Name, ["scale"] = v.Scale,
            ["owned"] = Util.OwnedBy(doc, v.Id).Count, ["entity"] = Spike.ReadEntity(v),
        }).ToArray());
        o["sheets"] = new JsonArray(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Select(s => (JsonNode)new JsonObject { ["number"] = s.SheetNumber, ["name"] = s.Name, ["viewports"] = s.GetAllViewports().Count }).ToArray());
        return o;
    }
}
