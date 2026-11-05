<#
OptimizadorPC: aligera Windows en este PC.
Pasa a Manual los servicios que no se usan, quita programas del arranque, aplica ajustes de
rendimiento, limpia temporales y puede desactivar Windows Update.
Cada cambio se guarda en un respaldo JSON (%LOCALAPPDATA%\OptimizadorPC) que se puede deshacer
desde el menú. Nunca desinstala nada ni toca la lista de protegidos.
#>
param(
    [switch]$Todo,         # aplica todo lo recomendado sin menú ni preguntas
    [switch]$Diagnostico   # solo muestra el diagnóstico (no necesita administrador)
)

$ErrorActionPreference = 'Continue'
$Carpeta = Join-Path $env:LOCALAPPDATA 'OptimizadorPC'
New-Item -ItemType Directory -Force $Carpeta | Out-Null
$ArchivoLog = Join-Path $Carpeta 'log.txt'
$ArchivoRespaldo = Join-Path $Carpeta ('respaldo-{0:yyyyMMdd-HHmmss}.json' -f (Get-Date))
$script:Cambios = New-Object System.Collections.ArrayList
$script:PuntoCreado = $false
$script:SinPreguntas = [bool]$Todo
$script:Resumen = @{ Aplicado = 0; Omitido = 0; Error = 0 }

# Nunca se tocan: ni sus servicios, ni su arranque, ni sus procesos.
$Protegidos = 'GoogleDriveFS', 'Google Drive', 'SecurityHealth', 'memreduct', 'Mem Reduct',
              'hshld', 'hsscp', 'Hotspot', 'urban-vpn-app', 'claude', 'CoworkVM', 'Code',
              'ChatGPT', 'codex', 'python'

# Servicios de Windows que este PC no necesita. Manual = Windows aún puede arrancarlos si algo los pide.
$ServiciosWindows = @(
    @{ Nombre = 'Spooler';        Modo = 'Manual';        Desc = 'Cola de impresión (solo hay AnyDesk Printer)' }
    @{ Nombre = 'stisvc';         Modo = 'Manual';        Desc = 'Adquisición de imágenes (escáneres)' }
    @{ Nombre = 'TrkWks';         Modo = 'Manual';        Desc = 'Seguimiento de vínculos distribuidos' }
    @{ Nombre = 'PcaSvc';         Modo = 'Manual';        Desc = 'Asistente de compatibilidad de programas' }
    @{ Nombre = 'DPS';            Modo = 'Manual';        Desc = 'Directivas de diagnóstico' }
    @{ Nombre = 'WdiServiceHost'; Modo = 'Manual';        Desc = 'Host del servicio de diagnóstico' }
    @{ Nombre = 'WdiSystemHost';  Modo = 'Manual';        Desc = 'Host de sistema de diagnóstico' }
    @{ Nombre = 'lfsvc';          Modo = 'Manual';        Desc = 'Geolocalización' }
    @{ Nombre = 'PhoneSvc';       Modo = 'Manual';        Desc = 'Servicio telefónico' }
    @{ Nombre = 'InventorySvc';   Modo = 'Manual';        Desc = 'Inventario y compatibilidad' }
    @{ Nombre = 'whesvc';         Modo = 'Manual';        Desc = 'Estado y experiencias optimizadas' }
    @{ Nombre = 'CDPSvc';         Modo = 'Manual';        Desc = 'Plataforma de dispositivos conectados' }
    @{ Nombre = 'DoSvc';          Modo = 'Manual';        Desc = 'Optimización de distribución (P2P de updates)'; Blando = $true }
    @{ Nombre = 'MapsBroker';     Modo = 'Manual';        Desc = 'Mapas descargados' }
    @{ Nombre = 'RetailDemo';     Modo = 'Manual';        Desc = 'Modo demo de tienda' }
    @{ Nombre = 'WerSvc';         Modo = 'Manual';        Desc = 'Informe de errores de Windows' }
    @{ Nombre = 'XblAuthManager'; Modo = 'Deshabilitado'; Desc = 'Xbox Live: autenticación' }
    @{ Nombre = 'XblGameSave';    Modo = 'Deshabilitado'; Desc = 'Xbox Live: partidas guardadas' }
    @{ Nombre = 'XboxGipSvc';     Modo = 'Deshabilitado'; Desc = 'Xbox: accesorios' }
    @{ Nombre = 'XboxNetApiSvc';  Modo = 'Deshabilitado'; Desc = 'Xbox Live: red' }
)

