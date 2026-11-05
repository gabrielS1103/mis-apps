<#
ActualizarReVanced: construye YouTube y YT Music ReVanced con los parches oficiales, verificando
cada pieza, y los instala en el teléfono por adb.

  1. Descarga los parches oficiales (api.revanced.app) y comprueba su firma PGP contra la clave
     de ReVanced fijada en este script.
  2. Elige la versión de cada app que soportan esos parches, descarga el APK original y comprueba
     que lo firmó Google con el mismo certificado que la app del Play Store.
  3. Aplica todos los parches salvo los de descargas y firma con tu clave propia.
  4. Instala encima de lo que hay en el teléfono (sin borrar ajustes) y actualiza la carpeta
     "ReVanced para compartir" del Escritorio.

Si algo no cuadra (una firma distinta, un parche que falla) no instala nada.
  -Forzar       vuelve a construir aunque ya esté al día
  -SinInstalar  solo construye los APK; no toca el teléfono
#>
param([switch]$Forzar, [switch]$SinInstalar)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

# --- Lo que se considera auténtico (si cambia, el script se detiene en lugar de confiar) ---
$ClaveReVanced = 'A7835DFCACA14BDA3CD6EDD3633C6920FBE6A2FF'   # contact@revanced.app
$Apps = @(
    @{ Nombre = 'YouTube'; Paquete = 'com.google.android.youtube'; Parcheado = 'app.revanced.android.youtube'
       SlugApp = 'youtube'; SlugRel = 'youtube'
       CertGoogle = '3d7a1223019aa39d9ea0e3436ab7c0896bfb4fb679f4de5fe7c23f326c8f994a' },
    @{ Nombre = 'YT Music'; Paquete = 'com.google.android.apps.youtube.music'; Parcheado = 'app.revanced.android.apps.youtube.music'
       SlugApp = 'youtube-music'; SlugRel = 'youtube-music'
       CertGoogle = 'a2a1ad7ba7f41dfca4514e2afeb90691719af6d0fdbed4b09bbf0ed897701ceb' }
)
$CertGmsCore = 'd732e4055c97f8d7a43170a6dc737dc71b6f3e0410940e1b3706c6ade1ff3488'
$ExcluirParches = '(?i)download'   # nada que descargue contenido
$ClientePorDefecto = 'VISIONOS'     # cliente de "Falsificar transmisiones"; '' para dejar el de ReVanced
$CalidadWifiYouTube = 1080          # calidad de video por defecto con Wi-Fi en YouTube; 0 para automática

$Base = Join-Path $env:LOCALAPPDATA 'ReVancedPropio'
$Herr = Join-Path $Base 'herramientas'
$Originales = Join-Path $Base 'originales'
$Manual = Join-Path $Base 'manual'
$Tmp = Join-Path $Base 'tmp'
$Clave = Join-Path $Base 'revanced-propio.keystore'
$EstadoArchivo = Join-Path $Base 'estado.json'
$Compartir = Join-Path ([Environment]::GetFolderPath('Desktop')) 'ReVanced para compartir'
$UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36'
$Galletas = Join-Path $Tmp 'galletas.txt'

function Escribir($texto, $tipo = 'Normal') {
    $color = @{ Normal = 'Gray'; Ok = 'Green'; Aviso = 'Yellow'; Error = 'Red'; Titulo = 'Cyan' }[$tipo]
    Write-Host $texto -ForegroundColor $color
}
function Terminar($codigo) {
    if (-not $env:SIN_PAUSA) { [void](Read-Host "`nPulsa Enter para cerrar") }
    exit $codigo
}
function Fallar($texto) { Escribir "`n[!!] $texto" 'Error'; Escribir 'No se instaló nada nuevo.' 'Aviso'; Terminar 1 }

# Ejecuta un programa y devuelve su salida (stdout + stderr) como texto.
function Ejecutar($exe, [string[]]$argumentos) {
    $salida = & $exe @argumentos 2>&1 | ForEach-Object { "$_" }
    [pscustomobject]@{ Codigo = $LASTEXITCODE; Texto = ($salida -join "`n") }
}
function Bajar($url, $destino, $referer) {
    $a = @('-sSL', '--fail', '-m', '900', '-A', $UA, '-c', $Galletas, '-b', $Galletas, '-o', $destino)
    if ($referer) { $a += @('-e', $referer) }
    $r = Ejecutar 'curl.exe' ($a + $url)
    ($r.Codigo -eq 0) -and (Test-Path $destino)
}
function Leer($url, $referer) {
    $f = Join-Path $Tmp 'pagina.html'
    Remove-Item $f -ErrorAction SilentlyContinue
    if (Bajar $url $f $referer) { [IO.File]::ReadAllText($f) } else { $null }
}
function RutaMsys($p) { '/' + $p.Substring(0, 1).ToLower() + ($p.Substring(2) -replace '\\', '/') }

