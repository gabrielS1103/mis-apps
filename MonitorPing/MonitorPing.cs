using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorPing
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool nueva;
            using (Mutex m = new Mutex(true, "MonitorPing_UnicaInstancia", out nueva))
            {
                if (!nueva)
                {
                    MessageBox.Show("MonitorPing ya se está ejecutando (mira en la barra de iconos ocultos).",
                        "MonitorPing", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }

    static class Native
    {
        public const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;
        public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000;
        public const int GWL_EXSTYLE = -20;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int indice);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int indice, int valor);

        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint ms);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint ms);

        // Para dejar el widget por debajo de las ventanas del sistema (iconos ocultos, menús, Inicio...)
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_SHOW = 0x8002, WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
        public const uint GA_ROOT = 2;
        public const int DWMWA_CLOAKED = 14;

        public delegate void WinEventDelegate(IntPtr gancho, uint evento, IntPtr hwnd, int idObjeto, int idHijo, uint hilo, uint tiempo);
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr param);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventoMin, uint eventoMax, IntPtr modulo, WinEventDelegate proc, uint idProceso, uint idHilo, uint flags);

        [DllImport("user32.dll")]
        public static extern bool UnhookWinEvent(IntPtr gancho);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr param);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hwnd, StringBuilder clase, int max);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int atributo, out int valor, int tam);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tam);

        // Cambia el estilo visual de un control (por ejemplo, barras de desplazamiento oscuras)
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string aplicacion, string lista);

        // Raw Input: el ratón avisa a esta app de cada reporte aunque no tenga el foco
        public const int WM_INPUT = 0xFF;
        public const uint RID_INPUT = 0x10000003, RIDEV_INPUTSINK = 0x100, RIM_TYPEMOUSE = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort UsagePage, Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] dispositivos, uint numero, uint tam);

        [DllImport("user32.dll")]
        public static extern uint GetRawInputData(IntPtr hRaw, uint comando, IntPtr datos, ref uint tam, uint tamCabecera);

        public const uint RIDI_DEVICENAME = 0x20000007;
        public const int WM_APP = 0x8000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint GetRawInputDeviceInfo(IntPtr dispositivo, uint comando, StringBuilder datos, ref uint tam);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        // Pide a Windows los reportes del ratón para esa ventana (con RIDEV_INPUTSINK, también en segundo plano)
        public static bool RegistrarRaton(IntPtr ventana, uint flags)
        {
            RAWINPUTDEVICE[] d = new RAWINPUTDEVICE[1];
            d[0].UsagePage = 1; // escritorio genérico
            d[0].Usage = 2;     // ratón
            d[0].Flags = flags;
            d[0].Target = ventana;
            return RegisterRawInputDevices(d, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        }

        const int AF_INET = 2, TCP_TABLE_OWNER_PID_ALL = 5;
        const uint MIB_TCP_STATE_ESTAB = 5, ERROR_INSUFFICIENT_BUFFER = 122;

        [StructLayout(LayoutKind.Sequential)]
        struct MIB_TCPROW_OWNER_PID
        {
            public uint state, localAddr, localPort, remoteAddr, remotePort, owningPid;
        }

        [DllImport("iphlpapi.dll")]
        static extern uint GetExtendedTcpTable(IntPtr tabla, ref int tam, bool ordenar, int familia, int clase, uint reservado);

        [DllImport("iphlpapi.dll")]
        static extern int GetBestInterface(uint destino, out uint indice);

        public class ConexionTcp
        {
            public IPAddress Ip;
            public int Puerto;
            public int Pid;
        }

        // Conexiones TCP establecidas (IPv4) cuyo puerto remoto está en la lista, con el PID del proceso dueño
        public static List<ConexionTcp> ConexionesA(int[] puertos)
        {
            List<ConexionTcp> res = new List<ConexionTcp>();
            IntPtr buf = IntPtr.Zero;
            int tam = 0;
            try
            {
                uint r = ERROR_INSUFFICIENT_BUFFER;
                for (int intento = 0; intento < 4 && r == ERROR_INSUFFICIENT_BUFFER; intento++)
                {
                    if (tam > 0)
                    {
                        if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                        tam += 1024; // margen por si aparecen conexiones nuevas entre llamadas
                        buf = Marshal.AllocHGlobal(tam);
                    }
                    r = GetExtendedTcpTable(buf, ref tam, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
                }
                if (r != 0 || buf == IntPtr.Zero) return res;

                int n = Marshal.ReadInt32(buf);
                int tamFila = Marshal.SizeOf(typeof(MIB_TCPROW_OWNER_PID));
                long p = buf.ToInt64() + 4;
                for (int i = 0; i < n; i++, p += tamFila)
                {
                    MIB_TCPROW_OWNER_PID f = (MIB_TCPROW_OWNER_PID)Marshal.PtrToStructure(new IntPtr(p), typeof(MIB_TCPROW_OWNER_PID));
                    if (f.state != MIB_TCP_STATE_ESTAB || f.remoteAddr == 0) continue;
                    int puerto = (int)(((f.remotePort & 0xFF) << 8) | ((f.remotePort >> 8) & 0xFF)); // orden de red
                    if (Array.IndexOf(puertos, puerto) < 0) continue;
                    ConexionTcp c = new ConexionTcp();
                    c.Ip = new IPAddress((long)f.remoteAddr);
                    c.Puerto = puerto;
                    c.Pid = (int)f.owningPid;
                    res.Add(c);
                }
            }
            catch { }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            }
            return res;
        }

        // Puerta de enlace (router) de la interfaz que se usa para salir a internet
        public static IPAddress PuertaDeEnlace()
        {
            try
            {
                uint indice;
                bool hayIndice = GetBestInterface(BitConverter.ToUInt32(new byte[] { 8, 8, 8, 8 }, 0), out indice) == 0;
                IPAddress primera = null;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    IPInterfaceProperties props = ni.GetIPProperties();
                    IPAddress gw = null;
                    foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
                    {
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) { gw = g.Address; break; }
                    }
                    if (gw == null) continue;
                    if (primera == null) primera = gw;
                    try
                    {
                        IPv4InterfaceProperties v4 = props.GetIPv4Properties();
                        if (hayIndice && v4 != null && v4.Index == indice) return gw;
                    }
                    catch { }
                }
                return primera;
            }
            catch { return null; }
        }

        // Nombre de la VPN si el tráfico a internet sale por un túnel (WinTun, TAP, WireGuard...); null si sale directo
        public static string VpnActiva()
        {
            try
            {
                uint indice;
                if (GetBestInterface(BitConverter.ToUInt32(new byte[] { 8, 8, 8, 8 }, 0), out indice) != 0) return null;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    IPInterfaceProperties props = ni.GetIPProperties();
                    IPv4InterfaceProperties v4;
                    try { v4 = props.GetIPv4Properties(); }
                    catch { continue; }
                    if (v4 == null || v4.Index != indice) continue;

                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel || ni.NetworkInterfaceType == NetworkInterfaceType.Ppp) return ni.Name;
                    string t = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    foreach (string marca in new string[] { "wintun", "tap-", "wireguard", "vpn", "hotspot", "openvpn", "nordlynx", "warp", "zerotier", "tailscale", "tunnel" })
                        if (t.Contains(marca)) return ni.Name;
                    // Si todo internet sale por una interfaz sin puerta de enlace, es un túnel
                    foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) return null;
                    return ni.Name;
                }
            }
            catch { }
            return null;
        }

        // IPv4 propias de las interfaces activas (Wi-Fi, cable, VPN), donde se escucha el tráfico del stream
        public static List<IPAddress> IPsLocales()
        {
            List<IPAddress> res = new List<IPAddress>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        byte[] b = u.Address.GetAddressBytes();
                        if (b[0] == 169 && b[1] == 254) continue;
                        res.Add(u.Address);
                    }
                }
            }
            catch { }
            return res;
        }

        public static bool EsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }

    static class Servidores
    {
        public static readonly string[] Nombres = { "Atlanta", "Miami", "Dallas", "Nueva Jersey", "Chicago", "Los Ángeles", "Seattle", "Silicon Valley" };
        public static readonly string[] Hosts = { "ga-us-ping.vultr.com", "fl-us-ping.vultr.com", "tx-us-ping.vultr.com", "nj-us-ping.vultr.com",
                                                  "il-us-ping.vultr.com", "lax-ca-us-ping.vultr.com", "wa-us-ping.vultr.com", "sjo-ca-us-ping.vultr.com" };

        public static string Etiqueta(int i) { return Nombres[i] + " — " + Hosts[i]; }

        public static int Indice(string host)
        {
            for (int i = 0; i < Hosts.Length; i++)
                if (string.Equals(Hosts[i], host, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public static string Nombre(string host)
        {
            int i = Indice(host);
            return i >= 0 ? Nombres[i] : host;
        }

        // Texto del desplegable de configuración -> host
        public static string HostDesdeTexto(string texto)
        {
            texto = (texto ?? "").Trim();
            for (int i = 0; i < Hosts.Length; i++)
                if (texto == Etiqueta(i)) return Hosts[i];
            int sep = texto.LastIndexOf(" — ");
            if (sep >= 0) texto = texto.Substring(sep + 3).Trim();
            return texto;
        }
    }

    // Zonas de un servicio de juego en la nube que se pueden medir con una conexión TCP al 443 antes de jugar
    class ListaZonas
    {
        // GeForce NOW: zonas de América de status.geforcenow.com (no responden al ping ICMP)
        public static readonly ListaZonas Gfn = new ListaZonas("GeForce NOW",
            new string[] {
                "LATAM South · Montevideo", "LATAM West · Santiago", "LATAM North · Bogotá",
                "EE.UU. · Miami", "EE.UU. · Atlanta", "EE.UU. · Dallas", "EE.UU. · Virginia", "EE.UU. · Nueva Jersey", "EE.UU. · Chicago",
                "EE.UU. · Los Ángeles", "EE.UU. · Arizona", "EE.UU. · San José", "EE.UU. · Oregón", "Canadá · Toronto", "Canadá · Montreal" },
            new string[] {
                "npa-aby-mvd-03", "npa-dig-scl-01", "npa-dig-bog-01",
                "np-mia-04", "np-atl-04", "np-dal-06", "np-ash-04", "np-nwk-04", "np-chi-05",
                "np-lax-03", "np-phx-02", "np-sjc6-06", "np-pdx-01", "np-yyz-01", "np-mon-02" },
            "{0}.cloudmatchbeta.nvidiagrid.net");

        // Xbox Cloud corre en regiones de Azure: se mide el punto de prueba de cada región (el mismo que usa AzureSpeed)
        public static readonly ListaZonas Xcloud = new ListaZonas("Xbox Cloud",
            new string[] {
                "Brasil · São Paulo", "México · Querétaro", "EE.UU. · Texas", "EE.UU. · Virginia", "EE.UU. · Virginia 2",
                "EE.UU. · Illinois", "EE.UU. · Iowa", "EE.UU. · California", "EE.UU. · Washington", "Canadá · Toronto" },
            new string[] {
                "brazilsouth", "mexicocentral", "southcentralus", "eastus", "eastus2",
                "northcentralus", "centralus", "westus", "westus2", "canadacentral" },
            "s8{0}.blob.core.windows.net");

        public readonly string Servicio;
        public readonly string[] Nombres, Codigos, Hosts;

        ListaZonas(string servicio, string[] nombres, string[] codigos, string formatoHost)
        {
            Servicio = servicio;
            Nombres = nombres;
            Codigos = codigos;
            Hosts = new string[codigos.Length];
            for (int i = 0; i < codigos.Length; i++) Hosts[i] = string.Format(formatoHost, codigos[i]);
        }

        public string Ciudad(int i)
        {
            int p = Nombres[i].IndexOf("· ");
            return p >= 0 ? Nombres[i].Substring(p + 2) : Nombres[i];
        }

        public int Indice(string codigo)
        {
            for (int i = 0; i < Codigos.Length; i++)
                if (string.Equals(Codigos[i], codigo, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // Mejor de 3 pings TCP a cada zona, todas a la vez. -1 = sin respuesta
        public int[] MedirTodas()
        {
            int[] res = new int[Hosts.Length];
            Thread[] hilos = new Thread[Hosts.Length];
            for (int i = 0; i < Hosts.Length; i++)
            {
                int z = i;
                hilos[i] = new Thread(delegate ()
                {
                    int mejor = -1;
                    IPAddress ip = Sonda.Resolver(Hosts[z]);
                    for (int n = 0; ip != null && n < 3; n++)
                    {
                        int ms = Sonda.PingTcp(ip, 443);
                        if (ms >= 0 && (mejor < 0 || ms < mejor)) mejor = ms;
                        Thread.Sleep(150);
                    }
                    res[z] = mejor;
                });
                hilos[i].IsBackground = true;
                hilos[i].Start();
            }
            foreach (Thread h in hilos) h.Join();
            return res;
        }
    }

    // Elige, en modo automático, la zona con menos ping (se vuelve a buscar cada 10 minutos)
    class SelectorZona
    {
        public readonly ListaZonas Lista;
        public volatile int Mejor = -1;
        public volatile bool Escaneando;
        DateTime ultimo = DateTime.MinValue;

        public SelectorZona(ListaZonas lista) { Lista = lista; }

        public void Escanear(bool forzar)
        {
            if (Escaneando || (!forzar && Mejor >= 0 && (DateTime.UtcNow - ultimo).TotalMinutes < 10)) return;
            Escaneando = true;
            ultimo = DateTime.UtcNow;
            Thread t = new Thread(delegate ()
            {
                int[] ms = Lista.MedirTodas();
                int mejor = -1;
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i] >= 0 && (mejor < 0 || ms[i] < ms[mejor])) mejor = i;
                if (mejor >= 0) Mejor = mejor;
                Escaneando = false;
            });
            t.IsBackground = true;
            t.Start();
        }
    }

    class Config
    {
        public string ServidorEEUU = "ga-us-ping.vultr.com";
        public string GfnManual = "";
        public string ZonaGfn = "";     // código de zona; vacío = la de menor ping
        public string ZonaXcloud = "";
        public int WidgetX = int.MinValue, WidgetY = int.MinValue;
        public bool MostrarAlIniciar = true;
        public bool Encima = true;
        public bool Bloqueado = true;   // el ratón atraviesa el widget (modo monitor)
        public bool Compacto = true;
        public int Opacidad = 90;
        public bool VerGfn = true, VerXcloud = true, VerEeuu = true, VerRouter = true, VerRaton = true;
        public int HzRaton;              // resultado de la última prueba de ratón (0 = sin probar)
        public string RatonMedido = "";
        public string NombreTema = "clasico";
        public bool Animado = true;      // los resplandores del fondo se mueven (temas nuevos)

        static string Ruta
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorPing");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "config.ini");
            }
        }

        public static Config Cargar()
        {
            Config c = new Config();
            try
            {
                if (!File.Exists(Ruta)) return c;
                foreach (string linea in File.ReadAllLines(Ruta))
                {
                    int i = linea.IndexOf('=');
                    if (i < 0) continue;
                    string k = linea.Substring(0, i).Trim(), v = linea.Substring(i + 1).Trim();
                    if (k == "eeuu" && v != "") c.ServidorEEUU = v;
                    else if (k == "gfnmanual") c.GfnManual = v;
                    else if (k == "zonagfn") c.ZonaGfn = v;
                    else if (k == "zonaxcloud") c.ZonaXcloud = v;
                    else if (k == "x") c.WidgetX = int.Parse(v);
                    else if (k == "y") c.WidgetY = int.Parse(v);
                    else if (k == "mostrar") c.MostrarAlIniciar = v == "1";
                    else if (k == "encima") c.Encima = v == "1";
                    else if (k == "bloqueado") c.Bloqueado = v == "1";
                    else if (k == "compacto") c.Compacto = v == "1";
                    else if (k == "opacidad") c.Opacidad = Math.Max(30, Math.Min(100, int.Parse(v)));
                    else if (k == "vergfn") c.VerGfn = v == "1";
                    else if (k == "verxcloud") c.VerXcloud = v == "1";
                    else if (k == "vereeuu") c.VerEeuu = v == "1";
                    else if (k == "verrouter") c.VerRouter = v == "1";
                    else if (k == "verraton") c.VerRaton = v == "1";
                    else if (k == "hzraton") c.HzRaton = int.Parse(v);
                    else if (k == "ratonmedido") c.RatonMedido = v;
                    else if (k == "tema" && v != "") c.NombreTema = v;
                    else if (k == "animado") c.Animado = v == "1";
                }
            }
            catch { }
            return c;
        }

        public void Guardar()
        {
            try
            {
                File.WriteAllLines(Ruta, new string[] {
                    "eeuu=" + ServidorEEUU,
                    "gfnmanual=" + GfnManual,
                    "zonagfn=" + ZonaGfn,
                    "zonaxcloud=" + ZonaXcloud,
                    "x=" + WidgetX,
                    "y=" + WidgetY,
                    "mostrar=" + (MostrarAlIniciar ? "1" : "0"),
                    "encima=" + (Encima ? "1" : "0"),
                    "bloqueado=" + (Bloqueado ? "1" : "0"),
                    "compacto=" + (Compacto ? "1" : "0"),
                    "opacidad=" + Opacidad,
                    "vergfn=" + (VerGfn ? "1" : "0"),
                    "verxcloud=" + (VerXcloud ? "1" : "0"),
                    "vereeuu=" + (VerEeuu ? "1" : "0"),
                    "verrouter=" + (VerRouter ? "1" : "0"),
                    "verraton=" + (VerRaton ? "1" : "0"),
                    "hzraton=" + HzRaton,
                    "ratonmedido=" + RatonMedido,
                    "tema=" + NombreTema,
                    "animado=" + (Animado ? "1" : "0")
                });
            }
            catch { }
        }
    }

    // Copia de las últimas mediciones de una sonda, para pintarla sin bloquear el hilo que mide
    class Estado
    {
        public const int SinDatos = -2, Perdido = -1;

        public bool HayDestino;
        public string Host, Ip, Error;
        public bool ModoTcp;
        public int[] Valores = new int[0];      // ms por segundo, de la más antigua a la más reciente (-1 = perdido)
        public int Actual = SinDatos;
        public int Minimo = int.MaxValue;       // mejor ping desde que se fijó el destino
        public double Promedio, Jitter, Perdida; // últimos 60 s
        public int Perdidos;
        public double Promedio30, Jitter30, Perdida30;
        public int Muestras30;

        public bool NuncaResponde { get { return Minimo == int.MaxValue && Valores.Length >= 5; } }
    }

    // Mide un destino una vez por segundo en un hilo propio
    class Sonda
    {
        const int Max = 60;

        readonly object bloqueo = new object();
        readonly int[] muestras = new int[Max];
        int cuantas, siguiente;
        string host = "";
        int puertoTcp, version;
        bool modoTcp;
        int intentosIcmp, exitosIcmp;
        int minimo = int.MaxValue;
        string error;
        IPAddress ip;
        DateTime resueltoEn;
        volatile bool parar;

        public Sonda()
        {
            Thread t = new Thread(Bucle);
            t.IsBackground = true;
            t.Start();
        }

        public void Parar() { parar = true; }

        // host vacío = nada que medir. Con puerto > 0, si el destino no responde al ping ICMP se mide la conexión TCP a ese puerto;
        // con soloTcp se mide por TCP desde el principio
        public void FijarDestino(string nuevoHost, int nuevoPuerto, bool soloTcp)
        {
            nuevoHost = (nuevoHost ?? "").Trim();
            lock (bloqueo)
            {
                if (string.Equals(nuevoHost, host, StringComparison.OrdinalIgnoreCase) && nuevoPuerto == puertoTcp) return;
                host = nuevoHost;
                puertoTcp = nuevoPuerto;
                version++;
                cuantas = 0;
                siguiente = 0;
                minimo = int.MaxValue;
                error = null;
                modoTcp = soloTcp && nuevoPuerto > 0;
                intentosIcmp = 0;
                exitosIcmp = 0;
                ip = null;
            }
        }

        void Bucle()
        {
            byte[] datos = new byte[32];
            using (Ping ping = new Ping())
            {
                while (!parar)
                {
                    Stopwatch reloj = Stopwatch.StartNew();
                    string h;
                    int puerto, ver;
                    bool tcp;
                    IPAddress dir;
                    DateTime resuelto;
                    lock (bloqueo) { h = host; puerto = puertoTcp; ver = version; tcp = modoTcp; dir = ip; resuelto = resueltoEn; }

                    if (h == "") { Thread.Sleep(300); continue; }

                    // El nombre se resuelve como mucho una vez por minuto; si falla se sigue usando la IP anterior
                    if (dir == null || (DateTime.UtcNow - resuelto).TotalSeconds > 60)
                    {
                        IPAddress nueva = Resolver(h);
                        lock (bloqueo)
                        {
                            if (ver == version)
                            {
                                if (nueva != null) { ip = nueva; error = null; }
                                else if (ip == null) error = "No se encuentra " + h;
                                resueltoEn = DateTime.UtcNow;
                            }
                            dir = ip;
                        }
                        if (dir == null) { Thread.Sleep(1000); continue; }
                    }

                    int rtt = tcp ? PingTcp(dir, puerto) : PingIcmp(ping, dir, datos);

                    lock (bloqueo)
                    {
                        if (ver == version)
                        {
                            bool descartar = false;
                            if (!tcp && puerto > 0 && exitosIcmp == 0)
                            {
                                intentosIcmp++;
                                if (rtt >= 0) exitosIcmp++;
                                else if (intentosIcmp >= 5)
                                {
                                    // El servidor no contesta al ping ICMP: a partir de ahora se mide el tiempo de conexión TCP
                                    modoTcp = true;
                                    cuantas = 0;
                                    siguiente = 0;
                                    descartar = true;
                                }
                            }
                            if (!descartar) Agregar(rtt);
                        }
                    }

                    int resto = 1000 - (int)reloj.ElapsedMilliseconds;
                    if (resto > 0) Thread.Sleep(resto);
                }
            }
        }

        void Agregar(int rtt)
        {
            muestras[siguiente] = rtt;
            siguiente = (siguiente + 1) % Max;
            if (cuantas < Max) cuantas++;
            if (rtt >= 0 && rtt < minimo) minimo = rtt;
        }

        public static IPAddress Resolver(string h)
        {
            IPAddress dir;
            if (IPAddress.TryParse(h, out dir)) return dir;
            try
            {
                foreach (IPAddress a in Dns.GetHostAddresses(h))
                    if (a.AddressFamily == AddressFamily.InterNetwork) return a;
            }
            catch { }
            return null;
        }

        static int PingIcmp(Ping ping, IPAddress dir, byte[] datos)
        {
            try
            {
                PingReply r = ping.Send(dir, 1000, datos);
                if (r.Status == IPStatus.Success) return (int)r.RoundtripTime;
            }
            catch { }
            return Estado.Perdido;
        }

        public const int SIO_TCP_INITIAL_RTO = unchecked((int)0x98000011);
        // TCP_INITIAL_RTO_PARAMETERS: RTT sin especificar (0xFFFF) y ningún reintento de SYN (0xFE).
        // Sin esto Windows reintenta ~2 s antes de avisar de un rechazo y no se podría medir.
        public static readonly byte[] SinReintentosSyn = { 0xFF, 0xFF, 0xFE, 0x00 };

        // Tiempo de ida y vuelta del saludo TCP: vale tanto si el servidor acepta (SYN-ACK) como si rechaza (RST)
        public static int PingTcp(IPAddress dir, int puerto)
        {
            Socket s = new Socket(dir.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                try { s.IOControl(SIO_TCP_INITIAL_RTO, SinReintentosSyn, null); } catch { }
                Stopwatch reloj = Stopwatch.StartNew();
                IAsyncResult ar = s.BeginConnect(dir, puerto, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(1000)) return Estado.Perdido;
                try { s.EndConnect(ar); }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode != SocketError.ConnectionRefused) return Estado.Perdido;
                }
                return (int)reloj.ElapsedMilliseconds;
            }
            catch { return Estado.Perdido; }
            finally { s.Close(); }
        }

        public Estado Leer()
        {
            Estado e = new Estado();
            lock (bloqueo)
            {
                e.HayDestino = host != "";
                e.Host = host;
                e.Ip = ip != null ? ip.ToString() : null;
                e.Error = error;
                e.ModoTcp = modoTcp;
                e.Minimo = minimo;
                e.Valores = new int[cuantas];
                int inicio = (siguiente - cuantas + Max) % Max;
                for (int i = 0; i < cuantas; i++) e.Valores[i] = muestras[(inicio + i) % Max];
            }
            int n = e.Valores.Length;
            if (n > 0) e.Actual = e.Valores[n - 1];
            int muestras60, perdidos30;
            Calcular(e.Valores, 0, out e.Promedio, out e.Jitter, out e.Perdida, out muestras60, out e.Perdidos);
            Calcular(e.Valores, Math.Max(0, n - 30), out e.Promedio30, out e.Jitter30, out e.Perdida30, out e.Muestras30, out perdidos30);
            return e;
        }

        static void Calcular(int[] v, int desde, out double promedio, out double jitter, out double perdida, out int muestras, out int perdidos)
        {
            int ok = 0, saltos = 0, previo = -1;
            long suma = 0, sumaSaltos = 0;
            perdidos = 0;
            for (int i = desde; i < v.Length; i++)
            {
                if (v[i] < 0) { perdidos++; previo = -1; continue; }
                ok++;
                suma += v[i];
                if (previo >= 0) { sumaSaltos += Math.Abs(v[i] - previo); saltos++; }
                previo = v[i];
            }
            muestras = v.Length - desde;
            promedio = ok > 0 ? (double)suma / ok : 0;
            jitter = saltos > 0 ? (double)sumaSaltos / saltos : 0;
            perdida = muestras > 0 ? perdidos * 100.0 / muestras : 0;
        }
    }

    static class Calidad
    {
        // Los colores de estado los pone el tema (en Aurora son menta, amarillo pastel y rosa, por ejemplo)
        public static Color Verde { get { return Tema.Actual.Verde; } }
        public static Color Amarillo { get { return Tema.Actual.Amarillo; } }
        public static Color Rojo { get { return Tema.Actual.Rojo; } }
        public static Color Gris { get { return Tema.Actual.Gris; } }

        // Color del ping actual: verde por debajo de limiteVerde ms, amarillo por debajo de limiteAmarillo, rojo si no
        public static Color De(Estado e, int limiteVerde, int limiteAmarillo)
        {
            if (e == null || !e.HayDestino || e.Actual == Estado.SinDatos) return Gris;
            if (e.Actual == Estado.Perdido || e.Perdida30 >= 5) return Rojo;
            Color c = e.Actual < limiteVerde ? Verde : e.Actual < limiteAmarillo ? Amarillo : Rojo;
            if (c == Verde && e.Perdida30 > 0) c = Amarillo;
            return c;
        }

        // Una línea está "mal" si pierde paquetes, es inestable (jitter) o su ping subió mucho respecto a su mejor valor
        public static bool Mal(Estado e)
        {
            if (e == null || !e.HayDestino || e.Valores.Length < 10) return false;
            if (e.Perdidos >= 2 && e.Perdida >= 2) return true;
            if (e.Jitter30 >= 15) return true;
            return e.Minimo != int.MaxValue && e.Muestras30 > 0 && e.Promedio30 >= e.Minimo + 40;
        }

        static int Rango(Color c) { return c == Verde ? 0 : c == Amarillo ? 1 : c == Rojo ? 2 : 3; }

        public static Color Mejor(Color a, Color b) { return Rango(a) <= Rango(b) ? a : b; }

        public static string Porcentaje(double p)
        {
            if (p <= 0) return "0%";
            return (p < 10 ? p.ToString("0.0") : Math.Round(p).ToString()) + "%";
        }
    }

    // Apariencia de la app. Clásico es el aspecto original; los demás copian el estilo de los diseños de referencia:
    // Aurora (azul marino con resplandores y degradados iridiscentes) y Cristal claro / violeta (cristal esmerilado sobre pastel)
    class Tema
    {
        public string Clave, Nombre;
        public bool Oscuro;              // texto claro sobre fondo oscuro
        public bool Ventanas;            // también cambia Configuración y las pruebas (Clásico las deja como Windows)
        public Color Fondo;              // color base del fondo
        public Color[] Manchas;          // resplandores del fondo; null = fondo liso
        public int AlfaManchas = 170;
        public bool Estrellas;
        public Color Panel, BordePanel;  // tarjetas de cristal (semitransparentes)
        public Color Texto, TextoTenue, Separador, Acento, Grafica;
        public Color[] Degradado;        // botón principal
        public Color TextoDegradado;
        public Color Verde, Amarillo, Rojo, Gris;      // estados en el widget y el icono
        public Color TextoOk, TextoAviso, TextoMal;    // estados escritos en las ventanas
        public Color ControlFondo, ControlBorde, MenuFondo;
        public int Radio;                // esquinas redondeadas; 0 = rectas
        public string FuenteTexto = "Segoe UI", FuenteFuerte = "Segoe UI Semibold", FuenteNumeros = "Segoe UI Semibold";
        public FontStyle EstiloNumeros = FontStyle.Regular;

        public bool Moderno { get { return Manchas != null; } }
        public Color TenueVentana { get { return Ventanas ? TextoTenue : SystemColors.GrayText; } }

        public static readonly Tema Clasico = new Tema
        {
            Clave = "clasico", Nombre = "Clásico", Oscuro = true, Ventanas = false,
            Fondo = Color.FromArgb(22, 24, 29), Panel = Color.Transparent, BordePanel = Color.FromArgb(58, 62, 72),
            Texto = Color.FromArgb(235, 238, 243), TextoTenue = Color.FromArgb(135, 140, 150), Separador = Color.FromArgb(42, 45, 53),
            Acento = Color.FromArgb(52, 152, 219), Grafica = Color.FromArgb(32, 35, 42),
            Verde = Color.FromArgb(46, 204, 113), Amarillo = Color.FromArgb(241, 196, 15), Rojo = Color.FromArgb(231, 76, 60), Gris = Color.FromArgb(130, 135, 145),
            TextoOk = Color.FromArgb(25, 135, 70), TextoAviso = Color.FromArgb(190, 110, 0), TextoMal = Color.FromArgb(200, 50, 40),
            ControlFondo = SystemColors.Window, ControlBorde = SystemColors.ControlDark, MenuFondo = SystemColors.Menu
        };

        // De la imagen: fondo azul marino con resplandores morado/azul y acentos iridiscentes menta-amarillo-rosa-lila
        public static readonly Tema Aurora = new Tema
        {
            Clave = "aurora", Nombre = "Aurora", Oscuro = true, Ventanas = true,
            Fondo = Color.FromArgb(9, 11, 32),
            Manchas = new Color[] { Color.FromArgb(122, 52, 235), Color.FromArgb(32, 96, 236), Color.FromArgb(86, 30, 150), Color.FromArgb(22, 120, 200) },
            AlfaManchas = 150, Estrellas = true,
            Panel = Color.FromArgb(26, 255, 255, 255), BordePanel = Color.FromArgb(50, 255, 255, 255),
            Texto = Color.FromArgb(246, 247, 255), TextoTenue = Color.FromArgb(160, 166, 212), Separador = Color.FromArgb(30, 255, 255, 255),
            Acento = Color.FromArgb(183, 156, 247), Grafica = Color.FromArgb(22, 255, 255, 255),
            Degradado = new Color[] { Color.FromArgb(168, 245, 200), Color.FromArgb(233, 245, 154), Color.FromArgb(246, 165, 216), Color.FromArgb(183, 156, 247) },
            TextoDegradado = Color.FromArgb(22, 24, 52),
            Verde = Color.FromArgb(125, 240, 195), Amarillo = Color.FromArgb(240, 226, 125), Rojo = Color.FromArgb(255, 112, 150), Gris = Color.FromArgb(128, 134, 180),
            TextoOk = Color.FromArgb(125, 240, 195), TextoAviso = Color.FromArgb(240, 226, 125), TextoMal = Color.FromArgb(255, 125, 160),
            ControlFondo = Color.FromArgb(22, 26, 60), ControlBorde = Color.FromArgb(74, 80, 140), MenuFondo = Color.FromArgb(20, 22, 54),
            Radio = 14, FuenteTexto = "Segoe UI Variable Text", FuenteFuerte = "Segoe UI Variable Text Semibold",
            FuenteNumeros = "Segoe UI Variable Display", EstiloNumeros = FontStyle.Bold
        };

        // Del video: cristal blanco esmerilado sobre degradados pastel (melocotón, celeste, menta, lila) con texto oscuro
        public static readonly Tema CristalClaro = new Tema
        {
            Clave = "cristal", Nombre = "Cristal claro", Oscuro = false, Ventanas = true,
            Fondo = Color.FromArgb(236, 239, 245),
            Manchas = new Color[] { Color.FromArgb(252, 200, 172), Color.FromArgb(168, 200, 252), Color.FromArgb(190, 234, 220), Color.FromArgb(214, 202, 248) },
            AlfaManchas = 210,
            Panel = Color.FromArgb(150, 255, 255, 255), BordePanel = Color.FromArgb(210, 255, 255, 255),
            Texto = Color.FromArgb(38, 44, 60), TextoTenue = Color.FromArgb(104, 112, 132), Separador = Color.FromArgb(34, 38, 44, 60),
            Acento = Color.FromArgb(79, 124, 247), Grafica = Color.FromArgb(120, 255, 255, 255),
            Degradado = new Color[] { Color.FromArgb(92, 136, 255), Color.FromArgb(150, 118, 250) },
            TextoDegradado = Color.White,
            Verde = Color.FromArgb(30, 158, 102), Amarillo = Color.FromArgb(222, 136, 36), Rojo = Color.FromArgb(222, 70, 96), Gris = Color.FromArgb(146, 152, 168),
            TextoOk = Color.FromArgb(30, 150, 98), TextoAviso = Color.FromArgb(200, 120, 20), TextoMal = Color.FromArgb(210, 60, 86),
            ControlFondo = Color.FromArgb(250, 251, 254), ControlBorde = Color.FromArgb(206, 212, 226), MenuFondo = Color.FromArgb(246, 247, 252),
            Radio = 14, FuenteTexto = "Segoe UI Variable Text", FuenteFuerte = "Segoe UI Variable Text Semibold",
            FuenteNumeros = "Segoe UI Variable Display", EstiloNumeros = FontStyle.Bold
        };

        // Del inicio del video: cristal violeta sobre degradado rosa y azul con texto blanco
        public static readonly Tema CristalVioleta = new Tema
        {
            Clave = "violeta", Nombre = "Cristal violeta", Oscuro = true, Ventanas = true,
            Fondo = Color.FromArgb(70, 46, 138),
            Manchas = new Color[] { Color.FromArgb(240, 138, 205), Color.FromArgb(96, 136, 255), Color.FromArgb(136, 80, 228), Color.FromArgb(205, 92, 214) },
            AlfaManchas = 185,
            Panel = Color.FromArgb(70, 36, 18, 84), BordePanel = Color.FromArgb(70, 255, 255, 255),
            Texto = Color.FromArgb(252, 250, 255), TextoTenue = Color.FromArgb(218, 208, 242), Separador = Color.FromArgb(40, 255, 255, 255),
            Acento = Color.FromArgb(244, 186, 255), Grafica = Color.FromArgb(50, 30, 12, 70),
            Degradado = new Color[] { Color.FromArgb(255, 172, 222), Color.FromArgb(152, 172, 255) },
            TextoDegradado = Color.FromArgb(52, 26, 92),
            Verde = Color.FromArgb(130, 240, 185), Amarillo = Color.FromArgb(255, 222, 120), Rojo = Color.FromArgb(255, 125, 160), Gris = Color.FromArgb(196, 184, 226),
            TextoOk = Color.FromArgb(140, 245, 190), TextoAviso = Color.FromArgb(255, 222, 120), TextoMal = Color.FromArgb(255, 140, 170),
            ControlFondo = Color.FromArgb(62, 40, 118), ControlBorde = Color.FromArgb(132, 102, 204), MenuFondo = Color.FromArgb(66, 42, 124),
            Radio = 14, FuenteTexto = "Segoe UI Variable Text", FuenteFuerte = "Segoe UI Variable Text Semibold",
            FuenteNumeros = "Segoe UI Variable Display", EstiloNumeros = FontStyle.Bold
        };

        public static readonly Tema[] Todos = { Clasico, Aurora, CristalClaro, CristalVioleta };
        public static Tema Actual = Clasico;

        public static Tema Buscar(string clave)
        {
            foreach (Tema t in Todos)
                if (string.Equals(t.Clave, clave, StringComparison.OrdinalIgnoreCase)) return t;
            return Clasico;
        }
    }

    // Dibujo y estilo compartidos por el widget, el menú y las ventanas
    static class Estilo
    {
        public static Color Mezclar(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // Fuente del tema; si esa tipografía no está instalada, Segoe UI
        public static Font Fuente(string familia, float tam, FontStyle estilo, GraphicsUnit unidad)
        {
            Font f = new Font(familia, tam, estilo, unidad);
            if (string.Equals(f.Name, familia, StringComparison.OrdinalIgnoreCase)) return f;
            f.Dispose();
            return new Font("Segoe UI", tam, estilo, unidad);
        }

        // Fondo del tema: color base, resplandores que se desplazan despacio según "fase" y, en Aurora, estrellitas
        public static void PintarFondo(Graphics g, RectangleF r, Tema t, double fase)
        {
            PintarManchas(g, r, t, fase);
            if (t.Estrellas) PintarEstrellas(g, r, fase);
        }

        // Los resplandores son degradados muy suaves: el widget los pinta en una imagen pequeña y la estira,
        // así la animación cuesta muy poco
        public static void PintarManchas(Graphics g, RectangleF r, Tema t, double fase)
        {
            using (SolidBrush b = new SolidBrush(t.Fondo)) g.FillRectangle(b, r);
            if (t.Manchas == null) return;
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < t.Manchas.Length; i++)
            {
                PointF c = CentroMancha(i, fase, r);
                float rad = RadioMancha(i, r);
                PintarMancha(g, new RectangleF(c.X - rad, c.Y - rad, rad * 2, rad * 2), t, i);
            }
            g.SmoothingMode = antes;
        }

        // Cada resplandor se mueve en una curva distinta y lenta
        public static PointF CentroMancha(int i, double fase, RectangleF r)
        {
            double a = fase * (0.6 + 0.17 * i) + i * 1.9;
            return new PointF(r.Left + r.Width * (float)(0.5 + 0.45 * Math.Sin(a)), r.Top + r.Height * (float)(0.5 + 0.45 * Math.Cos(a * 0.8 + i)));
        }

        public static float RadioMancha(int i, RectangleF r) { return Math.Max(r.Width, r.Height) * (0.5f + 0.08f * i); }

        // Un resplandor: color en el centro que se desvanece hasta transparente en el borde
        public static void PintarMancha(Graphics g, RectangleF elipse, Tema t, int i)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(elipse);
                using (PathGradientBrush pg = new PathGradientBrush(p))
                {
                    pg.CenterColor = Color.FromArgb(t.AlfaManchas, t.Manchas[i]);
                    pg.SurroundColors = new Color[] { Color.FromArgb(0, t.Manchas[i]) };
                    g.FillPath(pg, p);
                }
            }
        }

        public static void PintarEstrellas(Graphics g, RectangleF r, double fase)
        {
            SmoothingMode antes = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Random rnd = new Random(7); // siempre las mismas posiciones: solo titilan
            for (int i = 0; i < 26; i++)
            {
                float x = r.Left + (float)rnd.NextDouble() * r.Width, y = r.Top + (float)rnd.NextDouble() * r.Height;
                float d = 0.8f + (float)rnd.NextDouble() * 1.4f;
                int alfa = (int)(90 + 110 * (0.5 + 0.5 * Math.Sin(fase * 6 + i * 1.7)));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alfa, 255, 255, 255))) g.FillEllipse(b, x, y, d, d);
            }
            g.SmoothingMode = antes;
        }

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // Barra de título del color del tema (Windows 11) y modo oscuro para los temas oscuros
        public static void BarraTitulo(IntPtr ventana, Tema t)
        {
            int oscuro = t.Oscuro ? 1 : 0, titulo = ColorRef(t.MenuFondo), texto = ColorRef(t.Texto), borde = ColorRef(t.ControlBorde);
            Native.DwmSetWindowAttribute(ventana, 20, ref oscuro, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
            Native.DwmSetWindowAttribute(ventana, 35, ref titulo, 4); // DWMWA_CAPTION_COLOR
            Native.DwmSetWindowAttribute(ventana, 36, ref texto, 4);  // DWMWA_TEXT_COLOR
            Native.DwmSetWindowAttribute(ventana, 34, ref borde, 4);  // DWMWA_BORDER_COLOR
        }

        // Aplica el tema a todos los controles de una ventana
        public static void Controles(Control raiz, Tema t, Button principal)
        {
            foreach (Control c in raiz.Controls)
            {
                if (c is Label)
                {
                    c.BackColor = Color.Transparent;
                    if ("titulo".Equals(c.Tag)) c.ForeColor = t.Acento;
                    else if (c.ForeColor == SystemColors.GrayText) c.ForeColor = t.TextoTenue;
                    else if (c.ForeColor == SystemColors.ControlText) c.ForeColor = t.Texto;
                }
                else if (c is CheckBox) { c.BackColor = Color.Transparent; c.ForeColor = t.Texto; }
                else if (c is Button) Boton((Button)c, t, c == principal || "principal".Equals(c.Tag));
                else if (c is TextBox)
                {
                    ((TextBox)c).BorderStyle = BorderStyle.FixedSingle;
                    c.BackColor = t.ControlFondo;
                    c.ForeColor = t.Texto;
                }
                else if (c is ComboBox)
                {
                    ((ComboBox)c).FlatStyle = FlatStyle.Flat;
                    c.BackColor = t.ControlFondo;
                    c.ForeColor = t.Texto;
                    if (t.Oscuro) Native.SetWindowTheme(c.Handle, "DarkMode_CFD", null);
                }
                else if (c is ListBox)
                {
                    ((ListBox)c).BorderStyle = BorderStyle.FixedSingle;
                    c.BackColor = t.ControlFondo;
                    c.ForeColor = t.Texto;
                    if (t.Oscuro) Native.SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                }
                else if (c is ListView) Lista((ListView)c, t);
                else if (c is ProgressBar)
                {
                    Native.SetWindowTheme(c.Handle, "", ""); // estilo clásico: así respeta los colores
                    c.ForeColor = t.Acento;
                    c.BackColor = t.ControlFondo;
                }
                else if (c is TrackBar) c.BackColor = t.Fondo;
                else if (c is Panel) c.BackColor = Color.Transparent;
                if (c.HasChildren && !(c is ComboBox)) Controles(c, t, principal);
            }
        }

        static void Lista(ListView lv, Tema t)
        {
            lv.BackColor = t.ControlFondo;
            lv.ForeColor = t.Texto;
            lv.BorderStyle = BorderStyle.FixedSingle;
            lv.OwnerDraw = true;
            Color cabecera = Mezclar(t.ControlFondo, t.Acento, 0.18);
            lv.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(cabecera)) e.Graphics.FillRectangle(b, e.Bounds);
                Rectangle r = e.Bounds;
                r.Inflate(-3, 0);
                TextFormatFlags f = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                                    (e.Header.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, r, t.TextoTenue, f);
            };
            lv.DrawItem += delegate(object s, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
            lv.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
            if (lv.Columns.Count > 0) lv.Columns[lv.Columns.Count - 1].Width = -2; // la última columna llena el resto
            if (t.Oscuro) Native.SetWindowTheme(lv.Handle, "DarkMode_Explorer", null);
        }

        // Botones en forma de píldora; el principal con el degradado del tema
        static void Boton(Button b, Tema t, bool principal)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.Transparent;
            b.FlatAppearance.MouseDownBackColor = Color.Transparent;
            b.BackColor = Color.Transparent;
            b.ForeColor = t.Texto;
            b.Padding = new Padding(10, 3, 10, 3);
            b.Cursor = Cursors.Hand;
            b.MouseEnter += delegate { b.Invalidate(); };
            b.MouseLeave += delegate { b.Invalidate(); };
            b.EnabledChanged += delegate { b.Invalidate(); };
            b.Paint += delegate(object s, PaintEventArgs e) { DibujarBoton(b, e.Graphics, t, principal); };
        }

        static void DibujarBoton(Button b, Graphics g, Tema t, bool principal)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = b.ClientRectangle;
            r.Width -= 1;
            r.Height -= 1;
            bool sobre = b.Enabled && b.ClientRectangle.Contains(b.PointToClient(Cursor.Position));
            bool degradado = principal && t.Degradado != null && b.Enabled;
            using (GraphicsPath p = WidgetForm.Redondeado(r, r.Height / 2f))
            {
                Brush relleno;
                if (degradado)
                {
                    LinearGradientBrush lg = new LinearGradientBrush(r, t.Degradado[0], t.Degradado[t.Degradado.Length - 1], LinearGradientMode.Horizontal);
                    if (t.Degradado.Length > 2)
                    {
                        ColorBlend mezcla = new ColorBlend(t.Degradado.Length);
                        mezcla.Colors = t.Degradado;
                        float[] pos = new float[t.Degradado.Length];
                        for (int i = 0; i < pos.Length; i++) pos[i] = i / (float)(pos.Length - 1);
                        mezcla.Positions = pos;
                        lg.InterpolationColors = mezcla;
                    }
                    relleno = lg;
                }
                else relleno = new SolidBrush(sobre ? Mezclar(t.ControlFondo, t.Acento, 0.25) : t.ControlFondo);
                using (relleno) g.FillPath(relleno, p);
                if (!degradado)
                    using (Pen borde = new Pen(sobre ? t.Acento : t.ControlBorde)) g.DrawPath(borde, p);
                else if (sobre)
                    using (Pen borde = new Pen(Color.FromArgb(160, 255, 255, 255), 1.5f)) g.DrawPath(borde, p);
            }
            Color texto = !b.Enabled ? t.TextoTenue : degradado ? t.TextoDegradado : t.Texto;
            TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, texto,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // Ventanas de la app (configuración y pruebas): con los temas nuevos usan el fondo, los colores y la barra de título del tema
    class VentanaTema : Form
    {
        protected readonly Tema tema = Tema.Actual;

        public VentanaTema()
        {
            if (!tema.Ventanas) return;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            BackColor = tema.Fondo;
            ForeColor = tema.Texto;
        }

        // Se llama al final del constructor de cada ventana, cuando ya están todos los controles
        protected void AplicarTema()
        {
            if (!tema.Ventanas) return;
            Font = Estilo.Fuente(tema.FuenteTexto, 9f, FontStyle.Regular, GraphicsUnit.Point);
            Estilo.Controles(this, tema, AcceptButton as Button);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (tema.Ventanas) Estilo.BarraTitulo(Handle, tema);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!tema.Ventanas) { base.OnPaintBackground(e); return; }
            Estilo.PintarFondo(e.Graphics, ClientRectangle, tema, 1.7); // en las ventanas el fondo queda quieto
        }

        // El control deslizante de Windows no admite fondo transparente: se le pone el color que tiene el fondo justo detrás
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!tema.Ventanas) return;
            List<TrackBar> barras = new List<TrackBar>();
            BuscarBarras(this, barras);
            if (barras.Count == 0 || ClientSize.Width < 1 || ClientSize.Height < 1) return;
            using (Bitmap bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                using (Graphics g = Graphics.FromImage(bmp)) Estilo.PintarFondo(g, new RectangleF(0, 0, bmp.Width, bmp.Height), tema, 1.7);
                foreach (TrackBar tb in barras)
                {
                    Point p = PointToClient(tb.Parent.PointToScreen(new Point(tb.Left + tb.Width / 2, tb.Top + tb.Height / 2)));
                    if (p.X >= 0 && p.Y >= 0 && p.X < bmp.Width && p.Y < bmp.Height) tb.BackColor = bmp.GetPixel(p.X, p.Y);
                }
            }
        }

        static void BuscarBarras(Control c, List<TrackBar> lista)
        {
            foreach (Control h in c.Controls)
            {
                if (h is TrackBar) lista.Add((TrackBar)h);
                else if (h.HasChildren) BuscarBarras(h, lista);
            }
        }
    }

    // Menú de la bandeja con los colores del tema
    class RenderMenu : ToolStripProfessionalRenderer
    {
        readonly Tema t;

        public RenderMenu(Tema tema) : base(new ColoresMenu(tema)) { t = tema; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? t.Texto : t.TextoTenue;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = t.Texto;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            r.Inflate(2, 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = WidgetForm.Redondeado(r, 4))
            using (SolidBrush b = new SolidBrush(Estilo.Mezclar(t.MenuFondo, t.Acento, 0.45))) e.Graphics.FillPath(b, p);
            using (Pen pen = new Pen(t.Texto, 2f))
                e.Graphics.DrawLines(pen, new PointF[] {
                    new PointF(r.Left + r.Width * 0.26f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.44f, r.Top + r.Height * 0.70f),
                    new PointF(r.Left + r.Width * 0.76f, r.Top + r.Height * 0.32f) });
        }
    }

    class ColoresMenu : ProfessionalColorTable
    {
        readonly Color fondo, seleccion, borde;

        public ColoresMenu(Tema t)
        {
            UseSystemColors = false;
            fondo = t.MenuFondo;
            seleccion = Estilo.Mezclar(t.MenuFondo, t.Acento, t.Oscuro ? 0.32 : 0.2);
            borde = t.ControlBorde;
        }

        public override Color ToolStripDropDownBackground { get { return fondo; } }
        public override Color ImageMarginGradientBegin { get { return fondo; } }
        public override Color ImageMarginGradientMiddle { get { return fondo; } }
        public override Color ImageMarginGradientEnd { get { return fondo; } }
        public override Color MenuBorder { get { return borde; } }
        public override Color MenuItemBorder { get { return seleccion; } }
        public override Color MenuItemSelected { get { return seleccion; } }
        public override Color MenuItemSelectedGradientBegin { get { return seleccion; } }
        public override Color MenuItemSelectedGradientEnd { get { return seleccion; } }
        public override Color MenuItemPressedGradientBegin { get { return seleccion; } }
        public override Color MenuItemPressedGradientEnd { get { return seleccion; } }
        public override Color SeparatorDark { get { return borde; } }
        public override Color SeparatorLight { get { return fondo; } }
        public override Color CheckBackground { get { return seleccion; } }
        public override Color CheckSelectedBackground { get { return seleccion; } }
        public override Color CheckPressedBackground { get { return seleccion; } }
    }

    // Lee los encabezados de los paquetes UDP del stream (WebRTC/RTP de GeForce NOW o xCloud) para medir la pérdida,
    // los cortes y el ping reales, como los ve el propio juego. Necesita administrador (socket "raw").
    // Solo mira cabeceras: número de paquete, SSRC, marcas de tiempo y los mensajes STUN; el video va cifrado y no se toca.
    class AnalizadorStream
    {
        public class Resumen
        {
            public bool Activo;           // hay un stream en curso
            public string Remoto = "";    // IP del servidor del stream
            public long Segundo;          // contador que avanza una vez por segundo
            public double PerdidaSeg, Perdida30, Perdida60;
            public double Mbps;
            public int Cortes;            // congelones de 250 ms o más en los últimos 60 s
            public int CorteMax;          // el más largo, en ms
            public int Rtt = -1;          // ping medido dentro del stream (STUN); -1 = sin dato reciente
            public double JitterMs;
            public long PaquetesVistos;   // paquetes UDP leídos (para saber que el análisis funciona)
        }

        class Flujo
        {
            public uint Ip;
            public int PuertoRemoto, PuertoLocal;
            public int RtpSeg;            // paquetes RTP entrantes en el segundo actual
            public int SegundosSinDatos;
        }

        class Rtp
        {
            public long ExtMax, ExtInicio;
            public int MaxSeq, Ciclos, RecibidosSeg;
            public double Jitter;         // en unidades del reloj de 90 kHz
            public int TransitoPrevio;
            public bool HayTransito;
        }

        const int Hist = 60;
        static readonly double Frecuencia = Stopwatch.Frequency;

        readonly object bloqueo = new object();
        readonly List<Socket> sockets = new List<Socket>();
        string ipsUsadas = "";
        public string Error;

        readonly Dictionary<long, Flujo> flujos = new Dictionary<long, Flujo>();
        Flujo actual;
        readonly Dictionary<uint, Rtp> ssrcs = new Dictionary<uint, Rtp>();
        readonly Dictionary<string, long> stunPendientes = new Dictionary<string, long>();
        int rtt = -1;
        long rttCuando, ultimoPaquete, bytesSeg, paquetesVistos, ultimoTic, segundo;
        int cortesSeg, corteMaxSeg;
        readonly int[] esperadosH = new int[Hist], perdidosH = new int[Hist], cortesH = new int[Hist], corteMaxH = new int[Hist];
        int indice, llenos;
        Resumen ultimo = new Resumen();

        public bool Funciona { get { lock (bloqueo) return sockets.Count > 0; } }

        // Abre un socket "raw" por cada IP local; cada uno recibe copia de todos los paquetes de su interfaz
        public bool Iniciar()
        {
            Cerrar();
            List<IPAddress> ips = Native.IPsLocales();
            ipsUsadas = Clave(ips);
            Error = ips.Count == 0 ? "sin conexión de red" : null;
            foreach (IPAddress ip in ips)
            {
                try
                {
                    Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
                    s.Bind(new IPEndPoint(ip, 0));
                    s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.HeaderIncluded, true);
                    s.ReceiveBufferSize = 4 << 20;
                    // RCVALL_IPLEVEL (3): solo el tráfico de este equipo; si no está disponible, RCVALL_ON (1)
                    try { s.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(3), new byte[4]); }
                    catch (SocketException) { s.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), new byte[4]); }
                    uint local = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
                    Socket sock = s;
                    Thread t = new Thread(delegate () { Bucle(sock, local); });
                    t.IsBackground = true;
                    t.Start();
                    lock (bloqueo) sockets.Add(s);
                }
                catch (Exception ex) { Error = ex.Message; }
            }
            return Funciona;
        }

        static string Clave(List<IPAddress> ips)
        {
            List<string> t = new List<string>();
            foreach (IPAddress i in ips) t.Add(i.ToString());
            t.Sort();
            return string.Join(",", t.ToArray());
        }

        // Si cambian las IP locales (Wi-Fi reconectado, VPN encendida o apagada) se vuelve a abrir la captura
        public bool Revisar()
        {
            if (Clave(Native.IPsLocales()) != ipsUsadas || !Funciona) return Iniciar();
            return true;
        }

        public void Cerrar()
        {
            lock (bloqueo)
            {
                foreach (Socket s in sockets) { try { s.Close(); } catch { } }
                sockets.Clear();
            }
        }

        void Bucle(Socket s, uint local)
        {
            byte[] buf = new byte[65536];
            while (true)
            {
                int n;
                try { n = s.Receive(buf); }
                catch { return; } // socket cerrado
                if (n > 0) Procesar(buf, n, local, Stopwatch.GetTimestamp());
            }
        }

        static bool EsLocal(uint ip)
        {
            uint a = ip & 0xFF, b = (ip >> 8) & 0xFF;
            return a == 0 || a == 10 || a == 127 || a >= 224 || (a == 172 && b >= 16 && b <= 31) || (a == 192 && b == 168) ||
                   (a == 169 && b == 254) || (a == 100 && b >= 64 && b <= 127);
        }

        static bool EsRtp(byte[] b, int ini, int largo)
        {
            if (largo < 12 || (b[ini] >> 6) != 2) return false;
            int tipo = b[ini + 1];
            return tipo < 200 || tipo > 204; // 200-204 son paquetes RTCP
        }

        static bool EsStun(byte[] b, int ini, int largo)
        {
            return largo >= 20 && (b[ini] & 0xC0) == 0 &&
                   b[ini + 4] == 0x21 && b[ini + 5] == 0x12 && b[ini + 6] == 0xA4 && b[ini + 7] == 0x42;
        }

        // Analiza un paquete IPv4 completo (con su cabecera) visto en la interfaz cuya IP es "local"
        public void Procesar(byte[] b, int n, uint local, long ahora)
        {
            if (n < 28 || (b[0] >> 4) != 4 || b[9] != 17) return; // solo IPv4 + UDP
            int ihl = (b[0] & 0x0F) * 4;
            if (n < ihl + 8) return;
            uint origen = BitConverter.ToUInt32(b, 12), destino = BitConverter.ToUInt32(b, 16);
            bool entrante = destino == local;
            if (!entrante && origen != local) return;
            uint remoto = entrante ? origen : destino;
            if (EsLocal(remoto)) return;
            int pOrigen = (b[ihl] << 8) | b[ihl + 1], pDestino = (b[ihl + 2] << 8) | b[ihl + 3];
            int pRemoto = entrante ? pOrigen : pDestino, pLocal = entrante ? pDestino : pOrigen;
            int ini = ihl + 8, largo = n - ini;
            int largoUdp = ((b[ihl + 4] << 8) | b[ihl + 5]) - 8;
            if (largoUdp >= 0 && largoUdp < largo) largo = largoUdp;

            lock (bloqueo)
            {
                paquetesVistos++;
                long clave = (long)(((ulong)remoto << 32) | ((ulong)(uint)pRemoto << 16) | (uint)pLocal);
                Flujo f;
                if (!flujos.TryGetValue(clave, out f))
                {
                    if (flujos.Count > 4000) return;
                    f = new Flujo();
                    f.Ip = remoto;
                    f.PuertoRemoto = pRemoto;
                    f.PuertoLocal = pLocal;
                    flujos[clave] = f;
                }
                if (entrante && EsRtp(b, ini, largo)) f.RtpSeg++;
                if (f == actual) ProcesarStream(b, ini, largo, entrante, ahora);
            }
        }

        void ProcesarStream(byte[] b, int ini, int largo, bool entrante, long ahora)
        {
            // Ping del stream: WebRTC envía cada pocos segundos un STUN "Binding request" y el servidor responde
            if (EsStun(b, ini, largo))
            {
                int tipo = (b[ini] << 8) | b[ini + 1];
                string tx = Convert.ToBase64String(b, ini + 8, 12);
                if (!entrante && tipo == 0x0001)
                {
                    if (stunPendientes.Count > 64) stunPendientes.Clear();
                    stunPendientes[tx] = ahora;
                }
                else if (entrante && tipo == 0x0101)
                {
                    long t0;
                    if (stunPendientes.TryGetValue(tx, out t0))
                    {
                        rtt = (int)Math.Round((ahora - t0) * 1000.0 / Frecuencia);
                        rttCuando = ahora;
                        stunPendientes.Remove(tx);
                    }
                }
                return;
            }
            if (!entrante) return;

            // Corte: hueco largo sin ningún paquete del servidor (la imagen se congela)
            if (ultimoPaquete != 0)
            {
                int hueco = (int)((ahora - ultimoPaquete) * 1000 / Frecuencia);
                if (hueco >= 250)
                {
                    cortesSeg++;
                    if (hueco > corteMaxSeg) corteMaxSeg = hueco;
                }
            }
            ultimoPaquete = ahora;
            bytesSeg += largo;

            if (!EsRtp(b, ini, largo)) return;
            int seq = (b[ini + 2] << 8) | b[ini + 3];
            uint ts = ((uint)b[ini + 4] << 24) | ((uint)b[ini + 5] << 16) | ((uint)b[ini + 6] << 8) | b[ini + 7];
            uint ssrc = ((uint)b[ini + 8] << 24) | ((uint)b[ini + 9] << 16) | ((uint)b[ini + 10] << 8) | b[ini + 11];

            Rtp r;
            if (!ssrcs.TryGetValue(ssrc, out r))
            {
                if (ssrcs.Count > 32) ssrcs.Clear();
                r = new Rtp();
                r.MaxSeq = seq;
                r.ExtMax = seq;
                r.ExtInicio = seq - 1;
                ssrcs[ssrc] = r;
            }
            else
            {
                int avance = (seq - r.MaxSeq + 65536) % 65536;
                if (avance == 0) return; // duplicado
                if (avance < 3000)
                {
                    if (seq < r.MaxSeq) r.Ciclos++; // la numeración dio la vuelta (65535 -> 0)
                    r.MaxSeq = seq;
                    r.ExtMax = (long)r.Ciclos * 65536 + seq;
                }
                else if (avance <= 65536 - 3000)
                {
                    // Salto enorme: el emisor reinició la numeración
                    r.Ciclos = 0;
                    r.MaxSeq = seq;
                    r.ExtMax = seq;
                    r.ExtInicio = seq - 1;
                    r.RecibidosSeg = 0;
                }
                // si no, es un paquete atrasado (desordenado) y cuenta como recibido
            }
            r.RecibidosSeg++;

            // Jitter según RFC 3550, con el reloj de 90 kHz del video
            uint llegada = (uint)(long)(ahora * 90000.0 / Frecuencia);
            int transito = unchecked((int)(llegada - ts));
            if (r.HayTransito)
            {
                long d = Math.Abs((long)unchecked(transito - r.TransitoPrevio));
                if (d < 90000) r.Jitter += (d - r.Jitter) / 16.0;
            }
            r.TransitoPrevio = transito;
            r.HayTransito = true;
        }

        double PerdidaUltimos(int segundos)
        {
            long esperados = 0, perdidos = 0;
            for (int k = 1; k <= Math.Min(segundos, llenos); k++)
            {
                int i = (indice - k + Hist) % Hist;
                esperados += esperadosH[i];
                perdidos += perdidosH[i];
            }
            return esperados > 0 ? perdidos * 100.0 / esperados : 0;
        }

        void CambiarStream(Flujo f)
        {
            actual = f;
            ssrcs.Clear();
            stunPendientes.Clear();
            rtt = -1;
            ultimoPaquete = 0;
            indice = 0;
            llenos = 0;
            bytesSeg = 0;
            cortesSeg = 0;
            corteMaxSeg = 0;
        }

        // Cierra el segundo en curso y devuelve el resumen (si se llama antes de tiempo devuelve el último)
        public Resumen Tic() { return Tic(Stopwatch.GetTimestamp()); }

        public Resumen Tic(long ahora)
        {
            lock (bloqueo)
            {
                if (ultimoTic != 0 && (ahora - ultimoTic) < Frecuencia * 0.9) return ultimo;
                double segundos = ultimoTic == 0 ? 1 : (ahora - ultimoTic) / Frecuencia;
                ultimoTic = ahora;
                segundo++;

                // El stream es el flujo con más paquetes RTP entrantes (al menos 100 por segundo)
                Flujo mejor = null;
                foreach (Flujo f in flujos.Values)
                    if (f.RtpSeg >= 100 && (mejor == null || f.RtpSeg > mejor.RtpSeg)) mejor = f;
                if (actual != null)
                {
                    if (actual.RtpSeg > 0 && (mejor == null || mejor.RtpSeg < actual.RtpSeg * 2)) mejor = actual;
                    else if (mejor == null && actual.SegundosSinDatos < 5) mejor = actual; // congelado: se espera un poco
                }
                if (mejor != actual) CambiarStream(mejor);

                // Pérdida del segundo en el SSRC principal (el de más paquetes: el video)
                Rtp principal = null;
                foreach (Rtp r in ssrcs.Values)
                    if (principal == null || r.RecibidosSeg > principal.RecibidosSeg) principal = r;
                int esperados = 0, perdidos = 0;
                if (principal != null)
                {
                    esperados = (int)Math.Max(0, principal.ExtMax - principal.ExtInicio);
                    perdidos = Math.Max(0, esperados - principal.RecibidosSeg);
                }
                foreach (Rtp r in ssrcs.Values)
                {
                    r.ExtInicio = r.ExtMax;
                    r.RecibidosSeg = 0;
                }

                Resumen res = new Resumen();
                res.Segundo = segundo;
                res.PaquetesVistos = paquetesVistos;
                if (actual != null)
                {
                    esperadosH[indice] = esperados;
                    perdidosH[indice] = perdidos;
                    cortesH[indice] = cortesSeg;
                    corteMaxH[indice] = corteMaxSeg;
                    indice = (indice + 1) % Hist;
                    if (llenos < Hist) llenos++;

                    res.Activo = true;
                    res.Remoto = new IPAddress((long)actual.Ip).ToString();
                    res.PerdidaSeg = esperados > 0 ? perdidos * 100.0 / esperados : 0;
                    res.Perdida30 = PerdidaUltimos(30);
                    res.Perdida60 = PerdidaUltimos(60);
                    res.Mbps = bytesSeg * 8 / segundos / 1e6;
                    for (int k = 1; k <= llenos; k++)
                    {
                        int i = (indice - k + Hist) % Hist;
                        res.Cortes += cortesH[i];
                        if (corteMaxH[i] > res.CorteMax) res.CorteMax = corteMaxH[i];
                    }
                    if (rtt >= 0 && (ahora - rttCuando) < Frecuencia * 15) res.Rtt = rtt;
                    if (principal != null) res.JitterMs = principal.Jitter / 90.0;
                }

                // Reinicia los contadores del segundo y olvida los flujos inactivos
                List<long> viejos = null;
                foreach (KeyValuePair<long, Flujo> kv in flujos)
                {
                    Flujo f = kv.Value;
                    if (f.RtpSeg == 0) f.SegundosSinDatos++;
                    else f.SegundosSinDatos = 0;
                    f.RtpSeg = 0;
                    if (f.SegundosSinDatos > 10 && f != actual)
                    {
                        if (viejos == null) viejos = new List<long>();
                        viejos.Add(kv.Key);
                    }
                }
                if (viejos != null) foreach (long k in viejos) flujos.Remove(k);
                bytesSeg = 0;
                cortesSeg = 0;
                corteMaxSeg = 0;
                ultimo = res;
                return res;
            }
        }
    }

    // Mide el ratón con Raw Input (solo tiempos entre reportes y tamaño del movimiento, nunca posiciones ni teclas)
    // y las pausas del PC (cuando Windows tarda en atender a un hilo, ratón y teclado responden tarde)
    class MonitorRaton
    {
        public class Resumen
        {
            public int Hz;                // frecuencia de reporte del ratón principal (0 = todavía no se midió)
            public int Tirones;           // huecos anormales en medio de un movimiento rápido, últimos 60 s
            public int TironMax;          // ms
            public int PausaPc;           // pausa más larga del PC en los últimos 60 s (ms)
            public int PausasPc;          // pausas de 15 ms o más en los últimos 60 s
            public bool Moviendo;
            public int[] Grafica = new int[0]; // pausa máxima del PC por segundo; -1 = hubo un tirón
        }

        class Dispositivo
        {
            public long Ultimo;
            public int UltimoMov, EventosSeg;
            public long Eventos;
            public double Intervalo = 8;  // ms entre reportes, suavizado
        }

        const int Hist = 60;
        static readonly double Frecuencia = Stopwatch.Frequency;
        static readonly int[] Frecuencias = { 125, 250, 500, 1000, 2000, 4000, 8000 };

        readonly object bloqueo = new object();
        readonly Dictionary<IntPtr, Dispositivo> dispositivos = new Dictionary<IntPtr, Dispositivo>();
        readonly IntPtr bufer = Marshal.AllocHGlobal(256);
        readonly int[] tironesH = new int[Hist], tironMaxH = new int[Hist], pausaH = new int[Hist], pausasH = new int[Hist];
        int indice, llenos, hz;
        int tironesSeg, tironMaxSeg, pausaSeg, pausasSeg;
        long ultimoTic;
        bool hiloRaw;
        volatile bool midiendoPausas;
        VentanaRaw ventana; // se guarda para que el recolector de basura no la destruya
        Resumen ultimo = new Resumen();

        public void Iniciar()
        {
            if (!hiloRaw)
            {
                hiloRaw = true;
                Thread t = new Thread(delegate ()
                {
                    ventana = new VentanaRaw(this);
                    Application.Run(); // bucle de mensajes propio para recibir WM_INPUT sin esperar a la interfaz
                });
                t.IsBackground = true;
                t.Priority = ThreadPriority.Highest;
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
            if (!midiendoPausas)
            {
                midiendoPausas = true;
                Thread p = new Thread(MedirPausas);
                p.IsBackground = true;
                p.Priority = ThreadPriority.Highest;
                p.Start();
            }
        }

        // Apaga el temporizador de 1 ms (el ratón sigue registrado, pero no gasta nada si no se lee)
        public void Parar() { midiendoPausas = false; }

        void MedirPausas()
        {
            Native.timeBeginPeriod(1);
            try
            {
                while (midiendoPausas)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    Thread.Sleep(1);
                    int ms = (int)((Stopwatch.GetTimestamp() - t0) * 1000 / Frecuencia);
                    lock (bloqueo)
                    {
                        if (ms > pausaSeg) pausaSeg = ms;
                        if (ms >= 15) pausasSeg++;
                    }
                }
            }
            finally { Native.timeEndPeriod(1); }
        }

        public void Entrada(IntPtr hRaw)
        {
            IntPtr disp;
            int mov;
            if (LeerRaton(hRaw, bufer, out disp, out mov)) Movimiento(disp, mov, Stopwatch.GetTimestamp());
        }

        // Lee un WM_INPUT del ratón: qué dispositivo fue y cuánto se movió (en cuentas del sensor)
        public static bool LeerRaton(IntPtr hRaw, IntPtr bufer, out IntPtr disp, out int mov)
        {
            disp = IntPtr.Zero;
            mov = 0;
            int cabecera = 8 + 2 * IntPtr.Size; // RAWINPUTHEADER
            uint tam = 256;
            uint r = Native.GetRawInputData(hRaw, Native.RID_INPUT, bufer, ref tam, (uint)cabecera);
            if (r == uint.MaxValue || r < cabecera + 24) return false;
            if (Marshal.ReadInt32(bufer, 0) != Native.RIM_TYPEMOUSE) return false;
            if ((Marshal.ReadInt16(bufer, cabecera) & 1) != 0) return false; // movimiento absoluto (tableta, escritorio remoto)
            disp = Marshal.ReadIntPtr(bufer, 8);
            mov = Math.Abs(Marshal.ReadInt32(bufer, cabecera + 12)) + Math.Abs(Marshal.ReadInt32(bufer, cabecera + 16));
            return true;
        }

        // Marca del ratón según el fabricante (VID) de su nombre de dispositivo; sin VID suele ser el touchpad
        public static string NombreDispositivo(IntPtr disp)
        {
            try
            {
                uint tam = 0;
                Native.GetRawInputDeviceInfo(disp, Native.RIDI_DEVICENAME, null, ref tam);
                if (tam == 0) return "ratón";
                StringBuilder sb = new StringBuilder((int)tam + 1);
                Native.GetRawInputDeviceInfo(disp, Native.RIDI_DEVICENAME, sb, ref tam);
                string n = sb.ToString().ToUpperInvariant();
                if (n.Contains("VID_1532")) return "Razer";
                if (n.Contains("VID_046D")) return "Logitech";
                if (n.Contains("VID_1038")) return "SteelSeries";
                if (n.Contains("VID_1B1C")) return "Corsair";
                int i = n.IndexOf("VID_");
                if (i >= 0 && i + 8 <= n.Length) return "ratón USB (" + n.Substring(i, 8) + ")";
                return "touchpad u otro";
            }
            catch { return "ratón"; }
        }

        // La prueba de ratón se queda con los reportes mientras está abierta: al cerrarla se le devuelven al monitor
        public void Reanudar()
        {
            if (ventana != null) Native.PostMessage(ventana.Handle, VentanaRaw.WM_REGISTRAR, IntPtr.Zero, IntPtr.Zero);
        }

        // Un reporte del ratón: "mov" es cuánto se movió (en cuentas del sensor)
        public void Movimiento(IntPtr disp, int mov, long ahora)
        {
            if (mov == 0) return; // solo botones o rueda
            lock (bloqueo)
            {
                Dispositivo d;
                if (!dispositivos.TryGetValue(disp, out d)) { d = new Dispositivo(); dispositivos[disp] = d; }
                if (d.Ultimo != 0)
                {
                    double hueco = (ahora - d.Ultimo) * 1000.0 / Frecuencia;
                    // Tirón: un hueco grande en medio de un movimiento rápido (el ratón venía y siguió moviéndose)
                    if (hueco < 250 && d.UltimoMov >= 3 && mov >= 3 && hueco > Math.Max(8, d.Intervalo * 6))
                    {
                        tironesSeg++;
                        if (hueco > tironMaxSeg) tironMaxSeg = (int)hueco;
                    }
                    else if (hueco < 20) d.Intervalo = d.Intervalo * 0.98 + hueco * 0.02;
                }
                d.Ultimo = ahora;
                d.UltimoMov = mov;
                d.EventosSeg++;
                d.Eventos++;
            }
        }

        public static int Redondear(double hzMedido)
        {
            foreach (int f in Frecuencias)
                if (Math.Abs(hzMedido - f) <= f * 0.25) return f;
            return (int)Math.Round(hzMedido / 10) * 10;
        }

        public Resumen Tic() { return Tic(Stopwatch.GetTimestamp()); }

        public Resumen Tic(long ahora)
        {
            lock (bloqueo)
            {
                if (ultimoTic != 0 && (ahora - ultimoTic) < Frecuencia * 0.9) return ultimo;
                ultimoTic = ahora;

                // El ratón principal es el que más reportes mandó (el Razer, no el touchpad)
                Dispositivo principal = null;
                foreach (Dispositivo d in dispositivos.Values)
                    if (principal == null || d.Eventos > principal.Eventos) principal = d;
                if (principal != null && principal.EventosSeg >= 100) hz = Redondear(1000 / Math.Max(0.1, principal.Intervalo));

                tironesH[indice] = tironesSeg;
                tironMaxH[indice] = tironMaxSeg;
                pausaH[indice] = pausaSeg;
                pausasH[indice] = pausasSeg;
                indice = (indice + 1) % Hist;
                if (llenos < Hist) llenos++;

                Resumen res = new Resumen();
                res.Hz = hz;
                res.Moviendo = principal != null && principal.Ultimo != 0 && (ahora - principal.Ultimo) < Frecuencia * 2;
                res.Grafica = new int[llenos];
                for (int k = 0; k < llenos; k++)
                {
                    int i = (indice - llenos + k + Hist) % Hist;
                    res.Tirones += tironesH[i];
                    res.TironMax = Math.Max(res.TironMax, tironMaxH[i]);
                    res.PausaPc = Math.Max(res.PausaPc, pausaH[i]);
                    res.PausasPc += pausasH[i];
                    res.Grafica[k] = tironesH[i] > 0 ? -1 : pausaH[i];
                }

                foreach (Dispositivo d in dispositivos.Values) d.EventosSeg = 0;
                tironesSeg = 0;
                tironMaxSeg = 0;
                pausaSeg = 0;
                pausasSeg = 0;
                ultimo = res;
                return res;
            }
        }
    }

    // Ventana invisible que recibe los reportes del ratón (WM_INPUT) aunque la app no tenga el foco
    class VentanaRaw : NativeWindow
    {
        public const int WM_REGISTRAR = Native.WM_APP + 1; // vuelve a pedir los reportes (después de la prueba de ratón)
        readonly MonitorRaton monitor;

        public VentanaRaw(MonitorRaton m)
        {
            monitor = m;
            CreateHandle(new CreateParams());
            Native.RegistrarRaton(Handle, Native.RIDEV_INPUTSINK);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_INPUT) monitor.Entrada(m.LParam);
            else if (m.Msg == WM_REGISTRAR) Native.RegistrarRaton(Handle, Native.RIDEV_INPUTSINK);
            base.WndProc(ref m);
        }
    }

    // Últimos 60 valores, uno por segundo (gráfica de las filas del stream)
    class Historial
    {
        readonly int[] v = new int[60];
        int n, i;
        public long UltimoSegundo = -1;

        public void Agregar(int x)
        {
            v[i] = x;
            i = (i + 1) % 60;
            if (n < 60) n++;
        }

        public void Limpiar() { n = 0; i = 0; UltimoSegundo = -1; }

        public int[] Valores()
        {
            int[] r = new int[n];
            int ini = (i - n + 60) % 60;
            for (int k = 0; k < n; k++) r[k] = v[(ini + k) % 60];
            return r;
        }
    }

    // Lo que se pinta de cada fila del widget
    class Fila
    {
        public string Nombre = "", Corto = "", Sub = "";
        public bool Aviso;
        public Color Color = Calidad.Gris;
        public string Valor = "–", Unidad = "";
        public int[] Grafica = new int[0];        // ms por segundo; -1 = marca roja
        public string Detalle1 = "", Detalle2 = ""; // modo normal, bajo la gráfica
        public bool Detalle1Malo;
        public string Corto1 = "", Corto2 = "";     // modo compacto
    }

    class TrayContext : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string AppName = "MonitorPing";
        // Puerto TCP de la señalización de GeForce NOW: 49006 en la app de PC, 49100 en el navegador
        static readonly int[] PuertosGfn = { 49006, 49100 };
        static readonly int[] Opacidades = { 100, 90, 80, 70, 60, 50, 40 };
        // Ventanas del sistema que deben quedar por encima del widget: iconos ocultos (Windows 11 y 10), menús,
        // Inicio, notificaciones, configuración rápida, miniaturas de la barra y tooltips
        static readonly string[] ClasesSistema = {
            "TopLevelWindowForOverflowXamlIsland", "NotifyIconOverflowWindow", "Xaml_WindowedPopupClass",
            "Windows.UI.Core.CoreWindow", "ControlCenterWindow", "#32768", "TaskListThumbnailWnd", "tooltips_class32" };

        Config cfg;
        Native.WinEventDelegate alCambiarVentana; // se guarda para que el recolector de basura no lo borre
        IntPtr ganchoMostrar, ganchoPrimerPlano;
        NotifyIcon tray;
        ContextMenuStrip menu;
        ToolStripMenuItem itemWidget, itemBloquear, itemCompacto, itemOpacidad, itemEncima, itemInicio, itemAnalisis, itemTema, itemAnimado;
        WidgetForm widget;
        System.Windows.Forms.Timer timer;
        Sonda gfn = new Sonda(), xcloud = new Sonda(), eeuu = new Sonda(), router = new Sonda();
        SelectorZona selGfn = new SelectorZona(ListaZonas.Gfn), selXcloud = new SelectorZona(ListaZonas.Xcloud);
        AnalizadorStream analizador = new AnalizadorStream();
        MonitorRaton raton = new MonitorRaton();
        AnalizadorStream.Resumen stream = new AnalizadorStream.Resumen();
        MonitorRaton.Resumen estadoRaton = new MonitorRaton.Resumen();
        Historial histGfn = new Historial(), histXcloud = new Historial();
        bool analisisActivo, senalGfn, inicioWindows;
        string origenGfn = "", origenXcloud = "";
        string vpn; // nombre de la VPN por la que sale el tráfico, o null
        int tics;
        Icon icono;
        string claveIcono;
        ConfigForm formAbierto;
        PruebaRatonForm pruebaAbierta;
        PruebaPerdidaForm pruebaPerdida;
        RutaEnVivo ruta = new RutaEnVivo();
        RutaForm rutaForm;
        int cortesPrevios;
        DateTime inicioMinuto = DateTime.MinValue;
        long ultimoSegundoMinuto = -1;
        double sumaMbps;
        int segundosMinuto;

        public TrayContext()
        {
            cfg = Config.Cargar();
            Tema.Actual = Tema.Buscar(cfg.NombreTema);
            MigrarInicio();
            inicioWindows = InicioConWindows();

            menu = new ContextMenuStrip();
            itemWidget = new ToolStripMenuItem("Mostrar widget", null, delegate { AlternarWidget(); });
            itemBloquear = new ToolStripMenuItem("Bloquear widget (el ratón lo atraviesa)", null, delegate { cfg.Bloqueado = !cfg.Bloqueado; cfg.Guardar(); AplicarAspecto(); });
            itemCompacto = new ToolStripMenuItem("Modo compacto", null, delegate { cfg.Compacto = !cfg.Compacto; cfg.Guardar(); AplicarAspecto(); });
            itemOpacidad = new ToolStripMenuItem("Transparencia");
            foreach (int o in Opacidades)
            {
                int valor = o;
                ToolStripMenuItem it = new ToolStripMenuItem(o == 100 ? "Opaco (100 %)" : o + " %", null, delegate { cfg.Opacidad = valor; cfg.Guardar(); AplicarAspecto(); });
                it.Tag = valor;
                itemOpacidad.DropDownItems.Add(it);
            }
            itemTema = new ToolStripMenuItem("Tema");
            foreach (Tema t in Tema.Todos)
            {
                string clave = t.Clave;
                ToolStripMenuItem it = new ToolStripMenuItem(t.Nombre, null, delegate { cfg.NombreTema = clave; cfg.Guardar(); AplicarTema(); });
                it.Tag = clave;
                itemTema.DropDownItems.Add(it);
            }
            itemAnimado = new ToolStripMenuItem("Fondo animado", null, delegate { cfg.Animado = !cfg.Animado; cfg.Guardar(); AplicarTema(); });
            itemTema.DropDownItems.Add(new ToolStripSeparator());
            itemTema.DropDownItems.Add(itemAnimado);
            itemEncima = new ToolStripMenuItem("Siempre encima", null, delegate { cfg.Encima = !cfg.Encima; cfg.Guardar(); AplicarAspecto(); });
            itemInicio = new ToolStripMenuItem("Iniciar con Windows", null, delegate { FijarInicioConWindows(!inicioWindows); });
            itemAnalisis = new ToolStripMenuItem("");
            itemAnalisis.Enabled = false;
            menu.Items.Add(itemWidget);
            menu.Items.Add(itemBloquear);
            menu.Items.Add(itemCompacto);
            menu.Items.Add(itemOpacidad);
            menu.Items.Add(itemTema);
            menu.Items.Add(itemEncima);
            menu.Items.Add(itemInicio);
            menu.Items.Add("Configuración...", null, delegate { AbrirConfig(); });
            menu.Items.Add("Prueba de pérdida de paquetes...", null, delegate { AbrirPruebaPerdida(); });
            menu.Items.Add("Ruta en vivo (dónde se pierde)...", null, delegate { AbrirRuta(); });
            menu.Items.Add("Probar ratón...", null, delegate { AbrirPruebaRaton(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemAnalisis);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Salir", null, delegate { Salir(); });
            menu.Opening += delegate
            {
                itemWidget.Text = widget.Visible ? "Ocultar widget" : "Mostrar widget";
                itemBloquear.Text = cfg.Bloqueado ? "Desbloquear widget para moverlo" : "Bloquear widget (el ratón lo atraviesa)";
                itemCompacto.Checked = cfg.Compacto;
                itemEncima.Checked = cfg.Encima;
                itemInicio.Checked = inicioWindows;
                foreach (ToolStripMenuItem it in itemOpacidad.DropDownItems) it.Checked = (int)it.Tag == cfg.Opacidad;
                foreach (ToolStripItem it in itemTema.DropDownItems)
                    if (it is ToolStripMenuItem && it.Tag != null) ((ToolStripMenuItem)it).Checked = (string)it.Tag == Tema.Actual.Clave;
                itemAnimado.Checked = cfg.Animado;
                itemAnimado.Enabled = Tema.Actual.Moderno;
                itemAnalisis.Text = TextoAnalisis();
            };

            widget = new WidgetForm();
            widget.CambiarTema(cfg.Animado);
            EstiloMenu();
            widget.ContextMenuStrip = menu;
            widget.CerrarPulsado += delegate { widget.Hide(); };
            widget.Movido += delegate { cfg.WidgetX = widget.Left; cfg.WidgetY = widget.Top; cfg.Guardar(); };

            // Cuando se abre una ventana del sistema (iconos ocultos, menús, Inicio...) el widget se pone debajo al instante
            alCambiarVentana = AlCambiarVentana;
            ganchoMostrar = Native.SetWinEventHook(Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, IntPtr.Zero, alCambiarVentana, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            ganchoPrimerPlano = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, alCambiarVentana, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);

            tray = new NotifyIcon();
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) AlternarWidget(); };

            // Lectura del stream real: necesita administrador
            analisisActivo = Native.EsAdmin() && analizador.Iniciar();
            ruta.Iniciar();

            eeuu.FijarDestino(cfg.ServidorEEUU, 0, false);
            ActualizarRouter();
            ActualizarNubes();
            AplicarRaton();
            AplicarAspecto();
            ColocarWidget();
            Refrescar();
            tray.Visible = true;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 1000;
            timer.Tick += delegate { Tic(); };
            timer.Start();

            if (cfg.MostrarAlIniciar) MostrarWidget();
        }

        string TextoAnalisis()
        {
            if (!Native.EsAdmin()) return "Stream real: abre MonitorPing como administrador";
            if (!analisisActivo) return "Stream real: no disponible" + (analizador.Error != null ? " (" + analizador.Error + ")" : "");
            return "Stream real: activo · " + stream.PaquetesVistos.ToString("N0") + " paquetes leídos";
        }

        void Tic()
        {
            tics++;
            if (tics % 3 == 0) ActualizarNubes();
            if (tics % 10 == 0)
            {
                ActualizarRouter();
                if (Native.EsAdmin()) analisisActivo = analizador.Revisar();
            }
            Refrescar();
            // Algunos juegos a pantalla completa se ponen por encima: se reafirma el "siempre encima",
            // salvo que una ventana del sistema (iconos ocultos, un menú...) esté abierta encima del widget
            if (cfg.Encima && widget.Visible && !menu.Visible && formAbierto == null && pruebaAbierta == null)
            {
                IntPtr sistema = VentanaDelSistemaEncima();
                if (sistema != IntPtr.Zero) PonerDebajoDe(sistema);
                else Native.SetWindowPos(widget.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            }
        }

        void AlCambiarVentana(IntPtr gancho, uint evento, IntPtr hwnd, int idObjeto, int idHijo, uint hilo, uint tiempo)
        {
            if (idObjeto != 0 || hwnd == IntPtr.Zero || !widget.Visible) return; // solo ventanas, no cursores ni controles
            IntPtr raiz = Native.GetAncestor(hwnd, Native.GA_ROOT);
            if (raiz == IntPtr.Zero) raiz = hwnd;
            if (EsDelSistema(raiz) && TapaAlWidget(raiz)) PonerDebajoDe(raiz);
        }

        void PonerDebajoDe(IntPtr ventana)
        {
            Native.SetWindowPos(widget.Handle, ventana, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        static bool EsDelSistema(IntPtr hwnd)
        {
            StringBuilder clase = new StringBuilder(64);
            Native.GetClassName(hwnd, clase, clase.Capacity);
            return Array.IndexOf(ClasesSistema, clase.ToString()) >= 0;
        }

        // ¿La ventana se ve de verdad (no está oculta ni "camuflada" por Windows) y se cruza con el widget?
        bool TapaAlWidget(IntPtr hwnd)
        {
            if (!Native.IsWindowVisible(hwnd)) return false;
            int camuflada;
            if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out camuflada, 4) == 0 && camuflada != 0) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return false;
            Rectangle rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return rect.Width > 0 && rect.Height > 0 && rect.IntersectsWith(widget.Bounds);
        }

        IntPtr VentanaDelSistemaEncima()
        {
            IntPtr encontrada = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr h, IntPtr p)
            {
                if (EsDelSistema(h) && TapaAlWidget(h)) { encontrada = h; return false; }
                return true;
            }, IntPtr.Zero);
            return encontrada;
        }

        void ActualizarRouter()
        {
            IPAddress gw = Native.PuertaDeEnlace();
            router.FijarDestino(gw != null ? gw.ToString() : "", 0, false);
        }

        bool EsGfn(string ip) { return senalGfn || ip.StartsWith("66.22."); }

        void ActualizarNubes()
        {
            string vpnAntes = vpn;
            vpn = Native.VpnActiva();
            bool forzar = vpnAntes != null && vpn == null; // al apagar la VPN se vuelven a medir las zonas
            Native.ConexionTcp senal = BuscarSesionGfn();
            senalGfn = senal != null;
            bool streamGfn = stream.Activo && EsGfn(stream.Remoto);
            bool streamXcloud = stream.Activo && !streamGfn;

            if (!cfg.VerGfn) { origenGfn = ""; gfn.FijarDestino("", 0, false); }
            else if (cfg.GfnManual != "") { origenGfn = "Manual"; gfn.FijarDestino(cfg.GfnManual, 0, false); }
            else if (streamGfn) { origenGfn = "Jugando"; gfn.FijarDestino(stream.Remoto, 443, false); }
            else if (senal != null) { origenGfn = "Jugando"; gfn.FijarDestino(senal.Ip.ToString(), senal.Puerto, false); }
            else origenGfn = ZonaAntesDeJugar(gfn, selGfn, cfg.ZonaGfn, forzar);

            if (!cfg.VerXcloud) { origenXcloud = ""; xcloud.FijarDestino("", 0, false); }
            else if (streamXcloud) { origenXcloud = "Jugando"; xcloud.FijarDestino(stream.Remoto, 443, false); }
            else origenXcloud = ZonaAntesDeJugar(xcloud, selXcloud, cfg.ZonaXcloud, forzar);
        }

        // Antes de jugar se mide la zona elegida o, en automático, la de menor ping.
        // Con la VPN encendida no se busca zona: los tiempos serían los de la VPN
        string ZonaAntesDeJugar(Sonda s, SelectorZona sel, string codigo, bool forzar)
        {
            int fija = sel.Lista.Indice(codigo);
            if (fija < 0 && vpn == null) sel.Escanear(forzar);
            int zona = fija >= 0 ? fija : sel.Mejor;
            s.FijarDestino(zona >= 0 ? sel.Lista.Hosts[zona] : "", 443, true);
            return zona < 0 ? "" : sel.Lista.Ciudad(zona) + (fija < 0 ? " (auto)" : "");
        }

        static Native.ConexionTcp BuscarSesionGfn()
        {
            List<Native.ConexionTcp> lista = Native.ConexionesA(PuertosGfn);
            if (lista.Count == 0) return null;
            // Si hay varias, se prefiere la de la app de GeForce NOW
            foreach (Native.ConexionTcp c in lista)
                if (NombreProceso(c.Pid).IndexOf("geforce", StringComparison.OrdinalIgnoreCase) >= 0) return c;
            return lista[0];
        }

        static string NombreProceso(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid)) return p.ProcessName;
            }
            catch { return ""; }
        }

        void Refrescar()
        {
            stream = analisisActivo ? analizador.Tic() : new AnalizadorStream.Resumen();
            if (cfg.VerRaton) estadoRaton = raton.Tic();
            Estado eg = gfn.Leer(), ex = xcloud.Leer(), eu = eeuu.Leer(), er = router.Leer();
            bool streamGfn = stream.Activo && EsGfn(stream.Remoto);
            bool streamXcloud = stream.Activo && !streamGfn;
            FijarRuta(eg, streamGfn);

            List<Fila> filas = new List<Fila>();
            Fila fg = null, fx = null;
            if (cfg.VerGfn)
            {
                fg = FilaNube("GeForce NOW", "GFN", eg, streamGfn, histGfn, origenGfn, selGfn);
                filas.Add(fg);
            }
            if (cfg.VerXcloud)
            {
                fx = FilaNube("Xbox Cloud", "xCloud", ex, streamXcloud, histXcloud, origenXcloud, selXcloud);
                filas.Add(fx);
            }
            Fila fu = FilaEeuu(eu);
            if (cfg.VerEeuu) filas.Add(fu);
            if (cfg.VerRouter) filas.Add(FilaRouter(er));
            if (cfg.VerRaton) filas.Add(FilaRaton(estadoRaton));

            // Con VPN, lo que pasa por internet mide el camino de la VPN: se muestra en gris para no confundir
            if (vpn != null)
            {
                foreach (Fila f in new Fila[] { fg, fx, fu })
                    if (f != null) f.Color = Calidad.Gris;
                fu.Sub = "vía VPN · " + fu.Sub;
            }

            Color colorDiag;
            string diag = Diagnosticar(eg, ex, eu, er, out colorDiag);
            widget.Actualizar(filas.ToArray(), diag, colorDiag);

            // Icono: el stream en curso; si no, la primera nube con ping; si no, EE.UU.
            Fila principal = null;
            if (stream.Activo) principal = streamGfn ? fg : fx;
            if (principal == null)
                foreach (Fila f in new Fila[] { fg, fx })
                    if (f != null && f.Unidad == "ms") { principal = f; break; }
            if (principal == null) principal = fu;
            string texto = principal.Unidad == "ms" ? Math.Min(int.Parse(principal.Valor), 999).ToString() : principal.Valor == "X" ? "X" : "-";
            ActualizarIcono(texto, principal.Color);

            List<string> partes = new List<string>();
            foreach (Fila f in filas)
                if (f.Unidad == "ms" || f.Valor == "X") partes.Add(f.Corto + " " + f.Valor + f.Unidad + (f.Corto1 != "" && f.Corto1 != "0%" ? " " + f.Corto1 : ""));
            string tip = partes.Count > 0 ? string.Join(" | ", partes.ToArray()) : "MonitorPing";
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        // La ruta en vivo se mide sola mientras juegas, o cuando su ventana está abierta
        void FijarRuta(Estado eg, bool streamGfn)
        {
            // Un paquete suelto perdido lo recupera el propio stream: cuenta como malo un segundo con 1 % o más, o un corte
            bool mal = stream.Activo && (stream.PerdidaSeg >= 1 || stream.Cortes > cortesPrevios);
            cortesPrevios = stream.Activo ? stream.Cortes : 0;
            string contexto = "";
            if (stream.Activo)
            {
                string svc = streamGfn ? "GFN" : "xCloud";
                contexto = svc + " " + stream.Remoto + " · " + (vpn != null ? "por VPN" : "sin VPN") + " · " + stream.Mbps.ToString("0") + " Mbps · pérdida 30 s " +
                           Calidad.Porcentaje(stream.Perdida30) + " · " + TextoCortes(stream.Cortes) + (stream.Cortes > 0 ? " (máx " + stream.CorteMax + " ms)" : "") +
                           (stream.Rtt >= 0 ? " · ping " + stream.Rtt + " ms" : "");
                AnotarMinuto(svc);
            }
            else inicioMinuto = DateTime.MinValue;
            string elegido = rutaForm != null ? rutaForm.DestinoElegido : null;
            string destino, servicio;
            if (elegido != null) { destino = elegido; servicio = "elegido a mano"; }
            else if (stream.Activo) { destino = stream.Remoto; servicio = "jugando " + (streamGfn ? "GFN" : "xCloud"); }
            else if (eg.HayDestino) { destino = eg.Host; servicio = senalGfn ? "jugando GFN" : "zona de GFN antes de jugar"; }
            else { destino = ""; servicio = ""; }
            ruta.Fijar(destino, servicio, rutaForm != null || stream.Activo || senalGfn, mal, contexto, stream.Activo && vpn != null);
        }

        // Cada minuto de partida deja una línea en el registro: así se comparan horas, Mbps y con/sin VPN
        void AnotarMinuto(string svc)
        {
            DateTime ahora = DateTime.Now;
            if (inicioMinuto == DateTime.MinValue) { inicioMinuto = ahora; sumaMbps = 0; segundosMinuto = 0; }
            if (stream.Segundo != ultimoSegundoMinuto)
            {
                ultimoSegundoMinuto = stream.Segundo;
                sumaMbps += stream.Mbps;
                segundosMinuto++;
            }
            if ((ahora - inicioMinuto).TotalSeconds < 60) return;
            RutaEnVivo.Anotar(ahora.ToString("yyyy-MM-dd HH:mm:ss") + " · minuto de partida · " + svc + " " + stream.Remoto + " · " +
                (vpn != null ? "por VPN (" + vpn + ")" : "sin VPN") + " · " + (sumaMbps / Math.Max(1, segundosMinuto)).ToString("0") + " Mbps promedio · pérdida 60 s " +
                Calidad.Porcentaje(stream.Perdida60) + " · " + TextoCortes(stream.Cortes) + (stream.Cortes > 0 ? " (máx " + stream.CorteMax + " ms)" : "") +
                (stream.Rtt >= 0 ? " · ping " + stream.Rtt + " ms" : ""));
            inicioMinuto = ahora;
            sumaMbps = 0;
            segundosMinuto = 0;
        }

        static string ValorTexto(Estado e)
        {
            return e.Actual >= 0 ? e.Actual.ToString() : e.Actual == Estado.Perdido ? "X" : "–";
        }

        static string TextoCortes(int n) { return n == 1 ? "1 corte" : n + " cortes"; }

        Fila FilaNube(string nombre, string corto, Estado e, bool jugandoConStream, Historial hist, string origen, SelectorZona sel)
        {
            Fila f = new Fila();
            f.Nombre = nombre;
            f.Corto = corto;

            if (jugandoConStream)
            {
                // Datos del propio stream: los mismos que ve GeForce NOW / xCloud
                AnalizadorStream.Resumen st = stream;
                int rtt = st.Rtt >= 0 ? st.Rtt : e.Actual; // ping dentro del stream; si todavía no hay, el de la sonda
                if (hist.UltimoSegundo != st.Segundo)
                {
                    hist.UltimoSegundo = st.Segundo;
                    hist.Agregar(st.PerdidaSeg > 0 ? -1 : Math.Max(0, rtt)); // marca roja en los segundos con pérdida
                }
                f.Valor = rtt >= 0 ? rtt.ToString() : "–";
                f.Unidad = rtt >= 0 ? "ms" : "";
                f.Sub = "Jugando · " + st.Mbps.ToString("0") + " Mbps" + (st.Rtt >= 0 ? "" : " · ping externo");
                f.Detalle1 = "pérdida " + Calidad.Porcentaje(st.Perdida30);
                f.Detalle1Malo = st.Perdida30 > 0;
                f.Detalle2 = TextoCortes(st.Cortes);
                f.Corto1 = Calidad.Porcentaje(st.Perdida30);
                f.Corto2 = st.Mbps.ToString("0") + " Mbps · " + TextoCortes(st.Cortes);
                f.Grafica = hist.Valores();
                if (st.Perdida30 >= 2 || st.Cortes >= 2 || rtt >= 80) f.Color = Calidad.Rojo;
                else if (st.Perdida30 > 0 || st.Cortes > 0 || rtt >= 60) f.Color = Calidad.Amarillo;
                else f.Color = Calidad.Verde;
                return f;
            }
            hist.Limpiar();

            // Antes de jugar (o jugando sin lectura del stream): la sonda de siempre
            f.Color = Calidad.De(e, 60, 80); // NVIDIA pide menos de 80 ms
            f.Valor = ValorTexto(e);
            f.Unidad = e.Actual >= 0 ? "ms" : "";
            f.Grafica = e.Valores;
            if (e.HayDestino && e.Valores.Length > 0)
            {
                f.Detalle1 = "pérdida " + Calidad.Porcentaje(e.Perdida);
                f.Detalle1Malo = e.Perdida > 0;
                f.Detalle2 = "jitter " + Math.Round(e.Jitter) + " ms";
                f.Corto1 = Calidad.Porcentaje(e.Perdida);
            }
            if (!e.HayDestino)
            {
                if (vpn != null) f.Sub = "VPN activa: no se mide";
                else if (sel.Escaneando) f.Sub = "Buscando la zona más cercana...";
                else f.Sub = "Sin zona: elígela en Configuración";
                f.Corto2 = vpn != null ? "VPN activa" : sel.Escaneando ? "buscando zona..." : "";
            }
            else if (origen == "Manual") { f.Sub = "Manual · " + e.Host; f.Corto2 = "manual"; }
            else if (origen == "Jugando")
            {
                f.Sub = "Jugando · " + e.Host + (e.ModoTcp ? " (TCP)" : "") + (analisisActivo ? "" : " · sin lectura del stream");
                f.Corto2 = "jugando";
            }
            else if (e.NuncaResponde) { f.Sub = "Zona " + origen + " no responde"; f.Corto2 = origen + " no responde"; }
            else { f.Sub = "Zona " + origen; f.Corto2 = origen; }
            if (e.Error != null) { f.Sub = e.Error; f.Aviso = true; }
            return f;
        }

        Fila FilaEeuu(Estado e)
        {
            Fila f = new Fila();
            f.Nombre = "Internet EE.UU.";
            f.Corto = "EE.UU.";
            int min = e.Minimo == int.MaxValue ? 0 : e.Minimo;
            f.Color = Calidad.De(e, min + 25, min + 60); // se compara con su propio mejor ping, que ya es alto por la distancia
            f.Valor = ValorTexto(e);
            f.Unidad = e.Actual >= 0 ? "ms" : "";
            f.Grafica = e.Valores;
            f.Sub = Servidores.Nombre(e.Host) + (e.Ip != null && e.Ip != e.Host ? " · " + e.Ip : "");
            if (e.Valores.Length > 0)
            {
                f.Detalle1 = "pérdida " + Calidad.Porcentaje(e.Perdida);
                f.Detalle1Malo = e.Perdida > 0;
                f.Detalle2 = "jitter " + Math.Round(e.Jitter) + " ms";
                f.Corto1 = Calidad.Porcentaje(e.Perdida);
            }
            f.Corto2 = Servidores.Nombre(e.Host);
            if (e.Error != null) { f.Sub = e.Error; f.Aviso = true; }
            return f;
        }

        Fila FilaRouter(Estado e)
        {
            Fila f = new Fila();
            f.Nombre = "Router (Wi-Fi/LAN)";
            f.Corto = "Router";
            f.Color = Calidad.De(e, 10, 30);
            f.Valor = ValorTexto(e);
            f.Unidad = e.Actual >= 0 ? "ms" : "";
            f.Grafica = e.Valores;
            f.Sub = !e.HayDestino ? "Sin puerta de enlace" : e.NuncaResponde ? e.Host + " · no responde al ping" : e.Host;
            if (e.Valores.Length > 0)
            {
                f.Detalle1 = "pérdida " + Calidad.Porcentaje(e.Perdida);
                f.Detalle1Malo = e.Perdida > 0;
                f.Detalle2 = "jitter " + Math.Round(e.Jitter) + " ms";
                f.Corto1 = Calidad.Porcentaje(e.Perdida);
                f.Corto2 = "jitter " + Math.Round(e.Jitter) + " ms";
            }
            return f;
        }

        Fila FilaRaton(MonitorRaton.Resumen r)
        {
            Fila f = new Fila();
            f.Nombre = "Ratón y PC";
            f.Corto = "Ratón";
            // Los Hz salen de la prueba en primer plano: en segundo plano Windows limita los reportes y no serían los reales
            f.Valor = cfg.HzRaton > 0 ? cfg.HzRaton.ToString() : "–";
            f.Unidad = cfg.HzRaton > 0 ? "Hz" : "";
            string tirones = r.Tirones == 1 ? "1 tirón" : r.Tirones + " tirones";
            f.Detalle1 = tirones;
            f.Detalle1Malo = r.Tirones > 0;
            f.Detalle2 = "PC " + r.PausaPc + " ms"; // corto: si no, se pisa con los tirones
            f.Corto1 = tirones;
            f.Corto2 = "PC " + r.PausaPc + " ms";
            if (cfg.HzRaton <= 0) f.Sub = "Bandeja > Probar ratón para ver sus Hz";
            else if (r.Moviendo && r.TironMax > 0) f.Sub = "tirón más largo: " + r.TironMax + " ms";
            else f.Sub = cfg.RatonMedido + " · Hz medidos en la prueba";
            f.Grafica = r.Grafica;
            if (r.Tirones >= 3 || r.PausaPc >= 30) f.Color = Calidad.Rojo;
            else if (r.Tirones >= 1 || r.PausaPc >= 15 || (cfg.HzRaton > 0 && cfg.HzRaton < 500)) f.Color = Calidad.Amarillo;
            else if (cfg.HzRaton > 0 || r.PausaPc > 0) f.Color = Calidad.Verde;
            return f;
        }

        // Ventana normal (no modal): una prueba larga puede quedar corriendo mientras se usa el PC
        void AbrirPruebaPerdida()
        {
            if (pruebaPerdida != null)
            {
                if (pruebaPerdida.WindowState == FormWindowState.Minimized) pruebaPerdida.WindowState = FormWindowState.Normal;
                pruebaPerdida.Activate();
                return;
            }
            pruebaPerdida = new PruebaPerdidaForm(cfg, vpn, selGfn.Mejor, selXcloud.Mejor);
            pruebaPerdida.FormClosed += delegate { pruebaPerdida.Dispose(); pruebaPerdida = null; };
            pruebaPerdida.Show();
        }

        void AbrirRuta()
        {
            if (rutaForm != null)
            {
                if (rutaForm.WindowState == FormWindowState.Minimized) rutaForm.WindowState = FormWindowState.Normal;
                rutaForm.Activate();
                return;
            }
            rutaForm = new RutaForm(ruta, vpn);
            rutaForm.FormClosed += delegate { rutaForm.Dispose(); rutaForm = null; };
            rutaForm.Show();
        }

        void AbrirPruebaRaton()
        {
            if (pruebaAbierta != null) { pruebaAbierta.Activate(); return; }
            pruebaAbierta = new PruebaRatonForm();
            pruebaAbierta.ShowDialog();
            if (pruebaAbierta.Final != null)
            {
                cfg.HzRaton = pruebaAbierta.Final.Hz;
                cfg.RatonMedido = pruebaAbierta.Final.Dispositivo;
                cfg.Guardar();
            }
            pruebaAbierta.Dispose();
            pruebaAbierta = null;
            raton.Reanudar(); // la prueba se quedó con los reportes del ratón: se le devuelven al monitor de fondo
            Refrescar();
        }

        string Diagnosticar(Estado eg, Estado ex, Estado eu, Estado er, out Color c)
        {
            if (vpn != null) { c = Calidad.Amarillo; return "VPN activa: estos ms no son los reales"; }
            bool routerMal = !er.NuncaResponde && Calidad.Mal(er);
            bool eeuuMal = Calidad.Mal(eu);

            // Jugando: lo que manda es el stream real
            if (stream.Activo)
            {
                string svc = EsGfn(stream.Remoto) ? "GFN" : "xCloud";
                if (stream.Perdida30 < 1 && stream.Cortes == 0)
                {
                    c = Calidad.Verde;
                    return "Stream de " + svc + " estable (" + Calidad.Porcentaje(stream.Perdida30) + " pérdida)";
                }
                c = Calidad.Rojo;
                string donde = ruta.LugarReciente;
                if (donde != null) return "Pérdidas desde: " + (donde.Length > 38 ? donde.Substring(0, 37) + "…" : donde);
                if (routerMal) return "Cortes por tu Wi-Fi (el router también falla)";
                if (eeuuMal) return "Falla tu internet (también hacia EE.UU.)";
                return "Pérdidas del servidor de " + svc + " (tu internet va bien)";
            }

            if (eu.Valores.Length < 10) { c = Calidad.Gris; return "Midiendo..."; }
            bool gfnCuenta = cfg.VerGfn && eg.HayDestino && !eg.NuncaResponde;
            bool xcCuenta = cfg.VerXcloud && ex.HayDestino && !ex.NuncaResponde;
            bool eeuuCaido = eu.Muestras30 >= 10 && eu.Perdida30 >= 100;
            bool nubesCaidas = (!gfnCuenta || (eg.Muestras30 >= 10 && eg.Perdida30 >= 100)) &&
                               (!xcCuenta || (ex.Muestras30 >= 10 && ex.Perdida30 >= 100));
            if (eeuuCaido && nubesCaidas && (er.Perdida30 >= 100 || !er.HayDestino)) { c = Calidad.Rojo; return "Sin conexión (ni el router responde)"; }
            if (eeuuCaido && nubesCaidas) { c = Calidad.Rojo; return "Sin internet: el router responde, afuera no"; }
            if (eeuuCaido) { c = Calidad.Amarillo; return "El servidor de EE.UU. no responde; prueba otro"; }

            // Pausas del PC: afectan al ratón y al teclado aunque la red vaya bien
            if (cfg.VerRaton && estadoRaton.PausaPc >= 30)
            {
                c = Calidad.Amarillo;
                return "Tu PC se trabó " + estadoRaton.PausaPc + " ms (puede dar tirones)";
            }

            // Antes de jugar: ¿vale la pena entrar a la cola?
            bool gfnMal = gfnCuenta && Calidad.Mal(eg), xcMal = xcCuenta && Calidad.Mal(ex);
            string causa = null;
            if (routerMal && (gfnMal || xcMal || eeuuMal)) causa = "falla tu Wi-Fi";
            else if ((gfnMal || xcMal) && eeuuMal) causa = "falla tu internet";
            Color cg = Calidad.Gris, cx = Calidad.Gris;
            string vg = gfnCuenta && eg.Muestras30 >= 5 ? Veredicto(eg, gfnMal, out cg) : null;
            string vx = xcCuenta && ex.Muestras30 >= 5 ? Veredicto(ex, xcMal, out cx) : null;
            if (vg != null && vx != null)
            {
                c = Calidad.Mejor(cg, cx);
                return "GFN ~" + Math.Round(eg.Promedio30) + " ms " + vg + " · xCloud ~" + Math.Round(ex.Promedio30) + " ms " + vx;
            }
            if (vg != null) { c = cg; return Frase(vg, "GFN", eg, causa); }
            if (vx != null) { c = cx; return Frase(vx, "xCloud", ex, causa); }

            if (routerMal && eeuuMal) { c = Calidad.Rojo; return "Problema en tu Wi-Fi / red local"; }
            if (eeuuMal) { c = Calidad.Amarillo; return "Problema en tu internet (ruta a EE.UU.)"; }
            c = Calidad.Verde;
            return "Conexión estable";
        }

        static string Veredicto(Estado e, bool mal, out Color c)
        {
            double p = e.Promedio30;
            if (!mal && p < 60) { c = Calidad.Verde; return "bien"; }
            if (!mal && p < 80) { c = Calidad.Amarillo; return "regular"; }
            c = Calidad.Rojo;
            return "mal";
        }

        static string Frase(string veredicto, string svc, Estado e, string causa)
        {
            long p = (long)Math.Round(e.Promedio30);
            if (veredicto == "bien") return "Buen momento para " + svc + " (~" + p + " ms)";
            if (veredicto == "regular") return svc + " jugable, con algo de lag (~" + p + " ms)";
            return "Mal momento para " + svc + " (~" + p + " ms" + (causa != null ? "): " + causa : ")");
        }

        void ActualizarIcono(string texto, Color color)
        {
            string clave = texto + "|" + color.ToArgb();
            if (clave == claveIcono) return;
            Icon nuevo = CrearIcono(texto, color);
            tray.Icon = nuevo;
            if (icono != null) icono.Dispose();
            icono = nuevo;
            claveIcono = clave;
        }

        static Icon CrearIcono(string texto, Color fondo)
        {
            int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (Bitmap bmp = new Bitmap(s, s))
            using (Graphics g = Graphics.FromImage(bmp))
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                using (GraphicsPath p = WidgetForm.Redondeado(new RectangleF(0, 0, s, s), s / 5f))
                using (SolidBrush b = new SolidBrush(fondo))
                    g.FillPath(b, p);

                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                float tam = s * (texto.Length >= 3 ? 0.75f : 0.85f);
                Font f = FuenteIcono(tam);
                while (tam > 6 && g.MeasureString(texto, f, 1000, sf).Width > s)
                {
                    f.Dispose();
                    tam -= 0.5f;
                    f = FuenteIcono(tam);
                }
                // Texto oscuro sobre colores claros (amarillo, menta...) y blanco sobre los oscuros
                double luz = (0.299 * fondo.R + 0.587 * fondo.G + 0.114 * fondo.B) / 255;
                using (f)
                using (SolidBrush tinta = new SolidBrush(luz > 0.62 ? Color.FromArgb(20, 24, 40) : Color.White))
                    g.DrawString(texto, f, tinta, new RectangleF(0, 0, s, s + 1), sf);

                IntPtr h = bmp.GetHicon();
                Icon tmp = Icon.FromHandle(h);
                Icon copia = (Icon)tmp.Clone();
                tmp.Dispose();
                Native.DestroyIcon(h);
                return copia;
            }
        }

        // Fuente estrecha para que quepan 3 cifras en el icono; si no existe, Segoe UI
        static Font FuenteIcono(float tam)
        {
            Font f = new Font("Bahnschrift SemiBold Condensed", tam, FontStyle.Regular, GraphicsUnit.Pixel);
            if (f.Name == "Bahnschrift SemiBold Condensed") return f;
            f.Dispose();
            return new Font("Segoe UI", tam, FontStyle.Bold, GraphicsUnit.Pixel);
        }

        int FilasVisibles()
        {
            int n = 0;
            foreach (bool v in new bool[] { cfg.VerGfn, cfg.VerXcloud, cfg.VerEeuu, cfg.VerRouter, cfg.VerRaton }) if (v) n++;
            return n;
        }

        // Cambia el tema al instante: widget, menú e icono. Las ventanas lo toman la próxima vez que se abren
        void AplicarTema()
        {
            Tema.Actual = Tema.Buscar(cfg.NombreTema);
            widget.CambiarTema(cfg.Animado);
            EstiloMenu();
            claveIcono = null; // se vuelve a dibujar con los colores del tema
            Refrescar();
        }

        void EstiloMenu()
        {
            Tema t = Tema.Actual;
            ToolStripRenderer render = t.Ventanas ? (ToolStripRenderer)new RenderMenu(t) : new ToolStripProfessionalRenderer();
            menu.Renderer = render;
            ColorearMenu(menu.Items, t.Ventanas ? t.Texto : SystemColors.ControlText, render);
        }

        static void ColorearMenu(ToolStripItemCollection items, Color texto, ToolStripRenderer render)
        {
            foreach (ToolStripItem it in items)
            {
                it.ForeColor = texto;
                ToolStripMenuItem m = it as ToolStripMenuItem;
                if (m != null && m.HasDropDownItems)
                {
                    m.DropDown.Renderer = render;
                    ColorearMenu(m.DropDownItems, texto, render);
                }
            }
        }

        void AplicarRaton()
        {
            if (cfg.VerRaton) raton.Iniciar();
            else raton.Parar();
        }

        void AplicarAspecto()
        {
            widget.Configurar(cfg.Compacto, FilasVisibles(), cfg.Opacidad, cfg.Bloqueado);
            widget.TopMost = cfg.Encima;
            MantenerEnPantalla();
        }

        void MostrarWidget()
        {
            widget.TopMost = cfg.Encima;
            widget.Show();
        }

        void AlternarWidget()
        {
            if (widget.Visible) widget.Hide();
            else MostrarWidget();
        }

        void ColocarWidget()
        {
            widget.Location = new Point(cfg.WidgetX, cfg.WidgetY);
            MantenerEnPantalla();
        }

        // Si el widget quedó fuera de las pantallas (o crece al cambiar de modo), vuelve a la esquina inferior derecha
        void MantenerEnPantalla()
        {
            Rectangle r = widget.Bounds;
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.Contains(r)) return;
            foreach (Screen sc in Screen.AllScreens)
                if (sc.WorkingArea.Contains(new Rectangle(r.Location, new Size(40, 20))))
                {
                    Rectangle wa = sc.WorkingArea;
                    widget.Location = new Point(Math.Max(wa.Left, Math.Min(r.Left, wa.Right - r.Width)), Math.Max(wa.Top, Math.Min(r.Top, wa.Bottom - r.Height)));
                    return;
                }
            Rectangle p = Screen.PrimaryScreen.WorkingArea;
            widget.Location = new Point(p.Right - r.Width - 12, p.Bottom - r.Height - 12);
        }

        void AbrirConfig()
        {
            if (formAbierto != null) { formAbierto.Activate(); return; }
            formAbierto = new ConfigForm(cfg, inicioWindows, vpn);
            if (formAbierto.ShowDialog() == DialogResult.OK)
            {
                cfg.ServidorEEUU = formAbierto.ServidorEEUU;
                cfg.GfnManual = formAbierto.GfnManual;
                cfg.ZonaGfn = formAbierto.ZonaGfn;
                cfg.ZonaXcloud = formAbierto.ZonaXcloud;
                cfg.Encima = formAbierto.Encima;
                cfg.MostrarAlIniciar = formAbierto.MostrarAlIniciar;
                cfg.Compacto = formAbierto.Compacto;
                cfg.Bloqueado = formAbierto.Bloqueado;
                cfg.Opacidad = formAbierto.Opacidad;
                cfg.VerGfn = formAbierto.VerGfn;
                cfg.VerXcloud = formAbierto.VerXcloud;
                cfg.VerEeuu = formAbierto.VerEeuu;
                cfg.VerRouter = formAbierto.VerRouter;
                cfg.VerRaton = formAbierto.VerRaton;
                bool cambiaTema = cfg.NombreTema != formAbierto.NombreTema || cfg.Animado != formAbierto.Animado;
                cfg.NombreTema = formAbierto.NombreTema;
                cfg.Animado = formAbierto.Animado;
                cfg.Guardar();
                if (cambiaTema) AplicarTema();
                if (formAbierto.IniciarConWindows != inicioWindows) FijarInicioConWindows(formAbierto.IniciarConWindows);
                eeuu.FijarDestino(cfg.ServidorEEUU, 0, false);
                ActualizarNubes();
                AplicarRaton();
                AplicarAspecto();
                Refrescar();
            }
            formAbierto.Dispose();
            formAbierto = null;
        }

        static string Schtasks() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"); }

        static int Ejecutar(string archivo, string argumentos)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(archivo, argumentos);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        static bool InicioConWindows() { return Ejecutar(Schtasks(), "/Query /TN " + AppName) == 0; }

        // La clave Run del registro no puede abrir programas que piden administrador: se usa una tarea programada al iniciar sesión
        void FijarInicioConWindows(bool on)
        {
            if (on) Ejecutar(Schtasks(), "/Create /TN " + AppName + " /TR \"\\\"" + Application.ExecutablePath + "\\\"\" /SC ONLOGON /RL HIGHEST /F");
            else Ejecutar(Schtasks(), "/Delete /TN " + AppName + " /F");
            inicioWindows = InicioConWindows();
        }

        // Las versiones anteriores usaban la clave Run: se pasa a la tarea programada
        void MigrarInicio()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null || k.GetValue(AppName) == null) return;
                    k.DeleteValue(AppName);
                }
                FijarInicioConWindows(true);
            }
            catch { }
        }

        void Salir()
        {
            timer.Stop();
            if (pruebaPerdida != null) pruebaPerdida.Close();
            if (rutaForm != null) rutaForm.Close();
            ruta.Parar();
            Native.UnhookWinEvent(ganchoMostrar);
            Native.UnhookWinEvent(ganchoPrimerPlano);
            gfn.Parar();
            xcloud.Parar();
            eeuu.Parar();
            router.Parar();
            analizador.Cerrar();
            raton.Parar();
            tray.Visible = false;
            tray.Dispose();
            widget.Close();
            widget.Dispose();
            if (icono != null) icono.Dispose();
            ExitThread();
        }
    }

    // Ventanita flotante con el ping en vivo; en modo monitor el ratón la atraviesa
    class WidgetForm : Form
    {
        // Medidas a 96 ppp; al pintar se escalan según el DPI de la pantalla
        const int AnchoNormal = 384, AltoCabecera = 24, AltoFila = 50, AltoPie = 28;
        const int XPing = 165, XGrafica = 256;
        const int AnchoCompacto = 300, AltoFilaCompacta = 20, MargenCompacto = 5;

        float k = 1f;
        // Fondo animado de los temas nuevos: los resplandores se desplazan despacio
        readonly System.Windows.Forms.Timer animacion = new System.Windows.Forms.Timer();
        readonly Stopwatch relojAnimacion = Stopwatch.StartNew();
        double fase = 1.2;
        bool animado;
        Bitmap capa;
        Bitmap[] manchas;   // resplandores ya difuminados, listos para pegar
        Tema manchasTema;
        Size manchasTam;
        bool capaSucia = true; // hay que volver a dibujar el contenido (llegaron datos, cambió el tema o el tamaño)
        bool compacto = true, bloqueado = true;
        int ancho = AnchoCompacto, alto = 100;
        Fila[] filas = new Fila[0];
        string diagnostico = "Midiendo...";
        Color colorDiagnostico = Calidad.Gris;
        readonly Rectangle rCerrar = new Rectangle(AnchoNormal - 26, 2, 22, 20);
        bool sobreCerrar;
        Font fTitulo, fNombre, fSub, fPing, fMs, fDetalle, fDiag, fNombreC, fValorC, fUnidadC, fDetalleC, fDiagC;
        StringFormat recorte, derecha;

        public event EventHandler CerrarPulsado, Movido;

        public WidgetForm()
        {
            Text = "MonitorPing";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Tema.Actual.Fondo;
            AllowTransparency = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            CrearFuentes(Tema.Actual);
            animacion.Interval = 160; // unas 6 imágenes por segundo: de sobra para un movimiento tan lento
            animacion.Tick += delegate { fase = 1.2 + relojAnimacion.Elapsed.TotalSeconds * 0.12; Invalidate(); };

            recorte = new StringFormat();
            recorte.Trimming = StringTrimming.EllipsisCharacter;
            recorte.FormatFlags = StringFormatFlags.NoWrap;
            derecha = new StringFormat();
            derecha.Alignment = StringAlignment.Far;
            derecha.LineAlignment = StringAlignment.Center;
            derecha.FormatFlags = StringFormatFlags.NoWrap;
        }

        // Sin icono en Alt+Tab, sin robar el foco al juego y, si está bloqueado, transparente al ratón
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (bloqueado) cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        int S(int v) { return (int)Math.Round(v * k); }

        Rectangle Escalar(Rectangle r) { return new Rectangle(S(r.X), S(r.Y), S(r.Width), S(r.Height)); }

        public void Configurar(bool esCompacto, int numFilas, int opacidad, bool esBloqueado)
        {
            compacto = esCompacto;
            ancho = compacto ? AnchoCompacto : AnchoNormal;
            alto = compacto ? MargenCompacto * 2 + (numFilas + 1) * AltoFilaCompacta : AltoCabecera + numFilas * AltoFila + AltoPie;
            ClientSize = new Size(S(ancho), S(alto));
            AjustarRegion();
            // Nunca 1.0: así la ventana sigue siendo "layered" y el modo monitor (clic a través) funciona también al 100 %
            Opacity = Math.Min(0.99, Math.Max(30, Math.Min(100, opacidad)) / 100.0);
            bloqueado = esBloqueado;
            if (IsHandleCreated)
            {
                int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                ex = bloqueado ? ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED : ex & ~Native.WS_EX_TRANSPARENT;
                Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
            }
            capaSucia = true;
            Invalidate();
        }

        public void CambiarTema(bool animar)
        {
            Tema t = Tema.Actual;
            BackColor = t.Fondo;
            CrearFuentes(t);
            AjustarRegion();
            animado = animar && t.Moderno;
            if (!animado) fase = 1.2;
            if (animado && Visible) animacion.Start();
            else animacion.Stop();
            capaSucia = true;
            Invalidate();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (animado && Visible) animacion.Start();
            else animacion.Stop();
        }

        void CrearFuentes(Tema t)
        {
            if (fTitulo != null)
                foreach (Font f in new Font[] { fTitulo, fNombre, fSub, fPing, fMs, fDetalle, fDiag, fNombreC, fValorC, fUnidadC, fDetalleC, fDiagC }) f.Dispose();
            GraphicsUnit px = GraphicsUnit.Pixel;
            fTitulo = Estilo.Fuente(t.FuenteTexto, 11f, FontStyle.Regular, px);
            fNombre = Estilo.Fuente(t.FuenteFuerte, 13f, FontStyle.Regular, px);
            fSub = Estilo.Fuente(t.FuenteTexto, 10.5f, FontStyle.Regular, px);
            fPing = Estilo.Fuente(t.FuenteNumeros, 24f, t.EstiloNumeros, px);
            fMs = Estilo.Fuente(t.FuenteTexto, 10.5f, FontStyle.Regular, px);
            fDetalle = Estilo.Fuente(t.FuenteTexto, 10.5f, FontStyle.Regular, px);
            fDiag = Estilo.Fuente(t.FuenteFuerte, 12f, FontStyle.Regular, px);
            fNombreC = Estilo.Fuente(t.FuenteFuerte, 11.5f, FontStyle.Regular, px);
            fValorC = Estilo.Fuente(t.FuenteNumeros, 13f, t.EstiloNumeros, px);
            fUnidadC = Estilo.Fuente(t.FuenteTexto, 9.5f, FontStyle.Regular, px);
            fDetalleC = Estilo.Fuente(t.FuenteTexto, 10.5f, FontStyle.Regular, px);
            fDiagC = Estilo.Fuente(t.FuenteFuerte, 11f, FontStyle.Regular, px);
        }

        // Esquinas redondeadas en los temas nuevos (la ventana se recorta con esa forma)
        void AjustarRegion()
        {
            Region anterior = Region;
            if (Tema.Actual.Radio > 0)
                using (GraphicsPath p = Redondeado(new RectangleF(0, 0, ClientSize.Width, ClientSize.Height), S(Tema.Actual.Radio)))
                    Region = new Region(p);
            else Region = null;
            if (anterior != null) anterior.Dispose();
        }

        public void Actualizar(Fila[] nuevas, string diag, Color color)
        {
            filas = nuevas;
            diagnostico = diag;
            colorDiagnostico = color;
            capaSucia = true;
            if (Visible) Invalidate();
        }

        bool EnCerrar(Point p) { return !compacto && Escalar(rCerrar).Contains(p); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (bloqueado || e.Button != MouseButtons.Left || EnCerrar(e.Location)) return;
            // Arrastrar la ventana como si se agarrara la barra de título; vuelve al soltar el ratón
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, new IntPtr(Native.HTCAPTION), IntPtr.Zero);
            if (Movido != null) Movido(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!bloqueado && e.Button == MouseButtons.Left && EnCerrar(e.Location) && CerrarPulsado != null)
                CerrarPulsado(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool sobre = EnCerrar(e.Location);
            if (sobre != sobreCerrar) { sobreCerrar = sobre; capaSucia = true; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (sobreCerrar) { sobreCerrar = false; capaSucia = true; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.ScaleTransform(k, k);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Tema t = Tema.Actual;
            if (!t.Moderno)
            {
                PintarContenido(g, t);
                return;
            }
            // Temas nuevos: el fondo cambia en cada imagen de la animación, pero el contenido (tarjetas, texto, gráficas)
            // solo cuando llegan datos nuevos. Se dibuja una vez en una capa transparente y luego solo se pega encima
            PintarFondoRapido(g, t);
            if (capa == null || capa.Width != ClientSize.Width || capa.Height != ClientSize.Height)
            {
                if (capa != null) capa.Dispose();
                capa = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                capaSucia = true;
            }
            if (capaSucia)
            {
                using (Graphics gc = Graphics.FromImage(capa))
                {
                    gc.Clear(Color.Transparent);
                    gc.ScaleTransform(k, k);
                    gc.SmoothingMode = SmoothingMode.AntiAlias;
                    gc.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // sobre fondo transparente no se puede usar ClearType
                    PintarContenido(gc, t);
                }
                capaSucia = false;
            }
            g.ResetTransform();
            g.DrawImageUnscaled(capa, 0, 0);
        }

        void PintarContenido(Graphics g, Tema t)
        {
            // Borde del color de acento cuando está desbloqueado: se puede arrastrar
            using (Pen borde = new Pen(bloqueado ? t.BordePanel : t.Acento, bloqueado ? 1 : 2))
            {
                float m = bloqueado ? 0.5f : 1f;
                if (t.Radio > 0)
                    using (GraphicsPath p = Redondeado(new RectangleF(m, m, ancho - 2 * m, alto - 2 * m), t.Radio)) g.DrawPath(borde, p);
                else g.DrawRectangle(borde, bloqueado ? 0 : 1, bloqueado ? 0 : 1, ancho - (bloqueado ? 1 : 2), alto - (bloqueado ? 1 : 2));
            }

            if (compacto) PintarCompacto(g, t);
            else PintarNormal(g, t);
        }

        // Cada resplandor se dibuja una sola vez como imagen ya difuminada (lo caro) y en cada paso de la animación
        // solo se pega en su nueva posición, sin escalar (muy barato). Así la animación casi no gasta CPU
        void PintarFondoRapido(Graphics g, Tema t)
        {
            int w = ClientSize.Width, h = ClientSize.Height;
            RectangleF r = new RectangleF(0, 0, w, h);
            if (manchas == null || manchasTema != t || manchasTam != ClientSize)
            {
                LiberarManchas();
                manchas = new Bitmap[t.Manchas.Length];
                for (int i = 0; i < manchas.Length; i++)
                {
                    int d = (int)Math.Ceiling(Estilo.RadioMancha(i, r) * 2);
                    manchas[i] = new Bitmap(d, d, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    using (Graphics gm = Graphics.FromImage(manchas[i]))
                    {
                        gm.SmoothingMode = SmoothingMode.AntiAlias;
                        Estilo.PintarMancha(gm, new RectangleF(0, 0, d, d), t, i);
                    }
                }
                manchasTema = t;
                manchasTam = ClientSize;
            }
            GraphicsState estado = g.Save();
            g.ResetTransform(); // en píxeles reales: así las imágenes se pegan sin escalar
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            using (SolidBrush b = new SolidBrush(t.Fondo)) g.FillRectangle(b, r);
            Rectangle ventana = new Rectangle(0, 0, w, h);
            for (int i = 0; i < manchas.Length; i++)
            {
                PointF c = Estilo.CentroMancha(i, fase, r);
                Rectangle destino = new Rectangle((int)(c.X - manchas[i].Width / 2f), (int)(c.Y - manchas[i].Height / 2f), manchas[i].Width, manchas[i].Height);
                // Solo la parte del resplandor que cae dentro del widget
                Rectangle visible = Rectangle.Intersect(destino, ventana);
                if (visible.Width <= 0 || visible.Height <= 0) continue;
                Rectangle origen = new Rectangle(visible.X - destino.X, visible.Y - destino.Y, visible.Width, visible.Height);
                g.DrawImage(manchas[i], visible, origen, GraphicsUnit.Pixel);
            }
            g.Restore(estado);
            if (t.Estrellas) Estilo.PintarEstrellas(g, new RectangleF(0, 0, ancho, alto), fase);
        }

        void LiberarManchas()
        {
            if (manchas == null) return;
            foreach (Bitmap b in manchas) b.Dispose();
            manchas = null;
        }

        // Tarjeta de cristal de los temas nuevos
        static void Cristal(Graphics g, RectangleF r, float radio, Tema t)
        {
            using (GraphicsPath p = Redondeado(r, radio))
            {
                using (SolidBrush b = new SolidBrush(t.Panel)) g.FillPath(b, p);
                using (Pen borde = new Pen(t.BordePanel)) g.DrawPath(borde, p);
            }
        }

        void PintarCompacto(Graphics g, Tema t)
        {
            int y = MargenCompacto;
            foreach (Fila f in filas)
            {
                using (SolidBrush b = new SolidBrush(f.Color)) g.FillEllipse(b, 8, y + 6, 8, 8);
                using (SolidBrush b = new SolidBrush(t.Texto)) g.DrawString(f.Corto, fNombreC, b, new RectangleF(20, y + 2, 54, 17), recorte);
                using (SolidBrush b = new SolidBrush(f.Color)) g.DrawString(f.Valor, fValorC, b, new RectangleF(70, y, 48, 20), derecha);
                using (SolidBrush b = new SolidBrush(t.TextoTenue)) g.DrawString(f.Unidad, fUnidadC, b, 119, y + 5);
                using (SolidBrush b = new SolidBrush(f.Detalle1Malo ? Calidad.Rojo : t.TextoTenue))
                    g.DrawString(f.Corto1, fDetalleC, b, new RectangleF(138, y + 3, 60, 16), recorte);
                using (SolidBrush b = new SolidBrush(t.TextoTenue))
                    g.DrawString(f.Corto2, fDetalleC, b, new RectangleF(198, y + 3, AnchoCompacto - 202, 16), recorte);
                y += AltoFilaCompacta;
            }
            if (t.Moderno) Cristal(g, new RectangleF(4, y + 1, AnchoCompacto - 8, AltoFilaCompacta - 2), 9, t);
            using (SolidBrush b = new SolidBrush(colorDiagnostico)) g.FillEllipse(b, 8, y + 6, 8, 8);
            using (SolidBrush b = new SolidBrush(t.Texto))
                g.DrawString(diagnostico, fDiagC, b, new RectangleF(20, y + 2, AnchoCompacto - 26, 17), recorte);
        }

        void PintarNormal(Graphics g, Tema t)
        {
            using (SolidBrush b = new SolidBrush(t.TextoTenue))
                g.DrawString(bloqueado ? "MonitorPing · en vivo" : "MonitorPing · arrástralo para moverlo", fTitulo, b, 10, 5);

            if (!bloqueado)
            {
                if (sobreCerrar)
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(50, t.Texto)))
                        g.FillRectangle(b, rCerrar);
                using (Pen p = new Pen(t.TextoTenue, 1.4f))
                {
                    int cx = rCerrar.X + rCerrar.Width / 2, cy = rCerrar.Y + rCerrar.Height / 2;
                    g.DrawLine(p, cx - 4, cy - 4, cx + 4, cy + 4);
                    g.DrawLine(p, cx + 4, cy - 4, cx - 4, cy + 4);
                }
            }

            int y = AltoCabecera;
            using (Pen sep = new Pen(t.Separador))
            {
                foreach (Fila f in filas)
                {
                    // Temas nuevos: cada fila en una tarjeta de cristal; Clásico: separadores
                    if (t.Moderno) Cristal(g, new RectangleF(6, y + 3, AnchoNormal - 12, AltoFila - 6), 10, t);
                    else g.DrawLine(sep, 8, y, AnchoNormal - 8, y);
                    DibujarFila(g, f, y, t);
                    y += AltoFila;
                }
                if (!t.Moderno) g.DrawLine(sep, 8, y, AnchoNormal - 8, y);
            }

            if (t.Moderno) Cristal(g, new RectangleF(6, y + 3, AnchoNormal - 12, AltoPie - 6), 11, t);
            using (SolidBrush b = new SolidBrush(colorDiagnostico))
                g.FillEllipse(b, 11, y + 10, 8, 8);
            using (SolidBrush b = new SolidBrush(t.Texto))
                g.DrawString(diagnostico, fDiag, b, new RectangleF(25, y + 5, AnchoNormal - 32, 18), recorte);
        }

        void DibujarFila(Graphics g, Fila f, int y, Tema t)
        {
            using (SolidBrush b = new SolidBrush(t.Texto))
                g.DrawString(f.Nombre, fNombre, b, 12, y + 7);
            using (SolidBrush b = new SolidBrush(f.Aviso ? Calidad.Amarillo : t.TextoTenue))
                g.DrawString(f.Sub ?? "", fSub, b, new RectangleF(12, y + 27, XPing - 16, 16), recorte);

            using (SolidBrush b = new SolidBrush(f.Color))
                g.DrawString(f.Valor, fPing, b, new RectangleF(XPing, y + 4, 66, 36), derecha);
            if (f.Unidad != "")
                using (SolidBrush b = new SolidBrush(t.TextoTenue))
                    g.DrawString(f.Unidad, fMs, b, XPing + 67, y + 19);

            DibujarGrafica(g, new Rectangle(XGrafica, y + 7, AnchoNormal - XGrafica - 12, 22), f.Grafica, f.Color, t);

            using (SolidBrush b = new SolidBrush(f.Detalle1Malo ? Calidad.Rojo : t.TextoTenue))
                g.DrawString(f.Detalle1, fDetalle, b, XGrafica - 2, y + 31);
            using (SolidBrush b = new SolidBrush(t.TextoTenue))
            using (StringFormat alDerecho = new StringFormat())
            {
                alDerecho.Alignment = StringAlignment.Far;
                g.DrawString(f.Detalle2, fDetalle, b, new RectangleF(XGrafica, y + 31, AnchoNormal - XGrafica - 10, 15), alDerecho);
            }
        }

        // Últimos 60 s: línea con el valor y marcas rojas donde se perdió el paquete (o hubo un tirón).
        // En los temas nuevos el fondo es redondeado y el área bajo la línea se rellena
        static void DibujarGrafica(Graphics g, Rectangle r, int[] v, Color color, Tema t)
        {
            using (SolidBrush fondo = new SolidBrush(t.Grafica))
            {
                if (t.Moderno) using (GraphicsPath p = Redondeado(r, 5)) g.FillPath(fondo, p);
                else g.FillRectangle(fondo, r);
            }
            if (v == null || v.Length == 0) return;
            int max = 0, min = int.MaxValue;
            foreach (int x in v)
            {
                if (x < 0) continue;
                if (x > max) max = x;
                if (x < min) min = x;
            }
            // Escala ajustada a los valores (con al menos 30 de rango) para que se note la variación
            float bajo = min == int.MaxValue ? 0 : Math.Max(0, min - 10);
            float alto = Math.Max(max + 5, bajo + 30);
            float paso = (r.Width - 2) / 59f;
            int desplazamiento = 60 - Math.Min(60, v.Length);
            int desde = Math.Max(0, v.Length - 60);
            if (t.Moderno)
            {
                // Relleno suave bajo cada tramo continuo de la línea
                List<PointF> tramo = new List<PointF>();
                using (SolidBrush area = new SolidBrush(Color.FromArgb(50, color)))
                {
                    for (int i = desde; i <= v.Length; i++)
                    {
                        if (i < v.Length && v[i] >= 0)
                        {
                            float x = r.Left + 1 + (desplazamiento + i - desde) * paso;
                            tramo.Add(new PointF(x, r.Bottom - 2 - ((v[i] - bajo) / (alto - bajo)) * (r.Height - 4)));
                            continue;
                        }
                        if (tramo.Count >= 2)
                        {
                            tramo.Add(new PointF(tramo[tramo.Count - 1].X, r.Bottom - 1));
                            tramo.Add(new PointF(tramo[0].X, r.Bottom - 1));
                            g.FillPolygon(area, tramo.ToArray());
                        }
                        tramo.Clear();
                    }
                }
            }
            using (Pen linea = new Pen(color, 1.5f))
            using (SolidBrush punto = new SolidBrush(color))
            using (Pen perdido = new Pen(Color.FromArgb(220, Calidad.Rojo), 1.5f))
            {
                PointF previo = PointF.Empty;
                bool hayPrevio = false;
                for (int i = desde; i < v.Length; i++)
                {
                    float x = r.Left + 1 + (desplazamiento + i - desde) * paso;
                    if (v[i] < 0)
                    {
                        g.DrawLine(perdido, x, r.Top + 2, x, r.Bottom - 2);
                        hayPrevio = false;
                        continue;
                    }
                    PointF pt = new PointF(x, r.Bottom - 2 - ((v[i] - bajo) / (alto - bajo)) * (r.Height - 4));
                    if (hayPrevio) g.DrawLine(linea, previo, pt);
                    else g.FillEllipse(punto, pt.X - 1, pt.Y - 1, 2, 2);
                    previo = pt;
                    hayPrevio = true;
                }
            }
        }

        public static GraphicsPath Redondeado(RectangleF r, float radio)
        {
            float d = radio * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animacion.Dispose();
                LiberarManchas();
                if (capa != null) capa.Dispose();
                foreach (Font f in new Font[] { fTitulo, fNombre, fSub, fPing, fMs, fDetalle, fDiag, fNombreC, fValorC, fUnidadC, fDetalleC, fDiagC }) f.Dispose();
                recorte.Dispose();
                derecha.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Cálculos de la prueba de ratón (separados de la ventana para poder probarlos con datos simulados)
    class MedidorPrueba
    {
        public class Resultado
        {
            public string Dispositivo = "";
            public int Hz;               // frecuencia de reporte (125, 250, 500, 1000...)
            public double IntervaloMs;   // tiempo típico (mediana) entre reportes en movimiento rápido
            public double HuecoMaxMs;    // el hueco más largo en medio de un movimiento rápido
            public int Tirones;
            public int Reportes;
        }

        class Disp
        {
            public string Nombre;
            public long Ultimo;
            public int UltimoMov, Reportes;
            public readonly List<double> Intervalos = new List<double>(); // huecos normales (menos de 20 ms)
            public readonly List<double> Huecos = new List<double>();     // todos los huecos en movimiento rápido
        }

        static readonly double Frecuencia = Stopwatch.Frequency;
        readonly Dictionary<IntPtr, Disp> dispositivos = new Dictionary<IntPtr, Disp>();
        public long Inicio; // 0 = todavía no empezó (empieza con el primer movimiento)

        public void Agregar(IntPtr disp, string nombre, int mov, long ahora)
        {
            if (mov == 0) return;
            if (Inicio == 0) Inicio = ahora;
            Disp d;
            if (!dispositivos.TryGetValue(disp, out d)) { d = new Disp(); d.Nombre = nombre; dispositivos[disp] = d; }
            // Solo cuentan los huecos entre dos reportes con movimiento (el ratón venía moviéndose y siguió)
            if (d.Ultimo != 0 && d.UltimoMov >= 2 && mov >= 2)
            {
                double hueco = (ahora - d.Ultimo) * 1000.0 / Frecuencia;
                if (hueco < 250) d.Huecos.Add(hueco);
                if (hueco < 20) d.Intervalos.Add(hueco);
            }
            d.Ultimo = ahora;
            d.UltimoMov = mov;
            d.Reportes++;
        }

        public double Segundos(long ahora) { return Inicio == 0 ? 0 : (ahora - Inicio) / Frecuencia; }

        public Resultado Calcular()
        {
            Resultado r = new Resultado();
            Disp p = null; // el dispositivo que más se movió (el ratón, no el touchpad)
            foreach (Disp d in dispositivos.Values)
                if (p == null || d.Reportes > p.Reportes) p = d;
            if (p == null) return r;
            r.Dispositivo = p.Nombre;
            r.Reportes = p.Reportes;
            if (p.Intervalos.Count < 20) return r;
            List<double> orden = new List<double>(p.Intervalos);
            orden.Sort();
            r.IntervaloMs = orden[orden.Count / 2];
            r.Hz = MonitorRaton.Redondear(1000 / Math.Max(0.05, r.IntervaloMs));
            double limite = Math.Max(8, r.IntervaloMs * 6);
            foreach (double h in p.Huecos)
            {
                if (h > r.HuecoMaxMs) r.HuecoMaxMs = h;
                if (h > limite) r.Tirones++;
            }
            return r;
        }
    }

    // Prueba del ratón con la ventana en primer plano: así Windows entrega todos los reportes
    // (a los programas en segundo plano se los limita, por eso el widget solo puede medir tirones grandes)
    class PruebaRatonForm : VentanaTema
    {
        const double Duracion = 6; // segundos de medición desde el primer movimiento

        readonly IntPtr bufer = Marshal.AllocHGlobal(256);
        readonly Dictionary<IntPtr, string> nombres = new Dictionary<IntPtr, string>();
        MedidorPrueba medidor = new MedidorPrueba();
        bool terminado;
        Label lblEstado, lblHz, lblDetalle, lblVeredicto;
        ProgressBar barra;
        Button btnRepetir;
        readonly System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();

        public MedidorPrueba.Resultado Final; // null si no se completó

        public PruebaRatonForm()
        {
            Text = "MonitorPing - Prueba de ratón";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            float k;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            int ancho = (int)(420 * k);

            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.Padding = new Padding(14, 12, 14, 12);

            panel.Controls.Add(Etiqueta("Mueve el ratón (el Razer, no el touchpad) en círculos rápidos y amplios durante unos segundos, sin parar. " +
                "La medición empieza sola con el primer movimiento y dura " + Duracion + " segundos.", ancho, false));
            barra = new ProgressBar();
            barra.Width = ancho;
            barra.Margin = new Padding(3, 10, 3, 6);
            panel.Controls.Add(barra);
            lblEstado = Etiqueta("Esperando a que muevas el ratón...", ancho, true);
            panel.Controls.Add(lblEstado);
            lblHz = new Label();
            lblHz.AutoSize = true;
            lblHz.Font = new Font("Segoe UI Semibold", 26f);
            lblHz.Text = "– Hz";
            panel.Controls.Add(lblHz);
            lblDetalle = Etiqueta("", ancho, true);
            panel.Controls.Add(lblDetalle);
            lblVeredicto = Etiqueta("", ancho, false);
            lblVeredicto.Font = new Font("Segoe UI Semibold", 9.5f);
            lblVeredicto.Margin = new Padding(3, 8, 3, 3);
            panel.Controls.Add(lblVeredicto);

            btnRepetir = new Button();
            btnRepetir.Text = "Repetir";
            btnRepetir.AutoSize = true;
            btnRepetir.Enabled = false;
            btnRepetir.Click += delegate { Reiniciar(); };
            Button cerrar = new Button();
            cerrar.Text = "Cerrar";
            cerrar.AutoSize = true;
            cerrar.DialogResult = DialogResult.OK;
            FlowLayoutPanel botones = new FlowLayoutPanel();
            botones.FlowDirection = FlowDirection.RightToLeft;
            botones.AutoSize = true;
            botones.Anchor = AnchorStyles.Right;
            botones.Margin = new Padding(0, 10, 0, 0);
            botones.Controls.Add(cerrar);
            botones.Controls.Add(btnRepetir);
            panel.Controls.Add(botones);
            CancelButton = cerrar;
            Controls.Add(panel);

            reloj.Interval = 100;
            reloj.Tick += delegate { Actualizar(); };
            AplicarTema();
        }

        static Label Etiqueta(string texto, int ancho, bool tenue)
        {
            Label l = new Label();
            l.Text = texto;
            l.AutoSize = true;
            l.MaximumSize = new Size(ancho, 0);
            if (tenue) l.ForeColor = SystemColors.GrayText;
            return l;
        }

        // Sin RIDEV_INPUTSINK: solo llegan reportes mientras esta ventana está en primer plano, que es lo que se quiere
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RegistrarRaton(Handle, 0);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            reloj.Start();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_INPUT && !terminado)
            {
                IntPtr disp;
                int mov;
                if (MonitorRaton.LeerRaton(m.LParam, bufer, out disp, out mov) && mov > 0)
                {
                    string nombre;
                    if (!nombres.TryGetValue(disp, out nombre)) { nombre = MonitorRaton.NombreDispositivo(disp); nombres[disp] = nombre; }
                    medidor.Agregar(disp, nombre, mov, Stopwatch.GetTimestamp());
                }
            }
            base.WndProc(ref m);
        }

        void Actualizar()
        {
            if (terminado || medidor.Inicio == 0) return;
            double s = medidor.Segundos(Stopwatch.GetTimestamp());
            barra.Value = (int)Math.Min(100, s * 100 / Duracion);
            MedidorPrueba.Resultado r = medidor.Calcular();
            lblEstado.Text = "Midiendo: " + r.Dispositivo + ". Sigue moviéndolo...";
            lblHz.Text = r.Hz > 0 ? r.Hz + " Hz" : "– Hz";
            if (s >= Duracion) Terminar(r);
        }

        void Terminar(MedidorPrueba.Resultado r)
        {
            terminado = true;
            barra.Value = 100;
            btnRepetir.Enabled = true;
            if (r.Hz == 0)
            {
                lblEstado.Text = "No hubo suficiente movimiento rápido.";
                lblVeredicto.Text = "Repite moviendo el ratón más rápido y sin parar.";
                lblVeredicto.ForeColor = tema.TextoAviso;
                return;
            }
            Final = r;
            string tirones = r.Tirones == 1 ? "1 tirón" : r.Tirones + " tirones";
            lblEstado.Text = "Resultado: " + r.Dispositivo;
            lblHz.Text = r.Hz + " Hz";
            lblDetalle.Text = "Un reporte cada " + r.IntervaloMs.ToString("0.0#") + " ms · hueco más largo en movimiento: " +
                r.HuecoMaxMs.ToString("0") + " ms · " + tirones;

            List<string> partes = new List<string>();
            Color c = tema.TextoOk;
            if (r.Dispositivo == "touchpad u otro")
            {
                partes.Add("Esto parece el touchpad: repite moviendo el Razer.");
                c = tema.TextoAviso;
            }
            if (r.Hz < 500)
            {
                partes.Add("Reporta a solo " + r.Hz + " Hz; para jugar conviene 1000 Hz. Súbelo en Razer Synapse (Rendimiento > Frecuencia de sondeo).");
                c = tema.TextoAviso;
            }
            if (r.Tirones > 0)
            {
                partes.Add("Hubo " + tirones + " (el más largo de " + r.HuecoMaxMs.ToString("0") + " ms). Prueba otro puerto USB directo al PC (sin hub ni alargador) y cierra programas pesados.");
                c = tema.TextoMal;
            }
            if (partes.Count == 0)
                partes.Add("El ratón va perfecto: " + r.Hz + " Hz y sin tirones. Si en el juego notas tirones, vienen del stream (fila GFN/xCloud) o de pausas del PC.");
            lblVeredicto.Text = string.Join(" ", partes.ToArray());
            lblVeredicto.ForeColor = c;
        }

        void Reiniciar()
        {
            medidor = new MedidorPrueba();
            terminado = false;
            Final = null;
            barra.Value = 0;
            btnRepetir.Enabled = false;
            lblEstado.Text = "Esperando a que muevas el ratón...";
            lblHz.Text = "– Hz";
            lblDetalle.Text = "";
            lblVeredicto.Text = "";
        }

        bool liberado;

        protected override void Dispose(bool disposing)
        {
            if (disposing) reloj.Dispose();
            if (!liberado) { Marshal.FreeHGlobal(bufer); liberado = true; }
            base.Dispose(disposing);
        }
    }

    // Prueba de pérdida de paquetes: manda paquetes a varios servidores a la vez durante un tiempo y cuenta cuántos se pierden.
    // Cada paquete se envía sin esperar la respuesta del anterior, así una pérdida no frena el ritmo.
    class PruebaPerdida
    {
        public class Destino
        {
            public string Nombre, Host;
            public bool Tcp;              // servidores de juego: no responden al ping, se mide la conexión TCP al 443
            public int IntervaloMs, Total;
            public string Error;
            public int[] Resultados;      // ms por paquete; -1 = perdido; -2 = todavía sin respuesta
            public volatile int Enviados;
        }

        public class Estadistica
        {
            public int Enviados, Respondidos, Perdidos, RachaMax, Min = -1, Max = -1;
            public double Perdida, Promedio, Jitter;
        }

        public const int MaxTcpPorSegundo = 2; // para no saturar de conexiones a los servidores de juego
        public readonly List<Destino> Destinos = new List<Destino>();
        public int Tamano = 32;
        volatile bool cancelado;
        int activos;

        public bool Terminado { get { return Thread.VolatileRead(ref activos) == 0; } }

        public void Agregar(string nombre, string host, bool tcp, int porSegundo, int segundos)
        {
            Destino d = new Destino();
            d.Nombre = nombre;
            d.Host = host;
            d.Tcp = tcp;
            int ps = tcp ? Math.Min(porSegundo, MaxTcpPorSegundo) : porSegundo;
            d.IntervaloMs = 1000 / ps;
            d.Total = ps * segundos;
            d.Resultados = new int[d.Total];
            for (int i = 0; i < d.Total; i++) d.Resultados[i] = -2;
            Destinos.Add(d);
        }

        public void Iniciar()
        {
            byte[] datos = new byte[Tamano];
            activos = Destinos.Count;
            foreach (Destino d in Destinos)
            {
                Destino destino = d;
                Thread t = new Thread(delegate ()
                {
                    try { Enviar(destino, datos); }
                    finally { Interlocked.Decrement(ref activos); }
                });
                t.IsBackground = true;
                t.Start();
            }
        }

        public void Cancelar() { cancelado = true; }

        void Enviar(Destino d, byte[] datos)
        {
            IPAddress ip = Sonda.Resolver(d.Host);
            if (ip == null) { d.Error = "no se encuentra"; return; }
            Stopwatch reloj = Stopwatch.StartNew();
            for (int i = 0; i < d.Total && !cancelado; i++)
            {
                long espera = (long)i * d.IntervaloMs - reloj.ElapsedMilliseconds;
                if (espera > 0) Thread.Sleep((int)espera);
                if (cancelado) break;
                int n = i;
                Action<int> guardar = delegate(int ms) { d.Resultados[n] = ms; };
                if (d.Tcp) TcpAsincrono(ip, 443, guardar);
                else IcmpAsincrono(ip, datos, guardar);
                d.Enviados = i + 1;
            }
            Thread.Sleep(1200); // las últimas respuestas pueden tardar hasta 1 s
        }

        static void IcmpAsincrono(IPAddress ip, byte[] datos, Action<int> fin)
        {
            Ping p = new Ping();
            try
            {
                p.SendPingAsync(ip, 1000, datos).ContinueWith(delegate(Task<PingReply> t)
                {
                    int ms = -1;
                    if (t.Status == TaskStatus.RanToCompletion && t.Result.Status == IPStatus.Success) ms = (int)t.Result.RoundtripTime;
                    fin(ms);
                    p.Dispose();
                });
            }
            catch { fin(-1); p.Dispose(); }
        }

        // Como Sonda.PingTcp (SYN-ACK o RST cuentan como respuesta), pero sin bloquear el hilo
        static void TcpAsincrono(IPAddress ip, int puerto, Action<int> fin)
        {
            Socket s = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            Stopwatch reloj = Stopwatch.StartNew();
            int hecho = 0;
            System.Threading.Timer limite = null;
            Action<int> terminar = delegate(int ms)
            {
                if (Interlocked.Exchange(ref hecho, 1) != 0) return;
                fin(ms);
                try { s.Close(); } catch { }
            };
            try
            {
                try { s.IOControl(Sonda.SIO_TCP_INITIAL_RTO, Sonda.SinReintentosSyn, null); } catch { }
                s.BeginConnect(ip, puerto, delegate(IAsyncResult ar)
                {
                    int ms = -1;
                    try { s.EndConnect(ar); ms = (int)reloj.ElapsedMilliseconds; }
                    catch (SocketException ex) { if (ex.SocketErrorCode == SocketError.ConnectionRefused) ms = (int)reloj.ElapsedMilliseconds; }
                    catch { }
                    terminar(ms > 1000 ? -1 : ms);
                }, null);
                limite = new System.Threading.Timer(delegate { terminar(-1); limite.Dispose(); }, null, 1000, Timeout.Infinite);
            }
            catch { terminar(-1); }
        }

        public static Estadistica Calcular(Destino d)
        {
            Estadistica e = new Estadistica();
            e.Enviados = d.Enviados;
            long suma = 0, saltos = 0;
            int nSaltos = 0, previo = -1, racha = 0;
            for (int i = 0; i < e.Enviados; i++)
            {
                int v = d.Resultados[i];
                if (v == -2) continue; // aún esperando respuesta
                if (v < 0)
                {
                    e.Perdidos++;
                    racha++;
                    if (racha > e.RachaMax) e.RachaMax = racha;
                    previo = -1;
                    continue;
                }
                racha = 0;
                e.Respondidos++;
                suma += v;
                if (e.Min < 0 || v < e.Min) e.Min = v;
                if (v > e.Max) e.Max = v;
                if (previo >= 0) { saltos += Math.Abs(v - previo); nSaltos++; }
                previo = v;
            }
            int completos = e.Respondidos + e.Perdidos;
            e.Perdida = completos > 0 ? e.Perdidos * 100.0 / completos : 0;
            e.Promedio = e.Respondidos > 0 ? (double)suma / e.Respondidos : 0;
            e.Jitter = nSaltos > 0 ? (double)saltos / nSaltos : 0;
            return e;
        }

        static bool EsRouter(Destino d) { return d.Nombre.StartsWith("Router"); }

        // Si pierden todos, es tu conexión; si pierde solo alguno, es esa ruta o ese servidor
        public string Conclusion(out Color color)
        {
            List<string> conPerdida = new List<string>(), sinRespuesta = new List<string>();
            int internet = 0, internetConPerdida = 0;
            double minPerdida = double.MaxValue, maxPerdida = 0;
            Estadistica router = null;
            foreach (Destino d in Destinos)
            {
                Estadistica e = Calcular(d);
                if (d.Error != null || e.Respondidos + e.Perdidos < 10) continue;
                if (e.Respondidos == 0) { sinRespuesta.Add(d.Nombre); continue; }
                bool pierde = e.Perdidos >= 2 && e.Perdida >= 1;
                if (EsRouter(d)) { router = e; continue; }
                internet++;
                minPerdida = Math.Min(minPerdida, e.Perdida);
                maxPerdida = Math.Max(maxPerdida, e.Perdida);
                if (pierde) { internetConPerdida++; conPerdida.Add(d.Nombre + " (" + Calidad.Porcentaje(e.Perdida) + ")"); }
            }
            string extra = sinRespuesta.Count > 0 ? " No respondieron (pueden bloquear el ping): " + string.Join(", ", sinRespuesta.ToArray()) + "." : "";
            Tema t = Tema.Actual;
            if (router != null && router.Perdidos >= 2 && router.Perdida >= 1)
            {
                color = t.TextoMal;
                return "Se pierden paquetes ya en tu red local (router " + Calidad.Porcentaje(router.Perdida) + "): revisa el Wi-Fi o prueba con cable." + extra;
            }
            if (internet == 0)
            {
                color = t.TenueVentana;
                return "No hay datos suficientes para sacar una conclusión." + extra;
            }
            if (internetConPerdida == internet && internet >= 2)
            {
                color = t.TextoMal;
                return "Todos los servidores pierden paquetes (" + Calidad.Porcentaje(minPerdida) + " a " + Calidad.Porcentaje(maxPerdida) +
                       "): el problema es tu conexión a internet o tu proveedor, no los servidores." + extra;
            }
            if (internetConPerdida > 0)
            {
                color = t.TextoAviso;
                return "Solo se pierden paquetes hacia: " + string.Join(", ", conPerdida.ToArray()) +
                       ". Tu conexión está bien; el problema es la ruta hasta ese servidor o el propio servidor." + extra;
            }
            color = t.TextoOk;
            return (maxPerdida > 0 ? "Pérdida mínima (menos del 1 %), algo normal: tu conexión está bien." : "Sin pérdida de paquetes: tu conexión está limpia.") + extra;
        }
    }

    class PruebaPerdidaForm : VentanaTema
    {
        class Opcion
        {
            public string Nombre, Host;
            public bool Tcp;
            public override string ToString() { return Nombre + (Tcp ? "   (TCP)" : ""); }
        }

        static readonly int[] Duraciones = { 30, 60, 120, 300, 600, 1800 };
        static readonly string[] NombresDuracion = { "30 segundos", "1 minuto", "2 minutos", "5 minutos", "10 minutos", "30 minutos" };
        static readonly int[] Ritmos = { 1, 2, 5, 10 };

        CheckedListBox lstServidores;
        TextBox txtOtro;
        ComboBox cmbDuracion, cmbRitmo, cmbTamano;
        Button btnIniciar, btnCopiar;
        ProgressBar barra;
        Label lblTiempo, lblConclusion;
        ListView lvResultados;
        readonly List<Control> opciones = new List<Control>(); // se bloquean mientras corre la prueba
        readonly System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
        PruebaPerdida prueba;
        DateTime inicio;
        int segundosTotal;
        string parametros = "";

        public PruebaPerdidaForm(Config cfg, string vpn, int mejorGfn, int mejorXcloud)
        {
            Text = "MonitorPing - Prueba de pérdida de paquetes";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            float k;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            int ancho = (int)(600 * k);

            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.Padding = new Padding(12, 10, 12, 10);

            panel.Controls.Add(Etiqueta("Manda paquetes a los servidores que marques, todos a la vez, y cuenta cuántos se pierden. " +
                "Si pierden todos, el problema es tu conexión; si pierde solo uno, es la ruta hasta ese servidor.", ancho, false));
            if (vpn != null)
            {
                Label aviso = Etiqueta("La VPN (" + vpn + ") está activa: la prueba mediría el camino de la VPN. Apágala para probar tu conexión real.", ancho, false);
                aviso.ForeColor = tema.TextoAviso;
                panel.Controls.Add(aviso);
            }

            panel.Controls.Add(Etiqueta("Servidores:", ancho, false));
            lstServidores = new CheckedListBox();
            lstServidores.CheckOnClick = true;
            lstServidores.Width = ancho;
            lstServidores.Height = (int)(150 * k);
            lstServidores.IntegralHeight = false;
            LlenarServidores(cfg, mejorGfn, mejorXcloud);
            panel.Controls.Add(lstServidores);
            opciones.Add(lstServidores);

            FlowLayoutPanel filaOtro = Horizontal();
            txtOtro = new TextBox();
            txtOtro.Width = (int)(300 * k);
            Button btnAgregar = new Button();
            btnAgregar.Text = "Agregar otro host o IP";
            btnAgregar.AutoSize = true;
            btnAgregar.Click += delegate
            {
                string h = txtOtro.Text.Trim();
                if (h == "") return;
                Opcion o = new Opcion();
                o.Nombre = h;
                o.Host = h;
                lstServidores.Items.Add(o, true);
                txtOtro.Text = "";
            };
            filaOtro.Controls.Add(txtOtro);
            filaOtro.Controls.Add(btnAgregar);
            panel.Controls.Add(filaOtro);
            opciones.Add(txtOtro);
            opciones.Add(btnAgregar);

            FlowLayoutPanel filaParametros = Horizontal();
            cmbDuracion = Desplegable(NombresDuracion, 1, (int)(110 * k));
            string[] ritmos = new string[Ritmos.Length];
            for (int i = 0; i < Ritmos.Length; i++) ritmos[i] = Ritmos[i] + " por segundo";
            cmbRitmo = Desplegable(ritmos, 2, (int)(110 * k));
            cmbTamano = Desplegable(new string[] { "32 bytes", "1.200 bytes" }, 0, (int)(95 * k));
            filaParametros.Controls.Add(EtiquetaCentrada("Duración:"));
            filaParametros.Controls.Add(cmbDuracion);
            filaParametros.Controls.Add(EtiquetaCentrada("Paquetes:"));
            filaParametros.Controls.Add(cmbRitmo);
            filaParametros.Controls.Add(EtiquetaCentrada("Tamaño:"));
            filaParametros.Controls.Add(cmbTamano);
            panel.Controls.Add(filaParametros);
            opciones.Add(cmbDuracion);
            opciones.Add(cmbRitmo);
            opciones.Add(cmbTamano);
            panel.Controls.Add(Etiqueta("Los servidores de juego (TCP) no responden al ping: se miden con una conexión, como máximo " +
                PruebaPerdida.MaxTcpPorSegundo + " por segundo. El tamaño solo afecta a los demás.", ancho, true));

            FlowLayoutPanel filaBotones = Horizontal();
            filaBotones.Margin = new Padding(0, 8, 0, 4);
            btnIniciar = new Button();
            btnIniciar.Text = "Iniciar prueba";
            btnIniciar.Tag = "principal"; // con los temas nuevos lleva el degradado
            btnIniciar.AutoSize = true;
            btnIniciar.Click += delegate { IniciarODetener(); };
            btnCopiar = new Button();
            btnCopiar.Text = "Copiar resultados";
            btnCopiar.AutoSize = true;
            btnCopiar.Enabled = false;
            btnCopiar.Click += delegate { Copiar(); };
            filaBotones.Controls.Add(btnIniciar);
            filaBotones.Controls.Add(btnCopiar);
            panel.Controls.Add(filaBotones);

            barra = new ProgressBar();
            barra.Width = ancho;
            panel.Controls.Add(barra);
            lblTiempo = Etiqueta("", ancho, true);
            panel.Controls.Add(lblTiempo);

            lvResultados = new ListView();
            lvResultados.View = View.Details;
            lvResultados.FullRowSelect = true;
            lvResultados.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lvResultados.Width = ancho;
            lvResultados.Height = (int)(170 * k);
            string[] columnas = { "Servidor", "Enviados", "Perdidos", "Pérdida", "Mín", "Prom", "Máx", "Jitter", "Racha" };
            int[] anchos = { 196, 64, 64, 56, 40, 42, 42, 46, 46 };
            for (int i = 0; i < columnas.Length; i++)
                lvResultados.Columns.Add(columnas[i], (int)(anchos[i] * k), i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right);
            panel.Controls.Add(lvResultados);
            panel.Controls.Add(Etiqueta("Mín, Prom, Máx y Jitter en ms. Racha: la mayor cantidad de paquetes perdidos seguidos.", ancho, true));

            lblConclusion = Etiqueta("", ancho, false);
            lblConclusion.Font = new Font("Segoe UI Semibold", 9.5f);
            lblConclusion.Margin = new Padding(3, 6, 3, 3);
            panel.Controls.Add(lblConclusion);

            Controls.Add(panel);
            reloj.Interval = 250;
            reloj.Tick += delegate { Actualizar(); };
            FormClosing += delegate { if (prueba != null) prueba.Cancelar(); reloj.Stop(); };
            AplicarTema();
        }

        void LlenarServidores(Config cfg, int mejorGfn, int mejorXcloud)
        {
            IPAddress gw = Native.PuertaDeEnlace();
            if (gw != null) AgregarOpcion("Router (" + gw + ")", gw.ToString(), false, true);
            AgregarOpcion("Cloudflare (1.1.1.1)", "1.1.1.1", false, true);
            AgregarOpcion("Google (8.8.8.8)", "8.8.8.8", false, false);
            for (int i = 0; i < Servidores.Hosts.Length; i++)
                AgregarOpcion("EE.UU. · " + Servidores.Nombres[i], Servidores.Hosts[i], false,
                    string.Equals(Servidores.Hosts[i], cfg.ServidorEEUU, StringComparison.OrdinalIgnoreCase));
            int zg = ListaZonas.Gfn.Indice(cfg.ZonaGfn);
            if (zg < 0) zg = mejorGfn;
            for (int i = 0; i < ListaZonas.Gfn.Hosts.Length; i++)
                AgregarOpcion("GFN · " + ListaZonas.Gfn.Nombres[i], ListaZonas.Gfn.Hosts[i], true, i == zg);
            int zx = ListaZonas.Xcloud.Indice(cfg.ZonaXcloud);
            if (zx < 0) zx = mejorXcloud;
            for (int i = 0; i < ListaZonas.Xcloud.Hosts.Length; i++)
                AgregarOpcion("xCloud · " + ListaZonas.Xcloud.Nombres[i], ListaZonas.Xcloud.Hosts[i], true, i == zx);
        }

        void AgregarOpcion(string nombre, string host, bool tcp, bool marcada)
        {
            Opcion o = new Opcion();
            o.Nombre = nombre;
            o.Host = host;
            o.Tcp = tcp;
            lstServidores.Items.Add(o, marcada);
        }

        void IniciarODetener()
        {
            if (prueba != null && !prueba.Terminado)
            {
                prueba.Cancelar();
                btnIniciar.Enabled = false;
                btnIniciar.Text = "Deteniendo...";
                return;
            }
            if (lstServidores.CheckedItems.Count == 0)
            {
                MessageBox.Show("Marca al menos un servidor.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int segundos = Duraciones[cmbDuracion.SelectedIndex], ritmo = Ritmos[cmbRitmo.SelectedIndex];
            prueba = new PruebaPerdida();
            prueba.Tamano = cmbTamano.SelectedIndex == 0 ? 32 : 1200;
            foreach (Opcion o in lstServidores.CheckedItems) prueba.Agregar(o.Nombre, o.Host, o.Tcp, ritmo, segundos);

            lvResultados.Items.Clear();
            foreach (PruebaPerdida.Destino d in prueba.Destinos)
            {
                ListViewItem it = new ListViewItem(d.Nombre);
                for (int c = 1; c < lvResultados.Columns.Count; c++) it.SubItems.Add("");
                lvResultados.Items.Add(it);
            }
            inicio = DateTime.Now;
            segundosTotal = segundos;
            parametros = inicio.ToString("dd/MM/yyyy HH:mm") + " · " + NombresDuracion[cmbDuracion.SelectedIndex] + " · " +
                         ritmo + " paquetes por segundo · " + prueba.Tamano + " bytes";
            foreach (Control c in opciones) c.Enabled = false;
            btnCopiar.Enabled = false;
            btnIniciar.Text = "Detener";
            lblConclusion.Text = "";
            barra.Value = 0;
            prueba.Iniciar();
            reloj.Start();
        }

        static string Ms(int v) { return v < 0 ? "–" : v.ToString(); }

        void Actualizar()
        {
            if (prueba == null) return;
            double s = (DateTime.Now - inicio).TotalSeconds;
            barra.Value = (int)Math.Min(100, s * 100 / segundosTotal);
            int resta = (int)Math.Max(0, Math.Ceiling(segundosTotal - s));
            lblTiempo.Text = prueba.Terminado ? "Prueba terminada." : "Quedan " + (resta >= 60 ? resta / 60 + " min " + resta % 60 + " s" : resta + " s") + "...";

            for (int i = 0; i < prueba.Destinos.Count; i++)
            {
                PruebaPerdida.Destino d = prueba.Destinos[i];
                ListViewItem it = lvResultados.Items[i];
                if (d.Error != null) { it.SubItems[1].Text = d.Error; it.ForeColor = tema.TenueVentana; continue; }
                PruebaPerdida.Estadistica e = PruebaPerdida.Calcular(d);
                it.SubItems[1].Text = e.Enviados.ToString();
                it.SubItems[2].Text = e.Perdidos.ToString();
                it.SubItems[3].Text = e.Respondidos == 0 && e.Perdidos >= 5 ? "no responde" : Calidad.Porcentaje(e.Perdida);
                it.SubItems[4].Text = Ms(e.Min);
                it.SubItems[5].Text = e.Respondidos > 0 ? Math.Round(e.Promedio).ToString() : "–";
                it.SubItems[6].Text = Ms(e.Max);
                it.SubItems[7].Text = e.Respondidos > 1 ? Math.Round(e.Jitter).ToString() : "–";
                it.SubItems[8].Text = e.RachaMax.ToString();
                if (e.Respondidos == 0 && e.Perdidos >= 5) it.ForeColor = tema.TenueVentana;
                else if (e.Perdida >= 2) it.ForeColor = tema.TextoMal;
                else if (e.Perdida > 0) it.ForeColor = tema.TextoAviso;
                else it.ForeColor = tema.TextoOk;
            }

            if (prueba.Terminado)
            {
                reloj.Stop();
                barra.Value = 100;
                foreach (Control c in opciones) c.Enabled = true;
                btnIniciar.Enabled = true;
                btnIniciar.Text = "Iniciar prueba";
                btnCopiar.Enabled = true;
                Color color;
                lblConclusion.Text = prueba.Conclusion(out color);
                lblConclusion.ForeColor = color;
            }
        }

        void Copiar()
        {
            if (prueba == null) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Prueba de pérdida de paquetes (MonitorPing) · " + parametros);
            sb.AppendLine();
            sb.AppendLine(string.Format("{0,-42} {1,8} {2,8} {3,8} {4,6} {5,6} {6,6} {7,7} {8,6}", "Servidor", "Enviados", "Perdidos", "Pérdida", "Mín", "Prom", "Máx", "Jitter", "Racha"));
            foreach (PruebaPerdida.Destino d in prueba.Destinos)
            {
                if (d.Error != null) { sb.AppendLine(string.Format("{0,-42} {1}", d.Nombre, d.Error)); continue; }
                PruebaPerdida.Estadistica e = PruebaPerdida.Calcular(d);
                sb.AppendLine(string.Format("{0,-42} {1,8} {2,8} {3,8} {4,6} {5,6} {6,6} {7,7} {8,6}",
                    d.Nombre + (d.Tcp ? " (TCP)" : ""), e.Enviados, e.Perdidos, Calidad.Porcentaje(e.Perdida), Ms(e.Min),
                    e.Respondidos > 0 ? Math.Round(e.Promedio).ToString() : "–", Ms(e.Max),
                    e.Respondidos > 1 ? Math.Round(e.Jitter).ToString() : "–", e.RachaMax));
            }
            sb.AppendLine();
            sb.AppendLine("Conclusión: " + lblConclusion.Text);
            sb.AppendLine("(tiempos en ms)");
            try
            {
                Clipboard.SetText(sb.ToString());
                lblTiempo.Text = "Resultados copiados: pégalos donde quieras (Ctrl+V).";
            }
            catch { lblTiempo.Text = "No se pudo copiar al portapapeles; vuelve a intentarlo."; }
        }

        static ComboBox Desplegable(string[] items, int seleccion, int ancho)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Items.AddRange(items);
            c.SelectedIndex = seleccion;
            c.Width = ancho;
            return c;
        }

        static Label Etiqueta(string texto, int ancho, bool tenue)
        {
            Label l = new Label();
            l.Text = texto;
            l.AutoSize = true;
            l.MaximumSize = new Size(ancho, 0);
            if (tenue) l.ForeColor = SystemColors.GrayText;
            return l;
        }

        static Label EtiquetaCentrada(string texto)
        {
            Label l = new Label();
            l.Text = texto;
            l.AutoSize = true;
            l.Margin = new Padding(3, 7, 3, 3);
            return l;
        }

        static FlowLayoutPanel Horizontal()
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.FlowDirection = FlowDirection.LeftToRight;
            p.WrapContents = false;
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.Margin = new Padding(0, 3, 0, 3);
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) reloj.Dispose();
            base.Dispose(disposing);
        }
    }

    // Trazado de ruta en vivo: sondea cada salto hacia el servidor de juego una vez por segundo, saliendo por la conexión
    // física (Wi-Fi o cable) aunque haya una VPN, y localiza en qué salto empiezan la pérdida o las subidas de ping
    class RutaEnVivo
    {
        public class Salto
        {
            public int Ttl;
            public string Ip;              // null = el salto no responde
            public string Red = "";        // dueño de la red (por su ASN), "tu router" o "tu proveedor"
            public double Perdida = -1, Perdida10 = -1; // % en los últimos 60 s y 10 s
            public int Prom = -1, Max = -1, Ultimo = -1;
            public bool Inicio;            // aquí empieza el problema
            public string Lugar { get { return Ip == null ? "salto " + Ttl : Red + " (salto " + Ttl + ")"; } }
        }

        public class Evento
        {
            public DateTime Momento;
            public string Tipo, Lugar;
        }

        public class Vista
        {
            public string Destino = "", Ip = "", Origen = "", Estado = "";
            public Salto[] Saltos = new Salto[0];
            public Salto Final = new Salto();
            public string Diagnostico = "";
            public int Nivel;              // 0 bien, 1 aviso, 2 mal, -1 sin datos
            public string Lugar;           // dónde empieza el último problema detectado
            public DateTime LugarCuando;
            public Evento[] Eventos = new Evento[0];
        }

        class Interno
        {
            public int Ttl;
            public string Ip;
            public readonly int[] Rtt = new int[N];
            public string Red = "";
            public bool Privado;
        }

        const int N = 120;          // rondas guardadas (2 minutos)
        const int Ventana = 60;     // rondas que se analizan
        const int Perdido = -1, SinSonda = -2;
        const int TraceCada = 600;  // segundos entre trazados completos

        static readonly string[] MarcasVpn = { "wintun", "tap-", "wireguard", "vpn", "hotspot", "openvpn", "nordlynx", "warp",
            "zerotier", "tailscale", "tunnel", "virtual", "hyper-v", "vmware", "virtualbox", "loopback" };
        static readonly Dictionary<string, string> redes = new Dictionary<string, string>();
        public static string ArchivoRegistro
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorPing");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "ruta.log");
            }
        }

        readonly object bloqueo = new object();
        Thread hilo;
        volatile bool corriendo, activo, retrazar;
        volatile bool streamMal;     // el stream perdió 1 % o más, o se cortó, en el último segundo
        volatile bool porVpn;        // se juega por la VPN: la ruta del juego no es la que miden las sondas
        string contexto = "";        // cómo va el stream (se anota con cada evento)
        string destinoPedido = "", servicioPedido = "";
        string destinoActual = "", ipActual = "", origen = "", gateway = "", estado = "";
        List<Interno> saltos = new List<Interno>();
        readonly int[] rttFinal = new int[N];
        readonly bool[] malStream = new bool[N];
        long ronda;                  // rondas hechas desde el último cambio de ruta
        DateTime ultimoTrace = DateTime.MinValue, ultimoRegistro = DateTime.MinValue;
        string lugar;
        DateTime lugarCuando;
        readonly List<Evento> eventos = new List<Evento>();
        Vista ultima = new Vista();

        public void Iniciar()
        {
            if (hilo != null) return;
            corriendo = true;
            hilo = new Thread(Bucle);
            hilo.IsBackground = true;
            hilo.Name = "RutaEnVivo";
            hilo.Start();
        }

        public void Parar() { corriendo = false; }

        // Se llama cada segundo: a dónde trazar, si hay que medir y si el stream tuvo problemas en ese segundo
        public void Fijar(string destino, string servicio, bool medir, bool malEsteSegundo, string contextoStream, bool jugandoPorVpn)
        {
            lock (bloqueo)
            {
                destinoPedido = destino ?? "";
                servicioPedido = servicio ?? "";
                contexto = contextoStream ?? "";
            }
            activo = medir && destino != "";
            porVpn = jugandoPorVpn;
            if (malEsteSegundo) streamMal = true;
        }

        // Añade una línea suelta al registro (el resumen de cada minuto de partida)
        public static void Anotar(string linea)
        {
            try { File.AppendAllText(ArchivoRegistro, linea + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }

        public void Retrazar() { retrazar = true; }

        public Vista Leer() { lock (bloqueo) return ultima; }

        // Lugar del problema si se detectó en los últimos 90 s
        public string LugarReciente
        {
            get { lock (bloqueo) return lugar != null && (DateTime.Now - lugarCuando).TotalSeconds < 90 ? lugar : null; }
        }

        void Bucle()
        {
            while (corriendo)
            {
                if (!activo) { Thread.Sleep(300); continue; }
                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    string pedido, servicio;
                    lock (bloqueo) { pedido = destinoPedido; servicio = servicioPedido; }
                    if (pedido != destinoActual || retrazar || saltos.Count == 0 || (DateTime.Now - ultimoTrace).TotalSeconds > TraceCada)
                    {
                        retrazar = false;
                        Trazar(pedido, servicio);
                    }
                    if (ipActual != "") Ronda();
                    Analizar();
                }
                catch (Exception ex) { lock (bloqueo) estado = "Error: " + ex.Message; }
                int resto = 1000 - (int)sw.ElapsedMilliseconds;
                if (resto > 0) Thread.Sleep(resto);
            }
        }

        // Interfaz física con puerta de enlace (no la VPN): de ahí salen las sondas
        static bool SalidaFisica(out string local, out string gw)
        {
            local = gw = "";
            string wifiLocal = "", wifiGw = "";
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                bool cable = ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet || ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet;
                bool wifi = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                if (!cable && !wifi) continue;
                string t = (ni.Name + " " + ni.Description).ToLowerInvariant();
                bool esVpn = false;
                foreach (string m in MarcasVpn) if (t.Contains(m)) { esVpn = true; break; }
                if (esVpn) continue;
                IPInterfaceProperties p = ni.GetIPProperties();
                string g = "", l = "";
                foreach (GatewayIPAddressInformation x in p.GatewayAddresses)
                    if (x.Address.AddressFamily == AddressFamily.InterNetwork && !x.Address.Equals(IPAddress.Any)) { g = x.Address.ToString(); break; }
                foreach (UnicastIPAddressInformation u in p.UnicastAddresses)
                    if (u.Address.AddressFamily == AddressFamily.InterNetwork) { l = u.Address.ToString(); break; }
                if (g == "" || l == "") continue;
                if (cable) { local = l; gw = g; return true; } // el cable tiene prioridad, como en Windows
                if (wifiLocal == "") { wifiLocal = l; wifiGw = g; }
            }
            local = wifiLocal;
            gw = wifiGw;
            return local != "";
        }

        void Trazar(string destino, string servicio)
        {
            ultimoTrace = DateTime.Now;
            string local, gw;
            if (!SalidaFisica(out local, out gw))
            {
                lock (bloqueo) { estado = "No hay conexión Wi-Fi ni por cable"; ipActual = ""; destinoActual = destino; }
                return;
            }
            string ip = "";
            IPAddress directa;
            if (IPAddress.TryParse(destino, out directa)) ip = directa.ToString();
            else
            {
                try
                {
                    foreach (IPAddress a in System.Net.Dns.GetHostAddresses(destino))
                        if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a.ToString(); break; }
                }
                catch { }
            }
            if (ip == "")
            {
                lock (bloqueo) { estado = "No se pudo resolver " + destino; ipActual = ""; destinoActual = destino; }
                return;
            }
            lock (bloqueo) estado = "Trazando la ruta...";

            // 3 sondas por salto, todas a la vez; se corta al llegar o tras 5 saltos mudos seguidos
            List<Interno> nuevos = new List<Interno>();
            int mudos = 0;
            for (int ttl = 1; ttl <= 30 && corriendo; ttl++)
            {
                int[] sinUso;
                string[] r = Icmp.Varias(local, ip, new int[] { ttl, ttl, ttl }, 1200, out sinUso);
                string visto = null;
                foreach (string x in r) if (x != null) { visto = x; break; }
                Interno s = new Interno();
                s.Ttl = ttl;
                s.Ip = visto;
                nuevos.Add(s);
                if (visto == ip) { nuevos.RemoveAt(nuevos.Count - 1); break; } // el servidor responde al eco: es el final
                mudos = visto == null ? mudos + 1 : 0;
                if (mudos >= 5) break;
            }
            while (nuevos.Count > 0 && nuevos[nuevos.Count - 1].Ip == null) nuevos.RemoveAt(nuevos.Count - 1);

            foreach (Interno s in nuevos)
            {
                if (s.Ip == null) continue;
                s.Privado = EsPrivada(s.Ip);
                if (s.Ip == gw) s.Red = "tu router";
                else if (s.Privado) s.Red = "tu proveedor (red interna)";
                else
                {
                    string red;
                    lock (redes) redes.TryGetValue(s.Ip, out red);
                    s.Red = red ?? "...";
                    if (red == null) BuscarRed(s.Ip);
                }
            }

            lock (bloqueo)
            {
                bool igual = nuevos.Count == saltos.Count && destino == destinoActual;
                for (int i = 0; igual && i < nuevos.Count; i++) igual = nuevos[i].Ip == saltos[i].Ip;
                if (!igual)
                {
                    saltos = nuevos;
                    ronda = 0;
                }
                destinoActual = destino;
                ipActual = ip;
                origen = local;
                gateway = gw;
                estado = servicio;
            }
        }

        void Ronda()
        {
            List<Interno> lista;
            string ip, local;
            lock (bloqueo) { lista = saltos; ip = ipActual; local = origen; }
            int[] ttls = new int[lista.Count];
            for (int i = 0; i < lista.Count; i++) ttls[i] = lista[i].Ip != null ? lista[i].Ttl : 0;

            // El servidor final se mide con TCP 443 (los de GFN y xCloud no responden al ping), a la vez que los saltos
            Socket tcp = null;
            IAsyncResult ar = null;
            Stopwatch reloj = Stopwatch.StartNew();
            try
            {
                tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                tcp.Bind(new IPEndPoint(IPAddress.Parse(local), 0));
                ar = tcp.BeginConnect(IPAddress.Parse(ip), 443, null, null);
            }
            catch { }
            int[] tiempos;
            string[] r = Icmp.Varias(local, ip, ttls, 900, out tiempos);
            int final = Perdido;
            if (ar != null)
            {
                int queda = Math.Max(0, 900 - (int)reloj.ElapsedMilliseconds);
                if (ar.AsyncWaitHandle.WaitOne(queda))
                {
                    try { tcp.EndConnect(ar); final = (int)Math.Round(reloj.Elapsed.TotalMilliseconds); }
                    catch { }
                }
            }
            if (tcp != null) tcp.Close();

            lock (bloqueo)
            {
                if (lista != saltos) return; // la ruta cambió mientras tanto
                int k = (int)(ronda % N);
                for (int i = 0; i < lista.Count; i++)
                {
                    if (ttls[i] == 0) { lista[i].Rtt[k] = SinSonda; continue; }
                    lista[i].Rtt[k] = r[i] == null ? Perdido : tiempos[i];
                }
                rttFinal[k] = final;
                malStream[k] = streamMal;
                streamMal = false;
                ronda++;
            }
        }

        // Pérdida o subida de ping que empieza en un salto y sigue en todos los siguientes. Si un router solo pierde o tarda
        // al contestar (lo hacen muchos para protegerse) pero los siguientes van bien, no cuenta: el tráfico pasa por él sin problema
        void Analizar()
        {
            lock (bloqueo)
            {
                Vista v = new Vista();
                v.Destino = destinoActual;
                v.Ip = ipActual;
                v.Origen = origen;
                v.Estado = estado;
                int n = (int)Math.Min(ronda, Ventana);
                Salto[] vs = new Salto[saltos.Count];
                for (int i = 0; i < saltos.Count; i++)
                {
                    Interno s = saltos[i];
                    string red;
                    if (s.Red == "...") { lock (redes) if (redes.TryGetValue(s.Ip, out red) && red != null) s.Red = red; }
                    vs[i] = Resumir(s.Rtt, n);
                    vs[i].Ttl = s.Ttl;
                    vs[i].Ip = s.Ip;
                    vs[i].Red = s.Ip == null ? "no responde" : s.Red;
                }
                v.Saltos = vs;
                v.Final = Resumir(rttFinal, n);
                v.Final.Ip = ipActual;
                v.Final.Red = "servidor (TCP 443)";

                v.Nivel = -1;
                if (n >= 10 && porVpn)
                    v.Diagnostico = "Juegas por la VPN: el juego no va por esta ruta, así que no se localiza el tramo. Cada minuto se anota cómo va el stream.";
                else if (n >= 10)
                {
                    // Rondas malas: el stream perdió/cortó, o el servidor no contestó (si normalmente contesta)
                    int responde = 0, minFinal = int.MaxValue;
                    for (int j = 0; j < Math.Min(ronda, N); j++)
                        if (rttFinal[j] >= 0) minFinal = Math.Min(minFinal, rttFinal[j]);
                    for (int j = 0; j < n; j++) if (rttFinal[Idx(j)] >= 0) responde++;
                    bool finalCuenta = responde >= n / 2;
                    bool[] mal = new bool[n], pico = new bool[n];
                    int nMal = 0, nPico = 0, malRecientes = 0, picosRecientes = 0;
                    for (int j = 0; j < n; j++)
                    {
                        int k = Idx(j);
                        // El aviso del stream llega con hasta 1 s de retraso: también cuenta para la ronda anterior
                        bool ms = malStream[k] || (j > 0 && malStream[Idx(j - 1)]);
                        mal[j] = ms || (finalCuenta && rttFinal[k] == Perdido);
                        pico[j] = finalCuenta && rttFinal[k] >= 0 && rttFinal[k] - minFinal >= 25;
                        if (mal[j]) { nMal++; if (j < 10) malRecientes++; }
                        if (pico[j]) { nPico++; if (j < 10) picosRecientes++; }
                    }

                    string tipo = null;
                    int inicio = -2; // -1 = después del último salto visible
                    int sostenida = InicioPerdidaSostenida(vs);
                    if (nMal >= 3 && n - nMal >= 5)
                    {
                        tipo = "pérdida";
                        inicio = InicioPerdida(mal, n);
                        if (inicio < 0 && sostenida >= 0) inicio = sostenida;
                    }
                    else if (sostenida >= 0) { tipo = "pérdida en la ruta"; inicio = sostenida; }
                    else if (nPico >= 3 && n - nPico >= 5) { tipo = "subidas de ping"; inicio = InicioPico(pico, n, minFinal); }
                    bool sostenidaReciente = tipo == "pérdida en la ruta" && vs[inicio].Perdida10 >= 10;

                    if (tipo == null)
                    {
                        v.Nivel = 0;
                        v.Diagnostico = "Ruta limpia: sin pérdida ni subidas de ping en el último minuto";
                    }
                    else
                    {
                        string donde;
                        if (inicio >= 0) { vs[inicio].Inicio = true; donde = vs[inicio].Lugar; }
                        else
                        {
                            Salto ultimoVisible = null;
                            foreach (Salto s in vs) if (s.Ip != null) ultimoVisible = s;
                            donde = ultimoVisible != null ? "después de " + ultimoVisible.Lugar + ", en el servidor o su red" : "el servidor o su red";
                        }
                        v.Nivel = 2;
                        v.Diagnostico = char.ToUpper(tipo[0]) + tipo.Substring(1) + " · empieza en: " + donde;
                        if (malRecientes >= 2 || picosRecientes >= 2 || sostenidaReciente)
                        {
                            lugar = donde;
                            lugarCuando = DateTime.Now;
                            if ((DateTime.Now - ultimoRegistro).TotalSeconds >= 60)
                            {
                                ultimoRegistro = DateTime.Now;
                                Evento e = new Evento();
                                e.Momento = DateTime.Now;
                                e.Tipo = tipo;
                                e.Lugar = donde;
                                eventos.Insert(0, e);
                                if (eventos.Count > 50) eventos.RemoveAt(eventos.Count - 1);
                                Registrar(e, v);
                            }
                        }
                    }
                }
                else v.Diagnostico = ronda == 0 ? "" : "Midiendo... (" + n + " de 10 s)";
                v.Lugar = lugar;
                v.LugarCuando = lugarCuando;
                v.Eventos = eventos.ToArray();
                ultima = v;
            }
        }

        // Pérdida de las sondas que empieza en un salto y sigue en todos los siguientes (al menos dos saltos con respuesta).
        // Si solo pierde el último salto puede ser ese router limitando sus respuestas, así que no cuenta
        static int InicioPerdidaSostenida(Salto[] vs)
        {
            double[] d = new double[vs.Length];
            int ultimo = -1;
            for (int i = 0; i < vs.Length; i++)
            {
                d[i] = vs[i].Ip == null || vs[i].Perdida < 0 ? double.NaN : vs[i].Perdida;
                if (!double.IsNaN(d[i])) ultimo = i;
            }
            int inicio = PrimeroQueSigue(d, 3.0);
            return inicio >= 0 && inicio < ultimo ? inicio : -1;
        }

        // Índice de la ronda j (0 = la última) en el anillo
        int Idx(int j) { return (int)((ronda - 1 - j) % N); }

        Salto Resumir(int[] rtt, int n)
        {
            Salto s = new Salto();
            int env = 0, perd = 0, env10 = 0, perd10 = 0, suma = 0, ok = 0, max = -1;
            for (int j = 0; j < n; j++)
            {
                int v = rtt[Idx(j)];
                if (v == SinSonda) continue;
                env++;
                if (j < 10) env10++;
                if (v == Perdido) { perd++; if (j < 10) perd10++; continue; }
                suma += v;
                ok++;
                if (v > max) max = v;
                if (j == 0) s.Ultimo = v;
            }
            if (env > 0) s.Perdida = 100.0 * perd / env;
            if (env10 > 0) s.Perdida10 = 100.0 * perd10 / env10;
            if (ok > 0) { s.Prom = (int)Math.Round((double)suma / ok); s.Max = max; }
            return s;
        }

        // Primer salto desde el que la pérdida en las rondas malas (frente a las buenas) se mantiene hasta el último
        int InicioPerdida(bool[] mal, int n)
        {
            double[] d = new double[saltos.Count];
            for (int i = 0; i < saltos.Count; i++)
            {
                d[i] = double.NaN;
                if (saltos[i].Ip == null) continue;
                int eb = 0, pb = 0, eg = 0, pg = 0;
                for (int j = 0; j < n; j++)
                {
                    int v = saltos[i].Rtt[Idx(j)];
                    if (v == SinSonda) continue;
                    if (mal[j]) { eb++; if (v == Perdido) pb++; }
                    else { eg++; if (v == Perdido) pg++; }
                }
                if (eb > 0 && eg > 0) d[i] = (double)pb / eb - (double)pg / eg;
            }
            return PrimeroQueSigue(d, 0.3);
        }

        // Igual con la subida de ping: cuánto más tarda cada salto en las rondas con pico que en las normales
        int InicioPico(bool[] pico, int n, int minFinal)
        {
            double sb = 0, sg = 0;
            int cb = 0, cg = 0;
            for (int j = 0; j < n; j++)
            {
                int v = rttFinal[Idx(j)];
                if (v < 0) continue;
                if (pico[j]) { sb += v; cb++; } else { sg += v; cg++; }
            }
            if (cb == 0 || cg == 0) return -1;
            double subidaFinal = sb / cb - sg / cg;
            double[] d = new double[saltos.Count];
            for (int i = 0; i < saltos.Count; i++)
            {
                d[i] = double.NaN;
                if (saltos[i].Ip == null) continue;
                double b = 0, g = 0;
                int nb = 0, ng = 0;
                for (int j = 0; j < n; j++)
                {
                    int v = saltos[i].Rtt[Idx(j)];
                    if (v < 0) continue;
                    if (pico[j]) { b += v; nb++; } else { g += v; ng++; }
                }
                if (nb > 0 && ng > 0) d[i] = (b / nb - g / ng) / Math.Max(1, subidaFinal);
            }
            return PrimeroQueSigue(d, 0.5);
        }

        // El primer salto i tal que él y todos los siguientes superan el umbral (los saltos sin dato no cortan la cadena)
        static int PrimeroQueSigue(double[] d, double umbral)
        {
            int inicio = -1;
            for (int i = d.Length - 1; i >= 0; i--)
            {
                if (double.IsNaN(d[i])) continue;
                if (d[i] >= umbral) inicio = i;
                else break;
            }
            return inicio;
        }

        void Registrar(Evento e, Vista v)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(e.Momento.ToString("yyyy-MM-dd HH:mm:ss") + " · " + e.Tipo + " · empieza en: " + e.Lugar);
                sb.AppendLine("  destino " + v.Destino + " (" + v.Ip + ") desde " + v.Origen);
                if (contexto != "") sb.AppendLine("  stream: " + contexto);
                sb.Append(Tabla(v, "  "));
                sb.AppendLine();
                File.AppendAllText(ArchivoRegistro, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static string Tabla(Vista v, string sangria)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(sangria + string.Format("{0,-5} {1,-16} {2,-34} {3,8} {4,8} {5,5} {6,5}", "Salto", "IP", "Red", "Pérd60s", "Pérd10s", "Prom", "Máx"));
            List<Salto> filas = new List<Salto>(v.Saltos);
            filas.Add(v.Final);
            foreach (Salto s in filas)
            {
                string red = s.Red.Length > 34 ? s.Red.Substring(0, 34) : s.Red;
                sb.AppendLine(sangria + string.Format("{0,-5} {1,-16} {2,-34} {3,8} {4,8} {5,5} {6,5}{7}",
                    s == v.Final ? "fin" : s.Ttl.ToString(), s.Ip ?? "*", red,
                    s.Perdida < 0 ? "–" : Calidad.Porcentaje(s.Perdida), s.Perdida10 < 0 ? "–" : Calidad.Porcentaje(s.Perdida10),
                    s.Prom < 0 ? "–" : s.Prom.ToString(), s.Max < 0 ? "–" : s.Max.ToString(), s.Inicio ? "  <- empieza aquí" : ""));
            }
            return sb.ToString();
        }

        static bool EsPrivada(string ip)
        {
            byte[] b = IPAddress.Parse(ip).GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168) ||
                   (b[0] == 100 && b[1] >= 64 && b[1] < 128) || b[0] == 127 || (b[0] == 169 && b[1] == 254);
        }

        // Dueño de una IP por su ASN, con el servicio DNS público de Team Cymru (solo se envía la IP del router intermedio)
        static void BuscarRed(string ip)
        {
            lock (redes)
            {
                if (redes.ContainsKey(ip)) return;
                redes[ip] = null;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string nombre = "?";
                try
                {
                    string[] b = ip.Split('.');
                    string origenAsn = DnsTxt.Txt(b[3] + "." + b[2] + "." + b[1] + "." + b[0] + ".origin.asn.cymru.com");
                    if (origenAsn != null)
                    {
                        string asn = origenAsn.Split('|')[0].Trim().Split(' ')[0];
                        string info = DnsTxt.Txt("AS" + asn + ".asn.cymru.com");
                        nombre = "AS" + asn;
                        if (info != null)
                        {
                            string n = info.Substring(info.LastIndexOf('|') + 1).Trim();
                            int guion = n.IndexOf(" - ");
                            if (guion >= 0) n = n.Substring(guion + 3);
                            int coma = n.IndexOf(',');
                            if (coma > 0) n = n.Substring(0, coma);
                            if (n.Trim() != "") nombre = n.Trim();
                        }
                    }
                }
                catch { }
                lock (redes) redes[ip] = nombre;
            });
        }

        static class DnsTxt
        {
            [DllImport("dnsapi.dll", CharSet = CharSet.Unicode, EntryPoint = "DnsQuery_W")]
            static extern int DnsQuery(string nombre, ushort tipo, uint opciones, IntPtr extra, out IntPtr resultados, IntPtr reservado);
            [DllImport("dnsapi.dll")]
            static extern void DnsRecordListFree(IntPtr lista, int tipo);

            // Primer registro TXT de un nombre, o null
            public static string Txt(string nombre)
            {
                IntPtr res;
                if (DnsQuery(nombre, 16, 0, IntPtr.Zero, out res, IntPtr.Zero) != 0 || res == IntPtr.Zero) return null;
                try
                {
                    // DNS_RECORD: pNext, pName, wType, wDataLength, Flags, dwTtl, dwReserved y después los datos
                    int datos = IntPtr.Size * 2 + 16;
                    for (IntPtr r = res; r != IntPtr.Zero; r = Marshal.ReadIntPtr(r))
                    {
                        if (Marshal.ReadInt16(r, IntPtr.Size * 2) != 16) continue;
                        if (Marshal.ReadInt32(r, datos) < 1) continue;
                        return Marshal.PtrToStringUni(Marshal.ReadIntPtr(r, datos + IntPtr.Size));
                    }
                }
                finally { DnsRecordListFree(res, 1); }
                return null;
            }
        }

        // Eco ICMP con TTL y dirección de origen elegidos (así sale por el Wi-Fi aunque la VPN capture el resto del tráfico)
        static class Icmp
        {
            [StructLayout(LayoutKind.Sequential)]
            struct Opciones { public byte Ttl, Tos, Flags, Tamano; public IntPtr Datos; }
            [StructLayout(LayoutKind.Sequential)]
            struct Respuesta { public uint Direccion, Estado, Rtt; public ushort Tamano, Reservado; public IntPtr Datos; public Opciones Opc; }

            [DllImport("iphlpapi.dll", SetLastError = true)] static extern IntPtr IcmpCreateFile();
            [DllImport("iphlpapi.dll")] static extern bool IcmpCloseHandle(IntPtr h);
            [DllImport("iphlpapi.dll", SetLastError = true)]
            static extern uint IcmpSendEcho2Ex(IntPtr h, IntPtr evento, IntPtr apc, IntPtr ctx, uint origen, uint destino,
                byte[] datos, ushort tamano, ref Opciones opc, IntPtr respuesta, uint tamRespuesta, uint timeout);
            [DllImport("iphlpapi.dll")] static extern uint IcmpParseReplies(IntPtr respuesta, uint tamRespuesta);

            const uint TtlExcedido = 11013;
            const int TamBuffer = 256;
            static readonly byte[] Carga = new byte[32];

            static uint Ip(string s) { return BitConverter.ToUInt32(IPAddress.Parse(s).GetAddressBytes(), 0); }

            // Manda todas las sondas a la vez (una por TTL; 0 = no mandar) y devuelve quién contestó en cada una (o null) y en cuántos ms
            public static string[] Varias(string origen, string destino, int[] ttls, int timeout, out int[] tiempos)
            {
                int n = ttls.Length;
                string[] res = new string[n];
                tiempos = new int[n];
                IntPtr h = IcmpCreateFile();
                IntPtr[] buf = new IntPtr[n];
                ManualResetEvent[] ev = new ManualResetEvent[n];
                bool[] enviado = new bool[n];
                try
                {
                    uint o = Ip(origen), d = Ip(destino);
                    for (int i = 0; i < n; i++)
                    {
                        if (ttls[i] <= 0) continue;
                        buf[i] = Marshal.AllocHGlobal(TamBuffer);
                        ev[i] = new ManualResetEvent(false);
                        Opciones opc = new Opciones();
                        opc.Ttl = (byte)ttls[i];
                        uint r = IcmpSendEcho2Ex(h, ev[i].SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, IntPtr.Zero, o, d,
                            Carga, (ushort)Carga.Length, ref opc, buf[i], TamBuffer, (uint)timeout);
                        enviado[i] = r != 0 || Marshal.GetLastWin32Error() == 997; // 997 = en curso
                    }
                    Stopwatch sw = Stopwatch.StartNew();
                    for (int i = 0; i < n; i++)
                    {
                        if (!enviado[i]) continue;
                        int queda = Math.Max(0, timeout + 300 - (int)sw.ElapsedMilliseconds);
                        if (!ev[i].WaitOne(queda)) { buf[i] = IntPtr.Zero; continue; } // sin terminar: no se libera su buffer
                        if (IcmpParseReplies(buf[i], TamBuffer) == 0) continue;
                        Respuesta resp = (Respuesta)Marshal.PtrToStructure(buf[i], typeof(Respuesta));
                        if (resp.Estado != 0 && resp.Estado != TtlExcedido) continue;
                        res[i] = new IPAddress(resp.Direccion).ToString();
                        tiempos[i] = (int)resp.Rtt;
                    }
                }
                finally
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (buf[i] != IntPtr.Zero) Marshal.FreeHGlobal(buf[i]);
                        if (ev[i] != null) ev[i].Close();
                    }
                    IcmpCloseHandle(h);
                }
                return res;
            }
        }
    }

    // Ventana de la ruta en vivo: cada salto con su pérdida y su ping, y dónde empieza el problema
    class RutaForm : VentanaTema
    {
        readonly RutaEnVivo ruta;
        ComboBox cmbDestino;
        Label lblDestino, lblDiag;
        ListView lvSaltos, lvEventos;
        readonly System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
        readonly List<string> hosts = new List<string>();
        int eventosMostrados = -1;

        public string DestinoElegido { get { return cmbDestino.SelectedIndex <= 0 ? null : hosts[cmbDestino.SelectedIndex]; } }

        public RutaForm(RutaEnVivo ruta, string vpn)
        {
            this.ruta = ruta;
            Text = "MonitorPing - Ruta en vivo";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            float k;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            int ancho = (int)(690 * k);

            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.Padding = new Padding(12, 10, 12, 10);

            panel.Controls.Add(Etiqueta("Mide cada salto entre tu PC y el servidor una vez por segundo. Una pérdida o subida de ping es real " +
                "solo si empieza en un salto y sigue en todos los siguientes; si un router pierde pero los de después no, solo contesta lento " +
                "y el tráfico pasa bien. Mientras juegas se mide solo y cada problema queda guardado en el registro.", ancho, false));
            if (vpn != null)
                panel.Controls.Add(Etiqueta("La VPN (" + vpn + ") está activa, pero las sondas salen por tu Wi-Fi/cable: ves la ruta real de tu proveedor.", ancho, true));

            FlowLayoutPanel fila = Horizontal();
            fila.Controls.Add(EtiquetaCentrada("Destino:"));
            cmbDestino = new ComboBox();
            cmbDestino.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDestino.Width = (int)(330 * k);
            cmbDestino.Items.Add("Automático (lo que estés jugando o tu zona de GFN)");
            hosts.Add(null);
            for (int i = 0; i < ListaZonas.Gfn.Hosts.Length; i++)
            {
                cmbDestino.Items.Add("GFN · " + ListaZonas.Gfn.Nombres[i]);
                hosts.Add(ListaZonas.Gfn.Hosts[i]);
            }
            for (int i = 0; i < ListaZonas.Xcloud.Hosts.Length; i++)
            {
                cmbDestino.Items.Add("xCloud · " + ListaZonas.Xcloud.Nombres[i]);
                hosts.Add(ListaZonas.Xcloud.Hosts[i]);
            }
            cmbDestino.SelectedIndex = 0;
            fila.Controls.Add(cmbDestino);
            Button btnTrazar = new Button();
            btnTrazar.Text = "Volver a trazar";
            btnTrazar.AutoSize = true;
            btnTrazar.Click += delegate { ruta.Retrazar(); };
            fila.Controls.Add(btnTrazar);
            panel.Controls.Add(fila);

            lblDestino = Etiqueta("", ancho, true);
            panel.Controls.Add(lblDestino);

            lvSaltos = new ListView();
            lvSaltos.View = View.Details;
            lvSaltos.FullRowSelect = true;
            lvSaltos.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lvSaltos.Width = ancho;
            lvSaltos.Height = (int)(250 * k);
            string[] columnas = { "Salto", "IP", "Red", "Pérdida 60 s", "10 s", "Prom", "Máx", "Último" };
            int[] anchos = { 44, 108, 246, 80, 46, 46, 46, 50 };
            for (int i = 0; i < columnas.Length; i++)
                lvSaltos.Columns.Add(columnas[i], (int)(anchos[i] * k), i < 3 ? HorizontalAlignment.Left : HorizontalAlignment.Right);
            panel.Controls.Add(lvSaltos);
            panel.Controls.Add(Etiqueta("Tiempos en ms. El último renglón es el servidor, medido con TCP 443.", ancho, true));

            lblDiag = Etiqueta("", ancho, false);
            lblDiag.Font = new Font("Segoe UI Semibold", 9.5f);
            lblDiag.Margin = new Padding(3, 6, 3, 6);
            panel.Controls.Add(lblDiag);

            panel.Controls.Add(Etiqueta("Problemas detectados (como máximo uno por minuto):", ancho, false));
            lvEventos = new ListView();
            lvEventos.View = View.Details;
            lvEventos.FullRowSelect = true;
            lvEventos.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lvEventos.Width = ancho;
            lvEventos.Height = (int)(110 * k);
            lvEventos.Columns.Add("Hora", (int)(70 * k));
            lvEventos.Columns.Add("Qué", (int)(110 * k));
            lvEventos.Columns.Add("Empieza en", (int)(480 * k));
            panel.Controls.Add(lvEventos);

            FlowLayoutPanel botones = Horizontal();
            botones.Margin = new Padding(0, 8, 0, 4);
            Button btnCopiar = new Button();
            btnCopiar.Text = "Copiar tabla";
            btnCopiar.AutoSize = true;
            btnCopiar.Click += delegate { Copiar(); };
            Button btnRegistro = new Button();
            btnRegistro.Text = "Abrir registro";
            btnRegistro.AutoSize = true;
            btnRegistro.Click += delegate
            {
                try
                {
                    if (!File.Exists(RutaEnVivo.ArchivoRegistro)) File.WriteAllText(RutaEnVivo.ArchivoRegistro, "", Encoding.UTF8);
                    Process.Start("notepad.exe", "\"" + RutaEnVivo.ArchivoRegistro + "\"");
                }
                catch { }
            };
            botones.Controls.Add(btnCopiar);
            botones.Controls.Add(btnRegistro);
            panel.Controls.Add(botones);

            Controls.Add(panel);
            reloj.Interval = 1000;
            reloj.Tick += delegate { Actualizar(); };
            reloj.Start();
            FormClosing += delegate { reloj.Stop(); };
            AplicarTema();
            Actualizar();
        }

        static string Ms(int v) { return v < 0 ? "–" : v.ToString(); }
        static string Pct(double p) { return p < 0 ? "–" : Calidad.Porcentaje(p); }

        void Actualizar()
        {
            RutaEnVivo.Vista v = ruta.Leer();
            lblDestino.Text = v.Destino == "" ? "Sin destino: elige uno arriba o espera a que MonitorPing encuentre tu zona de GFN." :
                "Hacia " + v.Destino + (v.Ip != "" && v.Ip != v.Destino ? " (" + v.Ip + ")" : "") + (v.Origen != "" ? " · sale por " + v.Origen : "") +
                (v.Estado != "" ? " · " + v.Estado : "");

            List<RutaEnVivo.Salto> filas = new List<RutaEnVivo.Salto>(v.Saltos);
            if (v.Ip != "") filas.Add(v.Final);
            lvSaltos.BeginUpdate();
            while (lvSaltos.Items.Count > filas.Count) lvSaltos.Items.RemoveAt(lvSaltos.Items.Count - 1);
            while (lvSaltos.Items.Count < filas.Count)
            {
                ListViewItem nuevo = new ListViewItem("");
                for (int c = 1; c < lvSaltos.Columns.Count; c++) nuevo.SubItems.Add("");
                lvSaltos.Items.Add(nuevo);
            }
            for (int i = 0; i < filas.Count; i++)
            {
                RutaEnVivo.Salto s = filas[i];
                bool fin = s == v.Final;
                ListViewItem it = lvSaltos.Items[i];
                Poner(it, 0, fin ? "fin" : s.Ttl.ToString());
                Poner(it, 1, s.Ip ?? "*");
                Poner(it, 2, (s.Inicio ? "► " : "") + s.Red);
                Poner(it, 3, Pct(s.Perdida));
                Poner(it, 4, Pct(s.Perdida10));
                Poner(it, 5, Ms(s.Prom));
                Poner(it, 6, Ms(s.Max));
                Poner(it, 7, Ms(s.Ultimo));
                Color c = s.Ip == null ? tema.TenueVentana : s.Inicio ? tema.TextoMal : s.Perdida10 >= 30 ? tema.TextoAviso : tema.Ventanas ? tema.Texto : SystemColors.WindowText;
                if (it.ForeColor != c) it.ForeColor = c;
            }
            lvSaltos.EndUpdate();

            lblDiag.Text = v.Diagnostico;
            lblDiag.ForeColor = v.Nivel == 2 ? tema.TextoMal : v.Nivel == 0 ? tema.TextoOk : tema.TenueVentana;

            if (v.Eventos.Length != eventosMostrados)
            {
                eventosMostrados = v.Eventos.Length;
                lvEventos.BeginUpdate();
                lvEventos.Items.Clear();
                foreach (RutaEnVivo.Evento e in v.Eventos)
                {
                    ListViewItem it = new ListViewItem(e.Momento.ToString("HH:mm:ss"));
                    it.SubItems.Add(e.Tipo);
                    it.SubItems.Add(e.Lugar);
                    lvEventos.Items.Add(it);
                }
                lvEventos.EndUpdate();
            }
        }

        static void Poner(ListViewItem it, int col, string texto)
        {
            if (it.SubItems[col].Text != texto) it.SubItems[col].Text = texto;
        }

        void Copiar()
        {
            RutaEnVivo.Vista v = ruta.Leer();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Ruta en vivo (MonitorPing) · " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            sb.AppendLine("Hacia " + v.Destino + " (" + v.Ip + ") desde " + v.Origen);
            sb.AppendLine();
            sb.Append(RutaEnVivo.Tabla(v, ""));
            sb.AppendLine();
            sb.AppendLine(v.Diagnostico);
            try { Clipboard.SetText(sb.ToString()); lblDestino.Text = "Tabla copiada: pégala donde quieras (Ctrl+V)."; }
            catch { }
        }

        static Label Etiqueta(string texto, int ancho, bool tenue)
        {
            Label l = new Label();
            l.Text = texto;
            l.AutoSize = true;
            l.MaximumSize = new Size(ancho, 0);
            if (tenue) l.ForeColor = SystemColors.GrayText;
            return l;
        }

        static Label EtiquetaCentrada(string texto)
        {
            Label l = new Label();
            l.Text = texto;
            l.AutoSize = true;
            l.Margin = new Padding(3, 7, 3, 3);
            return l;
        }

        static FlowLayoutPanel Horizontal()
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.FlowDirection = FlowDirection.LeftToRight;
            p.WrapContents = false;
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.Margin = new Padding(0, 3, 0, 3);
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) reloj.Dispose();
            base.Dispose(disposing);
        }
    }

    class ConfigForm : VentanaTema
    {
        ComboBox cmbEEUU, cmbZonaGfn, cmbZonaXcloud, cmbTema;
        TextBox txtGfn;
        CheckBox chkEncima, chkMostrar, chkInicio, chkCompacto, chkBloqueado, chkGfn, chkXcloud, chkEeuu, chkRouter, chkRaton, chkAnimado;
        TrackBar trkOpacidad;
        Label lblOpacidad;
        ListBox lstZonas;
        Button btnMedir;
        string vpn;

        // Elemento del ranking de zonas
        class ItemZona
        {
            public ListaZonas Lista;
            public int Indice, Ms;
            public override string ToString()
            {
                return "   " + (Ms >= 0 ? Ms + " ms" : "sin respuesta").PadRight(14) + Lista.Nombres[Indice];
            }
        }

        public string ZonaGfn { get { return Codigo(cmbZonaGfn, ListaZonas.Gfn); } }
        public string ZonaXcloud { get { return Codigo(cmbZonaXcloud, ListaZonas.Xcloud); } }
        public string ServidorEEUU
        {
            get
            {
                string h = Servidores.HostDesdeTexto(cmbEEUU.Text);
                return h == "" ? Servidores.Hosts[0] : h;
            }
        }
        public string GfnManual { get { return txtGfn.Text.Trim(); } }
        public bool Encima { get { return chkEncima.Checked; } }
        public bool MostrarAlIniciar { get { return chkMostrar.Checked; } }
        public bool IniciarConWindows { get { return chkInicio.Checked; } }
        public bool Compacto { get { return chkCompacto.Checked; } }
        public bool Bloqueado { get { return chkBloqueado.Checked; } }
        public int Opacidad { get { return trkOpacidad.Value; } }
        public bool VerGfn { get { return chkGfn.Checked; } }
        public bool VerXcloud { get { return chkXcloud.Checked; } }
        public bool VerEeuu { get { return chkEeuu.Checked; } }
        public bool VerRouter { get { return chkRouter.Checked; } }
        public bool VerRaton { get { return chkRaton.Checked; } }
        public string NombreTema { get { return Tema.Todos[Math.Max(0, cmbTema.SelectedIndex)].Clave; } }
        public bool Animado { get { return chkAnimado.Checked; } }

        static string Codigo(ComboBox c, ListaZonas l) { return c.SelectedIndex <= 0 ? "" : l.Codigos[c.SelectedIndex - 1]; }

        public ConfigForm(Config cfg, bool inicioWindows, string vpnActiva)
        {
            vpn = vpnActiva;
            Text = "MonitorPing - Configuración";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            float k;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) k = g.DpiX / 96f;
            int ancho = (int)(380 * k);

            // Todo en un panel que se ajusta solo, para que no se corte con otras escalas de pantalla
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.Padding = new Padding(12, 8, 12, 10);

            // --- Widget ---
            panel.Controls.Add(Titulo("Widget"));
            FlowLayoutPanel filaTema = Horizontal();
            Label lTema = Etiqueta("Tema:", false, ancho);
            lTema.Margin = new Padding(3, 6, 3, 3);
            cmbTema = new ComboBox();
            cmbTema.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbTema.Width = (int)(150 * k);
            foreach (Tema t in Tema.Todos) cmbTema.Items.Add(t.Nombre);
            cmbTema.SelectedIndex = Math.Max(0, Array.IndexOf(Tema.Todos, Tema.Buscar(cfg.NombreTema)));
            chkAnimado = Casilla("Fondo animado", cfg.Animado);
            chkAnimado.Margin = new Padding(12, 5, 3, 3);
            filaTema.Controls.Add(lTema);
            filaTema.Controls.Add(cmbTema);
            filaTema.Controls.Add(chkAnimado);
            panel.Controls.Add(filaTema);
            panel.Controls.Add(Etiqueta("El tema cambia el widget, el menú, el icono y estas ventanas. Se aplica al guardar.", true, ancho));
            chkCompacto = Casilla("Modo compacto (una línea por servicio)", cfg.Compacto);
            chkBloqueado = Casilla("Modo monitor: el ratón atraviesa el widget", cfg.Bloqueado);
            panel.Controls.Add(chkCompacto);
            panel.Controls.Add(chkBloqueado);
            panel.Controls.Add(Etiqueta("Para moverlo, desbloquéalo desde el icono de la bandeja y arrástralo.", true, ancho));

            FlowLayoutPanel filaOpacidad = Horizontal();
            trkOpacidad = new TrackBar();
            trkOpacidad.Minimum = 30;
            trkOpacidad.Maximum = 100;
            trkOpacidad.TickFrequency = 10;
            trkOpacidad.SmallChange = 5;
            trkOpacidad.LargeChange = 10;
            trkOpacidad.Value = Math.Max(30, Math.Min(100, cfg.Opacidad));
            trkOpacidad.Width = (int)(220 * k);
            lblOpacidad = Etiqueta(trkOpacidad.Value + " %", false, ancho);
            lblOpacidad.Margin = new Padding(3, 8, 3, 3);
            trkOpacidad.ValueChanged += delegate { lblOpacidad.Text = trkOpacidad.Value + " %"; };
            Label lOp = Etiqueta("Opacidad:", false, ancho);
            lOp.Margin = new Padding(3, 8, 3, 3);
            filaOpacidad.Controls.Add(lOp);
            filaOpacidad.Controls.Add(trkOpacidad);
            filaOpacidad.Controls.Add(lblOpacidad);
            panel.Controls.Add(filaOpacidad);

            panel.Controls.Add(Etiqueta("Filas visibles:", false, ancho));
            FlowLayoutPanel filasVis = Horizontal();
            chkGfn = Casilla("GFN", cfg.VerGfn);
            chkXcloud = Casilla("xCloud", cfg.VerXcloud);
            chkEeuu = Casilla("EE.UU.", cfg.VerEeuu);
            chkRouter = Casilla("Router", cfg.VerRouter);
            chkRaton = Casilla("Ratón y PC", cfg.VerRaton);
            filasVis.Controls.AddRange(new Control[] { chkGfn, chkXcloud, chkEeuu, chkRouter, chkRaton });
            panel.Controls.Add(filasVis);

            chkEncima = Casilla("Siempre encima (también sobre el juego)", cfg.Encima);
            chkMostrar = Casilla("Mostrar el widget al abrir la aplicación", cfg.MostrarAlIniciar);
            chkInicio = Casilla("Iniciar con Windows", inicioWindows);
            panel.Controls.Add(chkEncima);
            panel.Controls.Add(chkMostrar);
            panel.Controls.Add(chkInicio);

            // --- Antes de jugar ---
            panel.Controls.Add(Titulo("Antes de jugar"));
            panel.Controls.Add(Etiqueta("Zona de GeForce NOW:", false, ancho));
            cmbZonaGfn = Zonas(ListaZonas.Gfn, cfg.ZonaGfn, ancho);
            panel.Controls.Add(cmbZonaGfn);
            panel.Controls.Add(Etiqueta("Región de Xbox Cloud (xCloud):", false, ancho));
            cmbZonaXcloud = Zonas(ListaZonas.Xcloud, cfg.ZonaXcloud, ancho);
            panel.Controls.Add(cmbZonaXcloud);
            panel.Controls.Add(Etiqueta("Ping actual a cada zona (doble clic para elegirla). Lo ideal es menos de 40 ms; con más de 80 ms se nota el lag.", true, ancho));
            if (vpn != null)
            {
                Label aviso = Etiqueta("La VPN (" + vpn + ") está activa: apágala para que estos tiempos sean los reales.", false, ancho);
                aviso.ForeColor = tema.TextoAviso;
                panel.Controls.Add(aviso);
            }
            lstZonas = new ListBox();
            lstZonas.Width = ancho;
            lstZonas.Height = (int)(150 * k);
            lstZonas.IntegralHeight = false;
            lstZonas.DoubleClick += delegate
            {
                ItemZona it = lstZonas.SelectedItem as ItemZona;
                if (it == null) return;
                if (it.Lista == ListaZonas.Gfn) cmbZonaGfn.SelectedIndex = it.Indice + 1;
                else cmbZonaXcloud.SelectedIndex = it.Indice + 1;
            };
            panel.Controls.Add(lstZonas);
            btnMedir = new Button();
            btnMedir.Text = "Medir zonas";
            btnMedir.AutoSize = true;
            btnMedir.Click += delegate { MedirZonas(); };
            panel.Controls.Add(btnMedir);
            Shown += delegate { MedirZonas(); };

            // --- Comparación ---
            panel.Controls.Add(Titulo("Comparación"));
            panel.Controls.Add(Etiqueta("Servidor en EE.UU. para comparar:", false, ancho));
            cmbEEUU = new ComboBox();
            cmbEEUU.DropDownStyle = ComboBoxStyle.DropDown;
            cmbEEUU.Width = ancho;
            for (int i = 0; i < Servidores.Hosts.Length; i++) cmbEEUU.Items.Add(Servidores.Etiqueta(i));
            int actual = Servidores.Indice(cfg.ServidorEEUU);
            cmbEEUU.Text = actual >= 0 ? Servidores.Etiqueta(actual) : cfg.ServidorEEUU;
            panel.Controls.Add(cmbEEUU);
            panel.Controls.Add(Etiqueta("IP o host fijo de GeForce NOW (opcional):", false, ancho));
            txtGfn = new TextBox();
            txtGfn.Width = ancho;
            txtGfn.Text = cfg.GfnManual;
            panel.Controls.Add(txtGfn);
            panel.Controls.Add(Etiqueta("Déjalo vacío: durante la partida el stream se detecta solo, en la app o en el navegador.", true, ancho));

            Button ok = new Button();
            ok.Text = "Guardar";
            ok.DialogResult = DialogResult.OK;
            ok.AutoSize = true;
            Button cancel = new Button();
            cancel.Text = "Cancelar";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.AutoSize = true;
            FlowLayoutPanel botones = new FlowLayoutPanel();
            botones.FlowDirection = FlowDirection.RightToLeft;
            botones.AutoSize = true;
            botones.Anchor = AnchorStyles.Right;
            botones.Margin = new Padding(0, 10, 0, 0);
            botones.Controls.Add(cancel);
            botones.Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;
            panel.Controls.Add(botones);
            Controls.Add(panel);
            AplicarTema();
        }

        static ComboBox Zonas(ListaZonas lista, string codigo, int ancho)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Width = ancho;
            c.Items.Add("Automática (la de menor ping)");
            foreach (string n in lista.Nombres) c.Items.Add(n);
            c.SelectedIndex = lista.Indice(codigo) + 1;
            return c;
        }

        void MedirZonas()
        {
            btnMedir.Enabled = false;
            lstZonas.Items.Clear();
            lstZonas.Items.Add("Midiendo las zonas...");
            Thread t = new Thread(delegate ()
            {
                int[] g = ListaZonas.Gfn.MedirTodas();
                int[] x = ListaZonas.Xcloud.MedirTodas();
                try { BeginInvoke((MethodInvoker)delegate { MostrarZonas(g, x); }); }
                catch { } // el formulario ya se cerró
            });
            t.IsBackground = true;
            t.Start();
        }

        void MostrarZonas(int[] msGfn, int[] msXcloud)
        {
            if (IsDisposed) return;
            lstZonas.Items.Clear();
            Agregar(ListaZonas.Gfn, msGfn);
            Agregar(ListaZonas.Xcloud, msXcloud);
            btnMedir.Enabled = true;
        }

        void Agregar(ListaZonas lista, int[] ms)
        {
            lstZonas.Items.Add("— " + lista.Servicio + " —");
            List<ItemZona> items = new List<ItemZona>();
            for (int i = 0; i < ms.Length; i++)
            {
                ItemZona it = new ItemZona();
                it.Lista = lista;
                it.Indice = i;
                it.Ms = ms[i];
                items.Add(it);
            }
            // De menor a mayor ping; las que no responden, al final
            items.Sort(delegate(ItemZona a, ItemZona b)
            {
                int va = a.Ms < 0 ? int.MaxValue : a.Ms, vb = b.Ms < 0 ? int.MaxValue : b.Ms;
                return va.CompareTo(vb);
            });
            foreach (ItemZona it in items) lstZonas.Items.Add(it);
        }

        static Label Titulo(string texto)
        {
            Label l = new Label();
            l.Tag = "titulo"; // con los temas nuevos va en el color de acento
            l.Text = texto;
            l.AutoSize = true;
            l.Font = new Font("Segoe UI Semibold", 10f);
            l.Margin = new Padding(3, 10, 3, 4);
            return l;
        }

        static Label Etiqueta(string texto, bool ayuda, int ancho)
        {
            Label l = new Label();
            l.Text = texto;
            l.AutoSize = true;
            l.MaximumSize = new Size(ancho, 0);
            if (ayuda) l.ForeColor = SystemColors.GrayText;
            return l;
        }

        static CheckBox Casilla(string texto, bool marcada)
        {
            CheckBox c = new CheckBox();
            c.Text = texto;
            c.Checked = marcada;
            c.AutoSize = true;
            return c;
        }

        static FlowLayoutPanel Horizontal()
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.FlowDirection = FlowDirection.LeftToRight;
            p.WrapContents = false;
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.Margin = new Padding(0);
            return p;
        }
    }
}
