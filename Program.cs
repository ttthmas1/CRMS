using Microsoft.EntityFrameworkCore;
using CRMS.API.Data;
using CRMS.API.Models;
using System.Security.Cryptography;
using System.Text;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "CRMS API", Version = "v1" });
    c.AddSecurityDefinition("basic", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "basic",
        In = ParameterLocation.Header,
        Description = "Enter username and password"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "basic"
                }
            },
            new string[] {}
        }
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

app.UseCors();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// ── HELPER FUNCTIONS ──────────────────────────────────────────────

string HashPassword(string password)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
    return Convert.ToHexString(bytes).ToLower();
}

async Task<User?> Authenticate(HttpContext ctx, AppDbContext db)
{
    if (!ctx.Request.Headers.TryGetValue("Authorization", out var authHeader))
        return null;
    var header = authHeader.ToString();
    if (!header.StartsWith("Basic ")) return null;
    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
    var parts = decoded.Split(':', 2);
    if (parts.Length != 2) return null;
    var hash = HashPassword(parts[1]);
    return await db.Users.FirstOrDefaultAsync(u => u.Username == parts[0] && u.PasswordHash == hash);
}

// ── SEED DATA ─────────────────────────────────────────────────────

try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();

        if (!db.Users.Any())
        {
            db.Users.AddRange(
                new User { Username = "admin", PasswordHash = HashPassword("admin123"), Role = "Admin", FullName = "Admin User", Email = "admin@crms.com", Phone = "0000000000" },
                new User { Username = "staff1", PasswordHash = HashPassword("staff123"), Role = "Staff", FullName = "Staff User", Email = "staff@crms.com", Phone = "1111111111" },
                new User { Username = "customer1", PasswordHash = HashPassword("cust123"), Role = "Customer", FullName = "Test Customer", Email = "customer@crms.com", Phone = "2222222222" }
            );
            db.Cars.AddRange(
                new Car { Make = "Toyota", Model = "Camry", Year = 2023, Category = "Sedan", DailyRate = 55.00m, LicencePlate = "ABC-001", Colour = "White", Status = "Available" },
                new Car { Make = "Ford", Model = "Explorer", Year = 2022, Category = "SUV", DailyRate = 85.00m, LicencePlate = "XYZ-002", Colour = "Black", Status = "Available" },
                new Car { Make = "Honda", Model = "Odyssey", Year = 2021, Category = "Van", DailyRate = 95.00m, LicencePlate = "VAN-003", Colour = "Silver", Status = "Available" }
            );
            await db.SaveChangesAsync();
        }
    }
}
catch
{
    // Database not available locally - will connect when deployed
}

// ── AUTH ──────────────────────────────────────────────────────────

app.MapPost("/auth/register", async (AppDbContext db, RegisterDto dto) =>
{
    if (await db.Users.AnyAsync(u => u.Username == dto.Username))
        return Results.Conflict("Username already taken.");
    var user = new User
    {
        Username = dto.Username,
        PasswordHash = HashPassword(dto.Password),
        Role = "Customer",
        FullName = dto.FullName,
        Email = dto.Email,
        Phone = dto.Phone
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Account created successfully." });
});

app.MapGet("/users", async (HttpContext ctx, AppDbContext db) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role != "Admin") return Results.Forbid();
    var users = await db.Users
        .Select(u => new UserDto(u.Id, u.Username, u.Role, u.FullName, u.Email, u.Phone, u.CreatedAt))
        .ToListAsync();
    return Results.Ok(users);
});

// ── CARS ──────────────────────────────────────────────────────────

app.MapGet("/cars", async (HttpContext ctx, AppDbContext db) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    var cars = await db.Cars
        .Select(c => new CarDto(c.Id, c.Make, c.Model, c.Year, c.Category, c.DailyRate, c.LicencePlate, c.Colour, c.Status))
        .ToListAsync();
    return Results.Ok(cars);
});

app.MapGet("/cars/{id}", async (int id, HttpContext ctx, AppDbContext db) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    var c = await db.Cars.FindAsync(id);
    if (c == null) return Results.NotFound();
    return Results.Ok(new CarDto(c.Id, c.Make, c.Model, c.Year, c.Category, c.DailyRate, c.LicencePlate, c.Colour, c.Status));
});

app.MapPost("/cars", async (HttpContext ctx, AppDbContext db, CarCreateDto dto) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role != "Admin") return Results.Forbid();
    var car = new Car { Make = dto.Make, Model = dto.Model, Year = dto.Year, Category = dto.Category, DailyRate = dto.DailyRate, LicencePlate = dto.LicencePlate, Colour = dto.Colour, Status = "Available" };
    db.Cars.Add(car);
    await db.SaveChangesAsync();
    return Results.Created($"/cars/{car.Id}", new CarDto(car.Id, car.Make, car.Model, car.Year, car.Category, car.DailyRate, car.LicencePlate, car.Colour, car.Status));
});

app.MapPut("/cars/{id}", async (int id, HttpContext ctx, AppDbContext db, CarCreateDto dto) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role != "Admin") return Results.Forbid();
    var car = await db.Cars.FindAsync(id);
    if (car == null) return Results.NotFound();
    car.Make = dto.Make; car.Model = dto.Model; car.Year = dto.Year;
    car.Category = dto.Category; car.DailyRate = dto.DailyRate;
    car.LicencePlate = dto.LicencePlate; car.Colour = dto.Colour;
    await db.SaveChangesAsync();
    return Results.Ok(new CarDto(car.Id, car.Make, car.Model, car.Year, car.Category, car.DailyRate, car.LicencePlate, car.Colour, car.Status));
});