# --- Herramientas ---------------------------------------------------------------------------
Escribir "=== Actualizar ReVanced ===`n" 'Titulo'
$jdk = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Android\jdk') -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending | Select-Object -First 1
$sdk = Join-Path $env:LOCALAPPDATA 'Android\Sdk'
$bt = Get-ChildItem (Join-Path $sdk 'build-tools') -Directory -ErrorAction SilentlyContinue |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$gpg = Join-Path $env:ProgramFiles 'Git\usr\bin\gpg.exe'
if (-not $jdk) { Fallar "No encuentro Java en $env:LOCALAPPDATA\Android\jdk." }
if (-not $bt) { Fallar "No encuentro las build-tools del SDK de Android en $sdk." }
if (-not (Test-Path $gpg)) { Fallar "No encuentro gpg ($gpg). Viene con Git para Windows." }
$env:JAVA_HOME = $jdk.FullName
$env:PATH = (Join-Path $jdk.FullName 'bin') + ';' + $env:PATH
$java = Join-Path $jdk.FullName 'bin\java.exe'
$apksigner = Join-Path $bt.FullName 'apksigner.bat'
$aapt2 = Join-Path $bt.FullName 'aapt2.exe'
$adb = Join-Path $sdk 'platform-tools\adb.exe'
foreach ($d in $Base, $Herr, $Originales, $Manual, $Tmp) { New-Item -ItemType Directory -Force $d | Out-Null }
if (-not (Test-Path $Clave)) { Escribir "[!] No existe tu clave de firma; se creará una nueva en $Clave." 'Aviso' }

$Estado = @{ parches = ''; cli = ''; gmscore = ''; modificado = ''; apps = @{}; instalado = @{} }
if (Test-Path $EstadoArchivo) {
    $j = Get-Content $EstadoArchivo -Raw | ConvertFrom-Json
    foreach ($k in 'parches', 'cli', 'gmscore', 'modificado') { if ($j.$k) { $Estado[$k] = $j.$k } }
    foreach ($k in 'apps', 'instalado') { if ($j.$k) { $j.$k.PSObject.Properties | ForEach-Object { $Estado[$k][$_.Name] = $_.Value } } }
}
function Guardar-Estado { $Estado | ConvertTo-Json -Depth 4 | Set-Content $EstadoArchivo -Encoding UTF8 }

# Comprueba que un APK sea el paquete y la versión esperados y que lo firmó el certificado indicado.
function Verificar-Apk($apk, $paquete, $version, $cert) {
    $b = (Ejecutar $aapt2 @('dump', 'badging', $apk)).Texto
    if ($b -cnotmatch "package: name='([^']+)'.* versionName='([^']+)'") { return 'no es un APK válido' }
    if ($Matches[1] -ne $paquete) { return "es otro paquete ($($Matches[1]))" }
    if ($version -and $Matches[2] -ne $version) { return "es la versión $($Matches[2]), no la $version" }
    $s = Ejecutar $apksigner @('verify', '--print-certs', $apk)
    if ($s.Codigo -ne 0) { return 'su firma está dañada' }
    if ($s.Texto -notmatch "SHA-256 digest: $cert") { return 'NO lo firmó el certificado esperado' }
    $null
}

