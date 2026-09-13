using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("products")]
public sealed class ProductsController(ProductService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductResponse>>> List([FromQuery] ProductQuery query, CancellationToken ct) => Ok(await service.ListAsync(query, ct));

    [HttpPost, Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<ActionResult<ProductResponse>> Create(ProductRequest request, CancellationToken ct)
    {
        var response = await service.CreateAsync(request, ct);
        return Created($"/products/{response.Id}", response);
    }

    [HttpPut("{id:guid}"), Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<ActionResult<ProductResponse>> Update(Guid id, ProductRequest request, CancellationToken ct) => Ok(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}"), Authorize(Policy = "ClinicAdminOrSecretary")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await service.DeleteAsync(id, ct); return NoContent(); }
}