$ServiciosUpdate = @(
    @{ Nombre = 'wuauserv';     Desc = 'Windows Update' }
    @{ Nombre = 'UsoSvc';       Desc = 'Orquestador de actualizaciones' }
    @{ Nombre = 'DoSvc';        Desc = 'Optimización de distribución'; Blando = $true }
    @{ Nombre = 'BITS';         Desc = 'Transferencia inteligente en segundo plano' }
    @{ Nombre = 'WaaSMedicSvc'; Desc = 'Reparador de Windows Update (protegido)'; Blando = $true }
)

#region Utilidades

function Escribir([string]$Texto, [string]$Nivel = 'Info') {
    $color = @{ Info = 'Gray'; Ok = 'Green'; Omitido = 'Yellow'; Error = 'Red'; Titulo = 'Cyan' }[$Nivel]
    Write-Host $Texto -ForegroundColor $color
    Add-Content -Path $ArchivoLog -Value ('{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}' -f (Get-Date), $Nivel, $Texto) -Encoding UTF8
}
function Ok([string]$Texto)      { $script:Resumen.Aplicado++; Escribir "  [OK] $Texto" 'Ok' }
function Omitido([string]$Texto) { $script:Resumen.Omitido++;  Escribir "  [--] $Texto" 'Omitido' }
function Fallo([string]$Texto)   { $script:Resumen.Error++;    Escribir "  [!!] $Texto" 'Error' }

function Es-Protegido([string]$Nombre) {
    foreach ($p in $Protegidos) { if ($Nombre -like "*$p*") { return $true } }
    $false
}

function Preguntar([string]$Texto, [bool]$PorDefecto = $false) {
    if ($script:SinPreguntas) { return $PorDefecto }
    $sufijo = if ($PorDefecto) { '(S/n)' } else { '(s/N)' }
    $r = Read-Host "$Texto $sufijo"
    if ($r -eq '') { return $PorDefecto }
    $r -match '^[sSyY]'
}

# Lista con casillas: todo marcado; el usuario escribe números para desmarcar/marcar.
function Elegir([array]$Elementos, [scriptblock]$Etiqueta) {
    if ($script:SinPreguntas) { return $Elementos }
    $marcados = @($Elementos | ForEach-Object { $true })
    while ($true) {
        Write-Host ''
        for ($i = 0; $i -lt $Elementos.Count; $i++) {
            $casilla = if ($marcados[$i]) { 'x' } else { ' ' }
            Write-Host ('{0,3}. [{1}] {2}' -f ($i + 1), $casilla, (& $Etiqueta $Elementos[$i]))
        }
        $r = Read-Host 'Números para marcar/desmarcar (ej. 1 4 7), Enter para aplicar, C para cancelar'
        if ($r -eq '') { break }
        if ($r -match '^[cC]$') { return @() }
        foreach ($n in ($r -split '[\s,]+')) {
            if ($n -match '^\d+$' -and [int]$n -ge 1 -and [int]$n -le $Elementos.Count) {
                $marcados[[int]$n - 1] = -not $marcados[[int]$n - 1]
            }
        }
    }
    for ($i = 0; $i -lt $Elementos.Count; $i++) { if ($marcados[$i]) { $Elementos[$i] } }
}

function Asegurar-PuntoRestauracion {
    if ($script:PuntoCreado) { return }
    $script:PuntoCreado = $true
    Escribir 'Creando punto de restauración (puede tardar un minuto)...'
    try {
        Checkpoint-Computer -Description 'OptimizadorPC' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop -WarningAction Stop
        Escribir '  Punto de restauración creado.' 'Ok'
    } catch {
        Escribir "  Sin punto de restauración ($($_.Exception.Message)). Los cambios igual quedan en el respaldo JSON." 'Omitido'
    }
}

function Registrar-Cambio([hashtable]$Cambio) {
    [void]$script:Cambios.Add($Cambio)
    @{ Fecha = (Get-Date).ToString('s'); Cambios = $script:Cambios } |
        ConvertTo-Json -Depth 6 | Set-Content -Path $ArchivoRespaldo -Encoding UTF8
}

#endregion

#region Servicios

$ModosSc = @{ 0 = 'boot'; 1 = 'system'; 2 = 'auto'; 3 = 'demand'; 4 = 'disabled' }

