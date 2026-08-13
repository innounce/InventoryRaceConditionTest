namespace Inventory.Api.Models;

public class InventoryTransaction
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public ChangeType ChangeType { get; set; }
    public int Quantity { get; set; }
    public int BalanceAfter { get; set; }
    public DateTime CreatedAt { get; set; }
}