# --- 1. Parches oficiales -------------------------------------------------------------------
Escribir '1) Parches oficiales de ReVanced' 'Titulo'
$rvp = Join-Path $Herr 'patches.rvp'
$info = $null
$r = Ejecutar 'curl.exe' @('-sS', '--fail', '-m', '60', '-A', $UA, 'https://api.revanced.app/v5/patches')
if ($r.Codigo -eq 0) { try { $info = $r.Texto | ConvertFrom-Json } catch { } }
if (-not $info.version) {
    if (-not (Test-Path $rvp)) { Fallar 'No pude consultar api.revanced.app y no hay parches guardados.' }
    Escribir "   [!] No pude consultar api.revanced.app; uso los parches guardados ($($Estado.parches))." 'Aviso'
} elseif ($info.version -ne $Estado.parches -or -not (Test-Path $rvp)) {
    Escribir "   Nueva versión: $($info.version) (tenías $($Estado.parches)). Descargando..."
    $nuevo = Join-Path $Tmp 'patches.rvp'; $firma = "$nuevo.asc"
    if (-not (Bajar $info.download_url $nuevo) -or -not (Bajar $info.signature_download_url $firma)) {
        Fallar 'No se pudieron descargar los parches.'
    }
    # Clave de ReVanced: se acepta solo si su huella es la fijada arriba.
    $dirGpg = Join-Path $Tmp 'gnupg'; Remove-Item $dirGpg -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory $dirGpg | Out-Null
    $claveAsc = Join-Path $Herr 'revanced-clave.asc'
    if (-not (Test-Path $claveAsc)) { [void](Bajar 'https://api.revanced.app/keys' $claveAsc) }
    $h = RutaMsys $dirGpg
    [void](Ejecutar $gpg @('--homedir', $h, '--batch', '--import', (RutaMsys $claveAsc)))
    $v = Ejecutar $gpg @('--homedir', $h, '--batch', '--status-fd', '1', '--verify', (RutaMsys $firma), (RutaMsys $nuevo))
    $valida = [regex]::Match($v.Texto, '\[GNUPG:\] VALIDSIG (?:\S+ ){9}(\S+)')
    if (-not $valida.Success -or $valida.Groups[1].Value -ne $ClaveReVanced) {
        Fallar "La firma de los parches no es válida o no es de ReVanced. Parches descartados.`n$($v.Texto)"
    }
    Move-Item $nuevo $rvp -Force; Move-Item $firma "$rvp.asc" -Force
    $Estado.parches = $info.version
    Escribir "   [OK] Parches $($info.version) con firma válida de ReVanced" 'Ok'
} else {
    Escribir "   [OK] Ya tienes la última versión ($($Estado.parches))" 'Ok'
}

# --- 2. ReVanced CLI y MicroG (GitHub oficial de ReVanced) ------------------------------------
Escribir "`n2) Herramienta de parcheo y MicroG" 'Titulo'
function Ultima-Version($repo) {
    $r = Ejecutar 'curl.exe' @('-sS', '--fail', '-m', '60', '-A', $UA, "https://api.github.com/repos/$repo/releases/latest")
    if ($r.Codigo -ne 0) { return $null }
    try { $r.Texto | ConvertFrom-Json } catch { $null }
}
$cli = Join-Path $Herr 'revanced-cli.jar'
$rel = Ultima-Version 'ReVanced/revanced-cli'
if ($rel -and ($rel.tag_name -ne $Estado.cli -or -not (Test-Path $cli))) {
    $asset = $rel.assets | Where-Object { $_.name -like '*-all.jar' } | Select-Object -First 1
    if ($asset -and (Bajar $asset.browser_download_url (Join-Path $Tmp 'cli.jar'))) {
        Move-Item (Join-Path $Tmp 'cli.jar') $cli -Force; $Estado.cli = $rel.tag_name
        Escribir "   [OK] ReVanced CLI $($rel.tag_name) descargado" 'Ok'
    }
}
if (-not (Test-Path $cli)) { Fallar 'No hay ReVanced CLI.' }
if (-not $rel -or $rel.tag_name -eq $Estado.cli) { Escribir "   [OK] ReVanced CLI $($Estado.cli)" 'Ok' }

$gms = Get-ChildItem $Base -Filter 'GmsCore-oficial-*.apk' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$rel = Ultima-Version 'ReVanced/GmsCore'
if ($rel -and ($rel.tag_name -ne $Estado.gmscore -or -not $gms)) {
    $asset = $rel.assets | Where-Object { $_.name -like '*-signed.apk' -and $_.name -notlike '*-hw-*' } | Select-Object -First 1
    $destino = Join-Path $Base "GmsCore-oficial-$($rel.tag_name.TrimStart('v')).apk"
    if ($asset -and (Bajar $asset.browser_download_url $destino)) {
        $mal = Verificar-Apk $destino 'app.revanced.android.gms' $null $CertGmsCore
        if ($mal) { Remove-Item $destino; Escribir "   [!!] El MicroG descargado $mal; se descarta." 'Error' }
        else {
            if ($gms -and $gms.FullName -ne $destino) { Remove-Item $gms.FullName }
            $gms = Get-Item $destino; $Estado.gmscore = $rel.tag_name
            Escribir "   [OK] MicroG $($rel.tag_name) con la firma oficial de ReVanced" 'Ok'
        }
    }
} elseif ($gms) { Escribir "   [OK] MicroG $($Estado.gmscore)" 'Ok' }

