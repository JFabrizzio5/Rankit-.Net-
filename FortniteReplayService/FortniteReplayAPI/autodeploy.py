import subprocess
import os
import sys
import datetime

# --- INSTRUCCIONES PARA CRONJOB (Cada 30 Segundos) ---
# Ejecuta 'crontab -e' y agrega estas dos líneas al final:
#
# * * * * * cd /root/CometaXMicroservices/Rankit-.Net-/FortniteReplayService/FortniteReplayAPI && /usr/bin/python3 autodeploy.py >> deploy.log 2>&1
# * * * * * sleep 30 && cd /root/CometaXMicroservices/Rankit-.Net-/FortniteReplayService/FortniteReplayAPI && /usr/bin/python3 autodeploy.py >> deploy.log 2>&1
# -----------------------------------------------------

# --- CONFIGURACIÓN ---

# Rama que quieres vigilar (usualmente 'main' o 'master')
RAMA = "main"

# IMPORTANTE: Este nombre debe ser EXACTAMENTE el que pusiste en 'container_name' dentro de tu docker-compose.yml
# Si usaste la configuración de producción anterior, probablemente sea "fortnite_replay_prod"
# Si usaste la de desarrollo, puede ser "fortnite_replay_container"
NOMBRE_CONTENEDOR = "fortnite_replay_container"

# Detecta automáticamente la ruta donde está guardado este archivo script
DIR_PROYECTO = os.path.dirname(os.path.abspath(__file__))

def log(mensaje):
    """Imprime mensajes con la fecha y hora actual para el log."""
    ahora = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(f"[{ahora}] {mensaje}")

def ejecutar_comando(comando):
    """Ejecuta un comando de terminal y devuelve el resultado limpio."""
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
        # No logueamos error aquí para evitar ruido si es un chequeo simple
        return None

def esta_corriendo():
    """Devuelve True si el contenedor está activo, False si está apagado o no existe."""
    cmd = f"docker inspect -f '{{{{.State.Running}}}}' {NOMBRE_CONTENEDOR}"
    resultado = ejecutar_comando(cmd)
    return resultado == "true"

def verificar_estado_contenedor():
    """Revisa si el contenedor está vivo y cuándo se creó."""
    cmd = f"docker inspect -f '{{{{.State.StartedAt}}}}' {NOMBRE_CONTENEDOR}"
    fecha_inicio = ejecutar_comando(cmd)
    
    if fecha_inicio:
        log(f"✅ ESTADO: El contenedor '{NOMBRE_CONTENEDOR}' está CORRIENDO.")
        log(f"🕒 INICIADO: {fecha_inicio}")
    else:
        log(f"⚠️ ALERTA: El contenedor '{NOMBRE_CONTENEDOR}' NO parece estar corriendo.")

def main():
    # 1. Asegurar que estamos en el directorio correcto
    if os.path.exists(DIR_PROYECTO):
        os.chdir(DIR_PROYECTO)
    else:
        log(f"❌ Error crítico: La ruta {DIR_PROYECTO} no existe.")
        sys.exit(1)

    # --- NUEVO: FASE DE AUTO-REPARACIÓN (WATCHDOG) ---
    # Antes de buscar actualizaciones, verificamos que el servicio esté vivo
    if not esta_corriendo():
        log(f"⚠️ ALERTA: El contenedor '{NOMBRE_CONTENEDOR}' está DETENIDO o no existe.")
        log("🚑 Iniciando protocolo de recuperación (Levantando servicio)...")
        ejecutar_comando("docker-compose up -d")
        # Si acabamos de levantarlo, quizás no necesitemos actualizar inmediatamente, 
        # pero dejamos que el flujo continúe por si acaso la versión local era vieja.

    # 2. Traer información de GitHub (sin descargar código aún)
    # log("🔄 Buscando actualizaciones...")
    ejecutar_comando("git fetch origin")

    # 3. Comparar versión local vs remota
    estado_local = ejecutar_comando(f"git rev-parse {RAMA}")
    estado_remoto = ejecutar_comando(f"git rev-parse origin/{RAMA}")

    if not estado_local or not estado_remoto:
        # Si falló git fetch, al menos nos aseguramos que el contenedor siga vivo con el código actual
        if not esta_corriendo():
             ejecutar_comando("docker-compose up -d")
        return

    # Si son iguales, no hacemos nada (termina el script para ahorrar CPU)
    if estado_local == estado_remoto:
        # Descomenta la siguiente línea solo si quieres ver logs cada 30 seg
        # log("✅ Sistema actualizado y corriendo.")
        return

    # 4. Si llegamos aquí, ¡HAY CAMBIOS EN EL CÓDIGO!
    log("⚡ DETECTADOS CAMBIOS EN GITHUB. INICIANDO DESPLIEGUE AUTOMÁTICO...")

    # A) Descargar código
    log(f"⬇️  Descargando últimos cambios de {RAMA}...")
    ejecutar_comando(f"git pull origin {RAMA}")

    # B) Reconstruir Docker
    log("🐳 Reconstruyendo y reiniciando contenedor...")
    resultado_build = ejecutar_comando("docker-compose up -d --build")
    
    if resultado_build:
        log("🚀 Despliegue de Docker finalizado.")
        
        # C) Limpieza de imágenes viejas
        ejecutar_comando("docker image prune -f")
        
        # D) Verificación final
        verificar_estado_contenedor()
    else:
        log("🔥 ERROR CRÍTICO: Falló el docker-compose up. Revisa el código.")

if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        log(f"❌ Error inesperado en el script: {e}")