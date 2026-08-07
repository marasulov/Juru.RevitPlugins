namespace Juru.RevitPlugins.Openings.Services
{
    using Autodesk.Revit.DB;
    using Juru.RevitPlugins.Openings.Contracts.DTOs;


    public class EntityToDtoConverter
    {
        public OutdatedFamilyDto Convert(Element element)
        {
            var res = new OutdatedFamilyDto(
                element.Id.IntegerValue.ToString(),
                element.Name,
                TryGetLocation(element));

            return res;
        }

        public PointDTO? TryGetLocation(Element e)
        {
            return e.Location is LocationPoint lp ?
                new PointDTO(lp.Point.X, lp.Point.X, lp.Point.X) :
                null;
        }
    }
}
