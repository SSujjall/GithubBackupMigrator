using GithubBackupMigrator.Server.Hubs;
using GithubBackupMigrator.Server.Models;
using GithubBackupMigrator.Server.Services;
using GithubBackupMigrator.Server.Services.Helpers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

#region Register Services
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddSingleton<LogHelper>();
builder.Services.AddScoped<SignalRHelper>();
builder.Services.AddScoped<GithubCommandHelper>();
#endregion

#region CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});
#endregion

builder.Services.AddSignalR();

var app = builder.Build();

// Use CORS policy
app.UseCors("AllowAll");

app.UseDefaultFiles();
app.UseStaticFiles();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.UseRouting();

app.MapControllers();

app.MapHub<SignalRHub>("/backupProgress");

app.MapFallbackToFile("/index.html");

app.Run();
