using Newtonsoft.Json;

using dosymep.SimpleServices;

namespace dosymep.Serializers {
    /// <summary>
    /// Сервис сериализации на Newtonsoft.Json. Платформенная реализация из Bim4Everyone построена на
    /// pyRevitLabs.Json; здесь используется Newtonsoft, который плагин и так тянет. Единственный
    /// потребитель - сохранение позиции окна в WpfUIPlatformWindow.
    /// </summary>
    public class JsonSerializationService : ISerializationService {
        public string FileExtension => ".json";

        public string Serialize<T>(T @object) => JsonConvert.SerializeObject(@object);

        public T Deserialize<T>(string text) => JsonConvert.DeserializeObject<T>(text);
    }
}
