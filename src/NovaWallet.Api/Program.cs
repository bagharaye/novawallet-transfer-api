using NovaWallet.Api.Middleware;
using NovaWallet.Api.Models;
using NovaWallet.Api.Options;
using NovaWallet.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<WalletStore>();
builder.Services.AddSingleton<TransferService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<BearerAuthMiddleware>();

app.MapPost("/wallets", (TransferService transfers) =>
{
    var wallet = transfers.CreateWallet();
    var response = new WalletResponse(wallet.Id, wallet.BalanceKobo, wallet.CreatedAt);
    return Results.Created($"/wallets/{wallet.Id}", response);
});

app.MapGet("/wallets/{id}", (string id, TransferService transfers) =>
{
    var wallet = transfers.GetWallet(id);
    return Results.Ok(new WalletResponse(wallet.Id, wallet.BalanceKobo, wallet.CreatedAt));
});

app.MapPost("/wallets/{id}/credit", async (string id, HttpRequest httpRequest, TransferService transfers) =>
{
    var request = await RequestBodyReader.ReadAsync<CreditRequest>(httpRequest);

    var wallet = await transfers.CreditAsync(id, request.AmountKobo);
    return Results.Ok(new WalletResponse(wallet.Id, wallet.BalanceKobo, wallet.CreatedAt));
});

app.MapPost("/transfers", async (HttpRequest httpRequest, TransferService transfers) =>
{
    var request = await RequestBodyReader.ReadAsync<TransferRequest>(httpRequest);

    string? idempotencyKey = httpRequest.Headers.TryGetValue("Idempotency-Key", out var values)
        ? values.ToString()
        : null;
    idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey;

    var (response, _) = await transfers.TransferAsync(request, idempotencyKey);
    return Results.Created($"/transfers/{response.Id}", response);
});

app.Run();

// Exposed so the test project's WebApplicationFactory<Program> can boot this app in-process.
public partial class Program;
