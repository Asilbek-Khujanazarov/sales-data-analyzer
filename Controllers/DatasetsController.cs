using Microsoft.AspNetCore.Mvc;
using SalesDataAnalyzer.Api.Models;
using SalesDataAnalyzer.Api.Services;

namespace SalesDataAnalyzer.Api.Controllers;

[ApiController]
[Route("api/datasets")]
public sealed class DatasetsController : ControllerBase
{
    private readonly IDatasetStore _datasetStore;
    private readonly IAnalyticsService _analyticsService;

    public DatasetsController(IDatasetStore datasetStore, IAnalyticsService analyticsService)
    {
        _datasetStore = datasetStore;
        _analyticsService = analyticsService;
    }

    [HttpGet("{datasetId}/preview")]
    public ActionResult<PreviewResponse> Preview([FromRoute] string datasetId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (!_datasetStore.TryGetDataset(datasetId, out var dataset) || dataset is null)
        {
            return NotFound("Dataset topilmadi.");
        }

        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 200);
        var rows = dataset.Rows
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToList();

        return Ok(new PreviewResponse
        {
            DatasetId = dataset.Id,
            TotalRows = dataset.Rows.Count,
            Page = safePage,
            PageSize = safePageSize,
            Columns = dataset.Columns,
            Rows = rows
        });
    }

    [HttpGet("{datasetId}/metrics")]
    public ActionResult<MetricsResponse> Metrics([FromRoute] string datasetId)
    {
        if (!_datasetStore.TryGetDataset(datasetId, out var dataset) || dataset is null)
        {
            return NotFound("Dataset topilmadi.");
        }

        return Ok(_analyticsService.BuildMetrics(dataset));
    }

    [HttpGet("{datasetId}/charts")]
    public ActionResult<ChartResponse> Charts([FromRoute] string datasetId, [FromQuery] string type = "region-sales")
    {
        if (!_datasetStore.TryGetDataset(datasetId, out var dataset) || dataset is null)
        {
            return NotFound("Dataset topilmadi.");
        }

        return Ok(_analyticsService.BuildChart(dataset, type));
    }
}
