using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;

namespace Juru.RevitPlugins.ClashNavigator.Services.ClashHighlight;
/// <summary>
/// Рисует элементы выбранной коллизии цветом поверх модели на виде коллизий.
/// </summary>
/// <remarks>
/// Переопределение графики (<c>View.SetElementOverrides</c>) не годится: оно не действует на
/// отдельный элемент связи, а у проверок плагина одна сторона почти всегда связь. Временная
/// графика DirectContext3D рисуется поверх любой геометрии, в модель ничего не пишет и
/// транзакции не требует.
/// <para>
/// Грани чуть отодвинуты наружу по нормали: геометрия, совпадающая с гранями модели, мерцала бы
/// на их фоне. Грани полупрозрачные и идут в прозрачном проходе, рёбра - непрозрачные, в обычном.
/// </para>
/// <para>
/// Грани - формат <c>PositionNormal</c>, цвет задаётся эффектом, включая собственное свечение.
/// Цвет в вершинах (<c>PositionNormalColored</c>) проверен в Ревите 2024 и не годится: грани
/// выходили почти чёрными, освещение вида затирало цвет. Поэтому грани идут порциями одного
/// цвета, по порции на элемент коллизии.
/// </para>
/// <para>
/// Буферы видеокарты строятся лениво, при первой отрисовке новой подсветки: создавать их можно
/// только в контексте Revit API, а отрисовка в нём всегда.
/// </para>
/// </remarks>
internal sealed class ClashHighlightServer : IDirectContext3DServer {
    /// <summary>
    /// Индексы в буфере 16-битные: на один буфер не больше 65 535 вершин.
    /// </summary>
    private const int _maxVerticesPerBuffer = 60000;

    /// <summary>
    /// Прозрачность граней: 0 - непрозрачные, 1 - невидимые.
    /// </summary>
    private const double _faceTransparency = 0.35;

    private readonly Guid _serverId = Guid.NewGuid();
    private readonly object _sync = new();

    private ClashHighlightScene _scene;
    private List<RenderBuffer> _faceBuffers = [];
    private List<RenderBuffer> _edgeBuffers = [];
    private bool _buffersDirty;

    public Guid GetServerId() => _serverId;
    public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;
    public string GetName() => "Juru clash highlight";
    public string GetVendorId() => "Juru";
    public string GetDescription() => "Highlights the elements of the selected clash.";
    public string GetApplicationId() => string.Empty;
    public string GetSourceId() => string.Empty;
    public bool UsesHandles() => false;
    public bool UseInTransparentPass(View view) => true;

    /// <summary>
    /// Задаёт, что рисовать, или убирает подсветку при <see langword="null" />.
    /// </summary>
    public void SetScene(ClashHighlightScene scene) {
        lock(_sync) {
            _scene = scene;
            _buffersDirty = true;
        }
    }

    public bool CanExecute(View view) {
        var scene = _scene;
        return scene is not null && scene.IsTarget(view);
    }

    public Outline GetBoundingBox(View view) {
        return _scene?.Bounds;
    }

    public void RenderScene(View view, DisplayStyle displayStyle) {
        lock(_sync) {
            if(_buffersDirty) {
                RebuildBuffers();
            }
        }

        bool transparentPass = DrawContext.IsTransparentPass();
        foreach(var buffer in transparentPass ? _faceBuffers : _edgeBuffers) {
            buffer.Flush();
        }
    }

    private void RebuildBuffers() {
        _buffersDirty = false;
        _faceBuffers = [];
        _edgeBuffers = [];
        if(_scene is null) {
            return;
        }

        foreach(var faces in _scene.Faces) {
            foreach(var chunk in Chunk(faces.Triangles, _maxVerticesPerBuffer / 3)) {
                _faceBuffers.Add(RenderBuffer.FromTriangles(chunk, faces.Color));
            }
        }
        foreach(var chunk in Chunk(_scene.Edges, _maxVerticesPerBuffer / 2)) {
            _edgeBuffers.Add(RenderBuffer.FromLines(chunk));
        }
    }

