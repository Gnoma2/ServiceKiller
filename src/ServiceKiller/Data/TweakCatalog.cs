using System.Collections.Generic;
using ServiceKillerV1.Models;
using ServiceKillerV1.Core;

namespace ServiceKillerV1.Data
{
    public static class TweakCatalog
    {
        public static List<TweakDefinition> Create()
        {
            List<TweakDefinition> list = new List<TweakDefinition>();
            bool windows7 = WindowsCompatibility.IsWindows7;
            bool modernWindows = WindowsCompatibility.SupportsModernWidgets;

            list.Add(Service("win.diagtrack", "Telemetría / DiagTrack", "Windows", "Connected User Experiences and Telemetry.", "Reduce telemetría de Windows. Algunas funciones de diagnóstico/experiencias conectadas pueden perder datos.", ImpactLevel.Low, true, true, true, true, "DiagTrack"));
            if (!windows7)
            {
                list.Add(Service("win.maps", "Mapas sin conexión", "Windows", "Gestor de mapas descargados de Windows.", "Los mapas sin conexión de Windows dejarán de actualizarse/estar disponibles mediante este servicio.", ImpactLevel.Low, true, true, true, true, "MapsBroker"));
                list.Add(Service("win.retail", "Retail Demo", "Windows", "Modo de demostración para equipos de exposición.", "Sin impacto esperado en un PC doméstico que no esté en modo demo.", ImpactLevel.Low, true, true, true, true, "RetailDemo"));
            }
            list.Add(Service("win.trkwks", "Distributed Link Tracking", "Windows", "Mantiene vínculos de archivos NTFS entre volúmenes/equipos.", "Puede dejar de actualizar referencias a archivos movidos en escenarios específicos.", ImpactLevel.Low, true, true, true, true, "TrkWks"));

            // V1.1.2.7: en Windows 11 build 26200 la directiva de equipo Dsh puede
            // rechazar escritura incluso desde SYSTEM. Para el boost usamos la preferencia
            // de usuario documentada por Windows para ocultar el botón Widgets y cerramos
            // la residencia actual. Es reversible y evita tocar ACL/propietarios.
            if (modernWindows)
            {
                TweakDefinition widgets = Persistent("win.widgets", "Widgets de Windows 11", "Windows", "Oculta el botón Widgets para el usuario actual y cierra sus procesos residentes.", "El botón Widgets dejará de mostrarse y su residencia se cerrará. Restaurar devuelve exactamente la preferencia anterior; no fuerza una política de equipo.", ImpactLevel.Medium, false, true, true);
                widgets.RegistryStrings.Add(StringValue("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDa", "SystemSettings_DesktopTaskbar_Da", "0"));
                widgets.ProcessNames.Add("Widgets");
                widgets.ProcessNames.Add("WidgetService");
                list.Add(widgets);
            }

            list.Add(Service("win.search", "Windows Search / indexación", "Windows", "Detiene y deshabilita WSearch.", "Las búsquedas indexadas de archivos y aplicaciones que dependan del índice pueden ser más lentas o incompletas.", ImpactLevel.Medium, false, false, true, false, "WSearch"));
            list.Add(Service("win.location", "Geolocalización", "Windows", "Deshabilita el servicio de ubicación de Windows.", "Aplicaciones que necesiten ubicación/geofencing dejarán de recibirla.", ImpactLevel.Medium, false, true, true, true, "lfsvc"));
            if (!windows7)
                list.Add(Service("win.connected", "Dispositivos conectados / Phone", "Windows", "Deshabilita Connected Devices Platform y Phone Service.", "Puede afectar Phone Link, experiencias entre dispositivos, proximidad y algunas funciones de teléfono.", ImpactLevel.Medium, false, false, true, true, "CDPSvc", "PhoneSvc"));
            list.Add(Service("win.webclient", "WebClient / WebDAV", "Windows", "Deshabilita el cliente WebDAV de Windows.", "Unidades o recursos WebDAV dejarán de funcionar hasta restaurarlo.", ImpactLevel.Medium, false, true, true, true, "WebClient"));
            list.Add(Service("win.smartcard", "Smart Card", "Windows", "Deshabilita el servicio de tarjetas inteligentes.", "Lectores/certificados que dependan de smart cards no funcionarán.", ImpactLevel.Medium, false, true, true, true, "SCardSvr"));
            list.Add(Service("win.tapi", "Telefonía TAPI", "Windows", "Deshabilita la API de telefonía clásica de Windows.", "Software que use TAPI dejará de funcionar.", ImpactLevel.Low, false, true, true, true, "TapiSrv"));
            list.Add(Service("win.branchcache", "BranchCache", "Windows", "Deshabilita PeerDistSvc, orientado principalmente a redes empresariales.", "BranchCache dejará de funcionar.", ImpactLevel.Low, false, true, true, true, "PeerDistSvc"));
            if (!windows7)
                list.Add(Service("win.alljoyn", "AllJoyn Router", "Windows", "Deshabilita el servicio AllJoyn Router.", "Aplicaciones/dispositivos que dependan de AllJoyn no podrán usarlo.", ImpactLevel.Low, false, true, true, true, "AJRouter"));
            list.Add(Service("win.alg", "Application Layer Gateway", "Windows", "Deshabilita ALG, usado por escenarios antiguos de ICS/protocolos.", "Puede afectar software antiguo que dependa de plugins ALG.", ImpactLevel.Low, false, true, true, true, "ALG"));
            if (!windows7)
                list.Add(Service("win.wallet", "Wallet Service", "Windows", "Deshabilita WalletService de Windows.", "Funciones de Wallet de Windows que dependan del servicio dejarán de funcionar.", ImpactLevel.Low, false, true, true, true, "WalletService"));

            TweakDefinition dlna = Persistent("win.dlna", "DLNA / UPnP multimedia", "Funciones opcionales", "Deshabilita servicios de descubrimiento/host UPnP y compartición multimedia de Windows Media Player.", "El PC dejará de actuar como servidor/host DLNA-UPnP y puede dejar de descubrir ciertos dispositivos UPnP.", ImpactLevel.Medium, false, false, true);
            AddServices(dlna, "WMPNetworkSvc", "SSDPSRV", "upnphost");
            dlna.SkipManualStoppedServices = true;
            list.Add(dlna);

            list.Add(Service("win.print", "Impresión / Print Spooler", "Funciones opcionales", "Detiene y deshabilita Print Spooler.", "No podrás imprimir y algunas impresoras PDF/virtuales pueden dejar de funcionar.", ImpactLevel.High, false, false, true, false, "Spooler"));

            if (!windows7)
            {
                TweakDefinition xbox = Persistent("win.xbox.services", "Servicios Xbox Live", "Gaming / Xbox", "Deshabilita autenticación, guardado, red y accesorios Xbox del conjunto clásico.", "Las funciones Xbox Live y algunos accesorios Xbox pueden dejar de funcionar. Gaming Services no se toca.", ImpactLevel.Medium, false, false, true);
                AddServices(xbox, "XboxGipSvc", "XblAuthManager", "XblGameSave", "XboxNetApiSvc");
                xbox.SkipManualStoppedServices = false;
                list.Add(xbox);
    
                TweakDefinition gameBar = Persistent("win.gamebar", "Xbox Game Bar / Game DVR", "Gaming / Xbox", "Deshabilita captura Game DVR y cierra Game Bar.", "No podrás grabar/capturar mediante Game Bar hasta restaurarlo.", ImpactLevel.Medium, false, false, true);
                gameBar.RegistryDwords.Add(Dword("HKCU", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0));
                gameBar.RegistryDwords.Add(Dword("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 0));
                gameBar.RegistryDwords.Add(Dword("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0));
                gameBar.ProcessNames.Add("GameBar");
                gameBar.ProcessNames.Add("GameBarFTServer");
                gameBar.ProcessNames.Add("GameBarPresenceWriter");
                list.Add(gameBar);
            }

            list.Add(Service("win.touch", "Touch Keyboard / escritura táctil", "Funciones opcionales", "Deshabilita TabletInputService si existe.", "Teclado táctil y escritura manuscrita asociados a este servicio dejarán de funcionar.", ImpactLevel.Medium, false, false, true, true, "TabletInputService"));
            list.Add(Service("win.fax", "Fax", "Funciones opcionales", "Deshabilita el servicio Fax si está instalado.", "Envío/recepción de fax mediante Windows dejará de funcionar.", ImpactLevel.Low, false, false, true, true, "Fax"));
            list.Add(Service("win.biometric", "Biometría / Windows Hello biométrico", "Funciones opcionales", "Deshabilita Windows Biometric Service.", "Huella/biometría dependiente de WbioSrvc no funcionará. El PIN no se elimina.", ImpactLevel.High, false, false, true, true, "WbioSrvc"));

            TweakDefinition remote = Persistent("win.rdp", "Remote Desktop (servidor)", "Funciones opcionales", "Deshabilita los servicios principales de host de Escritorio remoto.", "Este PC no aceptará sesiones RDP mientras permanezcan deshabilitados.", ImpactLevel.High, false, false, true);
            AddServices(remote, "TermService", "SessionEnv", "UmRdpService");
            remote.SkipManualStoppedServices = true;
            list.Add(remote);

            TweakDefinition diagnostics = Persistent("win.diagnostics", "Diagnóstico de Windows", "Funciones opcionales", "Deshabilita la infraestructura principal de diagnóstico/troubleshooting seleccionada.", "Los solucionadores de problemas y diagnósticos automáticos pueden dejar de funcionar. No es un tweak de FPS demostrado; se incluye solo para el perfil agresivo solicitado.", ImpactLevel.High, false, false, true);
            AddServices(diagnostics, "DPS", "diagsvc", "WdiServiceHost", "WdiSystemHost");
            diagnostics.SkipManualStoppedServices = true;
            list.Add(diagnostics);

            list.Add(Service("win.wer", "Windows Error Reporting", "Funciones opcionales", "Deshabilita WerSvc cuando aporte un cambio real.", "Windows dejará de enviar/procesar algunos informes de errores mediante este servicio.", ImpactLevel.Medium, false, false, true, true, "WerSvc"));

            TweakDefinition sensors = Persistent("win.sensors", "Sensores", "Funciones opcionales", "Deshabilita servicios de sensores presentes en el equipo si están activos/automáticos.", "Puede afectar sensores de luz, orientación, rotación u otros sensores físicos.", ImpactLevel.Medium, false, false, true);
            AddServices(sensors, "SensorDataService", "SensorService", "SensrSvc");
            sensors.SkipManualStoppedServices = true;
            list.Add(sensors);

            if (WindowsCompatibility.SupportsClientHypervisor)
            {
                TweakDefinition hypervisor = Persistent("win.hypervisor", "Hypervisor (Hyper-V / Windows Sandbox)", "Virtualización", "Impide que el hipervisor se cargue en el siguiente arranque sin desinstalar las características.", "Windows Sandbox, Hyper-V y funciones que necesiten el hipervisor no funcionarán hasta restaurar y reiniciar.", ImpactLevel.High, false, false, true);
                hypervisor.ChangeKind = ChangeKind.RestartRequired;
                hypervisor.BootTargets.Add(new BootTarget { Name = "hypervisorlaunchtype", TargetValue = "off" });
                list.Add(hypervisor);
            }

            TweakDefinition epicClose = Temporary("app.epic.close", "Cerrar Epic Games", "Epic Games", "Cierra Epic Games Launcher y sus procesos web hijos.", "Epic se puede volver a abrir manualmente en cualquier momento.", ImpactLevel.Low, true);
            epicClose.ProcessPrefixes.Add("EpicGamesLauncher");
            epicClose.ProcessPrefixes.Add("EpicWebHelper");
            list.Add(epicClose);

            TweakDefinition epicStartup = Persistent("app.epic.startup", "Quitar Epic del inicio automático", "Epic Games", "Desactiva mecanismos de inicio automático detectados para Epic Games Launcher (Run/RunOnce, Inicio o tarea de logon) con copia reversible.", "Epic seguirá funcionando al abrirlo manualmente. ServiceKiller conserva el estado original para restaurarlo.", ImpactLevel.Low, false, false, true);
            epicStartup.IsApplication = true;
            epicStartup.IsStartupOnlyAction = true;
            epicStartup.StartupRules.Add(new StartupRule { MatchText = "EpicGamesLauncher", SearchValueName = true, SearchValueData = true });
            list.Add(epicStartup);

            TweakDefinition powerToys = Temporary("app.powertoys.close", "Cerrar PowerToys", "PowerToys", "Cierra PowerToys y procesos cuyo nombre comienza por PowerToys.", "Las utilidades de PowerToys no estarán disponibles hasta que vuelvas a abrir PowerToys.", ImpactLevel.Low, true);
            powerToys.ProcessPrefixes.Add("PowerToys");
            list.Add(powerToys);

            TweakDefinition powerToysStartup = Persistent("app.powertoys.startup", "Quitar PowerToys del inicio automático", "PowerToys", "Desactiva mecanismos de inicio automático detectados para PowerToys (Run/RunOnce, Inicio o tarea programada de logon).", "PowerToys seguirá funcionando al abrirlo manualmente. El mecanismo original se conserva para restauración.", ImpactLevel.Low, false, false, true);
            powerToysStartup.IsApplication = true;
            powerToysStartup.IsStartupOnlyAction = true;
            powerToysStartup.StartupRules.Add(new StartupRule { MatchText = "PowerToys", SearchValueName = true, SearchValueData = true });
            list.Add(powerToysStartup);

            TweakDefinition teams = Temporary("app.teams.close", "Cerrar Microsoft Teams", "Microsoft Teams", "Cierra Teams y su árbol de procesos sin cambiar su configuración de arranque.", "No recibirás llamadas/notificaciones de Teams hasta volver a abrirlo.", ImpactLevel.Medium, true);
            teams.ProcessNames.Add("ms-teams");
            teams.ProcessNames.Add("Teams");
            list.Add(teams);

            TweakDefinition teamsStartup = Persistent("app.teams.startup", "Quitar Teams del inicio automático", "Microsoft Teams", "Desactiva el StartupTask de Teams moderno y también entradas clásicas Run/RunOnce/Inicio/tareas de logon que puedan existir.", "Teams seguirá funcionando al abrirlo manualmente. El estado original del StartupTask y de cualquier entrada clásica queda respaldado.", ImpactLevel.Low, false, false, true);
            teamsStartup.IsApplication = true;
            teamsStartup.IsStartupOnlyAction = true;
            teamsStartup.StartupRules.Add(new StartupRule { MatchText = "Teams", SearchValueName = true, SearchValueData = true });
            if (!windows7)
                teamsStartup.RegistryDwords.Add(Dword("HKCU", @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\MSTeams_8wekyb3d8bbwe\TeamsTfwStartupTask", "State", 1));
            list.Add(teamsStartup);

            TweakDefinition rewasd = Temporary("app.rewasd.close", "Cerrar reWASD", "reWASD", "Cierra todos los procesos reWASD detectados y detiene temporalmente servicios cuyo nombre contenga reWASD.", "Los remapeos que dependan de reWASD dejarán de funcionar hasta volver a iniciar la aplicación/servicio o reiniciar.", ImpactLevel.Medium, true);
            rewasd.ProcessPrefixes.Add("reWASD");
            rewasd.TemporaryServiceNameContains.Add("reWASD");
            list.Add(rewasd);

            TweakDefinition rewasdStartup = Persistent("app.rewasd.startup", "Quitar reWASD del inicio automático", "reWASD", "Desactiva entradas de inicio de reWASD y el arranque automático de reWASDService cuando existe.", "reWASD seguirá pudiendo abrirse manualmente. El tipo de inicio original del servicio y cualquier entrada encontrada quedan respaldados.", ImpactLevel.Low, false, false, true);
            rewasdStartup.IsApplication = true;
            rewasdStartup.IsStartupOnlyAction = true;
            rewasdStartup.StartupRules.Add(new StartupRule { MatchText = "reWASD", SearchValueName = true, SearchValueData = true });
            rewasdStartup.Services.Add(new ServiceTarget { Name = "reWASDService", Stop = false, DisableStartup = true, OnlyIfAutomaticStartup = true });
            rewasdStartup.SkipManualStoppedServices = true;
            list.Add(rewasdStartup);

            list.Add(Protected("protected.bluetooth", "Bluetooth", "Se mantiene intacto porque forma parte de periféricos que deben seguir disponibles."));
            list.Add(Protected("protected.security", "Defender / SmartScreen / Firewall", "ServiceKiller no modifica estas capas de seguridad."));
            list.Add(Protected("protected.update", windows7 ? "Windows Update / BITS" : "Windows Update / BITS / Update Medic / Delivery Optimization", "ServiceKiller no deshabilita permanentemente la infraestructura de actualización."));
            list.Add(Protected("protected.media", "Audio / micrófono / cámara", "Se conserva para Teams, webcam, micrófono y audio del sistema."));
            list.Add(Protected("protected.network", "DHCP / DNS / RPC / Plug and Play / Event Log / Cryptographic Services", "Servicios base de Windows y red: fuera del alcance de ServiceKiller."));

            ApplyPerformanceBenefits(list);
            return list;
        }


        private static void ApplyPerformanceBenefits(List<TweakDefinition> list)
        {
            foreach (TweakDefinition tweak in list)
            {
                // La columna BENEFICIO ESPERADO expresa una estimación cualitativa del
                // ahorro potencial de actividad en segundo plano. No son FPS garantizados.
                switch (tweak.Id)
                {
                    case "win.retail":
                    case "win.fax":
                    case "protected.bluetooth":
                    case "protected.security":
                    case "protected.update":
                    case "protected.media":
                    case "protected.network":
                        tweak.PerformanceBenefit = PerformanceBenefitLevel.None;
                        break;

                    case "win.maps":
                    case "win.trkwks":
                    case "win.location":
                    case "win.webclient":
                    case "win.smartcard":
                    case "win.tapi":
                    case "win.branchcache":
                    case "win.alljoyn":
                    case "win.alg":
                    case "win.wallet":
                    case "win.print":
                    case "win.touch":
                    case "win.biometric":
                    case "win.wer":
                    case "win.sensors":
                        tweak.PerformanceBenefit = PerformanceBenefitLevel.VeryLow;
                        break;

                    case "win.diagtrack":
                    case "win.widgets":
                    case "win.connected":
                    case "win.dlna":
                    case "win.xbox.services":
                    case "win.gamebar":
                    case "win.rdp":
                    case "win.diagnostics":
                    case "app.epic.startup":
                    case "app.powertoys.startup":
                    case "app.teams.startup":
                    case "app.rewasd.startup":
                    case "app.powertoys.close":
                    case "app.rewasd.close":
                        tweak.PerformanceBenefit = PerformanceBenefitLevel.Low;
                        break;

                    case "win.search":
                    case "win.hypervisor":
                    case "app.epic.close":
                    case "app.teams.close":
                        tweak.PerformanceBenefit = PerformanceBenefitLevel.Medium;
                        break;

                    default:
                        tweak.PerformanceBenefit = PerformanceBenefitLevel.VeryLow;
                        break;
                }
            }
        }

        private static TweakDefinition Service(string id, string name, string category, string description, string consequences, ImpactLevel impact, bool conservative, bool balanced, bool aggressive, bool skipManualStopped, params string[] serviceNames)
        {
            TweakDefinition tweak = Persistent(id, name, category, description, consequences, impact, conservative, balanced, aggressive);
            AddServices(tweak, serviceNames);
            tweak.SkipManualStoppedServices = skipManualStopped;
            return tweak;
        }

        private static TweakDefinition Persistent(string id, string name, string category, string description, string consequences, ImpactLevel impact, bool conservative, bool balanced, bool aggressive)
        {
            TweakDefinition tweak = new TweakDefinition();
            tweak.Id = id;
            tweak.Name = name;
            tweak.Category = category;
            tweak.Description = description;
            tweak.Consequences = consequences;
            tweak.Impact = impact;
            tweak.ChangeKind = ChangeKind.Persistent;
            tweak.Conservative = conservative;
            tweak.Balanced = balanced;
            tweak.Aggressive = aggressive;
            return tweak;
        }

        private static TweakDefinition Temporary(string id, string name, string category, string description, string consequences, ImpactLevel impact, bool aggressive)
        {
            TweakDefinition tweak = new TweakDefinition();
            tweak.Id = id;
            tweak.Name = name;
            tweak.Category = category;
            tweak.Description = description;
            tweak.Consequences = consequences;
            tweak.Impact = impact;
            tweak.ChangeKind = ChangeKind.Temporary;
            tweak.Aggressive = aggressive;
            tweak.IsApplication = true;
            return tweak;
        }

        private static TweakDefinition Protected(string id, string name, string description)
        {
            TweakDefinition tweak = new TweakDefinition();
            tweak.Id = id;
            tweak.Name = name;
            tweak.Category = "Protegido";
            tweak.Description = description;
            tweak.Consequences = "No se modifica desde ServiceKiller.";
            tweak.Impact = ImpactLevel.Low;
            tweak.ChangeKind = ChangeKind.Persistent;
            tweak.IsProtectedInfo = true;
            return tweak;
        }

        private static RegistryDwordTarget Dword(string hive, string path, string name, int value)
        {
            return new RegistryDwordTarget { Hive = hive, KeyPath = path, ValueName = name, TargetValue = value };
        }

        private static RegistryStringTarget StringValue(string hive, string path, string name, string value)
        {
            return new RegistryStringTarget { Hive = hive, KeyPath = path, ValueName = name, TargetValue = value };
        }

        private static void AddServices(TweakDefinition tweak, params string[] serviceNames)
        {
            foreach (string serviceName in serviceNames)
                tweak.Services.Add(new ServiceTarget { Name = serviceName, Stop = true, DisableStartup = true });
        }
    }
}
