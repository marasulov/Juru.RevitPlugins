using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;

using Juru.RevitPlugins.ClashNavigator.Models.Extensions;

namespace Juru.RevitPlugins.ClashNavigator.Services.ClashHighlight;
/// <summary>
/// Треугольник подсветки в координатах открытого документа.
/// </summary>
/// <remarks>
/// Цвет у треугольника не хранится: грани рисуются порциями одного цвета, а цвет задаётся
/// эффектом отрисовки (см. <see cref="ClashHighlightServer" />).
/// </remarks>
internal readonly record struct HighlightTriangle(XYZ A, XYZ B, XYZ C, XYZ Normal);

/// <summary>
/// Грани одного элемента подсветки и их цвет.
/// </summary>
internal sealed record HighlightFaces(Color Color, IReadOnlyList<HighlightTriangle> Triangles);

/// <summary>
/// Отрезок ребра подсветки в координатах открытого документа.
/// </summary>
internal readonly record struct HighlightLine(XYZ Start, XYZ End, ColorWithTransparency Color);

/// <summary>
/// Элемент коллизии с положением его документа и цветом подсветки.
/// </summary>
/// <param name="Element">Элемент открытого документа или связи.</param>
/// <param name="Transform">Положение документа элемента относительно открытого.</param>
/// <param name="Color">Цвет подсветки.</param>
internal sealed record HighlightedElement(Element Element, Transform Transform, Color Color);

/// <summary>
/// Что рисует <see cref="ClashHighlightServer" />: готовая геометрия и вид, на котором она видна.
/// </summary>
/// <remarks>
/// Геометрия разворачивается в треугольники сразу, в контексте Revit API: сервер отрисовки потом
/// только раскладывает её по буферам. Документ не запоминается - только путь и id вида,
/// иначе после закрытия файла обращение к нему роняло бы Ревит.
/// </remarks>
internal sealed class ClashHighlightScene {
    /// <summary>
    /// Сдвиг граней наружу, футы (около 1,5 мм) - чтобы они не мерцали на гранях модели.
    /// </summary>
    private const double _faceOffset = 0.005;

    private readonly ElementId _viewId;
    private readonly string _documentKey;

    private ClashHighlightScene(View view, List<HighlightFaces> faces, List<HighlightLine> edges) {
        _viewId = view.Id;
        _documentKey = GetDocumentKey(view.Document);
        Faces = faces;
        Edges = edges;
        Bounds = GetBounds(faces.SelectMany(item => item.Triangles).ToArray());
    }

    public IReadOnlyList<HighlightFaces> Faces { get; }
    public IReadOnlyList<HighlightLine> Edges { get; }
    public Outline Bounds { get; }

    /// <summary>
    /// Собирает подсветку для вида; <see langword="null" />, если рисовать нечего.
    /// </summary>
    public static ClashHighlightScene Create(View view, IEnumerable<HighlightedElement> elements) {
        if(view is null) { throw new ArgumentNullException(nameof(view)); }
        if(elements is null) { throw new ArgumentNullException(nameof(elements)); }

        List<HighlightFaces> faces = [];
        List<HighlightLine> edges = [];
        foreach(var item in elements) {
            List<HighlightTriangle> triangles = [];
            var edgeColor = new ColorWithTransparency(item.Color.Red, item.Color.Green, item.Color.Blue, 0);
            foreach(var solid in item.Element.GetSolids()) {
                AddSolid(solid, item.Transform ?? Transform.Identity, edgeColor, triangles, edges);
            }
            if(triangles.Count > 0) {
                faces.Add(new HighlightFaces(item.Color, triangles));
            }
        }

        return faces.Count == 0 && edges.Count == 0
            ? null
            : new ClashHighlightScene(view, faces, edges);
    }

    public bool IsTarget(View view) {
        return view.Id == _viewId && GetDocumentKey(view.Document) == _documentKey;
    }

    private static void AddSolid(
        Solid solid,
        Transform transform,
        ColorWithTransparency edgeColor,
        List<HighlightTriangle> triangles,
        List<HighlightLine> edges) {

        foreach(Face face in solid.Faces) {
            Mesh mesh;
            try {
                mesh = face.Triangulate();
            } catch(Autodesk.Revit.Exceptions.ApplicationException) {
                continue;
            }
            if(mesh is null) {
                continue;
            }

            for(int i = 0; i < mesh.NumTriangles; i++) {
                var triangle = mesh.get_Triangle(i);
                var a = transform.OfPoint(triangle.get_Vertex(0));
                var b = transform.OfPoint(triangle.get_Vertex(1));
                var c = transform.OfPoint(triangle.get_Vertex(2));
                var normal = (b - a).CrossProduct(c - a);
                if(normal.GetLength() < 1e-12) {
                    continue;
                }
                normal = normal.Normalize();
                var offset = normal * _faceOffset;
                triangles.Add(new HighlightTriangle(a + offset, b + offset, c + offset, normal));
            }
        }

        foreach(Edge edge in solid.Edges) {
            IList<XYZ> points;
            try {
                points = edge.Tessellate();
            } catch(Autodesk.Revit.Exceptions.ApplicationException) {
                continue;
            }
            for(int i = 1; i < points.Count; i++) {
                edges.Add(new HighlightLine(transform.OfPoint(points[i - 1]), transform.OfPoint(points[i]), edgeColor));
            }
        }
    }

    private static Outline GetBounds(IReadOnlyCollection<HighlightTriangle> triangles) {
        if(triangles.Count == 0) {
            return new Outline(XYZ.Zero, XYZ.Zero);
        }
        var points = triangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToArray();
        var min = new XYZ(points.Min(p => p.X), points.Min(p => p.Y), points.Min(p => p.Z));
        var max = new XYZ(points.Max(p => p.X), points.Max(p => p.Y), points.Max(p => p.Z));
        return new Outline(min, max);
    }

    private static string GetDocumentKey(Document document) {
        return string.IsNullOrEmpty(document.PathName) ? document.Title : document.PathName;
    }
}
