using TriviaSync.Api.Models;

namespace TriviaSync.Api.Services;

public interface IExportService
{
    byte[] ExportLeaderboardToCsv(IEnumerable<PersistentPlayer> players);
    byte[] ExportLeaderboardToExcelXml(IEnumerable<PersistentPlayer> players);
    byte[] ExportSessionToCsv(GameSession session);
    byte[] ExportSessionToExcelXml(GameSession session);
}
