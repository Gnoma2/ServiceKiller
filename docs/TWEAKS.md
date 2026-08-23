# Catálogo de tweaks

Este documento resume el catálogo definido en `src/ServiceKiller/Data/TweakCatalog.cs`.

Los perfiles son preselecciones. El hecho de que un tweak pertenezca a un perfil no significa que siempre produzca un cambio: ServiceKiller detecta servicios ausentes, estados manual/parado y configuraciones que ya coinciden con el objetivo.

**Impacto** describe el riesgo funcional de perder una característica, no el rendimiento esperado.

| ID | Función | Tipo | Conservador | Equilibrado | Agresivo | Impacto | Targets principales |
| --- | --- | --- | :---: | :---: | :---: | --- | --- |
| `win.diagtrack` | Telemetría / DiagTrack | Persistente | ✓ | ✓ | ✓ | Low | Servicios: `DiagTrack` |
| `win.maps` | Mapas sin conexión | Persistente | ✓ | ✓ | ✓ | Low | Servicios: `MapsBroker` |
| `win.retail` | Retail Demo | Persistente | ✓ | ✓ | ✓ | Low | Servicios: `RetailDemo` |
| `win.trkwks` | Distributed Link Tracking | Persistente | ✓ | ✓ | ✓ | Low | Servicios: `TrkWks` |
| `win.widgets` | Widgets de Windows 11 | Persistente | — | ✓ | ✓ | Medium | Registro: `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDa` → `SystemSettings_DesktopTaskbar_Da="0"`<br>Procesos: `Widgets`, `WidgetService` |
| `win.search` | Windows Search / indexación | Persistente | — | — | ✓ | Medium | Servicios: `WSearch` |
| `win.location` | Geolocalización | Persistente | — | ✓ | ✓ | Medium | Servicios: `lfsvc` |
| `win.connected` | Dispositivos conectados / Phone | Persistente | — | — | ✓ | Medium | Servicios: `CDPSvc`, `PhoneSvc` |
| `win.webclient` | WebClient / WebDAV | Persistente | — | ✓ | ✓ | Medium | Servicios: `WebClient` |
| `win.smartcard` | Smart Card | Persistente | — | ✓ | ✓ | Medium | Servicios: `SCardSvr` |
| `win.tapi` | Telefonía TAPI | Persistente | — | ✓ | ✓ | Low | Servicios: `TapiSrv` |
| `win.branchcache` | BranchCache | Persistente | — | ✓ | ✓ | Low | Servicios: `PeerDistSvc` |
| `win.alljoyn` | AllJoyn Router | Persistente | — | ✓ | ✓ | Low | Servicios: `AJRouter` |
| `win.alg` | Application Layer Gateway | Persistente | — | ✓ | ✓ | Low | Servicios: `ALG` |
| `win.wallet` | Wallet Service | Persistente | — | ✓ | ✓ | Low | Servicios: `WalletService` |
| `win.dlna` | DLNA / UPnP multimedia | Persistente | — | — | ✓ | Medium | Servicios: `WMPNetworkSvc`, `SSDPSRV`, `upnphost` |
| `win.print` | Impresión / Print Spooler | Persistente | — | — | ✓ | High | Servicios: `Spooler` |
| `win.xbox.services` | Servicios Xbox Live | Persistente | — | — | ✓ | Medium | Servicios: `XboxGipSvc`, `XblAuthManager`, `XblGameSave`, `XboxNetApiSvc` |
| `win.gamebar` | Xbox Game Bar / Game DVR | Persistente | — | — | ✓ | Medium | Registro: `HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR` → `AppCaptureEnabled=0`; `HKCU\System\GameConfigStore` → `GameDVR_Enabled=0`; `HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR` → `AllowGameDVR=0`<br>Procesos: `GameBar`, `GameBarFTServer`, `GameBarPresenceWriter` |
| `win.touch` | Touch Keyboard / escritura táctil | Persistente | — | — | ✓ | Medium | Servicios: `TabletInputService` |
| `win.fax` | Fax | Persistente | — | — | ✓ | Low | Servicios: `Fax` |
| `win.biometric` | Biometría / Windows Hello biométrico | Persistente | — | — | ✓ | High | Servicios: `WbioSrvc` |
| `win.rdp` | Remote Desktop (servidor) | Persistente | — | — | ✓ | High | Servicios: `TermService`, `SessionEnv`, `UmRdpService` |
| `win.diagnostics` | Diagnóstico de Windows | Persistente | — | — | ✓ | High | Servicios: `DPS`, `diagsvc`, `WdiServiceHost`, `WdiSystemHost` |
| `win.wer` | Windows Error Reporting | Persistente | — | — | ✓ | Medium | Servicios: `WerSvc` |
| `win.sensors` | Sensores | Persistente | — | — | ✓ | Medium | Servicios: `SensorDataService`, `SensorService`, `SensrSvc` |
| `win.hypervisor` | Hypervisor (Hyper-V / Windows Sandbox) | Reinicio | — | — | ✓ | High | BCD: `hypervisorlaunchtype=off` |
| `app.epic.close` | Cerrar Epic Games | Temporal | — | — | ✓ | Low | Procesos: `EpicGamesLauncher`, `EpicWebHelper` |
| `app.epic.startup` | Quitar Epic del inicio automático | Persistente | — | — | ✓ | Low | Gestiona mecanismos de inicio detectados y conserva el estado original para restauración. |
| `app.powertoys.close` | Cerrar PowerToys | Temporal | — | — | ✓ | Low | Procesos: `PowerToys` |
| `app.powertoys.startup` | Quitar PowerToys del inicio automático | Persistente | — | — | ✓ | Low | Gestiona mecanismos de inicio detectados y conserva el estado original para restauración. |
| `app.teams.close` | Cerrar Microsoft Teams | Temporal | — | — | ✓ | Medium | Procesos: `ms-teams`, `Teams` |
| `app.teams.startup` | Quitar Teams del inicio automático | Persistente | — | — | ✓ | Low | Registro: `HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\MSTeams_8wekyb3d8bbwe\TeamsTfwStartupTask` → `State=1`<br>Gestiona mecanismos de inicio detectados y conserva el estado original para restauración. |
| `app.rewasd.close` | Cerrar reWASD | Temporal | — | — | ✓ | Medium | Procesos: `reWASD`<br>Detiene temporalmente servicios cuyo nombre contiene `reWASD`. |
| `app.rewasd.startup` | Quitar reWASD del inicio automático | Persistente | — | — | ✓ | Low | Servicios: `reWASDService`<br>Gestiona mecanismos de inicio detectados y conserva el estado original para restauración. |

