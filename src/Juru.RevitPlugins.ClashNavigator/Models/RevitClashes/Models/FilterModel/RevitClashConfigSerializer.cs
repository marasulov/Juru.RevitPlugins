using System;

using Autodesk.Revit.DB;

using Juru.RevitPlugins.Core.ProjectConfigs;

using dosymep.Serializers;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
internal class RevitClashConfigSerializer : IConfigSerializer {
    private readonly ISerializationBinder _serializationBinder;
    private readonly Document _document;
    private readonly RevitParamConverter _systemParamConverter;
    private readonly ElementIdConverter _elementIdConverter;

    public RevitClashConfigSerializer(ISerializationBinder serializationBinder, Document document) {
        if(serializationBinder is null) { throw new ArgumentNullException(nameof(serializationBinder)); }
        if(document is null) { throw new ArgumentNullException(nameof(document)); }

        _serializationBinder = serializationBinder;
        _document = document;
        _systemParamConverter = new RevitParamConverter(_document);
        _elementIdConverter = new ElementIdConverter();
    }


    public T Deserialize<T>(string text) {
        return JsonConvert.DeserializeObject<T>(text, new JsonSerializerSettings {
            TypeNameHandling = TypeNameHandling.Objects,
            SerializationBinder = _serializationBinder,
            Converters = [_systemParamConverter, _elementIdConverter]
        });
    }

    public string Serialize<T>(T @object) {
        return JsonConvert.SerializeObject(@object, Formatting.Indented, new JsonSerializerSettings {
            TypeNameHandling = TypeNameHandling.Objects,
            SerializationBinder = _serializationBinder,
            Converters = [_systemParamConverter, _elementIdConverter]
        });
    }
}
