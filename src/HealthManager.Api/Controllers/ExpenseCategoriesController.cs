using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Route("expense-categories")]
public sealed class ExpenseCategoriesController(ExpenseCategoryService service) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "FinanceCategoriesView")]
    public async Task<ActionResult<PagedResult<ExpenseCategoryResponse>>> List([FromQuery] ExpenseCategoryQuery query, CancellationToken ct)
        => Ok(await service.ListAsync(query, ct));

    [HttpPost]
    [Authorize(Policy = "FinanceCategoriesManage")]
    public async Task<ActionResult<ExpenseCategoryResponse>> Create([FromBody] ExpenseCategoryRequest request, CancellationToken ct)
    {
        var response = await service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Create), new { id = response.Id }, response);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "FinanceCategoriesManage")]
    public async Task<ActionResult<ExpenseCategoryResponse>> Update(Guid id, [FromBody] ExpenseCategoryRequest request, CancellationToken ct)
        => Ok(await service.UpdateAsync(id, request, ct));

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "FinanceCategoriesManage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return NoContent();
    }
}