# --- 2b. Únicas modificaciones propias: valores por defecto de dos ajustes de ReVanced -------
# - Cliente de "Falsificar transmisiones" (YouTube y YT Music): con el que trae ReVanced (Android
#   Reel sin cuenta) la música se corta al minuto y los videos se quedan pegados tras saltar un
#   patrocinio.
# - Calidad de video por defecto con Wi-Fi en YouTube.
# Solo cambia el valor inicial de esos ajustes (se pueden cambiar en la app); el resto del código
# de los parches queda intacto. Si el código de ReVanced cambia, se usan los parches sin tocar.
$rvpUsar = $rvp; $Marca = ''
$Smali = Join-Path $Herr 'smali'
function Modificar-Parches {
    $pedido = @()
    if ($ClientePorDefecto) { $pedido += $ClientePorDefecto }
    if ($CalidadWifiYouTube) { $pedido += "$($CalidadWifiYouTube)p" }
    if (-not $pedido) { return }
    $id = $pedido -join '|'
    $mod = Join-Path $Herr 'patches-propio.rvp'
    if ((Test-Path $mod) -and $Estado.modificado -eq "$($Estado.parches)|$id") {
        $script:rvpUsar = $mod; $script:Marca = "|$id"
        Escribir "   [OK] Ya aplicado: $($pedido -join ', ')" 'Ok'; return
    }
    $jars = @(Get-ChildItem $Smali -Filter '*.jar' -ErrorAction SilentlyContinue)
    if ($jars.Count -lt 12) { Escribir '   [!] No están las herramientas smali; se usan los parches sin cambios.' 'Aviso'; return }
    $cp = ($jars.FullName) -join ';'
    $dir = Join-Path $Tmp 'smali'; Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $dir | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($rvp)
    try {
        foreach ($n in 'shared', 'music', 'youtube') {
            $e = $zip.GetEntry("extensions/$n.rve")
            if (-not $e) { Escribir "   [!] Los parches ya no traen extensions/$n.rve; se usan sin cambios." 'Aviso'; return }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $dir "$n.rve"), $true)
        }
    } finally { $zip.Dispose() }
    # El cliente elegido tiene que existir en el código de ReVanced; si no, la app fallaría.
    $shared = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Join-Path $dir 'shared.rve')))
    if ($ClientePorDefecto -and $shared -notmatch "\b$ClientePorDefecto\b") {
        Escribir "   [!] ReVanced ya no tiene el cliente $ClientePorDefecto; se usan los parches sin cambios." 'Aviso'; return
    }
    $cambios = @()
    if ($ClientePorDefecto) {
        foreach ($n in 'music', 'youtube') {
            $cambios += @{ Ext = $n; Que = "cliente ($n)"; Valor = $ClientePorDefecto
                Patron = '(?s)(sget-object v\d+, Lapp/revanced/extension/shared/spoof/ClientType;->)([A-Z0-9_]+)(:Lapp/revanced/extension/shared/spoof/ClientType;(?:(?!sput-object).)*?const-string v\d+, "revanced_spoof_video_streams_client_type")'
                Nuevo = "`${1}$ClientePorDefecto`${3}" }
        }
    }
    if ($CalidadWifiYouTube) {
        $hex = '0x{0:x}' -f [int]$CalidadWifiYouTube
        $cambios += @{ Ext = 'youtube'; Que = 'calidad Wi-Fi (youtube)'; Valor = "$($CalidadWifiYouTube)p"
            Patron = '(?s)const(?:/4|/16)? (v\d+), (-?0x[0-9a-f]+)(\s+invoke-static \{\1\}, Ljava/lang/Integer;->valueOf\(I\)Ljava/lang/Integer;\s+move-result-object \1\s+const-string v\d+, "revanced_video_quality_default_wifi")'
            Nuevo = "const/16 `${1}, $hex`${3}" }
    }
    $nuevos = @()
    foreach ($n in 'music', 'youtube') {
        $mios = @($cambios | Where-Object { $_.Ext -eq $n })
        if (-not $mios) { continue }
        $rve = Join-Path $dir "$n.rve"; $src = Join-Path $dir $n
        $r = Ejecutar $java @('-cp', $cp, 'com.android.tools.smali.baksmali.Main', 'd', $rve, '-o', $src)
        $ajustes = Get-ChildItem $src -Recurse -Filter 'Settings.smali' -ErrorAction SilentlyContinue | Where-Object {
            (Get-Content $_.FullName -Raw) -match 'revanced_spoof_video_streams_client_type' } | Select-Object -First 1
        if ($r.Codigo -ne 0 -or -not $ajustes) { Escribir "   [!] No encontré los ajustes en $n; se usan los parches sin cambios." 'Aviso'; return }
        $texto = [IO.File]::ReadAllText($ajustes.FullName)
        foreach ($c in $mios) {
            $m = [regex]::Matches($texto, $c.Patron)
            if ($m.Count -ne 1) { Escribir "   [!] El código de $($c.Que) cambió ($($m.Count) coincidencias); se usan los parches sin cambios." 'Aviso'; return }
            $texto = [regex]::Replace($texto, $c.Patron, $c.Nuevo)
            Escribir "   $($c.Que): $($m[0].Groups[2].Value) -> $($c.Valor)"
        }
        [IO.File]::WriteAllText($ajustes.FullName, $texto, (New-Object Text.UTF8Encoding $false))
        # Misma versión de dex que el original (cabecera "dex\n0NN").
        $ver = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($rve), 4, 3)
        $api = @{ '035' = '21'; '037' = '24'; '038' = '26'; '039' = '28'; '040' = '35' }[$ver]
        if (-not $api) { $api = '26' }
        $salida = Join-Path $dir "$n-mod.rve"
        $r = Ejecutar $java @('-cp', $cp, 'com.android.tools.smali.smali.Main', 'a', '--api', $api, '-o', $salida, $src)
        if ($r.Codigo -ne 0 -or -not (Test-Path $salida)) { Escribir "   [!] No se pudo recompilar $n; se usan los parches sin cambios." 'Aviso'; return }
        $nuevos += @{ Nombre = "extensions/$n.rve"; Archivo = $salida }
    }
    Copy-Item $rvp "$mod.tmp" -Force
    $zip = [IO.Compression.ZipFile]::Open("$mod.tmp", 'Update')
    try {
        foreach ($x in $nuevos) {
            $zip.GetEntry($x.Nombre).Delete()
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $x.Archivo, $x.Nombre)
        }
    } finally { $zip.Dispose() }
    Move-Item "$mod.tmp" $mod -Force
    Get-ChildItem $Herr -Filter 'patches-*.rvp' | Where-Object FullName -ne $mod | Remove-Item
    $Estado.modificado = "$($Estado.parches)|$id"
    $script:rvpUsar = $mod; $script:Marca = "|$id"
    Escribir "   [OK] Valores por defecto cambiados: $($pedido -join ', ')" 'Ok'
}
Escribir "`n2b) Valores por defecto propios" 'Titulo'
Modificar-Parches

