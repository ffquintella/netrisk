using System;
using System.Text.Json;
using Material.Icons;
using Model.Entities;

namespace GUIClient.Tools;

public static class ExtensionMethods
{
    public static T? DeepCopy<T>(this T self)
    {
        //var serialized = JsonConvert.SerializeObject(self);
        var serialized = JsonSerializer.Serialize(self);
        return JsonSerializer.Deserialize<T>(serialized);
        //return JsonConvert.DeserializeObject<T>(serialized);
    }

    public static MaterialIconKind GetIcon(this EntityDefinition self)
    {
        var iconName = self.IconKind;
        if(iconName is not null)
        {
            if(Enum.TryParse<MaterialIconKind>(iconName, out var icon))
            {
                return icon;
            }
        }
        return MaterialIconKind.Forbid;
    }
}

