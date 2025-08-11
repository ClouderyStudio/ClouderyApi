using ClouderyApi.Data;
using ClouderyApi.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// 注册数据库上下文
builder.Services.AddDbContext<ClouderyApiContext>(options =>
{
    options.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")!);
});

// 注册Swagger生成器，并配置Swagger文档信息
builder.Services.AddSwaggerGen(u => {
    u.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Version = "Ver:1.0.0",//版本
        Title = "ClouderyApi",//标题
        Description = "ClouderyApi",//描述
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "JustQiyi",
            Email = "justqiyi@qq.com"
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

//启用Swagger中间件
app.UseSwagger();
//配置SwaggerUI
app.UseSwaggerUI(u =>
{
    u.SwaggerEndpoint("/swagger/v1/swagger.json", "WebAPI_v1");
});

app.Run();
