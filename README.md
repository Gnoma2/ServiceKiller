<p align="center">
  <img src="ServiceKiller_256.png" alt="ServiceKiller" width="140">
</p>

# ServiceKiller
[![Release](https://img.shields.io/github/v/release/Gnoma2/ServiceKiller?display_name=tag)](https://github.com/Gnoma2/ServiceKiller/releases/latest)
[![Build](https://github.com/Gnoma2/ServiceKiller/actions/workflows/build.yml/badge.svg)](https://github.com/Gnoma2/ServiceKiller/actions/workflows/build.yml)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0--only-blue.svg)](LICENSE)
[![Validated on Windows 11](https://img.shields.io/badge/Validated-Windows%2011%20Pro%2025H2-0078D4?logo=windows11&logoColor=white)](#)

<p align="center">
  <a href="https://github.com/Gnoma2/ServiceKiller/releases/latest">
    <strong>⬇ Descargar última versión</strong>
  </a>
</p>

**ServiceKiller** es una utilidad open source para Windows que permite reducir actividad en segundo plano mediante cambios **reversibles** sobre servicios, procesos, inicio automático y determinadas configuraciones del sistema.

> **Plataforma validada actualmente:** Windows 11 Pro 25H2 x64, build 26200.  
> **Windows 10:** no validado en esta versión pública. No se afirma compatibilidad.

La versión pública actual es **V1.1.3.01** y se distribuye bajo **GPL-3.0-only**.

[English README](README.en.md)

<p align="center">
  <img src="ServiceKiller_README.png" alt="Interfaz de ServiceKiller v1.1.3.01" width="100%">
</p>

## Instalación rápida

1. Descarga `ServiceKiller-v1.1.3.01-win-x64.zip` desde [Releases](https://github.com/Gnoma2/ServiceKiller/releases/latest).
2. Extrae **todo el contenido del ZIP** en una carpeta.
3. Mantén `ServiceKiller.exe` y `ServiceKiller.exe.config` juntos en la misma carpeta.
4. Ejecuta `ServiceKiller.exe` **como administrador**.

> ServiceKiller no utiliza instalador. Antes de aplicar cambios, revisa el perfil y las acciones seleccionadas.

## Qué hace

ServiceKiller ofrece tres perfiles predefinidos —**Conservador**, **Equilibrado** y **Agresivo**— y permite revisar las acciones antes de aplicarlas. El catálogo incluye servicios de Windows, Game Bar/Game DVR, aplicaciones residentes y mecanismos de inicio automático. El programa guarda el estado original de los componentes respaldables para poder restaurarlos.

Hay dos modos de aplicación:

- **Persistente:** los cambios respaldados permanecen hasta que el usuario los restaura.
- **Temporal hasta reinicio:** aplica solo acciones aptas para sesión y programa una restauración automática en el siguiente inicio de sesión.

El modo temporal utiliza Task Scheduler 2.0 COM, una copia protegida del restaurador dentro de `C:\ProgramData\ServiceKiller\SessionRestore`, control de permisos y verificación SHA-256 antes de confiar en el worker.

## Seguridad por diseño

ServiceKiller **no desactiva** Defender, SmartScreen, Firewall, Windows Update/BITS, audio, micrófono/cámara ni los servicios base de red marcados como protegidos en el catálogo.

El código fuente público no implementa telemetría ni transmisión de datos por red. Los journals y logs se almacenan localmente. El diagnóstico incorpora anonimización *best effort*, pero debe revisarse antes de publicarlo en un issue.

Consulta:

- [Catálogo completo de tweaks](docs/TWEAKS.md)
- [Arquitectura y modelo de restauración](docs/ARCHITECTURE.md)
- [Compatibilidad](docs/COMPATIBILITY.md)
- [Validación realizada](docs/VALIDATION.md)
- [Privacidad](PRIVACY.md)
- [Política de seguridad](SECURITY.md)

## Compilar

Requisitos:

- Windows
- .NET Framework 4.8
- Visual Studio con soporte para proyectos .NET Framework, **o** el compilador C# incluido con .NET Framework

La forma más directa:

```bat
BUILD_RELEASE.bat
```

El ejecutable se genera en:

```text
artifacts\ServiceKiller.exe
```

El script muestra su SHA-256 y **no ejecuta automáticamente** el binario recién compilado.

También puede abrirse:

```text
src\ServiceKiller\ServiceKillerV1.sln
```

Más detalles en [BUILDING.md](BUILDING.md).

## Advertencias

ServiceKiller modifica configuración del sistema y requiere elevación UAC para las operaciones que lo necesitan. Antes de aplicar cambios, revisa las consecuencias mostradas por la propia interfaz.

No se prometen aumentos universales de FPS, menor latencia o mejoras de rendimiento en todos los equipos. La columna de beneficio esperado es una estimación cualitativa de reducción potencial de actividad en segundo plano, no una garantía de rendimiento.

Si un antivirus marca una build, **no desactives la protección para ejecutarla**. Verifica que el binario proceda de una release oficial, comprueba su SHA-256 y revisa el código fuente. Consulta [docs/ANTIVIRUS.md](docs/ANTIVIRUS.md).

## Estado del proyecto

V1.1.3.01 se ha validado funcionalmente en Windows 11 Pro 25H2 x64, build 26200, incluyendo:

- ciclo Agresivo/Persistente;
- persistencia tras reinicio;
- restauración global 18/18;
- ciclo Agresivo/Temporal;
- restauración automática 18/18 componentes verificados;
- limpieza de journal, tarea programada y restaurador protegido.

Los detalles se documentan en [docs/VALIDATION.md](docs/VALIDATION.md).

## Licencia

Copyright © 2026 **@SirAlexelgrande**.

ServiceKiller se publica bajo **GNU General Public License v3.0 only (`GPL-3.0-only`)**. Si distribuyes una versión modificada, debes cumplir las obligaciones de la GPL, incluido proporcionar el código fuente correspondiente bajo una licencia compatible.

Consulta [LICENSE](LICENSE).

## Independencia

ServiceKiller es un proyecto independiente. No está afiliado, patrocinado ni respaldado por Microsoft ni por los fabricantes de las aplicaciones que puede detectar o cerrar. Las marcas y nombres de producto pertenecen a sus respectivos propietarios.
