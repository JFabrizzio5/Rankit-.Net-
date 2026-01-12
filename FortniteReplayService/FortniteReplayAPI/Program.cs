using FortniteReplayAPI.Services;
using FortniteReplayAPI.Models;
using Microsoft.OpenApi.Models;
using System.Reflection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// --- AGREGAR SERVICIOS ---

// 0. CONFIGURAR LÍMITES DE TAMAÑO DE ARCHIVOS
// Importante: Aumentar el límite predeterminado (30MB) para permitir subir múltiples replays.
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 524_288_000; // 500 MB
});

builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = 524_288_000; // 500 MB
    options.MemoryBufferThreshold = int.MaxValue;
});

// 1. Configurar CORS: Permitir cualquier origen, método y encabezado.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 2. Configuración avanzada de Swagger
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

// 3. Registrar el servicio de análisis de Replays
builder.Services.AddScoped<ReplayService>();

// 4. Configuración de logging (importante para ver errores de la librería)
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

// Activar el middleware de CORS (Debe ir antes de Authorization)
app.UseCors("AllowAll");

// Opcional: Si tienes problemas con HTTPS en local, puedes comentar esta línea temporalmente
app.UseHttpsRedirection();

app.UseAuthorization();
app.MapControllers();

app.Run();