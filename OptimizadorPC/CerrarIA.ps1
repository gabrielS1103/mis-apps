<#
CerrarIA: cierra Claude, ChatGPT/Codex y VS Code junto con sus procesos de fondo, y detiene sus
servicios hasta el próximo reinicio. No cambia cómo arrancan: al reiniciar todo vuelve a ser como antes.
#>
param(
    [switch]$SinCode,  # deja VS Code (y Claude Code dentro de él) abierto
    [switch]$Prueba,   # solo lista lo que cerraría, sin cerrar nada
    [switch]$Ya        # no pide confirmación
)

$Rutas = @('\WindowsApps\Claude_', '\AnthropicClaude\', '\WindowsApps\OpenAI.', '\Programs\ChatGPT\')
$Nombres = @('claude', 'ChatGPT', 'codex', 'cowork-svc', 'codex-windows-sandbox-service')
if (-not $SinCode) {
    $Rutas += '\Microsoft VS Code\', '\.vscode\extensions\'
    $Nombres += 'Code'
}
# Solo se detienen: su tipo de inicio no se toca, así que vuelven solos al reiniciar.
$Servicios = 'CoworkVMService', 'CodexSandboxService.OpenAI.Codex'

function Es-Objetivo($p) {
    if ($p.ProcessId -eq $PID) { return $false }
    $ruta = "$($p.ExecutablePath)"
    if ($SinCode -and ($ruta -like '*\Microsoft VS Code\*' -or $ruta -like '*\.vscode\*')) { return $false }
    foreach ($r in $Rutas) { if ($ruta -like "*$r*") { return $true } }
    $Nombres -contains [IO.Path]::GetFileNameWithoutExtension($p.Name)
}

function Buscar-Procesos { @(Get-CimInstance Win32_Process | Where-Object { Es-Objetivo $_ }) }

$procesos = Buscar-Procesos
$activos = @(Get-Service -Name $Servicios -ErrorAction SilentlyContinue | Where-Object Status -eq 'Running')

if ($Prueba) {
    Write-Host 'Se cerrarían:' -ForegroundColor Cyan
    $procesos | Group-Object Name | ForEach-Object {
        '  {0,-36} x{1,-3} {2,6:N0} MB' -f $_.Name, $_.Count, (($_.Group | Measure-Object WorkingSetSize -Sum).Sum / 1MB)
    }
    $activos | ForEach-Object { '  servicio {0} (vuelve al reiniciar)' -f $_.Name }
    return
}

if ($procesos.Count -eq 0 -and $activos.Count -eq 0) {
    Write-Host 'No hay nada de Claude, ChatGPT ni VS Code abierto.' -ForegroundColor Green
    Start-Sleep -Seconds 2
    return
}

if (-not $Ya) {
    $que = if ($SinCode) { 'Claude y ChatGPT' } else { 'Claude, ChatGPT y VS Code (también Claude Code)' }
    Write-Host "Se van a cerrar $que con todos sus procesos de fondo." -ForegroundColor Yellow
    if ((Read-Host '¿Seguir? (S/n)') -match '^[nN]') { return }
}

$esAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $esAdmin -and $activos.Count -gt 0) {
    # Los servicios necesitan administrador; si se rechaza el permiso, se cierra lo demás igual.
    $argumentos = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Ya')
    if ($SinCode) { $argumentos += '-SinCode' }
    try {
        Start-Process powershell.exe -Verb RunAs -ArgumentList $argumentos -ErrorAction Stop
        return
    } catch {
        Write-Host 'Sin permiso de administrador: sus servicios seguirán hasta reiniciar.' -ForegroundColor Yellow
    }
}

$ramAntes = ($procesos | Measure-Object WorkingSetSize -Sum).Sum

if ($esAdmin) {
    foreach ($s in $activos) {
        try {
            Stop-Service -Name $s.Name -Force -ErrorAction Stop -WarningAction SilentlyContinue
            Write-Host "  [OK] Servicio $($s.Name) detenido" -ForegroundColor Green
        } catch {
            Write-Host "  [!!] Servicio $($s.Name): $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

# Primero se piden cerrar las ventanas, como al pulsar la X: VS Code guarda su sesión y los
# archivos sin guardar. Lo que siga vivo a los 10 s se termina a la fuerza.
Get-Process -Id ($procesos.ProcessId) -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne 0 } | ForEach-Object { [void]$_.CloseMainWindow() }
for ($i = 0; $i -lt 20; $i++) {
    $conVentana = Get-Process -Id ($procesos.ProcessId) -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }
    if (-not $conVentana) { break }
    Start-Sleep -Milliseconds 500
}

$restantes = Buscar-Procesos
foreach ($p in $restantes) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
Start-Sleep -Milliseconds 800

$quedan = Buscar-Procesos
Write-Host ('  [OK] {0} procesos cerrados, {1:N0} MB de RAM liberados' -f ($procesos.Count - $quedan.Count), ($ramAntes / 1MB)) -ForegroundColor Green
if ($quedan) { Write-Host ("  [!!] No se pudieron cerrar: {0}" -f (($quedan.Name | Sort-Object -Unique) -join ', ')) -ForegroundColor Red }
Write-Host 'Al reiniciar, todo vuelve a funcionar como siempre.' -ForegroundColor Cyan
Start-Sleep -Seconds 4