# --- 3. Apps ---------------------------------------------------------------------------------
function Version-Soportada($paquete) {
    $r = Ejecutar $java @('-jar', $cli, 'list-versions', '-p', $rvp, '-b', "--filter-package-names=$paquete")
    $versiones = [regex]::Matches($r.Texto, '(?m)^\s*(\d+(?:\.\d+)+)\s+\((\d+) patches\)') | ForEach-Object {
        [pscustomobject]@{ Version = $_.Groups[1].Value; Parches = [int]$_.Groups[2].Value }
    }
    if (-not $versiones) { return $null }
    $max = ($versiones | Measure-Object Parches -Maximum).Maximum
    ($versiones | Where-Object Parches -eq $max | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1).Version
}

# Busca en APKMirror la variante APK (no bundle) para arm64 y la descarga.
function Bajar-DeApkMirror($app, $version, $destino) {
    $v = $version -replace '\.', '-'
    $ruta = "/apk/google-inc/$($app.SlugApp)/$($app.SlugRel)-$v-release/"
    $pagina = Leer "https://www.apkmirror.com$ruta"
    if (-not $pagina) { return 'no encontré la página de esa versión en APKMirror' }
    $candidatos = foreach ($fila in ($pagina -split 'table-row headerFont')) {
        $m = [regex]::Match($fila, "href=`"($([regex]::Escape($ruta))[^`"]+-android-apk-download/)`"")
        if (-not $m.Success) { continue }
        $t = ($fila.Substring(0, [Math]::Min(4000, $fila.Length)) -replace '<[^>]+>', ' ')
        if ($t -cmatch '\bBUNDLE\b' -or $t -cnotmatch '\bAPK\b') { continue }
        $puntos = 0
        if ($t -match 'arm64-v8a' -and $t -notmatch 'armeabi') { $puntos = 3 }
        elseif ($t -match '\b(universal|noarch)\b') { $puntos = 2 }
        elseif ($t -match 'arm64-v8a') { $puntos = 1 }
        if ($puntos) { [pscustomobject]@{ Url = 'https://www.apkmirror.com' + $m.Groups[1].Value; Puntos = $puntos } }
    }
    $candidatos = @($candidatos | Sort-Object Url -Unique | Sort-Object Puntos -Descending)
    if (-not $candidatos) { return 'no hay una variante APK para arm64 en APKMirror' }
    foreach ($c in $candidatos) {
        $p1 = Leer $c.Url
        $k = [regex]::Match("$p1", 'href="([^"]+/download/\?key=[^"]+)"')
        if (-not $k.Success) { continue }
        $url2 = 'https://www.apkmirror.com' + ($k.Groups[1].Value -replace '&amp;', '&')
        $p2 = Leer $url2 $c.Url
        $d = [regex]::Match("$p2", 'href="([^"]*download\.php\?[^"]+)"')
        if (-not $d.Success) { continue }
        $url3 = 'https://www.apkmirror.com' + ($d.Groups[1].Value -replace '&amp;', '&')
        Remove-Item $destino -ErrorAction SilentlyContinue
        if (Bajar $url3 $destino $url2) {
            $mal = Verificar-Apk $destino $app.Paquete $version $app.CertGoogle
            if (-not $mal) { return $null }
            Escribir "   [!] La variante $($c.Url) $mal; pruebo otra." 'Aviso'
            Remove-Item $destino -ErrorAction SilentlyContinue
        }
    }
    'APKMirror no dejó descargar ninguna variante válida'
}

function Conseguir-Original($app, $version) {
    $destino = Join-Path $Originales "$($app.Paquete)-$version.apk"
    if ((Test-Path $destino) -and -not (Verificar-Apk $destino $app.Paquete $version $app.CertGoogle)) { return $destino }
    # Primero, lo que hayas dejado a mano en la carpeta "manual".
    foreach ($f in Get-ChildItem $Manual -Filter '*.apk' -ErrorAction SilentlyContinue) {
        if (-not (Verificar-Apk $f.FullName $app.Paquete $version $app.CertGoogle)) {
            Copy-Item $f.FullName $destino -Force; return $destino
        }
    }
    Escribir "   Descargando $($app.Nombre) $version original de APKMirror..."
    $fallo = Bajar-DeApkMirror $app $version $destino
    if (-not $fallo) { return $destino }
    Escribir "   [!!] $fallo." 'Error'
    Escribir "   Descárgalo a mano: busca `"$($app.Nombre) $version`" en apkmirror.com, elige la variante APK" 'Aviso'
    Escribir "   (no BUNDLE) para arm64-v8a o universal, guárdalo en $Manual y vuelve a ejecutar." 'Aviso'
    Escribir '   El script igual comprobará que lo firmó Google antes de usarlo.' 'Aviso'
    $null
}

$Listos = @{}
foreach ($app in $Apps) {
    Escribir "`n3) $($app.Nombre)" 'Titulo'
    $version = Version-Soportada $app.Paquete
    if (-not $version) { Escribir '   [!!] Los parches no indican ninguna versión compatible.' 'Error'; continue }
    $salida = Join-Path $Base ("$($app.Nombre -replace ' ', '')-ReVanced-$version.apk")
    $firma = "$version|$($Estado.parches)$Marca"
    if (-not $Forzar -and $Estado.apps[$app.Paquete] -eq $firma -and (Test-Path $salida)) {
        Escribir "   [OK] Ya está al día: versión $version con parches $($Estado.parches)" 'Ok'
        $Listos[$app.Parcheado] = @{ Apk = $salida; Version = $version; Firma = $firma; App = $app }
        continue
    }
    Escribir "   Versión compatible con los parches: $version"
    $original = Conseguir-Original $app $version
    if (-not $original) { continue }
    Escribir "   [OK] Original de Google verificado (certificado $($app.CertGoogle.Substring(0, 8))...)" 'Ok'

    $lista = (Ejecutar $java @('-jar', $cli, 'list-patches', '-p', $rvp, '-b', "--filter-package-name=$($app.Paquete)")).Texto
    $quitar = [regex]::Matches($lista, '(?m)Name: (.+)\r?\n(?:.*\r?\n)*?\s*Enabled: true') |
        ForEach-Object { $_.Groups[1].Value.Trim() } | Where-Object { $_ -match $ExcluirParches } | Sort-Object -Unique
    $nuevo = Join-Path $Tmp 'parcheado.apk'; Remove-Item $nuevo -ErrorAction SilentlyContinue
    $argumentos = @('-jar', $cli, 'patch', '-p', $rvpUsar, '-b', '--keystore', $Clave, '-t', (Join-Path $Tmp 'parcheo'), '--purge',
        '-o', $nuevo, $original)
    foreach ($q in $quitar) { $argumentos += @('-d', $q) }
    if ($quitar) { Escribir "   Sin: $($quitar -join ', ')" }
    Escribir '   Aplicando parches (tarda unos minutos)...'
    $r = Ejecutar $java $argumentos
    $r.Texto | Set-Content (Join-Path $Base "registro-$($app.Nombre -replace ' ', '').txt") -Encoding UTF8
    $ok = ([regex]::Matches($r.Texto, '" succeeded')).Count
    $fallos = [regex]::Matches($r.Texto, '"([^"]+)" failed') | ForEach-Object { $_.Groups[1].Value }
    if ($r.Codigo -ne 0 -or -not (Test-Path $nuevo)) {
        Escribir '   [!!] El parcheo terminó con error; se mantiene la versión anterior.' 'Error'; continue
    }
    if ($fallos) {
        Remove-Item $nuevo
        Escribir "   [!!] Fallaron: $($fallos -join ', '). Se mantiene la versión anterior." 'Error'; continue
    }
    Get-ChildItem $Base -Filter ("$($app.Nombre -replace ' ', '')-ReVanced-*.apk") | Where-Object FullName -ne $salida | Remove-Item
    Move-Item $nuevo $salida -Force
    Get-ChildItem $Originales -Filter "$($app.Paquete)-*.apk" | Where-Object FullName -ne $original | Remove-Item
    $Estado.apps[$app.Paquete] = $firma; Guardar-Estado
    Escribir "   [OK] $ok parches aplicados, ninguno falló" 'Ok'
    $Listos[$app.Parcheado] = @{ Apk = $salida; Version = $version; Firma = $firma; App = $app }
}
Guardar-Estado

# --- 4. Instalar en el teléfono --------------------------------------------------------------
Escribir "`n4) Teléfono" 'Titulo'
if ($SinInstalar) { Escribir '   Omitido (-SinInstalar).' 'Aviso' }
else {
    $dispositivos = @((Ejecutar $adb @('devices')).Texto -split "`n" | Where-Object { $_ -match "^(\S+)\s+device\s*$" } |
        ForEach-Object { ($_ -split '\s+')[0] })
    if (-not $dispositivos) {
        Escribir '   [!] No hay ningún teléfono conectado por adb (cable o depuración inalámbrica).' 'Aviso'
        Escribir "   Los APK quedan en `"$Compartir`" para instalarlos a mano." 'Aviso'
    } else {
        $serie = $dispositivos[0]
        function Version-EnTelefono($paquete) {
            $t = (Ejecutar $adb @('-s', $serie, 'shell', 'dumpsys', 'package', $paquete)).Texto
            if ($t -match 'versionName=(\S+)') { $Matches[1] } else { $null }
        }
        function Instalar($apk, $nombre) {
            $r = Ejecutar $adb @('-s', $serie, 'install', '-r', '--user', '0', $apk)
            if ($r.Texto -match 'Success') { Escribir "   [OK] $nombre instalado" 'Ok'; return $true }
            if ($r.Texto -match 'UPDATE_INCOMPATIBLE') {
                Escribir "   [!!] $nombre`: la app del teléfono está firmada con otra clave. Desinstálala y vuelve a ejecutar." 'Error'
            } else { Escribir "   [!!] $nombre`: $($r.Texto.Trim())" 'Error' }
            $false
        }
        if ($gms) {
            $enTel = Version-EnTelefono 'app.revanced.android.gms'
            if ($enTel -ne $Estado.gmscore.TrimStart('v')) { [void](Instalar $gms.FullName "MicroG $($Estado.gmscore)") }
            else { Escribir "   [OK] MicroG ya está al día" 'Ok' }
        }
        foreach ($p in $Listos.Keys) {
            $l = $Listos[$p]
            if ($Estado.instalado[$p] -eq $l.Firma -and (Version-EnTelefono $p) -eq $l.Version) {
                Escribir "   [OK] $($l.App.Nombre) ReVanced ya está al día" 'Ok'; continue
            }
            if (Instalar $l.Apk "$($l.App.Nombre) ReVanced $($l.Version)") { $Estado.instalado[$p] = $l.Firma; Guardar-Estado }
        }
    }
}