function Leer-InicioServicio([string]$Nombre) {
    $p = Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$Nombre" -ErrorAction SilentlyContinue
    if (-not $p -or $null -eq $p.Start) { return $null }
    @{ Start = [int]$p.Start; Delayed = [int]$p.DelayedAutostart }
}

function Escribir-InicioServicio([string]$Nombre, [int]$Start, [int]$Delayed) {
    $modo = $ModosSc[$Start]
    if ($Start -eq 2 -and $Delayed -eq 1) { $modo = 'delayed-auto' }
    & sc.exe config "$Nombre" start= $modo | Out-Null
    if ($LASTEXITCODE -eq 0) { return $true }
    # Algunos servicios rechazan sc.exe pero aceptan el cambio directo en el registro.
    try {
        Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\$Nombre" -Name Start -Value $Start -Type DWord -ErrorAction Stop
        return $true
    } catch { return $false }
}

function Configurar-Servicio([string]$Nombre, [string]$Modo, [string]$Desc, [bool]$Blando = $false) {
    $etiqueta = "$Desc ($Nombre)"
    if (Es-Protegido $Nombre) { Omitido "${etiqueta}: protegido"; return }
    $antes = Leer-InicioServicio $Nombre
    if (-not $antes) { Omitido "${etiqueta}: no existe en este equipo"; return }
    $start = if ($Modo -eq 'Deshabilitado') { 4 } else { 3 }
    $svc = Get-Service -Name $Nombre -ErrorAction SilentlyContinue
    $corriendo = $svc -and $svc.Status -eq 'Running'
    # Manual no "sube" un servicio que ya estaba deshabilitado.
    $cambiarInicio = if ($Modo -eq 'Manual') { $antes.Start -lt 3 } else { $antes.Start -ne 4 }
    if (-not $cambiarInicio -and -not $corriendo) {
        Omitido "${etiqueta}: ya estaba en $(if ($antes.Start -eq 4) { 'Deshabilitado' } else { 'Manual' }) y parado"
        return
    }
    if ($cambiarInicio) {
        Asegurar-PuntoRestauracion
        if (Escribir-InicioServicio $Nombre $start 0) {
            Registrar-Cambio @{ Tipo = 'Servicio'; Nombre = $Nombre; Start = $antes.Start; Delayed = $antes.Delayed }
        } elseif ($Blando) {
            Omitido "${etiqueta}: Windows protege este servicio y no deja cambiarlo"
            return
        } else {
            Fallo "${etiqueta}: Windows no deja cambiar su inicio"
            return
        }
    }
    if ($corriendo) {
        try { Stop-Service -Name $Nombre -Force -ErrorAction Stop -WarningAction SilentlyContinue }
        catch { Escribir '       (no se pudo detener ahora; quedará parado al reiniciar)' 'Omitido' }
    }
    Ok "$etiqueta -> $Modo"
}

#endregion

#region Arranque y tareas

$RaizAprobados = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved'

