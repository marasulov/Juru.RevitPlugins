using System;
using System.Reflection;

using Newtonsoft.Json.Serialization;

namespace Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
/// <summary>
/// Привязка <c>$type</c> в файлах настроек к типам плагина независимо от имени сборки.
/// </summary>
/// <remarks>
/// Под AddinManager типы плагина живут в <c>Juru.RevitPlugins.ClashNavigator</c>, в релизе ILRepack
/// сливает их в <c>Juru.RevitPlugins.App</c>. Если сравнивать имя сборки из файла с текущим, файл,
/// сохранённый одной сборкой, не читается другой: Newtonsoft грузит сборку по имени из файла
/// (а это может оказаться старый DLL из папки установки) и получает одноимённый, но чужой тип.
/// Поэтому тип ищется по полному имени в своей сборке, а пишется всегда с исходным именем сборки -
/// одним и тем же для отладки и релиза.
/// </remarks>
public class RevitClashesSerializationBinder : ISerializationBinder {
    /// <summary>
    /// Имя сборки, которое пишется в файлы для типов плагина.
    /// </summary>
    public const string PluginAssemblyName = "Juru.RevitPlugins.ClashNavigator";

    private static readonly Assembly _pluginAssembly = typeof(RevitClashesSerializationBinder).Assembly;

    private readonly DefaultSerializationBinder _defaultBinder = new();

    public void BindToName(Type serializedType, out string assemblyName, out string typeName) {
        if(serializedType.Assembly == _pluginAssembly) {
            assemblyName = PluginAssemblyName;
            typeName = serializedType.FullName;
        } else {
            _defaultBinder.BindToName(serializedType, out assemblyName, out typeName);
        }
    }

    public Type BindToType(string assemblyName, string typeName) {
        return _pluginAssembly.GetType(typeName)
               ?? _defaultBinder.BindToType(assemblyName, typeName);
    }
}
