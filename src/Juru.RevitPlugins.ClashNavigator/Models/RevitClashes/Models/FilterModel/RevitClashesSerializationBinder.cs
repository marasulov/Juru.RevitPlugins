using System;
using System.Reflection;

using Newtonsoft.Json.Serialization;

namespace Juru.RevitPlugins.ClashNavigator.Models.FilterModel;
public class RevitClashesSerializationBinder : Newtonsoft.Json.Serialization.ISerializationBinder
{

    private readonly DefaultSerializationBinder defaultBinder = new();

    public void BindToName(Type serializedType, out string assemblyName, out string typeName) {
        if(serializedType.Assembly.GetName().Name.Equals(GetCurrentAssemblyName())) {
            assemblyName = GetCurrentAssemblyName();
            typeName = serializedType.FullName;
        } else {
            defaultBinder.BindToName(serializedType, out assemblyName, out typeName);
        }
    }

    public Type BindToType(string assemblyName, string typeName) {
        return assemblyName.Equals(GetCurrentAssemblyName())
            ? Assembly.GetExecutingAssembly().GetType(typeName)
            : defaultBinder.BindToType(assemblyName, typeName);
    }

    private string GetCurrentAssemblyName() {
        return Assembly.GetExecutingAssembly().GetName().Name;
    }
}
