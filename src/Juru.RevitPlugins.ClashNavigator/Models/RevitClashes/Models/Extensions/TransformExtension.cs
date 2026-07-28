using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.ClashNavigator.Models.Extensions;
internal static class TransformExtension {
    public static Transform GetTransitionMatrix(this Transform transform, Transform otherTransform) {
        var inversedTransform = transform.Inverse;
        return inversedTransform.Multiply(otherTransform);
    }
}
