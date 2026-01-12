# Etapa 1: Construcción (Build)
# Usamos la imagen del SDK de .NET 8.0 para compilar el proyecto
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 1. Copiar primero el archivo de proyecto y restaurar dependencias
# Esto optimiza el caché de Docker; si no cambias el .csproj, no se vuelven a descargar los NuGets.
COPY ["FortniteReplayAPI.csproj", "./"]
RUN dotnet restore "FortniteReplayAPI.csproj"

# 2. Copiar el resto del código fuente
COPY . .

# 3. Compilar y publicar la aplicación en modo Release
RUN dotnet publish "FortniteReplayAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Etapa 2: Ejecución (Runtime)
# Usamos la imagen de ASP.NET Core Runtime (más ligera, solo para ejecutar)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Exponer el puerto 8080 (puerto por defecto en .NET 8)
EXPOSE 8080

# Copiar los archivos publicados desde la etapa de construcción
COPY --from=build /app/publish .

# Configurar el punto de entrada
ENTRYPOINT ["dotnet", "FortniteReplayAPI.dll"]