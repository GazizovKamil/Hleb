using DotNetEnv;
using Hleb.Database;
using Hleb.Helpers;
using Hleb.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

Env.Load();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.JsonSerializerOptions.WriteIndented = true;
    })
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new DateOnlyJsonConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//builder.Services.AddSignalR()
//    .AddJsonProtocol(options =>
//    {
//        options.PayloadSerializerOptions.PropertyNamingPolicy = null;
//        options.PayloadSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
//        options.PayloadSerializerOptions.WriteIndented = true;
//    });

builder.Services.AddDbContext<AppDbContext>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var port = Env.GetString("PORT") ?? "3003";

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, int.Parse(port), listenOptions =>
    {
        listenOptions.UseConnectionLogging();  // Это для логирования подключения
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();

    endpoints.MapHub<WorkHub>("/workhub").RequireCors("AllowAll");
});

app.UseStaticFiles();

app.Run();
