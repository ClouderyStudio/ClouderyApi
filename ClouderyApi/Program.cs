using ClouderyApi.Data;
using ClouderyApi.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddOpenApi();

builder.Services.AddDbContext<ClouderyApiContext>(options =>
{
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!);
});

builder.Services.AddCors(c =>
{
    c.AddPolicy("AllowAllOrigins", policy =>
    {
        policy.AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader();
    });
});

builder.Services.AddSwaggerGen(u => {
    u.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Version = "Ver:1.0.0",
        Title = "ClouderyApi",
        Description = "ClouderyApi",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "JustQiyi",
            Email = "justqiyi@qq.com"
        }
    });
});

var app = builder.Build();

app.UseCors("AllowAllOrigins");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.UseSwagger();

app.UseSwaggerUI(u =>
{
    u.SwaggerEndpoint("/swagger/v1/swagger.json", "WebAPI_v1");
});

app.Run();
