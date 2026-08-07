using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SimpleInjector;
using System;
using Juru.RevitPlugins.Openings.Contracts;
using Juru.RevitPlugins.Openings.Services;
using Juru.RevitPlugins.Openings.Settings;
using Juru.RevitPlugins.Openings.Views;
using Juru.RevitPlugins.Openings.ViewModels;
using Juru.RevitPlugins.Openings.ViewModels.Commands;

namespace Juru.RevitPlugins.Openings.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class CreateHoleCommand : IExternalCommand
    {
        private static HoleTaskView? _windowInstanse;
        static AddInId addinId = new AddInId(new Guid("0596598B-2718-4D68-9988-ECE735BC437A"));
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDocument = commandData
                .Application
                .ActiveUIDocument;

            var app = commandData
                .Application
                .Application;

            var container = ConfigureUI();

            container.RegisterInstance(app);
            container.RegisterInstance(uiDocument);
            container.Register<ISettingsReader, SettingsReader>();
            container.Register<IFamilyInsertService, FamilyInsertService>();
            container.Register<IZoomEntity, ZoomElementService>();
            container.Register<IFindHoleService, FindHolesService>();
            container.Register<EntityToDtoConverter>();

            var window = container
                .GetInstance<HoleTaskView>();

            var context = (HolesVm)window.DataContext;
            context.FindHolesCommand.Execute(null);

            window.ShowDialog();

            return Result.Succeeded;
        }


        public static Container ConfigureUI()
        {
            var container = new Container();
            container.Register<CreateHolesCommand>();
            container.Register<ZoomToPointCommand>();
            container.Register<FindHolesCommand>();
            container.Register<HolesVm>();
            container.Register<HoleTaskView>();
            return container;
        }
    }
}
