var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllForDev", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "http://85.91.16.21:5173",   // ← your actual IP
                "https://derivrisk-frontend.vercel.app"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();                    // if you ever need cookies/auth
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAllForDev");   // Must come BEFORE UseAuthorization / MapControllers

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Bind to Render's PORT (fallback to 5053 for local dev)
var port = Environment.GetEnvironmentVariable("PORT") ?? "5053";
app.Run($"http://0.0.0.0:{port}");
//app.Run();

