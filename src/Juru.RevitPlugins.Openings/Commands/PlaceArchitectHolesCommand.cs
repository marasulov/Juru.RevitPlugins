using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Juru.RevitPlugins.Openings.Contracts;
using Juru.RevitPlugins.Openings.Services;
using Juru.RevitPlugins.Openings.Settings;
using SimpleInjector;

namespace Juru.RevitPlugins.Openings.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class PlaceArchitectHolesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDocument = commandData.Application.ActiveUIDocument;
            var app = commandData.Application.Application;

            // Настраиваем контейнер
            var container = ConfigureUI(app, uiDocument);

            // Получаем готовый сервис из контейнера
            var architectService = container.GetInstance<IArchitectHoleService>();

            try
            {
                // Запускаем бизнес-логику
                architectService.PlaceHolesFromLink();

                TaskDialog.Show("Успех", "Архитектурные отверстия успешно расставлены!");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"Ошибка: {ex.Message}";
                return Result.Failed;
            }
        }

        public static Container ConfigureUI(Autodesk.Revit.ApplicationServices.Application app, UIDocument uiDocument)
        {
            var container = new Container();

            // Регистрируем инстансы Revit
            container.RegisterInstance(app);
            container.RegisterInstance(uiDocument);

            // Регистрируем наши сервисы
            container.Register<ISettingsReader, SettingsReader>();
            container.Register<IArchitectHoleService, ArchitectHoleService>();

            return container;
        }
    }
}
