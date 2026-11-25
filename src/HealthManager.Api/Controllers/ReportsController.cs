using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicAdmin")]
[Route("reports")]
public sealed class ReportsController(ReportsService reports, ReportSourcesService sources) : ControllerBase
{
    [HttpGet]
    public Task<ReportDirectory> Directory(CancellationToken ct) => reports.DirectoryAsync(ct);

    [HttpPost("evidence/search")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(2048)]
    public Task<EvidencePage> SearchEvidence(EvidenceRequest input, [FromServices] EvidenceSearchService search, CancellationToken ct) =>
        search.SearchAsync(input, ct);

    [HttpGet("{id}")]
    public Task<ReportResult> Run(string id, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        reports.RunAsync(id, from, to, ct);

    [HttpGet("sources/{source}")]
    public Task<SourcePage> Read(string source, [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int page = 1, CancellationToken ct = default) =>
        sources.ReadAsync(source, from, to, page, ct);

    [HttpPost("sources/{source}/import")]
    [RequestSizeLimit(4_000_000)]
    public Task<int> Import(string source, ReportCsv body, CancellationToken ct) => sources.ImportAsync(source, body, ct);
}
