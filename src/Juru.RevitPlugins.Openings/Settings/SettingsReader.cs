using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Juru.RevitPlugins.Openings.Contracts;

namespace Juru.RevitPlugins.Openings.Settings
{
    public class SettingsReader : ISettingsReader
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true
        };

        private readonly string _jsonFileName;
        private SettingsRoot _cachedSettings;

        public SettingsReader()
        {
            _jsonFileName = Path.Combine(AssemblyDirectory, "Settings.json");
        }

        public Dictionary<string, MappingInfo> GetArMappings()
        {
            var dict = new Dictionary<string, MappingInfo>();

            // Получаем корень настроек (HoleFamilyTypes)
            var root = GetFamilyTypes();

            // Проверяем, чтобы не было null на уровне промежуточного класса FamilyTypes
            if (root?.FamilyTypes == null) return dict;

            // Теперь обращаемся правильно, с учетом вложенности:
            var circled = root.FamilyTypes.Circled;
            var rect = root.FamilyTypes.Rectangled;

            // Заполняем словарь круглыми отверстиями
            if (circled?.FamilyNames != null && circled?.ArFamilyNames != null)
            {
                dict[circled.FamilyNames.WallType] = new MappingInfo { ARFamilyName = circled.ArFamilyNames.WallType, IsRect = false };
                dict[circled.FamilyNames.FloorType] = new MappingInfo { ARFamilyName = circled.ArFamilyNames.FloorType, IsRect = false };
            }

            // Заполняем словарь прямоугольными отверстиями
            if (rect?.FamilyNames != null && rect?.ArFamilyNames != null)
            {
                dict[rect.FamilyNames.WallType] = new MappingInfo { ARFamilyName = rect.ArFamilyNames.WallType, IsRect = true };
                dict[rect.FamilyNames.FloorType] = new MappingInfo { ARFamilyName = rect.ArFamilyNames.FloorType, IsRect = true };
            }

            return dict;
        }

        private SettingsRoot GetRootSettings()
        {
            // Если уже читали - возвращаем сохраненный результат
            if (_cachedSettings != null)
                return _cachedSettings;

            if (!File.Exists(_jsonFileName))
                throw new FileNotFoundException($"Файл настроек не найден по пути: {_jsonFileName}");

            using FileStream fs = new FileStream(_jsonFileName, FileMode.Open, FileAccess.Read);
            _cachedSettings = JsonSerializer.Deserialize<SettingsRoot>(fs, _options);

            return _cachedSettings;
        }


        public HoleFamilyTypes? GetFamilyTypes()
        {
            return GetRootSettings()?.FamilyTypes;
        }

        public List<string> GetFamilyNames()
        {
            return GetRootSettings()?.FamilyNames;
        }

        public static string AssemblyDirectory
        {
            get
            {
                string location = Assembly.GetExecutingAssembly().Location;
                return Path.GetDirectoryName(location);
            }
        }
    }
}