app.MapDelete("/cars/{id}", async (int id, HttpContext ctx, AppDbContext db) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role != "Admin") return Results.Forbid();
    var car = await db.Cars.FindAsync(id);
    if (car == null) return Results.NotFound();
    db.Cars.Remove(car);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// ── BOOKINGS ──────────────────────────────────────────────────────

app.MapPost("/bookings", async (HttpContext ctx, AppDbContext db, BookingCreateDto dto) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role != "Customer") return Results.Forbid();
    var car = await db.Cars.FindAsync(dto.CarId);
    if (car == null) return Results.NotFound("Car not found.");
    var conflict = await db.Bookings.AnyAsync(b =>
        b.CarId == dto.CarId &&
        (b.Status == "Active" || b.Status == "Approved") &&
        b.PickupDate < dto.ReturnDate && b.ReturnDate > dto.PickupDate);
    if (conflict) return Results.Conflict("Car is not available for selected dates.");
    int days = (dto.ReturnDate - dto.PickupDate).Days;
    if (days < 1) return Results.BadRequest("Return date must be after pickup date.");
    var booking = new Booking
    {
        CustomerId = user.Id,
        CarId = dto.CarId,
        PickupDate = dto.PickupDate,
        ReturnDate = dto.ReturnDate,
        TotalAmount = car.DailyRate * days,
        Status = "Pending"
    };
    db.Bookings.Add(booking);
    await db.SaveChangesAsync();
    return Results.Created($"/bookings/{booking.Id}",
        new BookingDto(booking.Id, user.FullName, $"{car.Make} {car.Model}", booking.PickupDate, booking.ReturnDate, booking.TotalAmount, booking.Status, booking.CreatedAt));
});

app.MapGet("/bookings/my", async (HttpContext ctx, AppDbContext db) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role != "Customer") return Results.Forbid();
    var bookings = await db.Bookings
        .Include(b => b.Car)
        .Include(b => b.Customer)
        .Where(b => b.CustomerId == user.Id)
        .Select(b => new BookingDto(b.Id, b.Customer.FullName, b.Car.Make + " " + b.Car.Model, b.PickupDate, b.ReturnDate, b.TotalAmount, b.Status, b.CreatedAt))
        .ToListAsync();
    return Results.Ok(bookings);
});

app.MapDelete("/bookings/{id}", async (int id, HttpContext ctx, AppDbContext db) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role != "Customer") return Results.Forbid();
    var booking = await db.Bookings.FindAsync(id);
    if (booking == null) return Results.NotFound();
    if (booking.CustomerId != user.Id) return Results.Forbid();
    if (booking.Status != "Pending") return Results.BadRequest("Only Pending bookings can be cancelled.");
    booking.Status = "Cancelled";
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Booking cancelled." });
});

app.MapGet("/bookings", async (HttpContext ctx, AppDbContext db) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role == "Customer") return Results.Forbid();
    var bookings = await db.Bookings
        .Include(b => b.Car)
        .Include(b => b.Customer)
        .Select(b => new BookingDto(b.Id, b.Customer.FullName, b.Car.Make + " " + b.Car.Model, b.PickupDate, b.ReturnDate, b.TotalAmount, b.Status, b.CreatedAt))
        .ToListAsync();
    return Results.Ok(bookings);
});

app.MapPut("/bookings/{id}/approve", async (int id, HttpContext ctx, AppDbContext db) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role == "Customer") return Results.Forbid();
    var booking = await db.Bookings.FindAsync(id);
    if (booking == null) return Results.NotFound();
    if (booking.Status != "Pending") return Results.BadRequest("Only Pending bookings can be approved.");
    booking.Status = "Approved";
    booking.ApprovedById = user.Id;
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Booking approved." });
});

app.MapPut("/bookings/{id}/reject", async (int id, HttpContext ctx, AppDbContext db) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role == "Customer") return Results.Forbid();
    var booking = await db.Bookings.FindAsync(id);
    if (booking == null) return Results.NotFound();
    if (booking.Status != "Pending") return Results.BadRequest("Only Pending bookings can be rejected.");
    booking.Status = "Rejected";
    booking.ApprovedById = user.Id;
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Booking rejected." });
});

app.MapPut("/bookings/{id}/complete", async (int id, HttpContext ctx, AppDbContext db) =>
{
    var user = await Authenticate(ctx, db);
    if (user == null) return Results.Unauthorized();
    if (user.Role == "Customer") return Results.Forbid();
    var booking = await db.Bookings.FindAsync(id);
    if (booking == null) return Results.NotFound();
    if (booking.Status != "Active") return Results.BadRequest("Only Active bookings can be completed.");
    booking.Status = "Completed";
    await db.SaveChangesAsync();
    return Results.Ok(new { message = "Booking completed." });
});

app.Run();

// ── DTOs ──────────────────────────────────────────────────────────

record RegisterDto(string Username, string Password, string FullName, string Email, string Phone);
record UserDto(int Id, string Username, string Role, string FullName, string Email, string Phone, DateTime CreatedAt);
record CarDto(int Id, string Make, string Model, int Year, string Category, decimal DailyRate, string LicencePlate, string Colour, string Status);
record CarCreateDto(string Make, string Model, int Year, string Category, decimal DailyRate, string LicencePlate, string Colour);
record BookingCreateDto(int CarId, DateTime PickupDate, DateTime ReturnDate);
record BookingDto(int Id, string CustomerName, string Car, DateTime PickupDate, DateTime ReturnDate, decimal TotalAmount, string Status, DateTime CreatedAt);