# Devuelve las entradas de arranque que coinciden con el patrón y la clave StartupApproved que
# las activa o desactiva (la misma que usa el Administrador de tareas).
function Buscar-Inicio([string]$Patron) {
    $origenes = @(
        @{ Run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run';             Aprobado = "HKCU:\$RaizAprobados\Run" }
        @{ Run = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run';             Aprobado = "HKLM:\$RaizAprobados\Run" }
        @{ Run = 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'; Aprobado = "HKLM:\$RaizAprobados\Run32" }
    )
    foreach ($o in $origenes) {
        $k = Get-Item $o.Run -ErrorAction SilentlyContinue
        if (-not $k) { continue }
        foreach ($n in $k.GetValueNames()) { if ($n -like $Patron) { @{ Nombre = $n; Aprobado = $o.Aprobado } } }
    }
    $carpetas = @(
        @{ Dir = [Environment]::GetFolderPath('Startup');       Aprobado = "HKCU:\$RaizAprobados\StartupFolder" }
        @{ Dir = [Environment]::GetFolderPath('CommonStartup'); Aprobado = "HKLM:\$RaizAprobados\StartupFolder" }
    )
    foreach ($c in $carpetas) {
        Get-ChildItem $c.Dir -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like $Patron } |
            ForEach-Object { @{ Nombre = $_.Name; Aprobado = $c.Aprobado } }
    }
}

function Desactivar-Inicio([string]$Patron, [string]$Desc, [string[]]$Procesos = @()) {
    $entradas = @(Buscar-Inicio $Patron)
    if ($entradas.Count -eq 0) { Omitido "${Desc}: no está en el arranque" }
    foreach ($e in $entradas) {
        if (Es-Protegido $e.Nombre) { Omitido "$Desc ($($e.Nombre)): protegido"; continue }
        $actual = (Get-ItemProperty $e.Aprobado -Name $e.Nombre -ErrorAction SilentlyContinue).($e.Nombre)
        if ($actual -and ($actual[0] -band 1)) { Omitido "$Desc ($($e.Nombre)): ya estaba desactivado del arranque"; continue }
        Asegurar-PuntoRestauracion
        if (-not (Test-Path $e.Aprobado)) { New-Item $e.Aprobado -Force | Out-Null }
        $valor = [byte[]](@(3, 0, 0, 0) + [BitConverter]::GetBytes((Get-Date).ToFileTime()))
        Set-ItemProperty $e.Aprobado -Name $e.Nombre -Value $valor -Type Binary
        Registrar-Cambio @{ Tipo = 'Registro'; Ruta = $e.Aprobado; Nombre = $e.Nombre; Existia = ($null -ne $actual)
                            Valor = $(if ($actual) { [int[]]$actual } else { $null }); TipoValor = 'Binary' }
        Ok "$Desc ($($e.Nombre)): quitado del arranque"
    }
    foreach ($p in $Procesos) {
        if (Es-Protegido $p) { continue }
        Get-Process -Name $p -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

function Desactivar-Tareas([string]$Ruta, [string]$Nombre, [string]$Desc) {
    $tareas = @(Get-ScheduledTask -TaskPath $Ruta -TaskName $Nombre -ErrorAction SilentlyContinue)
    if ($tareas.Count -eq 0) { Omitido "${Desc}: no existe"; return }
    foreach ($t in $tareas) {
        if (Es-Protegido $t.TaskName) { continue }
        if ($t.State -eq 'Disabled') { Omitido "$Desc ($($t.TaskName)): ya estaba desactivada"; continue }
        Asegurar-PuntoRestauracion
        try {
            Disable-ScheduledTask -TaskPath $t.TaskPath -TaskName $t.TaskName -ErrorAction Stop | Out-Null
            Registrar-Cambio @{ Tipo = 'Tarea'; Ruta = $t.TaskPath; Nombre = $t.TaskName }
            Ok "$Desc ($($t.TaskName)): desactivada"
        } catch {
            Omitido "$Desc ($($t.TaskName)): solo SYSTEM puede tocarla"
        }
    }
}

#endregion

#region Registro y energía

# Devuelve $true si cambió algo.
function Fijar-Registro([string]$Ruta, [string]$Nombre, $Valor, [string]$Tipo = 'DWord') {
    $prop = Get-ItemProperty $Ruta -Name $Nombre -ErrorAction SilentlyContinue
    $existia = $null -ne $prop
    $anterior = if ($existia) { $prop.$Nombre } else { $null }
    if ($existia -and "$anterior" -eq "$Valor") { return $false }
    Asegurar-PuntoRestauracion
    $tipoAnterior = if ($existia) { (Get-Item $Ruta).GetValueKind($Nombre).ToString() } else { $null }
    if (-not (Test-Path $Ruta)) { New-Item $Ruta -Force | Out-Null }
    Set-ItemProperty $Ruta -Name $Nombre -Value $Valor -Type $Tipo
    Registrar-Cambio @{ Tipo = 'Registro'; Ruta = $Ruta; Nombre = $Nombre; Existia = $existia; Valor = $anterior; TipoValor = $tipoAnterior }
    $true
}

# Aplica un grupo de valores de registro y da una sola línea de resultado.
function Ajuste([string]$Desc, [array]$Valores) {
    $cambio = $false
    foreach ($v in $Valores) { if (Fijar-Registro $v[0] $v[1] $v[2]) { $cambio = $true } }
    if ($cambio) { Ok $Desc } else { Omitido "${Desc}: ya estaba aplicado" }
}

function Plan-AltoRendimiento {
    $alto = '8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c'
    $guidRegex = '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}'
    $activo = ([regex]::Match((powercfg /getactivescheme | Out-String), $guidRegex)).Value
    $destino = $alto
    if ((powercfg /list | Out-String) -notmatch $alto) {
        $destino = ([regex]::Match((powercfg -duplicatescheme $alto | Out-String), $guidRegex)).Value
        if (-not $destino) { Fallo 'Plan Alto rendimiento: no existe en este Windows'; return }
    }
    if ($activo -eq $destino) { Omitido 'Plan de energía: ya estaba en Alto rendimiento'; return }
    Asegurar-PuntoRestauracion
    powercfg /setactive $destino
    Registrar-Cambio @{ Tipo = 'Energia'; Guid = $activo }
    Ok 'Plan de energía -> Alto rendimiento'
}

#endregion

#region Acciones del menú

function Mostrar-Diagnostico {
    Escribir "`n=== Diagnóstico ===" 'Titulo'
    $os = Get-CimInstance Win32_OperatingSystem
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $nucleos = [Environment]::ProcessorCount
    $usada = ($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / 1MB
    Write-Host ('CPU: {0} | carga {1}%' -f $cpu.Name.Trim(), $cpu.LoadPercentage)
    Write-Host ('RAM: {0:N1} de {1:N1} GB usados | procesos: {2} | encendido hace {3:%d}d {3:hh}h {3:mm}m' -f `
        $usada, ($os.TotalVisibleMemorySize / 1MB), (Get-Process).Count, ((Get-Date) - $os.LastBootUpTime))

    Write-Host "`nTop 15 procesos por CPU (muestra de 2 s):" -ForegroundColor Cyan
    $antes = @{}
    Get-Process | ForEach-Object { $antes[$_.Id] = $_.CPU }
    Start-Sleep -Seconds 2
    Get-Process | Where-Object { $antes.ContainsKey($_.Id) -and $null -ne $_.CPU } | ForEach-Object {
        [pscustomobject]@{
            Proceso   = $_.ProcessName
            PID       = $_.Id
            'CPU %'   = [math]::Round(($_.CPU - $antes[$_.Id]) / 2 / $nucleos * 100, 1)
            'RAM MB'  = [math]::Round($_.WorkingSet64 / 1MB)
            Protegido = $(if (Es-Protegido $_.ProcessName) { 'sí' } else { '' })
        }
    } | Sort-Object 'CPU %', 'RAM MB' -Descending | Select-Object -First 15 | Format-Table -AutoSize | Out-Host

    Write-Host 'Servicios del catálogo que aún se pueden aligerar:' -ForegroundColor Cyan
    $pendientes = foreach ($s in $ServiciosWindows) {
        $svc = Get-Service -Name $s.Nombre -ErrorAction SilentlyContinue
        if ($svc -and ($svc.Status -eq 'Running' -or $svc.StartType -eq 'Automatic')) {
            '  {0,-16} {1,-8} {2,-10} {3}' -f $s.Nombre, $svc.Status, $svc.StartType, $s.Desc
        }
    }
    if ($pendientes) { $pendientes | Out-Host } else { Write-Host '  ninguno' }

    Write-Host "`nProgramas en el arranque:" -ForegroundColor Cyan
    Get-CimInstance Win32_StartupCommand | Sort-Object Name -Unique | ForEach-Object {
        $marca = if (Es-Protegido "$($_.Name) $($_.Command)") { '(protegido)' } else { '' }
        '  {0,-16} {1}' -f $_.Name, $marca
    } | Out-Host

    $wu = Get-Service wuauserv -ErrorAction SilentlyContinue
    $pol = (Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' -ErrorAction SilentlyContinue).NoAutoUpdate
    Write-Host ("`nWindows Update: servicio {0}/{1}, actualizaciones automáticas {2}" -f $wu.Status, $wu.StartType,
        $(if ($pol -eq 1) { 'DESACTIVADAS' } else { 'activadas' })) -ForegroundColor Cyan
    $temp = (Get-ChildItem $env:TEMP -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    Write-Host ('Temporales del usuario: {0:N0} MB' -f ($temp / 1MB)) -ForegroundColor Cyan
}

function Aligerar-Servicios {
    Escribir "`n=== Servicios de Windows ===" 'Titulo'
    $elegidos = Elegir $ServiciosWindows { param($s) '{0,-15} {1,-13} {2}' -f $s.Nombre, $s.Modo, $s.Desc }
    foreach ($s in $elegidos) { Configurar-Servicio $s.Nombre $s.Modo $s.Desc ([bool]$s.Blando) }
}

function Aligerar-Arranque {
    Escribir "`n=== Programas de terceros en el arranque ===" 'Titulo'
    Escribir 'Se conservan: Google Drive, Mem Reduct, Hotspot Shield, la app de UrbanVPN y Seguridad de Windows.'
    # El servicio de UrbanVPN corre siempre aunque la VPN no se use; la app sigue en el arranque.
    Configurar-Servicio 'UrbanVPN-Service' 'Manual' 'Servicio de UrbanVPN'
    Desactivar-Inicio 'RazerAppEngine*' 'Razer Synapse' @('RazerAppEngine')
    Configurar-Servicio 'Razer Game Manager Service 3' 'Manual' 'Razer Game Manager'
    Desactivar-Inicio '*AnyDesk*' 'AnyDesk'
    Configurar-Servicio 'AnyDesk' 'Manual' 'AnyDesk'
    Desactivar-Tareas '\' 'ASC_PerformanceMonitor' 'Tarea huérfana de Advanced SystemCare'
    if (Preguntar '¿Pasar también el servicio de Windscribe a Manual?' $false) {
        Configurar-Servicio 'WindscribeService' 'Manual' 'Windscribe'
    } else {
        Omitido 'Windscribe: se deja como está'
    }
}

function Ajustes-Rendimiento {
    Escribir "`n=== Ajustes de rendimiento ===" 'Titulo'
    Plan-AltoRendimiento
    Ajuste 'Transparencia de Windows desactivada' @(
        , @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize', 'EnableTransparency', 0))
    Ajuste 'Apps de la Tienda en segundo plano desactivadas' @(
        , @('HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications', 'GlobalUserDisabled', 1))
    $cdm = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
    Ajuste 'Sugerencias, consejos y apps promocionadas desactivadas' @(
        @($cdm, 'SystemPaneSuggestionsEnabled', 0), @($cdm, 'SilentInstalledAppsEnabled', 0),
        @($cdm, 'SoftLandingEnabled', 0), @($cdm, 'SubscribedContent-338388Enabled', 0),
        @($cdm, 'SubscribedContent-338389Enabled', 0), @($cdm, 'SubscribedContent-353694Enabled', 0),
        @($cdm, 'SubscribedContent-353696Enabled', 0), @($cdm, 'SubscribedContent-310093Enabled', 0))
    Ajuste 'Grabación de juegos (Game DVR / Game Bar) desactivada' @(
        @('HKCU:\System\GameConfigStore', 'GameDVR_Enabled', 0),
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR', 'AppCaptureEnabled', 0))
}

# Borra solo lo que lleva más de 2 días sin tocarse, para no romper lo que esté en uso
# (otras sesiones o renders pueden tener archivos de trabajo en %TEMP%).
function Vaciar-Carpeta([string]$Ruta, [int]$Dias = 2) {
    if (-not (Test-Path $Ruta)) { return 0 }
    $limite = (Get-Date).AddDays(-$Dias)
    $liberado = 0L
    Get-ChildItem $Ruta -Recurse -Force -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $limite } | ForEach-Object {
            $tam = $_.Length
            Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
            if (-not (Test-Path -LiteralPath $_.FullName)) { $liberado += $tam }
        }
    Get-ChildItem $Ruta -Recurse -Force -Directory -ErrorAction SilentlyContinue |
        Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { $_.LastWriteTime -lt $limite -and -not (Get-ChildItem $_.FullName -Force -ErrorAction SilentlyContinue) } |
        Remove-Item -Force -ErrorAction SilentlyContinue
    $liberado
}

function Limpiar {
    Escribir "`n=== Limpieza ===" 'Titulo'
    foreach ($ruta in @($env:TEMP, "$env:WINDIR\Temp")) {
        $mb = (Vaciar-Carpeta $ruta) / 1MB
        if ($mb -ge 1) { Ok ('{0}: {1:N0} MB liberados' -f $ruta, $mb) } else { Omitido "${ruta}: nada que limpiar" }
    }
    $wu = Get-Service wuauserv -ErrorAction SilentlyContinue
    $estaba = $wu -and $wu.Status -eq 'Running'
    if ($estaba) { Stop-Service wuauserv -Force -ErrorAction SilentlyContinue }
    $mb = (Vaciar-Carpeta "$env:WINDIR\SoftwareDistribution\Download" 0) / 1MB
    if ($estaba) { Start-Service wuauserv -ErrorAction SilentlyContinue }
    if ($mb -ge 1) { Ok ('Caché de Windows Update: {0:N0} MB liberados' -f $mb) } else { Omitido 'Caché de Windows Update: vacía' }
    if (Preguntar '¿Vaciar también la papelera de reciclaje?' $false) {
        Clear-RecycleBin -Force -ErrorAction SilentlyContinue
        Ok 'Papelera vaciada'
    }
}

function Desactivar-WindowsUpdate {
    Escribir "`n=== Windows Update ===" 'Titulo'
    $wu = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'
    Ajuste 'Directivas: no descargar ni instalar actualizaciones' @(
        @("$wu\AU", 'NoAutoUpdate', 1), @($wu, 'DisableOSUpgrade', 1), @($wu, 'SetDisableUXWUAccess', 1))
    foreach ($s in $ServiciosUpdate) { Configurar-Servicio $s.Nombre 'Deshabilitado' $s.Desc ([bool]$s.Blando) }
    Desactivar-Tareas '\Microsoft\Windows\UpdateOrchestrator\' '*' 'Tarea de actualización'
    Desactivar-Tareas '\Microsoft\Windows\WindowsUpdate\' '*' 'Tarea de Windows Update'
    Escribir '  Ojo: las firmas de Defender ya no se actualizan solas. Usa la opción 9 de vez en cuando.' 'Omitido'
    Desactivar-EdgeUpdate
}

function Desactivar-EdgeUpdate {
    Escribir "`n=== Actualización de Edge ===" 'Titulo'
    $pol = 'HKLM:\SOFTWARE\Policies\Microsoft\EdgeUpdate'
    Ajuste 'Directivas: Edge no se actualiza solo' @(
        @($pol, 'UpdateDefault', 0), @($pol, 'AutoUpdateCheckPeriodMinutes', 0),
        @($pol, 'Update{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}', 0))
    Configurar-Servicio 'edgeupdate' 'Deshabilitado' 'Actualizador de Edge'
    Configurar-Servicio 'edgeupdatem' 'Deshabilitado' 'Actualizador de Edge (bajo demanda)'
    Desactivar-Tareas '\' 'MicrosoftEdgeUpdate*' 'Tarea de actualización de Edge'
    Get-Process MicrosoftEdgeUpdate -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Actualizar-FirmasDefender {
    Escribir "`n=== Firmas de Defender ===" 'Titulo'
    Escribir 'Descargando directamente de Microsoft (sin Windows Update)...'
    try {
        Update-MpSignature -UpdateSource MMPC -ErrorAction Stop
        Ok ('Firmas actualizadas: {0}' -f (Get-MpComputerStatus).AntivirusSignatureVersion)
    } catch {
        Fallo "No se pudieron actualizar: $($_.Exception.Message)"
    }
}

function Deshacer {
    Escribir "`n=== Deshacer ===" 'Titulo'
    $archivos = @(Get-ChildItem $Carpeta -Filter 'respaldo-*.json' | Where-Object { $_.FullName -ne $ArchivoRespaldo } |
        Sort-Object LastWriteTime -Descending)
    if ($archivos.Count -eq 0) { Omitido 'No hay respaldos de sesiones anteriores'; return }
    for ($i = 0; $i -lt $archivos.Count; $i++) {
        $n = @((Get-Content $archivos[$i].FullName -Raw -Encoding UTF8 | ConvertFrom-Json).Cambios).Count
        Write-Host ('{0,3}. {1}  ({2} cambios)' -f ($i + 1), $archivos[$i].Name, $n)
    }
    $r = Read-Host 'Número del respaldo a deshacer (Enter = el más reciente, C = cancelar)'
    if ($r -match '^[cC]$') { return }
    $indice = if ($r -match '^\d+$') { [int]$r - 1 } else { 0 }
    if ($indice -lt 0 -or $indice -ge $archivos.Count) { Omitido 'Número fuera de rango'; return }
    $archivo = $archivos[$indice]
    $cambios = @((Get-Content $archivo.FullName -Raw -Encoding UTF8 | ConvertFrom-Json).Cambios)
    [array]::Reverse($cambios)
    foreach ($c in $cambios) {
        switch ($c.Tipo) {
            'Servicio' {
                if (Escribir-InicioServicio $c.Nombre $c.Start $c.Delayed) {
                    if ($c.Start -eq 2) { Start-Service -Name $c.Nombre -ErrorAction SilentlyContinue }
                    Ok "Servicio $($c.Nombre) restaurado"
                } else { Fallo "Servicio $($c.Nombre): no se pudo restaurar" }
            }
            'Registro' {
                try {
                    if ($c.Existia) {
                        $valor = if ($c.TipoValor -eq 'Binary') { [byte[]]@($c.Valor) } else { $c.Valor }
                        Set-ItemProperty $c.Ruta -Name $c.Nombre -Value $valor -Type $c.TipoValor -ErrorAction Stop
                    } else {
                        Remove-ItemProperty $c.Ruta -Name $c.Nombre -ErrorAction Stop
                    }
                    Ok "Registro $($c.Nombre) restaurado"
                } catch { Fallo "Registro $($c.Nombre): $($_.Exception.Message)" }
            }
            'Tarea' {
                try {
                    Enable-ScheduledTask -TaskPath $c.Ruta -TaskName $c.Nombre -ErrorAction Stop | Out-Null
                    Ok "Tarea $($c.Nombre) reactivada"
                } catch { Fallo "Tarea $($c.Nombre): $($_.Exception.Message)" }
            }
            'Energia' { powercfg /setactive $c.Guid; Ok 'Plan de energía restaurado' }
        }
    }
    Rename-Item $archivo.FullName ($archivo.BaseName + '.deshecho.json')
    Escribir 'Listo. Reinicia para que todo vuelva a su estado anterior.' 'Titulo'
}

function Mostrar-Resumen {
    Escribir ("`nResumen: {0} aplicados, {1} sin cambios, {2} errores. Log: {3}" -f `
        $script:Resumen.Aplicado, $script:Resumen.Omitido, $script:Resumen.Error, $ArchivoLog) 'Titulo'
    if ($script:Cambios.Count -gt 0) { Escribir "Respaldo para deshacer: $ArchivoRespaldo" 'Titulo' }
    $script:Resumen = @{ Aplicado = 0; Omitido = 0; Error = 0 }
}

function Aplicar-Todo {
    Aligerar-Servicios
    Aligerar-Arranque
    Ajustes-Rendimiento
    Limpiar
    Desactivar-WindowsUpdate
}

#endregion

if ($Diagnostico) { Mostrar-Diagnostico; return }

$esAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $esAdmin) {
    $argumentos = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($Todo) { $argumentos += '-Todo' }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $argumentos
    return
}

$Host.UI.RawUI.WindowTitle = 'OptimizadorPC'
Escribir "`n===== OptimizadorPC  $(Get-Date -Format 'yyyy-MM-dd HH:mm') =====" 'Titulo'

if ($Todo) {
    Aplicar-Todo
    Mostrar-Resumen
    Escribir 'Reinicia el PC para que todos los cambios surtan efecto.' 'Titulo'
    Read-Host 'Enter para cerrar' | Out-Null
    return
}

while ($true) {
    Write-Host @'

  1  Diagnóstico
  2  Aligerar servicios de Windows
  3  Programas de terceros en el arranque
  4  Ajustes de rendimiento
  5  Limpieza de temporales
  6  Desactivar Windows Update y la actualización de Edge
  7  Deshacer cambios anteriores
  8  Aplicar todo lo recomendado (2 a 6)
  9  Actualizar firmas de Defender
  0  Salir
'@ -ForegroundColor White
    switch (Read-Host 'Elige una opción') {
        '1' { Mostrar-Diagnostico }
        '2' { Aligerar-Servicios; Mostrar-Resumen }
        '3' { Aligerar-Arranque; Mostrar-Resumen }
        '4' { Ajustes-Rendimiento; Mostrar-Resumen }
        '5' { Limpiar; Mostrar-Resumen }
        '6' { Desactivar-WindowsUpdate; Mostrar-Resumen }
        '7' { Deshacer; Mostrar-Resumen }
        '8' { Aplicar-Todo; Mostrar-Resumen; Escribir 'Reinicia el PC para que todos los cambios surtan efecto.' 'Titulo' }
        '9' { Actualizar-FirmasDefender; Mostrar-Resumen }
        '0' { return }
    }
}
