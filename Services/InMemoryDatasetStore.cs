using System.Collections.Concurrent;
using SalesDataAnalyzer.Api.Models;

namespace SalesDataAnalyzer.Api.Services;

public sealed class InMemoryDatasetStore : IDatasetStore
{
    private readonly ConcurrentDictionary<string, DatasetModel> _datasets = new();
    private readonly ConcurrentDictionary<string, List<ChatMessageModel>> _chatHistory = new();
    private readonly ConcurrentDictionary<string, object> _sessionLocks = new();

    public string SaveDataset(DatasetModel dataset)
    {
        _datasets[dataset.Id] = dataset;
        return dataset.Id;
    }

    public bool TryGetDataset(string datasetId, out DatasetModel? dataset)
    {
        var found = _datasets.TryGetValue(datasetId, out var value);
        dataset = value;
        return found;
    }

    public void AddChatMessage(string sessionId, ChatMessageModel message)
    {
        var lockObject = _sessionLocks.GetOrAdd(sessionId, _ => new object());
        lock (lockObject)
        {
            var history = _chatHistory.GetOrAdd(sessionId, _ => new List<ChatMessageModel>());
            history.Add(message);
        }
    }

    public IReadOnlyList<ChatMessageModel> GetChatHistory(string sessionId)
    {
        if (!_chatHistory.TryGetValue(sessionId, out var history))
        {
            return [];
        }

        var lockObject = _sessionLocks.GetOrAdd(sessionId, _ => new object());
        lock (lockObject)
        {
            return history.ToArray();
        }
    }
}
