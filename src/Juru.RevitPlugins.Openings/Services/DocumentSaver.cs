using Autodesk.Revit.DB;

namespace Juru.RevitPlugins.Openings.Services;

/// <summary>
/// Сохраняет файл: синхронизирует с центральной моделью, если файл общий, иначе сохраняет локально.
/// </summary>
/// <remarks>
/// Нужно двум местам, и по одной причине: и отправка заданий, и прорезание согласованных отверстий
/// делают так, что сервер начинает знать о том, что должно лежать в файле. Несохранённый файл
/// расходится с сервером молча - задание на нём согласовано и, значит, больше не попадёт на глаза
/// тому, кто его согласовал.
/// </remarks>
public static class DocumentSaver {
    /// <summary>
    /// Сохраняет файл, если он изменён. Вызывается только в контексте Revit API и вне транзакции.
    /// </summary>
    public static void SaveOrSynchronize(Document doc) {
        if(doc is null || !doc.IsModified) {
            return;
        }

        if(doc.IsWorkshared) {
            doc.SynchronizeWithCentral(new TransactWithCentralOptions(), new SynchronizeWithCentralOptions());
        } else {
            doc.Save();
        }
    }
}
