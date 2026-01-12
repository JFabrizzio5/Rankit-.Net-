using FortniteReplayAPI.Services;
using Microsoft.OpenApi.Models;
using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

// --- AGREGAR SERVICIOS ---

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configuración avanzada de Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "Fortnite Replay Parser API", 
        Version = "v1",
        Description = "API para analizar replays de Fortnite con reglas de puntuación dinámicas."
    });

    // Permitir anotaciones de datos (opcional pero útil)
    c.EnableAnnotations();

    // Incluir comentarios XML si existen (para ver descripciones en Swagger)
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Registrar el servicio de análisis de Replays
builder.Services.AddScoped<ReplayService>();

// Configuración de logging (importante para ver errores de la librería)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// --- CONFIGURAR PIPELINE ---

// Activar Swagger siempre para facilitar tus pruebas
app.UseSwagger();
app.UseSwaggerUI(c => 
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Fortnite Parser V1");
    c.RoutePrefix = string.Empty; // Abre Swagger directamente en la raíz (http://localhost:port/)
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();