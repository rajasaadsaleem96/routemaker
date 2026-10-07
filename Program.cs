using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Panorra.LKH;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private const string DefaultStart = "33.5871849, 73.1193211";

    private readonly TextBox startBox = new();
    private readonly TextBox endBox = new();
    private readonly RichTextBox gpsBox = new();
    private readonly TextBox matrixBox = new();
    private readonly TextBox mappingBox = new();
    private readonly TextBox infoBox = new();
    private readonly NumericUpDown runsBox = new();
    private readonly NumericUpDown moveBox = new();
    private readonly NumericUpDown candidatesBox = new();
    private readonly TextBox timeLimitBox = new();
    private readonly Label matrixStatus = new();
    private readonly Label status = new();
    private readonly Label resultStatus = new();
    private readonly RichTextBox resultBox = new();
    private readonly Button optimizeButton = new();
    private readonly Button stopButton = new();
    private Process? runningProcess;
    private string? lastRouteText;
    private string? lastWorkingDirectory;

    public MainForm()
    {
        Text = "Panorra LKH Route Optimizer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 760);
        Size = new Size(1180, 900);
        Font = new Font("Segoe UI", 10F);
        AutoScaleMode = AutoScaleMode.Dpi;

        startBox.Text = DefaultStart;
        endBox.Text = DefaultStart;
        startBox.Dock = DockStyle.Fill;
        endBox.Dock = DockStyle.Fill;
        startBox.MaxLength = 1024;
        endBox.MaxLength = 1024;

        gpsBox.Dock = DockStyle.Fill;
        gpsBox.Multiline = true;
        gpsBox.ScrollBars = RichTextBoxScrollBars.Both;
        gpsBox.WordWrap = false;
        gpsBox.MaxLength = int.MaxValue;
        gpsBox.Font = new Font("Consolas", 10F);

        matrixBox.Dock = DockStyle.Fill;
        mappingBox.Dock = DockStyle.Fill;
        infoBox.Dock = DockStyle.Fill;

        ConfigureNumeric(runsBox, 10, 1, 1000);
        ConfigureNumeric(moveBox, 5, 2, 5);
        ConfigureNumeric(candidatesBox, 5, 1, 50);
        timeLimitBox.Text = "0";
        timeLimitBox.Width = 120;

        resultBox.Dock = DockStyle.Fill;
        resultBox.Multiline = true;
        resultBox.ReadOnly = true;
        resultBox.BackColor = Color.White;
        resultBox.Font = new Font("Consolas", 10F);
        resultBox.WordWrap = false;
        resultBox.ScrollBars = RichTextBoxScrollBars.Both;
        resultBox.MaxLength = int.MaxValue;

        matrixStatus.AutoSize = true;
        matrixStatus.ForeColor = Color.DimGray;
        matrixStatus.Text = "No matrix loaded.";

        status.AutoSize = true;
        status.ForeColor = Color.DimGray;
        status.Text = "Ready.";

        resultStatus.AutoSize = true;
        resultStatus.Text = "No optimized route yet.";

        stopButton.Text = "Stop";
        stopButton.Enabled = false;
        stopButton.AutoSize = true;
        stopButton.Click += (_, _) => StopOptimization();

        optimizeButton.Text = "Optimize with LKH";
        optimizeButton.AutoSize = true;
        optimizeButton.Font = new Font(Font, FontStyle.Bold);
        optimizeButton.Click += async (_, _) => await StartOptimizationAsync();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildTitle(), 0, 0);
        root.Controls.Add(BuildEndpoints(), 0, 1);
        root.Controls.Add(BuildGpsSection(), 0, 2);
        root.Controls.Add(BuildMatrixAndSettings(), 0, 3);
        root.Controls.Add(BuildResultSection(), 0, 4);
        root.Controls.Add(BuildBottomBar(), 0, 5);

        Controls.Add(root);
        FormClosing += (_, _) =>
        {
            try
            {
                if (runningProcess is { HasExited: false })
                    runningProcess.Kill(entireProcessTree: true);
            }
            catch { }
        };
    }

    private Control BuildTitle()
    {
        var p = new Panel { Dock = DockStyle.Fill, Height = 54 };
        var title = new Label
        {
            Text = "Panorra • LKH Route Optimizer",
            Dock = DockStyle.Left,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 20F),
        };
        var subtitle = new Label
        {
            Text = "Windows 10/11 • native desktop application • LKH-3",
            Dock = DockStyle.Right,
            AutoSize = true,
            ForeColor = Color.DimGray,
            Padding = new Padding(0, 9, 0, 0)
        };
        p.Controls.Add(subtitle);
        p.Controls.Add(title);
        return p;
    }

    private Control BuildEndpoints()
    {
        var outer = new GroupBox { Text = "Start / End", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        t.Controls.Add(new Label { Text = "Start point:", Anchor = AnchorStyles.Left, AutoSize = true }, 0, 0);
        t.Controls.Add(startBox, 1, 0);
        t.Controls.Add(new Label { Text = "End point:", Anchor = AnchorStyles.Left, AutoSize = true, Padding = new Padding(12,0,0,0) }, 2, 0);
        t.Controls.Add(endBox, 3, 0);
        outer.Controls.Add(t);
        return outer;
    }

    private Control BuildGpsSection()
    {
        var outer = new GroupBox
        {
            Text = "GPS stops — paste one coordinate / Plus Code per line",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        outer.Controls.Add(gpsBox);
        return outer;
    }

    private Control BuildMatrixAndSettings()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
        };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));

        var files = new GroupBox { Text = "Distance matrix files", Dock = DockStyle.Fill, Height = 150, Padding = new Padding(8) };
        var ft = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4 };
        ft.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        ft.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        ft.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddFileRow(ft, 0, "distance-matrix.uint16", matrixBox, SelectMatrix);
        AddFileRow(ft, 1, "gps-mapping.txt", mappingBox, SelectMapping);
        AddFileRow(ft, 2, "matrix-info.txt", infoBox, SelectInfo);
        ft.Controls.Add(matrixStatus, 0, 3);
        ft.SetColumnSpan(matrixStatus, 3);
        files.Controls.Add(ft);

        var settings = new GroupBox { Text = "LKH settings", Dock = DockStyle.Fill, Height = 150, Padding = new Padding(8) };
        var st = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
        st.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        st.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        AddSetting(st, 0, "Runs", runsBox);
        AddSetting(st, 1, "Move type", moveBox);
        AddSetting(st, 2, "Max candidates", candidatesBox);
        AddSetting(st, 3, "Time limit (sec)", timeLimitBox);
        st.Controls.Add(new Label
        {
            Text = "0 = unlimited",
            AutoSize = true,
            ForeColor = Color.DimGray
        }, 1, 4);
        settings.Controls.Add(st);

        outer.Controls.Add(files, 0, 0);
        outer.Controls.Add(settings, 1, 0);
        return outer;
    }

    private Control BuildResultSection()
    {
        var outer = new GroupBox
        {
            Text = "Optimized route — exact GPS strings",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.Controls.Add(resultStatus, 0, 0);
        t.Controls.Add(resultBox, 0, 1);
        outer.Controls.Add(t);
        return outer;
    }

    private Control BuildBottomBar()
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, AutoSize = true };
        p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        p.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var copy = new Button { Text = "Copy route", AutoSize = true };
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(lastRouteText))
                Clipboard.SetText(lastRouteText);
        };

        var export = new Button { Text = "Export TXT", AutoSize = true };
        export.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(lastRouteText))
                return;
            using var dlg = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = "panorra-optimized-route.txt"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                File.WriteAllText(dlg.FileName, lastRouteText, new UTF8Encoding(false));
        };

        p.Controls.Add(optimizeButton, 0, 0);
        p.Controls.Add(stopButton, 1, 0);
        p.Controls.Add(status, 2, 0);
        p.Controls.Add(copy, 3, 0);
        p.Controls.Add(export, 4, 0);
        return p;
    }

    private static void ConfigureNumeric(NumericUpDown box, decimal value, decimal min, decimal max)
    {
        box.Dock = DockStyle.Fill;
        box.Minimum = min;
        box.Maximum = max;
        box.Value = value;
        box.TextAlign = HorizontalAlignment.Center;
    }

    private static void AddFileRow(TableLayoutPanel t, int row, string label, TextBox box, EventHandler handler)
    {
        t.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        t.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        t.Controls.Add(box, 1, row);
        var b = new Button { Text = "Browse…", AutoSize = true };
        b.Click += handler;
        t.Controls.Add(b, 2, row);
    }

    private static void AddSetting(TableLayoutPanel t, int row, string label, Control control)
    {
        t.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left, AutoSize = true }, 0, row);
        t.Controls.Add(control, 1, row);
    }

    private void SelectMatrix(object? sender, EventArgs e) => SelectFile(matrixBox, "Raw uint16 matrix (*.uint16;*.bin)|*.uint16;*.bin|All files (*.*)|*.*");
    private void SelectMapping(object? sender, EventArgs e) => SelectFile(mappingBox, "Mapping text (*.txt)|*.txt|All files (*.*)|*.*");
    private void SelectInfo(object? sender, EventArgs e) => SelectFile(infoBox, "Matrix info (*.txt)|*.txt|All files (*.*)|*.*");

    private void SelectFile(TextBox target, string filter)
    {
        using var dlg = new OpenFileDialog { Filter = filter };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            target.Text = dlg.FileName;
            UpdateMatrixStatus();
        }
    }

    private void UpdateMatrixStatus()
    {
        if (!File.Exists(matrixBox.Text) || !File.Exists(mappingBox.Text) || !File.Exists(infoBox.Text))
        {
            matrixStatus.Text = "Select all three matrix files.";
            return;
        }
        try
        {
            var info = ReadMatrixInfo(infoBox.Text);
            long n = info.Count;
            long expected = checked(n * n * 2);
            long actual = new FileInfo(matrixBox.Text).Length;
            matrixStatus.Text = $"Matrix detected: {n:N0} × {n:N0} • directed uint16 • {FormatBytes(actual)}" +
                                (actual == expected ? "" : " • SIZE MISMATCH");
        }
        catch (Exception ex)
        {
            matrixStatus.Text = "Matrix info error: " + ex.Message;
        }
    }

    private async Task StartOptimizationAsync()
    {
        if (runningProcess is { HasExited: false })
            return;

        try
        {
            var start = startBox.Text.Trim();
            var end = endBox.Text.Trim();
            if (start.Length == 0 || end.Length == 0)
                throw new InvalidOperationException("Start and end points are required.");

            var pasted = ParseGpsLines(gpsBox.Text);
            if (pasted.Count == 0)
                throw new InvalidOperationException("Paste at least one GPS/Plus Code line.");

            ValidateFiles();
            var info = ReadMatrixInfo(infoBox.Text);
            var mapping = ReadMapping(mappingBox.Text);
            ValidateMatrixFile(matrixBox.Text, mapping.Count, info);

            var startKey = NormalizeKey(start);
            var endKey = NormalizeKey(end);
            if (!mapping.TryGetValue(startKey, out var startMatrixId))
                throw new InvalidOperationException("The Start point was not found in gps-mapping.txt.");
            if (!mapping.TryGetValue(endKey, out var endMatrixId))
                throw new InvalidOperationException("The End point was not found in gps-mapping.txt.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stops = new List<Stop>();

            AddStop(start, startMatrixId, seen, stops, force: true);
            foreach (var s in pasted)
            {
                if (NormalizeKey(s) == startKey || NormalizeKey(s) == endKey)
                    continue;
                if (mapping.TryGetValue(NormalizeKey(s), out var id))
                    AddStop(s, id, seen, stops, force: false);
                else
                    throw new InvalidOperationException("GPS not found in gps-mapping.txt: " + s);
            }

            bool sameEndpoint = string.Equals(startKey, endKey, StringComparison.Ordinal);
            if (!sameEndpoint)
            {
                if (mapping.TryGetValue(endKey, out var id))
                    AddStop(end, id, seen, stops, force: true);
            }

            if (stops.Count < 2)
                throw new InvalidOperationException("At least two distinct matrix nodes are required.");

            int dummyId = 0;
            if (!sameEndpoint)
                dummyId = stops.Count + 1;

            SetRunningUi(true);
            status.Text = $"Preparing LKH problem for {stops.Count:N0} matrix nodes…";
            resultStatus.Text = "LKH is optimizing. The route box will be filled when the run finishes.";
            resultBox.Clear();
            lastRouteText = null;

            var work = PrepareWorkingFiles(stops, sameEndpoint, dummyId, info.Count);
            lastWorkingDirectory = work.Directory;

            var output = await RunLkhAsync(
                work.ParameterFile,
                work.TourFile,
                matrixBox.Text,
                info.Count,
                dummyId,
                startLocalId: 1,
                endLocalId: sameEndpoint ? 1 : stops.Count);

            var tour = ParseTour(output);
            var orderedStops = ConvertTourToStops(tour, stops, sameEndpoint, dummyId, startMatrixId, endMatrixId);
            var routeLines = orderedStops.Select(x => x.OriginalText).ToList();

            long routeMeters = ComputeRouteDistance(routeLines, mapping, matrixBox.Text, sameEndpoint);
            lastRouteText = string.Join(Environment.NewLine, routeLines);

            resultBox.Text = lastRouteText;
            resultStatus.Text = $"Optimized route: {routeLines.Count:N0} stops • distance {FormatDistance(routeMeters)} • LKH completed.";
            status.Text = "Optimization completed.";
        }
        catch (Exception ex)
        {
            status.Text = "Error.";
            resultStatus.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Panorra LKH Route Optimizer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetRunningUi(false);
        }
    }

    private void ValidateFiles()
    {
        if (!File.Exists(matrixBox.Text))
            throw new FileNotFoundException("distance-matrix.uint16 was not found.");
        if (!File.Exists(mappingBox.Text))
            throw new FileNotFoundException("gps-mapping.txt was not found.");
        if (!File.Exists(infoBox.Text))
            throw new FileNotFoundException("matrix-info.txt was not found.");
    }

    private static List<string> ParseGpsLines(string text)
    {
        return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(x => x.Trim())
                   .Where(x => x.Length > 0)
                   .ToList();
    }

    private static void AddStop(string text, int matrixId, HashSet<string> seen, List<Stop> stops, bool force)
    {
        string key = NormalizeKey(text);
        if (force || seen.Add(key))
            stops.Add(new Stop(text.Trim(), matrixId));
    }

    private sealed record Stop(string OriginalText, int MatrixId);

    private sealed record MatrixInfo(long Count, string Type, string ArrayName, string Unit);

    private sealed record PreparedWork(string Directory, string ParameterFile, string TourFile);

    private static string NormalizeKey(string s)
    {
        s = s.Trim();
        if (s.Contains(','))
            s = Regex.Replace(s, @"\s*,\s*", ",");
        return s;
    }

    private static MatrixInfo ReadMatrixInfo(string file)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
                continue;
            int p = line.IndexOf('=');
            if (p < 0) p = line.IndexOf(':');
            if (p < 0) continue;
            d[line[..p].Trim()] = line[(p + 1)..].Trim();
        }

        if (!d.TryGetValue("COUNT", out var countText) || !long.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count < 1)
            throw new InvalidOperationException("matrix-info.txt has no valid COUNT.");
        string type = d.TryGetValue("TYPE", out var t) ? t : "";
        string arr = d.TryGetValue("ARRAY", out var a) ? a : "";
        string unit = d.TryGetValue("UNIT", out var u) ? u : "";

        if (!type.Equals("DIRECTED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This application expects TYPE=DIRECTED in matrix-info.txt.");
        if (!arr.Equals("Uint16Array", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This application expects ARRAY=Uint16Array in matrix-info.txt.");
        if (!unit.Equals("METERS", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This application expects UNIT=METERS in matrix-info.txt.");

        return new MatrixInfo(count, type, arr, unit);
    }

    private static Dictionary<string, int> ReadMapping(string file)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var ids = new HashSet<int>();
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.Equals("[GPS]", StringComparison.OrdinalIgnoreCase))
                continue;

            string? left = null;
            string? right = null;
            int p = line.IndexOf('|');
            if (p > 0)
            {
                left = line[..p].Trim();
                right = line[(p + 1)..].Trim();
            }
            else
            {
                var m = Regex.Match(line, @"^(\d+)\s+(.+)$");
                if (m.Success)
                {
                    left = m.Groups[1].Value;
                    right = m.Groups[2].Value.Trim();
                }
            }

            if (left is null || right is null || !int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                continue;
            if (id < 1)
                throw new InvalidOperationException("gps-mapping.txt contains a non-positive matrix index.");
            if (!ids.Add(id))
                throw new InvalidOperationException("gps-mapping.txt contains duplicate matrix index " + id + ".");
            string key = NormalizeKey(right);
            if (key.Length == 0)
                continue;
            if (result.ContainsKey(key))
                throw new InvalidOperationException("gps-mapping.txt contains duplicate GPS string: " + right);
            result.Add(key, id);
        }

        if (result.Count == 0)
            throw new InvalidOperationException("gps-mapping.txt contains no usable mappings.");
        return result;
    }

    private static void ValidateMatrixFile(string matrixFile, int mappingCount, MatrixInfo info)
    {
        if (info.Count != mappingCount)
            throw new InvalidOperationException($"Matrix COUNT ({info.Count:N0}) does not match gps-mapping.txt entries ({mappingCount:N0}).");
        long expected = checked(info.Count * info.Count * 2);
        long actual = new FileInfo(matrixFile).Length;
        if (actual != expected)
            throw new InvalidOperationException($"distance-matrix.uint16 size is {FormatBytes(actual)}, but COUNT={info.Count:N0} requires {FormatBytes(expected)}.");
    }

    private static PreparedWork PrepareWorkingFiles(List<Stop> stops, bool sameEndpoint, int dummyId, long matrixCount)
    {
        string dir = Path.Combine(Path.GetTempPath(), "PanorraLKH", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        string problemFile = Path.Combine(dir, "problem.tsp");
        string parFile = Path.Combine(dir, "problem.par");
        string tourFile = Path.Combine(dir, "result.tour");

        var sb = new StringBuilder();
        sb.AppendLine("NAME : PANORRA_ROUTE");
        sb.AppendLine("TYPE : ATSP");
        sb.AppendLine($"DIMENSION : {(sameEndpoint ? stops.Count : stops.Count + 1).ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine("EDGE_WEIGHT_TYPE : SPECIAL");
        sb.AppendLine("NODE_COORD_TYPE : TWOD_COORDS");
        sb.AppendLine("NODE_COORD_SECTION");
        for (int i = 0; i < stops.Count; i++)
            sb.AppendLine($"{i + 1} {stops[i].MatrixId.ToString(CultureInfo.InvariantCulture)} 0");
        if (!sameEndpoint)
            sb.AppendLine($"{dummyId} 0 0");
        sb.AppendLine("EOF");
        File.WriteAllText(problemFile, sb.ToString(), new UTF8Encoding(false));

        int runs = (int)10;
        int move = 5;
        int candidates = 5;

        // The caller's settings are read separately in RunLkhAsync.
        File.WriteAllText(parFile,
            "PROBLEM_FILE = " + problemFile + Environment.NewLine +
            "OUTPUT_TOUR_FILE = " + tourFile + Environment.NewLine +
            "RUNS = " + runs.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
            "MOVE_TYPE = " + move.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
            "MAX_CANDIDATES = " + candidates.ToString(CultureInfo.InvariantCulture) + Environment.NewLine +
            "CANDIDATE_SET_TYPE = ALPHA" + Environment.NewLine +
            "INITIAL_TOUR_ALGORITHM = NEAREST_NEIGHBOR" + Environment.NewLine +
            "TRACE_LEVEL = 1" + Environment.NewLine,
            new UTF8Encoding(false));

        return new PreparedWork(dir, parFile, tourFile);
    }

    private async Task<string> RunLkhAsync(string parameterFile, string tourFile, string matrixFile, long matrixCount, int dummyId, int startLocalId, int endLocalId)
    {
        string lkhPath = ExtractEmbeddedLkh();
        string content = File.ReadAllText(parameterFile);

        content = Regex.Replace(content, @"\bRUNS\s*=\s*\d+", $"RUNS = {((int)runsBox.Value).ToString(CultureInfo.InvariantCulture)}");
        content = Regex.Replace(content, @"\bMOVE_TYPE\s*=\s*\d+", $"MOVE_TYPE = {((int)moveBox.Value).ToString(CultureInfo.InvariantCulture)}");
        content = Regex.Replace(content, @"\bMAX_CANDIDATES\s*=\s*\d+", $"MAX_CANDIDATES = {((int)candidatesBox.Value).ToString(CultureInfo.InvariantCulture)}");

        if (double.TryParse(timeLimitBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds > 0)
            content += $"TIME_LIMIT = {seconds.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}";

        content += $"SEED = 1{Environment.NewLine}";
        File.WriteAllText(parameterFile, content, new UTF8Encoding(false));

        var psi = new ProcessStartInfo
        {
            FileName = lkhPath,
            Arguments = """ + parameterFile + """,
            WorkingDirectory = Path.GetDirectoryName(parameterFile)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["PANORRA_MATRIX_FILE"] = matrixFile;
        psi.Environment["PANORRA_MATRIX_COUNT"] = matrixCount.ToString(CultureInfo.InvariantCulture);
        if (dummyId > 0)
        {
            psi.Environment["PANORRA_DUMMY_ID"] = dummyId.ToString(CultureInfo.InvariantCulture);
            psi.Environment["PANORRA_START_ID"] = startLocalId.ToString(CultureInfo.InvariantCulture);
            psi.Environment["PANORRA_END_ID"] = endLocalId.ToString(CultureInfo.InvariantCulture);
        }

        runningProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!runningProcess.Start())
            throw new InvalidOperationException("LKH process could not be started.");

        var log = new StringBuilder();
        Task stdout = Task.Run(async () =>
        {
            while (await runningProcess.StandardOutput.ReadLineAsync() is { } line)
            {
                log.AppendLine(line);
                if (!IsDisposed)
                    BeginInvoke(() => status.Text = line);
            }
        });
        Task stderr = Task.Run(async () =>
        {
            while (await runningProcess.StandardError.ReadLineAsync() is { } line)
            {
                log.AppendLine(line);
                if (!IsDisposed)
                    BeginInvoke(() => status.Text = line);
            }
        });

        await runningProcess.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);

        int exitCode = runningProcess.ExitCode;
        runningProcess.Dispose();
        runningProcess = null;

        if (exitCode != 0)
            throw new InvalidOperationException("LKH exited with code " + exitCode + ".\n\n" + log.ToString());

        if (!File.Exists(tourFile))
            throw new InvalidOperationException("LKH finished but did not produce a tour file.\n\n" + log.ToString());

        return log.ToString();
    }

    private string ExtractEmbeddedLkh()
    {
        string dir = Path.Combine(Path.GetTempPath(), "PanorraLKH", "engine");
        Directory.CreateDirectory(dir);
        string exe = Path.Combine(dir, "LKH.exe");
        if (File.Exists(exe))
            return exe;

        var asm = Assembly.GetExecutingAssembly();
        string? resource = asm.GetManifestResourceNames()
            .FirstOrDefault(x => x.EndsWith("LKH.exe", StringComparison.OrdinalIgnoreCase));
        if (resource is null)
            throw new InvalidOperationException("Embedded LKH engine was not found in this build.");

        using Stream? input = asm.GetManifestResourceStream(resource);
        if (input is null)
            throw new InvalidOperationException("Could not open the embedded LKH engine.");

        using var output = File.Create(exe);
        input.CopyTo(output);
        return exe;
    }

    private static List<int> ParseTour(string log)
    {
        // This is only used as a fallback for diagnostics; the real tour is read separately.
        return new List<int>();
    }

    private List<Stop> ConvertTourToStops(List<int> ignored, List<Stop> stops, bool sameEndpoint, int dummyId, int startMatrixId, int endMatrixId)
    {
        string tourFile = Path.Combine(lastWorkingDirectory!, "result.tour");
        var ids = ReadTourFile(tourFile);
        var byLocal = new Dictionary<int, Stop>();
        for (int i = 0; i < stops.Count; i++)
            byLocal[i + 1] = stops[i];

        if (!sameEndpoint)
            ids = ids.Where(x => x != dummyId).ToList();

        if (ids.Count != stops.Count)
            throw new InvalidOperationException($"LKH returned {ids.Count:N0} route nodes; expected {stops.Count:N0}.");

        var result = new List<Stop>(ids.Count);
        var seen = new HashSet<int>();
        foreach (int id in ids)
        {
            if (!byLocal.TryGetValue(id, out var stop))
                throw new InvalidOperationException("LKH returned an unexpected node id " + id + ".");
            if (!seen.Add(id))
                throw new InvalidOperationException("LKH returned a duplicate node id " + id + ".");
            result.Add(stop);
        }

        if (NormalizeKey(result[0].OriginalText) != NormalizeKey(startBox.Text))
            throw new InvalidOperationException("The LKH tour did not start at the requested Start point.");
        if (!sameEndpoint &&
            NormalizeKey(result[^1].OriginalText) != NormalizeKey(endBox.Text))
            throw new InvalidOperationException("The LKH tour did not end at the requested End point.");

        return result;
    }

    private static List<int> ReadTourFile(string file)
    {
        var result = new List<int>();
        bool section = false;
        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Equals("TOUR_SECTION", StringComparison.OrdinalIgnoreCase))
            {
                section = true;
                continue;
            }
            if (!section) continue;
            if (line.Equals("-1", StringComparison.OrdinalIgnoreCase) || line.Equals("EOF", StringComparison.OrdinalIgnoreCase))
                break;
            if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                result.Add(id);
        }
        return result;
    }

    private static long ComputeRouteDistance(List<string> route, Dictionary<string, int> mapping, string matrixFile, bool closed)
    {
        using var fs = new FileStream(matrixFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.RandomAccess);
        using var br = new BinaryReader(fs);

        long total = 0;
        for (int i = 0; i + 1 < route.Count; i++)
        {
            int a = mapping[NormalizeKey(route[i])];
            int b = mapping[NormalizeKey(route[i + 1])];
            total += ReadUint16(fs, br, a, b, mapping.Count);
        }

        if (closed && route.Count > 1)
        {
            int a = mapping[NormalizeKey(route[^1])];
            int b = mapping[NormalizeKey(route[0])];
            total += ReadUint16(fs, br, a, b, mapping.Count);
        }
        return total;
    }

    private static ushort ReadUint16(FileStream fs, BinaryReader br, int row, int col, int n)
    {
        long offset = checked(((long)row - 1L) * n + (col - 1L)) * 2L;
        fs.Seek(offset, SeekOrigin.Begin);
        return br.ReadUInt16();
    }

    private void SetRunningUi(bool running)
    {
        optimizeButton.Enabled = !running;
        stopButton.Enabled = running;
        Cursor = running ? Cursors.WaitCursor : Cursors.Default;
        if (!runningProcessIsAlive())
            status.Text = running ? "Working…" : "Ready.";
    }

    private bool runningProcessIsAlive() => runningProcess is { HasExited: false };

    private void StopOptimization()
    {
        try
        {
            if (runningProcess is { HasExited: false })
                runningProcess.Kill(entireProcessTree: true);
            status.Text = "Optimization stopped.";
            resultStatus.Text = "Optimization was stopped by the user.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Stop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string FormatBytes(long bytes)
    {
        double b = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = 0;
        while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
        return b.ToString(i == 0 ? "N0" : "N1", CultureInfo.InvariantCulture) + " " + units[i];
    }

    private static string FormatDistance(long meters)
    {
        if (meters >= 1_000_000)
            return (meters / 1_000_000.0).ToString("N2", CultureInfo.InvariantCulture) + " km";
        return (meters / 1000.0).ToString("N2", CultureInfo.InvariantCulture) + " km";
    }

    private sealed record WorkInfo(string Directory, string ParameterFile, string TourFile);
}