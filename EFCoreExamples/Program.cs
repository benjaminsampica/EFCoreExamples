using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register dbcontext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")) // see appsettings.json
);

// register generic repository pattern
builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Using DbContext's built in repository pattern. DbSet<> implements the repository pattern already. I haven't found it practical to use repository pattern as the amount of providers EF Core supports allows for
// changes in data store's about as easy as it could be (it's always work) -> SQL Server to Cosmos to MongoDb, etc.
app.MapGet("/ordersdbContext", async (ApplicationDbContext dbContext, CancellationToken cancellationToken) =>
{
    var response = await dbContext.Orders
        .Select(order => new OrderResponse(order.Id, order.Number, order.Total, order.BillingAddress, order.ShippingAddress))
        .ToListAsync(cancellationToken);

    return response;
})
.WithName("GetOrdersDbContext");

app.MapGet("/ordersRepositoryPattern", async (IRepository<Order> orderRepository, CancellationToken cancellationToken) =>
{
    var orders = await orderRepository
        .GetAllAsync(cancellationToken);

    var response = orders.Select(order => new OrderResponse(order.Id, order.Number, order.Total, order.BillingAddress, order.ShippingAddress));

    return response;
})
.WithName("GetOrdersRepositoryPattern");

// seed some random data
using (var scope = app.Services.CreateScope())
{
    using var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    await dbContext.Database.EnsureCreatedAsync();

    dbContext.Orders.Add(new Order("123", 123, "123 Test Street", "456 Test Street"));

    await dbContext.SaveChangesAsync();
}

app.Run();

/// <summary>
///     So that we don't leak out IQueryable anywhere if we aren't using repository pattern explicity. Any DbContext repository (Orders) should be doing a '.Select' to materialize the data before it leaves a method.
/// </summary>
public record OrderResponse(int Id, string Number, decimal Total, string BillingAddress, string ShippingAddress);

public record Order(string Number, decimal Total, string BillingAddress, string ShippingAddress)
{
    public int Id { get; set; }
};

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions options) : base(options)
    {

    }

    public DbSet<Order> Orders { get; set; } = null!;
}

public interface IRepository<T> where T : class
{
    Task<T?> FindAsync(int id, CancellationToken cancellationToken);
    Task<List<T>> GetAllAsync(CancellationToken cancellationToken); // Pairs well with the specification pattern for GetAll, etc. There is some pain around how the query is going to materialize or _if_ it will (exceptions!) if you have multiple specifications at the same time. I can send you examples if you want.
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    void Remove(T entity);
    Task AddAsync(T entity, CancellationToken cancellationToken);
    Task AddRangeAsync(IEnumerable<T> entity, CancellationToken cancellationToken);
}

public class Repository<T> : IRepository<T> where T : class
{
    private readonly ApplicationDbContext _dbContext;
    public Repository(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // You can kind of see this is really odd - just wrapping the repository pattern with more repository pattern.
    public async Task AddAsync(T entity, CancellationToken cancellationToken) => await _dbContext.AddAsync(entity, cancellationToken);

    public async Task AddRangeAsync(IEnumerable<T> entities, CancellationToken cancellationToken) => await _dbContext.AddRangeAsync(entities, cancellationToken);

    public async Task<T?> FindAsync(int id, CancellationToken cancellationToken) => await _dbContext.Set<T>().FindAsync(new object[] { id }, cancellationToken);

    public async Task<List<T>> GetAllAsync(CancellationToken cancellationToken) => await _dbContext.Set<T>().ToListAsync(cancellationToken);

    public void Remove(T entity) => _dbContext.Remove(entity);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken) => await _dbContext.SaveChangesAsync(cancellationToken);
}