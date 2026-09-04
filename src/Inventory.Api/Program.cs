using Inventory.Api.Data;
using Inventory.Api.Middleware;
using Inventory.Api.Queue;
using Inventory.Api.Repositories;
using Inventory.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IInventoryTransactionRepository, InventoryTransactionRepository>();
builder.Services.AddScoped<IProductService, ProductService>();

// Queue-based serialization: InventoryService is the real processor (resolved by worker);
// QueuedInventoryService is the IInventoryService the controller sees.
builder.Services.AddScoped<InventoryService>();
builder.Services.AddSingleton<InventoryChannel>();
builder.Services.AddHostedService<InventoryQueueWorker>();
builder.Services.AddScoped<IInventoryService, QueuedInventoryService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
