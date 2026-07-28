using System;
using Newtonsoft.Json;
using Autodesk.Revit.DB;

using dosymep.Bim4Everyone.ProjectConfigs;
using dosymep.Revit;

namespace dosymep.Serializers {
    public class ConfigSerializer : IConfigSerializer {
        public T Deserialize<T>(string text) {
            return JsonConvert.DeserializeObject<T>(text,
                new ElementIdConverter());
        }

        public string Serialize<T>(T @object) {
            return JsonConvert.SerializeObject(@object,
                new ElementIdConverter());
        }
    }

    public class ElementIdConverter : JsonConverter<ElementId> {
        public override void WriteJson(JsonWriter writer, ElementId value, JsonSerializer serializer) {
            writer.WriteValue(value.GetIdValue());
        }

        public override ElementId ReadJson(
            JsonReader reader,
            Type objectType,
            ElementId existingValue,
            bool hasExistingValue,
            JsonSerializer serializer) {

            if(reader.Value is null) {
                return ElementId.InvalidElementId;
            }

#if REVIT2024_OR_GREATER
    return new ElementId(Convert.ToInt64(reader.Value));
#else
            return new ElementId(Convert.ToInt32(reader.Value));
#endif
        }
    }
}