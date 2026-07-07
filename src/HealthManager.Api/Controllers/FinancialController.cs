using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicStaff")]
[Route("financial")]
public sealed class FinancialController(
    FinancialService financialService,
    ExpenseService expenseService) : ControllerBase
{
    [HttpGet("receivables")]
    public async Task<ActionResult<PagedResult<ReceivableResponse>>> ListReceivables([FromQuery] FinancialQuery query, CancellationToken cancellationToken)
        => Ok(await financialService.ListReceivablesAsync(query, cancellationToken));

    [HttpGet("payments")]
    public async Task<ActionResult<PagedResult<PaymentResponse>>> ListPayments([FromQuery] PaymentQuery query, CancellationToken cancellationToken)
        => Ok(await financialService.ListPaymentsAsync(query, cancellationToken));

    [HttpPost("payments")]
    public async Task<ActionResult<PaymentResponse>> CreatePayment([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var response = await financialService.CreatePaymentAsync(request, cancellationToken);
        return CreatedAtAction(nameof(CreatePayment), new { id = response.Id }, response);
    }

    [HttpPost("receivables/manual")]
    public async Task<ActionResult<ReceivableResponse>> CreateManualReceivable([FromBody] CreateManualReceivableRequest request, CancellationToken cancellationToken)
    {
        var response = await financialService.CreateManualReceivableAsync(request, cancellationToken);
        return CreatedAtAction(nameof(CreateManualReceivable), new { id = response.Id }, response);
    }

    [HttpGet("expenses")]
    public async Task<ActionResult<PagedResult<ExpenseResponse>>> ListExpenses([FromQuery] ExpenseQuery query, CancellationToken cancellationToken)
        => Ok(await expenseService.ListAsync(query, cancellationToken));

    [HttpPost("expenses")]
    public async Task<ActionResult<ExpenseResponse>> CreateExpense([FromBody] ExpenseRequest request, CancellationToken cancellationToken)
    {
        var response = await expenseService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(CreateExpense), new { id = response.Id }, response);
    }

    [HttpPut("expenses/{id:guid}")]
    public async Task<ActionResult<ExpenseResponse>> UpdateExpense(Guid id, [FromBody] ExpenseRequest request, CancellationToken cancellationToken)
        => Ok(await expenseService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("expenses/{id:guid}")]
    public async Task<ActionResult> DeleteExpense(Guid id, CancellationToken cancellationToken)
    {
        await expenseService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<FinancialSummaryResponse>> GetSummary(CancellationToken cancellationToken)
        => Ok(await expenseService.GetSummaryAsync(cancellationToken));
}

