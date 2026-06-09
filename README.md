# File Sender

File Sender es una aplicación WinForms para Windows 7 en adelante. Usa un solo ejecutable con modo servidor o cliente y permite copiar archivos o carpetas entre dos equipos de una red local o, con configuración de puertos, por Internet.

Al iniciar, la pantalla muestra tres opciones:

- `Modo Local`: LAN, IP privada/manual y paneles local/remoto para copiar en ambos sentidos.
- `Modo Remoto`: transferencia por código usando `croc`, sin abrir puertos.
- `Remoto Directo`: conexión directa por IP pública, dominio, VPN o túnel, usando los mismos paneles local/remoto.

## Requisitos

- Windows 7 o superior.
- .NET Framework 4.7.2 Runtime en los equipos que ejecutan la app.
- Para compilar: Visual Studio o Build Tools con el .NET Framework 4.7.2 Developer Pack.

## Compilar

Desde una terminal con MSBuild disponible:

```powershell
MSBuild.exe .\FileSender.sln /p:Configuration=Release /m
```

El ejecutable portable queda en:

```text
FileSender\bin\Release\File Sender.exe
```

## Uso en LAN local

1. En ambas PCs, pulsar `Modo Local`. La app queda escuchando automáticamente.
2. En una de las PCs, pulsar `Conectar por IP`.
3. Si hace falta, abrir `Configuración` y ajustar puerto TCP o clave compartida.
4. Ingresar la IP privada de la otra PC y pulsar `Conectar`, o usar `Buscar PC`, seleccionar el equipo encontrado y pulsar `Conectar`.
5. Cuando la conexión esté activa, ambos equipos muestran panel `Local` y panel `Remoto`.
6. Usar `Unidades`, `Subir`, doble clic en carpetas o `Elegir carpeta` para navegar por discos y carpetas.
7. Enviar archivos o carpetas desde cualquiera de las dos PCs:
   - Seleccionarlos en el panel local y pulsar `Enviar ->`.
   - Arrastrarlos desde el panel local al panel remoto.
   - Arrastrarlos desde el panel remoto al panel local.
   - Arrastrarlos desde Windows Explorer al panel remoto.

Si `Buscar LAN` no encuentra equipos:

- Confirmar que la otra PC tenga File Sender abierto en `Modo Local`.
- Confirmar que ambas PCs estén en la misma red local.
- Permitir File Sender en Windows Firewall para redes privadas.
- Como alternativa, escribir la IP privada manualmente y pulsar `Conectar`.

## Uso remoto sin abrir puertos

Este modo usa `croc` y es el recomendado cuando no querés configurar router, IP pública ni reenvío de puertos.

1. En la PC que envía, pulsar `Modo Remoto`.
2. Elegir rol `Emisor`.
3. Elegir archivo o carpeta.
4. Generar o copiar el código mostrado.
5. Pulsar `Iniciar envío`.
6. En la PC que recibe, pulsar `Modo Remoto`.
7. Elegir rol `Receptor`.
8. Pegar el código del emisor.
9. Pulsar `Iniciar recepción`.

Notas:

- Ambos equipos deben estar abiertos mientras dura la transferencia.
- No requiere abrir puertos ni IP pública.
- Funciona detrás de NAT/CGNAT usando relay si hace falta.
- La transferencia es cifrada extremo a extremo por `croc`.

## Uso remoto directo por Internet

1. En el equipo que va a recibir la conexión, abrir `Remoto Directo`.
2. En `Configuración`, elegir el puerto TCP si no se usa el predeterminado `50505`.
3. En Windows Firewall, permitir conexiones entrantes para File Sender o para ese puerto TCP.
4. En el router del lugar donde está el equipo receptor, crear un reenvío de puerto:
   - Puerto externo TCP: el puerto elegido, por ejemplo `50505`.
   - IP interna destino: la IP LAN del equipo receptor, por ejemplo `192.168.1.50`.
   - Puerto interno TCP: el mismo puerto, por ejemplo `50505`.
5. En el otro equipo, abrir `Remoto Directo`.
6. Ingresar la IP pública o dominio/DDNS del receptor y pulsar `Conectar`.
7. Cuando la conexión esté activa, ambos equipos usan los mismos paneles `Local` y `Remoto` para arrastrar y soltar archivos o carpetas.

Notas para Internet:

- `Buscar PC` no funciona por Internet; usa broadcast local.
- Si el proveedor usa CGNAT, el reenvío de puertos del router no alcanza. En ese caso se necesita VPN, túnel o servidor intermedio.
- Cambiar la clave compartida `admin` antes de exponer el puerto a Internet.

## Configuración

El menú `Configuración` permite definir:

- `Puerto TCP local`: usado por el modo LAN y por conexiones manuales por IP.
- `Clave compartida LAN`: usada para autenticar conexiones LAN.
- `Carpeta para recibidos`: destino usado por herramientas auxiliares de recepción.
- `Carpeta local inicial`: carpeta que abre el panel local al iniciar.

La configuración se guarda en `FileSender.settings`, junto al ejecutable.

Notas:

- Ambos equipos deben estar abiertos mientras dura la transferencia.
- En modo remoto directo, el equipo receptor necesita IP pública, dominio/DDNS, VPN o reenvío de puerto equivalente.
- Si el proveedor usa CGNAT, se necesita VPN, túnel o servidor intermedio.

## Conflictos

Si el archivo o carpeta ya existe en destino, el equipo que recibe muestra un diálogo:

- `Sí`: sobrescribir.
- `No`: renombrar automáticamente.
- `Cancelar`: saltar.

## Red

- Transferencias TCP: puerto configurable, por defecto `50505`.
- Descubrimiento LAN UDP: puerto `50506`.

Si Windows Firewall bloquea la app, permitir tráfico privado/LAN para el ejecutable.

## Alcance de esta primera versión

- Transferencias grandes por bloques de 256 KB.
- Progreso por archivo con barra, velocidad y tiempo estimado.
- Una conexión activa por ventana.
- Transferencias serializadas para mantener el protocolo simple y estable.
- Sin historial persistente.
