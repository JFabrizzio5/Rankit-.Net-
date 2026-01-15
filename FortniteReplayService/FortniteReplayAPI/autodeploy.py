import subprocess
import os
import sys

# Configuración
RAMA = "main"          # O "master", según tu repo
NOMBRE_CONTENEDOR = "fortnite_replay_prod" # El nombre que pusimos en el docker-compose

def ejecutar_comando(comando):
    """Ejecuta un comando de terminal y devuelve el resultado."""
    try:
        resultado = subprocess.run(
            comando, 
            shell=True, 
            check=True, 
            capture_output=True, 
            text=True
        )
        return resultado.stdout.strip()
    except subprocess.CalledProcessError as e:
        print(f"❌ Error al ejecutar: {comando}")
        print(e.stderr)
        return None

def main():
    print("🔄 Verificando actualizaciones en GitHub...")
    
    # 1. Traer los cambios de la nube (git fetch) sin fusionar aún
    ejecutar_comando("git fetch origin")

    # 2. Ver si hay diferencias entre mi local y el remoto
    estado_local = ejecutar_comando(f"git rev-parse {RAMA}")
    estado_remoto = ejecutar_comando(f"git rev-parse origin/{RAMA}")

    if estado_local == estado_remoto:
        print("✅ El sistema está actualizado. No es necesario desplegar.")
        return

    print("⚡ Se detectaron cambios. Iniciando despliegue...")

    # 3. Descargar el código nuevo
    print("⬇️  Descargando código (git pull)...")
    ejecutar_comando(f"git pull origin {RAMA}")

    # 4. Reconstruir y levantar el contenedor (ESTA ES LA CLAVE)
    # --build: Fuerza a crear la imagen nueva con el código nuevo
    # -d: Lo deja corriendo en segundo plano
    print("🐳 Reconstruyendo contenedor Docker...")
    resultado_docker = ejecutar_comando("docker-compose up -d --build")
    
    if resultado_docker:
        print("🚀 ¡Despliegue completado con éxito!")
        
        # 5. (Opcional) Limpiar imágenes viejas para no llenar el disco
        ejecutar_comando("docker image prune -f")
    else:
        print("⚠️ Hubo un problema al levantar Docker.")

if __name__ == "__main__":
    main()