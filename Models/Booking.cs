namespace CRMS.API.Models;

public class Booking
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public User Customer { get; set; } = null!;
    public int CarId { get; set; }
    public Car Car { get; set; } = null!;
    public DateTime PickupDate { get; set; }
    public DateTime ReturnDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Pending"; // Pending | Approved | Active | Completed | Cancelled | Rejected
    public int? ApprovedById { get; set; }
    public User? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

