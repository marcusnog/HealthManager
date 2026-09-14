using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Route("financial")]
public sealed class FinancialController(
    FinancialService financialService,
    ExpenseService expenseService) : ControllerBase
{
    [HttpGet("receivables")]
    [Authorize(Policy = "FinanceReceivablesView")]
    public async Task<ActionResult<PagedResult<ReceivableResponse>>> ListReceivables([FromQuery] FinancialQuery query, CancellationToken cancellationToken)
        => Ok(await financialService.ListReceivablesAsync(query, cancellationToken));

    [HttpGet("payments")]
    [Authorize(Policy = "FinanceReceivablesView")]
    public async Task<ActionResult<PagedResult<PaymentResponse>>> ListPayments([FromQuery] PaymentQuery query, CancellationToken cancellationToken)
        => Ok(await financialService.ListPaymentsAsync(query, cancellationToken));

    [HttpPost("payments")]
    [Authorize(Policy = "FinanceReceivablesManage")]
    public async Task<ActionResult<PaymentResponse>> CreatePayment([FromBody] CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var response = await financialService.CreatePaymentAsync(request, cancellationToken);
        return CreatedAtAction(nameof(CreatePayment), new { id = response.Id }, response);
    }

    [HttpPost("receivables/manual")]
    [Authorize(Policy = "FinanceReceivablesManage")]
    public async Task<ActionResult<ReceivableResponse>> CreateManualReceivable([FromBody] CreateManualReceivableRequest request, CancellationToken cancellationToken)
    {
        var response = await financialService.CreateManualReceivableAsync(request, cancellationToken);
        return CreatedAtAction(nameof(CreateManualReceivable), new { id = response.Id }, response);
    }

    [HttpGet("expenses")]
    [Authorize(Policy = "FinancePayablesView")]
    public async Task<ActionResult<PagedResult<ExpenseResponse>>> ListExpenses([FromQuery] ExpenseQuery query, CancellationToken cancellationToken)
        => Ok(await expenseService.ListAsync(query, cancellationToken));

    [HttpPost("expenses")]
    [Authorize(Policy = "FinancePayablesManage")]
    public async Task<ActionResult<ExpenseResponse>> CreateExpense([FromBody] ExpenseRequest request, CancellationToken cancellationToken)
    {
        var response = await expenseService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(CreateExpense), new { id = response.Id }, response);
    }

    [HttpPut("expenses/{id:guid}")]
    [Authorize(Policy = "FinancePayablesManage")]
    public async Task<ActionResult<ExpenseResponse>> UpdateExpense(Guid id, [FromBody] ExpenseRequest request, CancellationToken cancellationToken)
        => Ok(await expenseService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("expenses/{id:guid}")]
    [Authorize(Policy = "FinancePayablesManage")]
    public async Task<ActionResult> DeleteExpense(Guid id, CancellationToken cancellationToken)
    {
        await expenseService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("summary")]
    [Authorize(Policy = "FinanceSummaryView")]
    public async Task<ActionResult<FinancialSummaryResponse>> GetSummary([FromQuery] string? destinationBank, CancellationToken cancellationToken)
        => Ok(await expenseService.GetSummaryAsync(destinationBank, cancellationToken));

    [HttpGet("professional-settlements")]
    [Authorize(Policy = "FinanceSettlements")]
    public async Task<ActionResult<IReadOnlyList<ProfessionalSettlementResponse>>> ListProfessionalSettlements(CancellationToken cancellationToken)
        => Ok(await financialService.ListProfessionalSettlementsAsync(cancellationToken));

    [HttpPost("professional-settlements")]
    [Authorize(Policy = "FinanceSettlements")]
    public async Task<ActionResult<SettlementResponse>> SettleProfessional([FromBody] ProfessionalSettlementRequest request, CancellationToken cancellationToken)
        => Ok(await financialService.SettleProfessionalAsync(request, cancellationToken));

    [HttpPost("owner-settlements")]
    [Authorize(Policy = "FinanceSettlements")]
    public async Task<ActionResult<SettlementResponse>> SettleOwner([FromBody] OwnerSettlementRequest request, CancellationToken cancellationToken)
        => Ok(await financialService.SettleOwnerAsync(request, cancellationToken));
}