    private static IEnumerable<List<T>> Chunk<T>(IReadOnlyList<T> items, int size) {
        for(int start = 0; start < items.Count; start += size) {
            var chunk = new List<T>(Math.Min(size, items.Count - start));
            for(int i = start; i < items.Count && i < start + size; i++) {
                chunk.Add(items[i]);
            }
            yield return chunk;
        }
    }

    /// <summary>
    /// Готовые к отрисовке буферы одной порции треугольников или отрезков.
    /// </summary>
    private sealed class RenderBuffer {
        private VertexBuffer _vertices;
        private IndexBuffer _indices;
        private VertexFormat _format;
        private EffectInstance _effect;
        private PrimitiveType _primitive;
        private int _vertexCount;
        private int _indexCount;
        private int _primitiveCount;

        public static RenderBuffer FromTriangles(IReadOnlyList<HighlightTriangle> triangles, Color color) {
            int vertexCount = triangles.Count * 3;
            var vertices = new VertexBuffer(vertexCount * VertexPositionNormal.GetSizeInFloats());
            vertices.Map(vertexCount * VertexPositionNormal.GetSizeInFloats());
            var vertexStream = vertices.GetVertexStreamPositionNormal();
            foreach(var triangle in triangles) {
                vertexStream.AddVertex(new VertexPositionNormal(triangle.A, triangle.Normal));
                vertexStream.AddVertex(new VertexPositionNormal(triangle.B, triangle.Normal));
                vertexStream.AddVertex(new VertexPositionNormal(triangle.C, triangle.Normal));
            }
            vertices.Unmap();

            var indices = new IndexBuffer(triangles.Count * IndexTriangle.GetSizeInShortInts());
            indices.Map(triangles.Count * IndexTriangle.GetSizeInShortInts());
            var indexStream = indices.GetIndexStreamTriangle();
            for(int i = 0; i < triangles.Count; i++) {
                indexStream.AddTriangle(new IndexTriangle(i * 3, i * 3 + 1, i * 3 + 2));
            }
            indices.Unmap();

            var effect = new EffectInstance(VertexFormatBits.PositionNormal);
            effect.SetColor(color);
            effect.SetDiffuseColor(color);
            effect.SetAmbientColor(color);
            effect.SetEmissiveColor(color);
            effect.SetSpecularColor(color);
            effect.SetTransparency(_faceTransparency);

            return new RenderBuffer {
                _vertices = vertices,
                _indices = indices,
                _format = new VertexFormat(VertexFormatBits.PositionNormal),
                _effect = effect,
                _primitive = PrimitiveType.TriangleList,
                _vertexCount = vertexCount,
                _indexCount = vertexCount,
                _primitiveCount = triangles.Count
            };
        }

        public static RenderBuffer FromLines(IReadOnlyList<HighlightLine> lines) {
            int vertexCount = lines.Count * 2;
            var vertices = new VertexBuffer(vertexCount * VertexPositionColored.GetSizeInFloats());
            vertices.Map(vertexCount * VertexPositionColored.GetSizeInFloats());
            var vertexStream = vertices.GetVertexStreamPositionColored();
            foreach(var line in lines) {
                vertexStream.AddVertex(new VertexPositionColored(line.Start, line.Color));
                vertexStream.AddVertex(new VertexPositionColored(line.End, line.Color));
            }
            vertices.Unmap();

            var indices = new IndexBuffer(lines.Count * IndexLine.GetSizeInShortInts());
            indices.Map(lines.Count * IndexLine.GetSizeInShortInts());
            var indexStream = indices.GetIndexStreamLine();
            for(int i = 0; i < lines.Count; i++) {
                indexStream.AddLine(new IndexLine(i * 2, i * 2 + 1));
            }
            indices.Unmap();

            return new RenderBuffer {
                _vertices = vertices,
                _indices = indices,
                _format = new VertexFormat(VertexFormatBits.PositionColored),
                _effect = new EffectInstance(VertexFormatBits.PositionColored),
                _primitive = PrimitiveType.LineList,
                _vertexCount = vertexCount,
                _indexCount = vertexCount,
                _primitiveCount = lines.Count
            };
        }

        public void Flush() {
            DrawContext.FlushBuffer(
                _vertices, _vertexCount, _indices, _indexCount, _format, _effect, _primitive, 0, _primitiveCount);
        }
    }
}
