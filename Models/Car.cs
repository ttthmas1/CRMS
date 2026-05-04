namespace CRMS.API.Models;

public class Car
{
    public int Id { get; set; }
    public string Make { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal DailyRate { get; set; }
    public string LicencePlate { get; set; } = string.Empty;
    public string Colour { get; set; } = string.Empty;
    public string Status { get; set; } = "Available"; // Available | Rented | Maintenance

    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}


