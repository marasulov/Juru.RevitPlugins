using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Juru.RevitPlugins.ProjectSetup.Views;

internal abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Строка таблицы объектов. Дисциплины — индексатор: их список приходит с сервера.</summary>
internal sealed class ObjectRow : Observable
{
    // Код по стандарту: буквы, цифры, «&» и «-», без пробелов и «_» (то же правило, что на сервере)
    private static readonly Regex NotAllowedInCode = new(@"[^A-Za-zА-Яа-яЁё0-9&\-]");

    private readonly HashSet<string> _disciplines = new(StringComparer.OrdinalIgnoreCase);
    private string _folderName = "";
    private string _code = "";
    private bool _codeTouched;

    public long? Id { get; set; }
    public string Stage { get; set; } = "draft";
    public HashSet<string> Created { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Объект уже прошёл этап А: папки названы, переименовать нельзя.</summary>
    public bool Locked => Stage != "draft";

    public string StageText => Stage switch
    {
        "draft" => "черновик",
        "awaiting_coordination" => "ждёт координат",
        "ready" => "готов",
        _ => Stage
    };

    public string FolderName
    {
        get => _folderName;
        set
        {
            _folderName = value ?? "";
            Raise();
            if (!_codeTouched)
            {
                _code = NotAllowedInCode.Replace(_folderName, "");
                Raise(nameof(Code));
            }
        }
    }

    public string Code
    {
        get => _code;
        set
        {
            _code = value ?? "";
            _codeTouched = true;
            Raise();
        }
    }

    public bool this[string discipline]
    {
        get => _disciplines.Contains(discipline);
        set
        {
            if (value) _disciplines.Add(discipline); else _disciplines.Remove(discipline);
            Raise("Item[]");
        }
    }

    public List<string> Disciplines => _disciplines.OrderBy(d => d).ToList();

    public static ObjectRow Existing(long id, string folder, string code, string stage, IEnumerable<string> disciplines, IEnumerable<string> created)
    {
        var row = new ObjectRow { Id = id, Stage = stage, _folderName = folder, _code = code, _codeTouched = true };
        foreach (string d in disciplines) row._disciplines.Add(d);
        foreach (string d in created) row.Created.Add(d);
        return row;
    }
}

/// <summary>Строка шаблона: генплан (GEN), базовый файл (CRD) или дисциплина.</summary>
internal sealed class TemplateRow : Observable
{
    private string _path = "";
    private string _note = "";
    private bool _problem;

    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Folder { get; init; }

    public string Path
    {
        get => _path;
        set { _path = value ?? ""; Raise(); }
    }

    public string Note
    {
        get => _note;
        private set { _note = value; Raise(); }
    }

    public Brush NoteBrush => _problem ? Brushes.Firebrick : Brushes.Gray;

    public void SetNote(string note, bool problem)
    {
        _problem = problem;
        Note = note;
        Raise(nameof(NoteBrush));
    }
}
