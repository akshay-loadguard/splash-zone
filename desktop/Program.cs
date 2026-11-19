using System.IO.Ports;
using System.Management;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SplashZone;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

sealed class MainForm : Form
{
    const string Host = "app.splashzone.example";
    static readonly Regex ComName = new(@"^COM\d{1,3}$", RegexOptions.CultureInvariant);

    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer watchdog = new() { Interval = 2000 };
    readonly string dataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SplashZone");
    SerialPort? port;

    public MainForm()
    {
        Text = "Splash Zone";
        MinimumSize = new Size(800, 600);
        WindowState = FormWindowState.Maximized;
        BackColor = Color.FromArgb(143, 211, 255);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        Controls.Add(web);
        Load += async (_, _) => await InitAsync();
        FormClosing += (_, _) => ClosePort();
        watchdog.Tick += (_, _) =>
        {
            if (port != null && !port.IsOpen) { ClosePort(); Post(new { type = "closed" }); }
        };
        watchdog.Start();
    }

    async Task InitAsync()
    {
        // The game page is embedded in the exe and served from a local folder.
        string appDir = Path.Combine(dataDir, "app");
        Directory.CreateDirectory(appDir);
        File.WriteAllText(Path.Combine(appDir, "index.html"), ReadResource("SplashZone.index.html"));

        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(dataDir, "WebView2"),
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            await web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "Splash Zone needs the Microsoft Edge WebView2 Runtime (included with Windows 11 and most Windows 10 PCs).\n\n" +
                "Install it from https://go.microsoft.com/fwlink/p/?LinkId=2124703 and start Splash Zone again.",
                "Splash Zone", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
            return;
        }

        var core = web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(Host, appDir, CoreWebView2HostResourceAccessKind.DenyCors);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(ReadResource("SplashZone.serial-shim.js"));

        // Stay on the local game page only: no popups, no navigation to the internet.
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };
        core.WebMessageReceived += OnMessage;
        core.Navigate($"https://{Host}/index.html");
    }

    static string ReadResource(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing resource " + name);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    void Post(object msg)
    {
        if (IsDisposed || web.CoreWebView2 == null) return;
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(msg));
    }

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var m = doc.RootElement;
            string cmd = m.GetProperty("cmd").GetString() ?? "";
            int id = m.TryGetProperty("id", out var idEl) ? idEl.GetInt32() : 0;
            switch (cmd)
            {
                case "list":
                    Post(new { id, ports = ListPorts() });
                    break;
                case "open":
                    _ = OpenAsync(id, m.GetProperty("path").GetString() ?? "", m.GetProperty("baud").GetInt32());
                    break;
                case "close":
                    ClosePort();
                    Post(new { id, ok = true });
                    break;
                case "fullscreen":
                    ToggleFullscreen();
                    break;
            }
        }
        catch (Exception) { /* ignore malformed messages */ }
    }

    static object[] ListPorts()
    {
        var friendly = new Dictionary<string, string>();
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            foreach (var o in q.Get())
            {
                var n = o["Name"]?.ToString() ?? "";
                var mm = Regex.Match(n, @"\((COM\d+)\)");
                if (mm.Success) friendly[mm.Groups[1].Value] = n;
            }
        }
        catch { /* friendly names are optional */ }

        return SerialPort.GetPortNames()
            .Where(p => ComName.IsMatch(p))
            .Distinct()
            .OrderBy(p => int.Parse(p[3..]))
            .Select(p => (object)new { path = p, name = friendly.GetValueOrDefault(p, p) })
            .ToArray();
    }

    async Task OpenAsync(int id, string path, int baud)
    {
        if (!ComName.IsMatch(path) || baud is < 300 or > 4_000_000)
        {
            Post(new { id, ok = false, error = "Invalid port settings" });
            return;
        }
        ClosePort();
        var sp = new SerialPort(path, baud) { DtrEnable = true, RtsEnable = true };
        try { await Task.Run(sp.Open); }
        catch (Exception ex)
        {
            sp.Dispose();
            Post(new { id, ok = false, error = ex.Message });
            return;
        }
        sp.DataReceived += (_, _) =>
        {
            try
            {
                int n = sp.BytesToRead;
                if (n <= 0) return;
                var buf = new byte[n];
                int read = sp.Read(buf, 0, n);
                var bytes = buf.Take(read).Select(b => (int)b).ToArray();
                BeginInvoke(() => Post(new { type = "data", bytes }));
            }
            catch { /* port closed mid-read */ }
        };
        port = sp;
        Post(new { id, ok = true });
    }

    void ClosePort()
    {
        var p = port;
        port = null;
        if (p == null) return;
        // Closing a Bluetooth serial link can take a moment; don't freeze the window.
        Task.Run(() => { try { p.Close(); } catch { } p.Dispose(); });
    }

    void ToggleFullscreen()
    {
        if (FormBorderStyle == FormBorderStyle.None)
        {
            FormBorderStyle = FormBorderStyle.Sizable;
        }
        else
        {
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
        }
        WindowState = FormWindowState.Maximized;
    }
}
