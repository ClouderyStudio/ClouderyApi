using ClouderyApi.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Authentication.Cookies;
using Casdoor.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddOpenApi();

builder.Services.AddDbContext<ClouderyApiContext>(options =>
{
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!);
});

builder.Services.AddDbContext<QisoulDbContext>(options =>
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCasdoor(builder.Configuration.GetSection("Casdoor"))
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.HttpOnly = false;
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/identity/auth/login";
        options.LogoutPath = "/identity/auth/logout";
    });

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] {
        "http://localhost:5173",
        "http://localhost:5174",
        "https://localhost:5173",
        "http://localhost:5175",
        "https://localhost:5174"
    };

builder.Services.AddCors(c =>
{
    c.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowCredentials()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Services.AddSwaggerGen(u =>
{
    u.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "Ver:1.0.0",
        Title = "ClouderyApi",
        Description = "ClouderyApi",
        Contact = new OpenApiContact
        {
            Name = "JustQiyi",
            Email = "justqiyi@qq.com"
        }
    });
});

var app = builder.Build();

app.UseCors("AllowAllOrigins");

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI(u => { u.SwaggerEndpoint("/swagger/v1/swagger.json", "WebAPI_v1"); });
}

app.Run();