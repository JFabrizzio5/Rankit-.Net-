##Directorios de compilación y cache

bin/
obj/
.vs/
.vscode/
.idea/

##Archivos de configuración de usuario o entorno local

\*.user
appsettings.Development.json

(Opcional) Si tienes secretos en appsettings.json local, descomenta la siguiente línea:

appsettings.json

Archivos grandes de prueba que no necesitamos en la imagen final

TESTREPLAYS/
_.replay
_.pdb

Logs y archivos temporales

\*.log
