using Microsoft.AspNetCore.Mvc;
using SalesDataAnalyzer.Api.Models;
using SalesDataAnalyzer.Api.Services;

namespace SalesDataAnalyzer.Api.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController : ControllerBase
{
    private readonly IDatasetStore _datasetStore;
    private readonly IAgentInsightService _agentInsightService;
    private readonly ISalesDomainValidationService _salesDomainValidationService;

    public AgentController(
        IDatasetStore datasetStore,
        IAgentInsightService agentInsightService,
        ISalesDomainValidationService salesDomainValidationService)
    {
        _datasetStore = datasetStore;
        _agentInsightService = agentInsightService;
        _salesDomainValidationService = salesDomainValidationService;
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AgentResponse>> Ask([FromBody] AskRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DatasetId))
        {
            return BadRequest("DatasetId yuborilmadi.");
        }

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest("Savol yuborilmadi.");
        }

        if (!_datasetStore.TryGetDataset(request.DatasetId, out var dataset) || dataset is null)
        {
            return NotFound("Dataset topilmadi.");
        }

        var questionValidation = _salesDomainValidationService.ValidateQuestion(dataset, request.Question);
        if (!questionValidation.IsValid)
        {
            return BadRequest(questionValidation.Message);
        }

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString("N")
            : request.SessionId.Trim();

        _datasetStore.AddChatMessage(sessionId, new ChatMessageModel
        {
            Role = "user",
            Content = request.Question.Trim()
        });

        var history = _datasetStore.GetChatHistory(sessionId);
        var response = await _agentInsightService.AskAsync(
            dataset,
            sessionId,
            request.Question,
            history,
            request.ApiKey,
            cancellationToken);

        _datasetStore.AddChatMessage(sessionId, new ChatMessageModel
        {
            Role = "assistant",
            Content = response.Answer
        });

        var finalHistory = _datasetStore.GetChatHistory(sessionId);

        return Ok(new AgentResponse
        {
            SessionId = sessionId,
            Answer = response.Answer,
            SuggestedCharts = response.SuggestedCharts,
            History = finalHistory,
            Source = response.Source,
            DebugMessage = response.DebugMessage
        });
    }
}
