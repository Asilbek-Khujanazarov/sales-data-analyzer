using Microsoft.AspNetCore.Mvc;
using SalesDataAnalyzer.Api.Models;
using SalesDataAnalyzer.Api.Services;

namespace SalesDataAnalyzer.Api.Controllers;

[ApiController]
[Route("api/files")]
public sealed class FilesController : ControllerBase
{
    private readonly IFileParsingService _fileParsingService;
    private readonly IDatasetStore _datasetStore;
    private readonly ISalesDomainValidationService _salesDomainValidationService;

    public FilesController(
        IFileParsingService fileParsingService,
        IDatasetStore datasetStore,
        ISalesDomainValidationService salesDomainValidationService)
    {
        _fileParsingService = fileParsingService;
        _datasetStore = datasetStore;
        _salesDomainValidationService = salesDomainValidationService;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(30_000_000)]
    public async Task<ActionResult<UploadResponse>> Upload([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("Fayl yuborilmadi yoki bo'sh.");
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".csv" or ".xlsx"))
        {
            return BadRequest("Faqat CSV yoki XLSX fayl yuboring.");
        }

        DatasetModel dataset;
        try
        {
            dataset = await _fileParsingService.ParseAsync(file, cancellationToken);
        }
        catch (Exception exception)
        {
            return BadRequest($"Faylni o'qib bo'lmadi: {exception.Message}");
        }

        var domainValidation = _salesDomainValidationService.ValidateDataset(dataset);
        if (!domainValidation.IsValid)
        {
            return BadRequest(domainValidation.Message);
        }

        var datasetId = _datasetStore.SaveDataset(dataset);
        return Ok(new UploadResponse(
            datasetId,
            dataset.FileName,
            dataset.Rows.Count,
            dataset.Columns));
    }
}