# --- 5. Carpeta para compartir ---------------------------------------------------------------
$yt = $Listos['app.revanced.android.youtube']; $ytm = $Listos['app.revanced.android.apps.youtube.music']
if ($gms -and $yt -and $ytm) {
    New-Item -ItemType Directory -Force $Compartir | Out-Null
    Get-ChildItem $Compartir -Filter '*.apk' | Remove-Item
    $vg = $Estado.gmscore.TrimStart('v')
    Copy-Item $gms.FullName (Join-Path $Compartir "1 - MicroG (GmsCore) $vg.apk")
    Copy-Item $yt.Apk (Join-Path $Compartir "2 - YouTube ReVanced $($yt.Version).apk")
    Copy-Item $ytm.Apk (Join-Path $Compartir "3 - YT Music ReVanced $($ytm.Version).apk")
    @"
ReVanced: YouTube y YouTube Music sin anuncios y con reproducción en segundo plano

Instalar en este orden:
  1. MicroG (GmsCore): permite iniciar sesión con tu cuenta de Google.
  2. YouTube ReVanced
  3. YT Music ReVanced

Al abrir el archivo .apk, Android pide permiso para "instalar apps desconocidas" desde el
gestor de archivos o la app donde lo recibiste: acéptalo.
Si MicroG pide desactivar la optimización de batería, acéptalo para que lleguen las
notificaciones.
Las opciones nuevas están en Ajustes > ReVanced, dentro de cada app.

Las dos apps ya vienen con el cliente "visionOS" en Ajustes > ReVanced > Falsificar
transmisiones de vídeo > Cliente por defecto. Con el que trae ReVanced, la música se cortaba
al minuto y los videos se quedaban pegados al saltar un patrocinio. Si alguna vez falla la
reproducción, prueba ahí otro cliente y cierra la app por completo antes de volver a abrirla.
YouTube viene con 1080p como calidad por defecto con Wi-Fi (Ajustes > ReVanced > Video).
En YT Music, la calidad de audio se elige en los ajustes propios de la app: Ajustes >
Calidad de audio y video > Calidad de audio con Wi-Fi.

Si ya tienes una versión anterior de estos mismos archivos, la nueva se instala encima sin
perder nada. No se instalan encima de otro YouTube ReVanced o YT Music ReVanced hecho por
otra persona: ese hay que desinstalarlo primero. El YouTube y el YouTube Music originales
pueden quedarse.

Requisitos: Android 9 o más nuevo, y un teléfono de 64 bits (arm64). YT Music no funciona en
teléfonos de 32 bits; casi todos los teléfonos de 2017 en adelante son de 64 bits.

Origen:
  - Apps de base: las originales de Google, versiones YouTube $($yt.Version) y YouTube Music
    $($ytm.Version). Se comprobó que la firma de Google coincide con la de las apps del Play
    Store.
  - Parches: los oficiales de ReVanced $($Estado.parches), de api.revanced.app, con firma
    digital verificada. No incluyen botones para descargar contenido. Única modificación
    propia: dos valores por defecto (el cliente de "Falsificar transmisiones de vídeo" y la
    calidad de video con Wi-Fi en YouTube).
  - MicroG: el oficial $vg, de github.com/ReVanced/GmsCore.
"@ | Set-Content (Join-Path $Compartir 'LEEME.txt') -Encoding UTF8
    Get-ChildItem $Compartir -Filter '*.apk' | ForEach-Object {
        '{0} *{1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name
    } | Set-Content (Join-Path $Compartir 'SHA256.txt') -Encoding UTF8
    Escribir "`n[OK] Carpeta `"$Compartir`" actualizada" 'Ok'
}
Remove-Item (Join-Path $Tmp '*') -Recurse -Force -ErrorAction SilentlyContinue
Escribir "`nListo." 'Titulo'
Terminar 0
