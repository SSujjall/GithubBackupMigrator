using GithubBackupMigrator.Server.Hubs;
using GithubBackupMigrator.Server.Models;
using GithubBackupMigrator.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

#region Register Services
builder.Services.AddScoped<IBackupService, BackupService>();
#endregion

builder.Services.AddSignalR();

var app = builder.Build();

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
