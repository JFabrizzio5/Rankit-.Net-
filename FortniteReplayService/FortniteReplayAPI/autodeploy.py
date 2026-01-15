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

# IMPORTANTE: Este nombre debe ser EXACTAMENTE el que sale en 'docker ps' (columna NAMES)
NOMBRE_CONTENEDOR = "fortnite_replay_prod"

# Nombre específico de tu archivo docker-compose (con el punto, no guion)
ARCHIVO_DOCKER = "docker-compose.prod.yml"

# Detecta automáticamente la ruta donde está guardado este archivo script
DIR_PROYECTO = os.path.dirname(os.path.abspath(__file__))

def log(mensaje):
    """Imprime mensajes con la fecha y hora actual para el log."""
    ahora = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    # flush=True fuerza a que el texto aparezca INMEDIATAMENTE en la consola/log
    print(f"[{ahora}] {mensaje}", flush=True)

def ejecutar_comando(comando, mostrar_salida=False):
    """
    Ejecuta un comando de terminal.
    Si mostrar_salida=True, imprime el progreso en pantalla (útil para Docker build).
    Si mostrar_salida=False, captura el texto para usarlo en variables (útil para Git).
    """
    try:
        if mostrar_salida:
            # Aseguramos que los prints anteriores se muestren antes de ejecutar el comando
            sys.stdout.flush()
            # Ejecuta y muestra todo directamente en la consola/log en tiempo real
            subprocess.run(comando, shell=True, check=True)
            return "OK"
        else:
            # Ejecuta silenciosamente y guarda el resultado
            resultado = subprocess.run(
                comando, 
                shell=True, 
                check=True, 
                capture_output=True, 
                text=True
            )
            return resultado.stdout.strip()
            
    except subprocess.CalledProcessError as e:
        if not mostrar_salida:
            # Solo si estaba oculto, mostramos el error ahora
            pass 
        else:
            log(f"❌ Falló el comando visible: {comando}")
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
        log(f"🚑 Iniciando protocolo de recuperación usando {ARCHIVO_DOCKER}...")
        # Usamos -f para especificar el archivo correcto
        ejecutar_comando(f"docker compose -f {ARCHIVO_DOCKER} up -d", mostrar_salida=True)
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
             ejecutar_comando(f"docker compose -f {ARCHIVO_DOCKER} up -d", mostrar_salida=True)
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
    ejecutar_comando(f"git pull origin {RAMA}", mostrar_salida=True)

    # B) Reconstruir Docker
    log(f"🐳 Reconstruyendo y reiniciando contenedor usando {ARCHIVO_DOCKER}...")
    
    # AQUÍ ESTÁ EL CAMBIO: mostrar_salida=True para ver el progreso en vivo y -f para el archivo
    # Usamos 'docker compose' (v2) o 'docker-compose' (v1) según lo que soporte el servidor
    # Si te falla, cambia 'docker compose' por 'docker-compose'
    resultado_build = ejecutar_comando(f"docker compose -f {ARCHIVO_DOCKER} up -d --build", mostrar_salida=True)
    
    if resultado_build:
        log("🚀 Despliegue de Docker finalizado.")
        
        # C) Limpieza de imágenes viejas
        ejecutar_comando("docker image prune -f", mostrar_salida=True)
        
        # D) Verificación final
        verificar_estado_contenedor()
    else:
        log("🔥 ERROR CRÍTICO: Falló el docker compose up. Revisa el código.")

if __name__ == "__main__":
    try:
        main()
    except Exception as e:
        log(f"❌ Error inesperado en el script: {e}")