## Elementos expresamente protegidos

ServiceKiller muestra como protegidos/informativos y no ofrece como tweaks:

- Bluetooth.
- Defender / SmartScreen / Firewall.
- Windows Update / BITS / Update Medic / Delivery Optimization.
- Audio / micrófono / cámara.
- DHCP / DNS / RPC / Plug and Play / Event Log / Cryptographic Services.

## Modos y catálogo

En **Persistente**, el perfil Agresivo puede seleccionar las 35 acciones del catálogo aplicable al sistema.

En **Temporal hasta reinicio**, se excluyen acciones cuya semántica es persistentemente de arranque —por ejemplo desactivar inicios automáticos— y el cambio BCD del hypervisor. En la validación de Windows 11, el perfil Agresivo temporal seleccionó 30 acciones.

## Notas importantes

- `win.hypervisor` requiere reinicio para que `hypervisorlaunchtype=off` afecte al siguiente arranque y también requiere reinicio tras restaurarlo.
- `win.gamebar` guarda el estado previo de los tres valores de Registro antes de escribir los valores objetivo.
- Los servicios se restauran al tipo de inicio y estado guardados; Windows puede volver a iniciar posteriormente algunos servicios por trigger.
- Los cierres de Epic, PowerToys, Teams y reWASD son acciones de sesión. Volver a abrir manualmente una aplicación no invalida la restauración de otros cambios.
- Los mecanismos de inicio automático de aplicaciones se respaldan antes de modificarlos en modo Persistente.
