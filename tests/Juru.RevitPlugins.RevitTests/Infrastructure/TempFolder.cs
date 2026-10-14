using System;
using System.IO;

namespace Juru.RevitPlugins.RevitTests.Infrastructure;

/// <summary>
/// Своя папка на тест: Ревит не откроет два документа с одним путём, а тесты с файлами
/// идут вперемешку.
/// </summary>
public sealed class TempFolder : IDisposable {
    public TempFolder() {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "JuruRevitTests", Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>
    /// Короткий суффикс для имён файлов. Ревит не держит открытыми два документа с одинаковым
    /// именем файла даже из разных папок.
    /// </summary>
    public string Suffix => System.IO.Path.GetFileName(Path);

    public string File(string name) {
        return System.IO.Path.Combine(Path, name);
    }

    public void Dispose() {
        try {
            Directory.Delete(Path, recursive: true);
        } catch(IOException) {
            // Файл ещё держит Ревит: мусор во временной папке не повод ронять тест.
        } catch(UnauthorizedAccessException) {
        }
    }
}
