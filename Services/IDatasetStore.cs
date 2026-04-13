using SalesDataAnalyzer.Api.Models;

namespace SalesDataAnalyzer.Api.Services;

public interface IDatasetStore
{
    string SaveDataset(DatasetModel dataset);

    bool TryGetDataset(string datasetId, out DatasetModel? dataset);

    void AddChatMessage(string sessionId, ChatMessageModel message);

    IReadOnlyList<ChatMessageModel> GetChatHistory(string sessionId);
}
