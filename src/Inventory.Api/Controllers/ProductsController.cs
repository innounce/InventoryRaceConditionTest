using Inventory.Api.Dtos;
using Inventory.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Api.Controllers;

[ApiController]
[Route("products")]
public class ProductsController(IProductService productService, IInventoryService inventoryService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ProductDto>>> GetAll() =>
        Ok(await productService.GetAllAsync());

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> GetById(Guid id) =>
        Ok(await productService.GetByIdAsync(id));

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(CreateProductRequest request)
    {
        var product = await productService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Update(Guid id, UpdateProductRequest request) =>
        Ok(await productService.UpdateAsync(id, request));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await productService.DeleteAsync(id);
        return NoContent();
    }

    [HttpPost("{id:guid}/stock-in")]
    public async Task<ActionResult<StockChangeResponse>> StockIn(Guid id, StockChangeRequest request) =>
        Ok(await inventoryService.StockInAsync(id, request));

    [HttpPost("{id:guid}/stock-out")]
    public async Task<ActionResult<StockChangeResponse>> StockOut(Guid id, StockChangeRequest request) =>
        Ok(await inventoryService.StockOutAsync(id, request));

    [HttpGet("{id:guid}/transactions")]
    public async Task<ActionResult<List<TransactionDto>>> GetTransactions(Guid id) =>
        Ok(await inventoryService.GetTransactionsAsync(id));
}
