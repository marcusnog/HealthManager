using System.Net;
using System.Text.Json;
using HealthManager.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HealthManager.Api.Controllers;

[ApiController]
[Authorize(Policy = "ClinicAdminOrSecretary")]
[Route("address")]
public sealed class AddressController(IHttpClientFactory httpClientFactory, IConfiguration configuration) : ControllerBase
{
    [HttpGet("cep/{cep}")]
    public async Task<ActionResult<CepAddressResponse>> FindByCep(string cep, CancellationToken cancellationToken)
    {
        var digits = new string(cep.Where(char.IsDigit).ToArray());
        if (digits.Length != 8)
            return BadRequest("CEP deve conter 8 digitos.");

        var baseUrl = (configuration["VIACEP_BASE_URL"] ?? "https://viacep.com.br/ws").TrimEnd('/');
        using var response = await httpClientFactory.CreateClient().GetAsync($"{baseUrl}/{digits}/json/", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return NotFound("CEP nao encontrado.");
        if (!response.IsSuccessStatusCode)
            return Problem(statusCode: StatusCodes.Status502BadGateway, detail: "O servico de CEP nao respondeu a consulta.");

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("erro", out var error) && error.ValueKind == JsonValueKind.True)
            return NotFound("CEP nao encontrado.");

        return Ok(new CepAddressResponse(
            digits,
            Read(root, "logradouro"),
            Read(root, "complemento"),
            Read(root, "bairro"),
            Read(root, "localidade"),
            Read(root, "uf")));
    }

    private static string? Read(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }
}
