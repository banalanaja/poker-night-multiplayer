using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PokerNight.App;
using PokerNight.Multiplayer;

namespace PokerNight.Desktop;

internal sealed class MainForm : Form
{
    private readonly TextBox game = new() { Dock = DockStyle.Fill };
    private readonly TextBox nickname = new() { Text = "Jogador", MaxLength = 24, Dock = DockStyle.Fill };
    private readonly ComboBox character = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox address = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown port = new() { Minimum = 1024, Maximum = 65535, Value = 27841, Dock = DockStyle.Fill };
    private readonly NumericUpDown people = new() { Minimum = 2, Maximum = 5, Value = 2, Dock = DockStyle.Fill };
    private readonly RadioButton host = new() { Text = "Criar sala", Checked = true, AutoSize = true };
    private readonly RadioButton guest = new() { Text = "Entrar na sala", AutoSize = true };
    private readonly CheckBox autoOpen = new() { Text = "Abrir o jogo quando todos estiverem prontos", Checked = true, AutoSize = true };
    private readonly Button install = Button("Instalar / atualizar mod");
    private readonly Button uninstall = Button("Remover mod");
    private readonly Button start = Button("Criar sala");
    private readonly Button stop = Button("Encerrar sessão");
    private readonly Button open = Button("Abrir jogo");
    private readonly Button export = Button("Exportar diagnóstico…");
    private readonly Button copy = Button("Copiar endereço");
    private readonly Button browse = Button("Escolher pasta…");
    private readonly Label status = new() { AutoSize = false, Width = 840, Height = 42, Text = "Selecione o jogo e instale o mod para começar." };
    private readonly Label gameStatus = new() { AutoSize = false, Width = 840, Height = 28 };
    private readonly ListView seats = new() { View = View.Details, FullRowSelect = true, Width = 840, Height = 140 };
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 500 };
    private readonly Installation setup;
    private readonly DiagnosticJournal journal;
    private readonly string settingsPath;
    private CancellationTokenSource? lifetime;
    private Task? session;
    private GameTransport? server;
    private GameClient? client;
    private FileGameRuntime? runtime;
    private bool busy, closing, allowClose, openedGame, readyLogged, failedLogged;
    private string lastGame = "", lastAddressCategory = "unknown", lastRole = "none";
    private int lastPort = 27841;
    private sealed record SeatOption(Seat Value, string Label) { public override string ToString() => Label; }
    private sealed record Preferences(string Game, string Name, string Address, int Port, Seat Seat);

    private static Button Button(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(8, 5, 8, 5) };
    private static Label TextLabel(string text, int height = 32) => new() { Text = text, Width = 840, Height = height };
    public MainForm()
    {
        Text = "Poker Night Multiplayer — " + AppRelease.Version;
        ClientSize = new Size(900, 840); MinimumSize = new Size(900, 730); StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        string data = Environment.GetEnvironmentVariable("PNM_DATA_DIRECTORY") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PokerNightMultiplayer");
        setup = new(AppAssets.Prepare(data), data);
        journal = new(Path.Combine(data, "diagnostics")); journal.Add("app_open");
        settingsPath = Path.Combine(data, "settings.json");
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(20) };
        Controls.Add(root);
        root.Controls.Add(new Label { Text = "Poker Night Multiplayer", Font = new Font(Font.FontFamily, 23, FontStyle.Bold), ForeColor = Color.DarkGreen, Width = 840, Height = 48 });
        root.Controls.Add(TextLabel("Prévia de testes • ainda precisa de validação com dois jogos completos e por VPN.", 35));
        var folder = new TableLayoutPanel { Width = 840, Height = 40, ColumnCount = 3 };
        folder.ColumnStyles.Add(new(SizeType.Absolute, 110)); folder.ColumnStyles.Add(new(SizeType.Percent, 100)); folder.ColumnStyles.Add(new(SizeType.AutoSize));
        folder.Controls.Add(new Label { Text = "Pasta do jogo", AutoSize = true, Anchor = AnchorStyles.Left }); folder.Controls.Add(game); folder.Controls.Add(browse);
        root.Controls.Add(folder); root.Controls.Add(Row(install, uninstall));
        var modes = Row(host, guest); modes.Margin = new Padding(0, 15, 0, 8); root.Controls.Add(modes);
        var fields = new TableLayoutPanel { Width = 840, Height = 125, ColumnCount = 4, RowCount = 3 };
        for (int i = 0; i < 3; i++) fields.RowStyles.Add(new(SizeType.Absolute, 38));
        fields.ColumnStyles.Add(new(SizeType.Absolute, 110)); fields.ColumnStyles.Add(new(SizeType.Percent, 50)); fields.ColumnStyles.Add(new(SizeType.Absolute, 130)); fields.ColumnStyles.Add(new(SizeType.Percent, 50));
        Field(fields, 0, 0, "Seu nome", nickname); Field(fields, 2, 0, "Personagem", character);
        Field(fields, 0, 1, "IP da VPN", address); Field(fields, 2, 1, "Porta", port);
        Field(fields, 0, 2, "Pessoas", people); fields.Controls.Add(copy, 3, 2);
        root.Controls.Add(fields);
        character.Items.AddRange(new object[] { new SeatOption(Seat.Player, "Lugar original"), new SeatOption(Seat.Heavy, "Heavy"), new SeatOption(Seat.Max, "Max"), new SeatOption(Seat.StrongBad, "Strong Bad"), new SeatOption(Seat.Tycho, "Tycho") }); character.SelectedIndex = 0;
        address.Items.AddRange(GameDiscovery.Addresses()); address.SelectedIndex = 0;
        root.Controls.Add(TextLabel("Usem a mesma VPN. O anfitrião copia o endereço; o convidado cola e escolhe outro personagem.", 40));
        root.Controls.Add(autoOpen); root.Controls.Add(Row(start, stop, open));
        root.Controls.Add(status); root.Controls.Add(gameStatus);
        seats.Columns.Add("Personagem", 200); seats.Columns.Add("Jogador", 360); seats.Columns.Add("Estado", 230);
        root.Controls.Add(seats); root.Controls.Add(Row(export));
        root.Controls.Add(TextLabel("Se algo falhar: exporte o ZIP de diagnóstico e envie para quem vai investigar. Nenhuma carta, save ou arquivo do jogo entra no ZIP.", 48));
        game.Text = GameDiscovery.Find(); LoadSettings();
        status.Text = setup.IsInstalled(game.Text) ? "Mod instalado. Escolha Criar sala ou Entrar na sala." : "Selecione a pasta do jogo e clique em Instalar / atualizar mod.";
        browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { Description = "Selecione a pasta que contém CelebrityPoker.exe", SelectedPath = game.Text }; if (dialog.ShowDialog(this) == DialogResult.OK) game.Text = dialog.SelectedPath; };
        install.Click += async (_, _) => await Install(false); uninstall.Click += async (_, _) => await Install(true);
        start.Click += async (_, _) => await StartSession(); stop.Click += async (_, _) => await StopSession();
        open.Click += (_, _) => OpenGame(); export.Click += (_, _) => Export();
        copy.Click += (_, _) => { if (IPAddress.TryParse(address.Text.Trim(), out _)) { Clipboard.SetText(address.Text.Trim() + ":" + port.Value); status.Text = "Endereço copiado. Envie para seu amigo."; } else status.Text = "Selecione o IP da sua VPN para compartilhar."; };
        host.CheckedChanged += (_, _) => { start.Text = host.Checked ? "Criar sala" : "Entrar e ficar pronto"; people.Enabled = host.Checked && lifetime is null; copy.Enabled = host.Checked; if (guest.Checked && character.SelectedIndex == 0) character.SelectedIndex = 1; };
        refresh.Tick += (_, _) => Tick(); refresh.Start(); UpdateButtons();
        FormClosing += async (_, e) => { if (allowClose) return; e.Cancel = true; if (closing) return; closing = true; refresh.Stop(); await StopSession(); if (busy) { closing = false; refresh.Start(); status.Text = "Aguarde a instalação terminar antes de fechar."; return; } SaveSettings(); allowClose = true; Close(); };
    }
    private static FlowLayoutPanel Row(params Control[] controls)
    { var row = new FlowLayoutPanel { Width = 840, AutoSize = true, WrapContents = true }; row.Controls.AddRange(controls); return row; }
    private static void Field(TableLayoutPanel panel, int column, int row, string label, Control control)
    { panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, column, row); panel.Controls.Add(control, column + 1, row); }
    private void UpdateButtons()
    {
        bool running = lifetime is not null;
        foreach (Control c in new Control[] { game, nickname, character, address, port, host, guest, browse, install, uninstall, start }) c.Enabled = !busy && !running;
        people.Enabled = !busy && !running && host.Checked; copy.Enabled = host.Checked;
        stop.Enabled = running; open.Enabled = running && (server?.Snapshot.Started == true || client?.Lobby?.Started == true);
        export.Enabled = !busy;
    }
    private async Task Install(bool remove)
    {
        busy = true; UpdateButtons(); journal.Add(remove ? "session_stop" : "install_start");
        try
        {
            await setup.Install(game.Text.Trim(), remove, new Progress<string>(value => status.Text = value), CancellationToken.None);
            journal.Add(remove ? "uninstall_ok" : "install_ok");
            status.Text = remove ? "Mod removido. O jogo original foi preservado." : "Instalado. Escolha Criar sala ou Entrar na sala.";
            SaveSettings();
        }
        catch (Exception e) { journal.Add(remove ? "uninstall_failed" : "install_failed", e); status.Text = Friendly(e); }
        finally { busy = false; UpdateButtons(); }
    }
    private async Task StartSession()
    {
        try
        {
            string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(game.Text.Trim()));
            if (!setup.IsInstalled(folder)) { status.Text = "Clique em Instalar / atualizar mod antes de abrir a sala."; return; }
            if (GameRunning(folder)) { status.Text = "Feche o jogo antes de abrir a sala. Ele será aberto com a sessão configurada."; return; }
            string name = nickname.Text.Trim(); if (name.Length is < 1 or > 24 || name.Any(char.IsControl)) throw new ArgumentException("Preencha seu nome (até 24 caracteres).");
            string ipText = address.Text.Trim(); int tcpPort = (int)port.Value;
            if (IPEndPoint.TryParse(ipText, out var endpoint) && ipText.Contains(':')) { ipText = endpoint.Address.ToString(); tcpPort = endpoint.Port; port.Value = Math.Clamp(tcpPort, (int)port.Minimum, (int)port.Maximum); }
            if (!IPAddress.TryParse(ipText, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork || tcpPort is < 1024 or > 65535) throw new ArgumentException("Informe o IP da VPN do anfitrião e uma porta entre 1024 e 65535.");
            bool hosting = host.Checked; Seat seat = ((SeatOption)character.SelectedItem!).Value; int humans = (int)people.Value;
            string digest = setup.Profile.Verify(folder); runtime = new(setup.RuntimeDirectory(folder));
            lastGame = folder; lastPort = tcpPort; lastAddressCategory = GameDiscovery.Category(ipText); lastRole = hosting ? "host" : "client";
            lifetime = new(); var tokenSource = lifetime; openedGame = readyLogged = failedLogged = false;
            journal.Add("session_start"); SaveSettings(); UpdateButtons(); status.Text = hosting ? "Sala aberta. Aguardando seu amigo ficar pronto…" : "Conectando ao anfitrião…";
            session = Task.Run(async () =>
            {
                Task watch = GameProcessLifetime.Watch(folder, tokenSource);
                try
                {
                    if (hosting)
                    {
                        await using var room = new GameTransport(IPAddress.Any, tcpPort, digest, name, seat, runtime, expectedHumans: humans);
                        server = room; await room.RunAsync(tokenSource.Token);
                    }
                    else { client = new GameClient(runtime); await client.RunAsync(ip, tcpPort, new(WireCodec.Version, digest, name), seat, tokenSource.Token); }
                }
                finally { tokenSource.Cancel(); await watch; if (runtime.Configuration is { } config) runtime.End(config.SessionId); }
            });
            await session;
            status.Text = "Sessão encerrada. Feche o jogo antes de abrir outra sala.";
        }
        catch (OperationCanceledException) { status.Text = "Sessão encerrada. Feche o jogo antes de abrir outra sala."; }
        catch (Exception e) { journal.Add("session_failed", e); status.Text = Friendly(e); }
        finally
        {
            lifetime?.Dispose(); lifetime = null; server = null; client = null; session = null; journal.Add("session_stop"); UpdateButtons();
        }
    }
    private async Task StopSession()
    {
        lifetime?.Cancel();
        if (session is { } active) try { await active; } catch (Exception) { /* StartSession records the failure. */ }
    }
    private void Tick()
    {
        var lobby = server?.Snapshot ?? client?.Lobby;
        if (lobby is not null)
        {
            seats.BeginUpdate(); seats.Items.Clear();
            foreach (var seat in lobby.Seats)
                seats.Items.Add(new ListViewItem([SeatLabel(seat.Seat), seat.IsBot ? "Bot" : seat.Nickname, seat.IsBot ? "Bot" : !seat.Connected ? "Desconectado" : seat.Ready ? "Pronto" : "Aguardando"]));
            seats.EndUpdate();
            if (lobby.Started)
            {
                if (!readyLogged) { readyLogged = true; journal.Add("lobby_ready"); status.Text = "Todos prontos. No jogo: Clique para começar → Play Game."; }
                if (autoOpen.Checked && !openedGame) OpenGame();
            }
        }
        var state = runtime?.Status;
        if (state is not null && state.SessionId == runtime?.Configuration?.SessionId)
        {
            if (state.SnapshotError || state.Error is not null)
            {
                gameStatus.Text = "O jogo relatou um erro de integração. Exporte o diagnóstico e reabra a sessão.";
                if (!failedLogged) { failedLogged = true; journal.Add("runtime_failed"); }
            }
            else gameStatus.Text = state.Ended ? "A sessão do jogo terminou." : state.Installed ? "Integração carregada no jogo." : "O jogo está carregando a integração…";
        }
        else if (lifetime is not null) gameStatus.Text = "Aguardando o jogo carregar a sessão.";
        UpdateButtons();
    }
    private void OpenGame()
    {
        try { openedGame = true; Process.Start(new ProcessStartInfo("steam://run/3897800") { UseShellExecute = true }); journal.Add("game_open"); status.Text = "No jogo: Clique para começar → Play Game. Mantenha este aplicativo aberto."; }
        catch (Exception e) { journal.Add("game_open_failed", e); status.Text = "Não foi possível abrir pela Steam. Abra o jogo pela sua biblioteca."; }
    }
    private void Export()
    {
        using var dialog = new SaveFileDialog { Filter = "Diagnóstico ZIP (*.zip)|*.zip", FileName = "PokerNight-diagnostico-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            string folder = lastGame.Length > 0 ? lastGame : game.Text.Trim();
            // Export to a fresh temporary name, then replace only the user-selected destination.
            string temporary = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string runtimeFolder = runtime?.DirectoryPath ?? Path.Combine(setup.DataDirectory, "runtime-none");
            try { if (!string.IsNullOrWhiteSpace(folder)) runtimeFolder = setup.RuntimeDirectory(folder); } catch (ArgumentException) { }
            try { DiagnosticBundle.Export(temporary, new(AppRelease.Version, folder, runtimeFolder, lastPort, lastAddressCategory, lastRole), journal); File.Move(temporary, dialog.FileName, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            journal.Add("export_ok"); status.Text = "Diagnóstico salvo. Envie esse ZIP inteiro para seu amigo ou anexe aqui no chat.";
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", dialog.FileName }, UseShellExecute = true });
        }
        catch (Exception e) { status.Text = Friendly(e); }
    }
    private static string SeatLabel(Seat seat) => seat switch { Seat.Player => "Lugar original", Seat.StrongBad => "Strong Bad", _ => seat.ToString() };
    internal void RecordUiFailure(Exception error) { journal.Add("app_failed", error); status.Text = "O aplicativo encontrou um erro. Clique em Exportar diagnóstico para salvar os detalhes."; }
    private static bool GameRunning(string directory)
    {
        bool running = false;
        foreach (var process in Process.GetProcessesByName("CelebrityPoker"))
            using (process) try { running |= string.Equals(process.MainModule?.FileName, Path.Combine(directory, "CelebrityPoker.exe"), StringComparison.OrdinalIgnoreCase); } catch (System.ComponentModel.Win32Exception) { }
        return running;
    }
    private static string Friendly(Exception error) => error switch
    {
        SocketException s when s.SocketErrorCode == SocketError.ConnectionRefused => "Conexão recusada. Seu amigo precisa criar a sala; confira o IP e a porta.",
        SocketException s when s.SocketErrorCode == SocketError.AddressAlreadyInUse => "Esta porta já está em uso. Feche a outra sala ou escolha outra porta.",
        SocketException => "Falha de rede. Confiram o IP, a VPN e a permissão do aplicativo no firewall.",
        UnauthorizedAccessException => "Sem permissão para acessar os arquivos. Confira as permissões da pasta do jogo e exporte o diagnóstico.",
        ArgumentException or InvalidOperationException or InvalidDataException or InstallationException => error.Message,
        IOException => "Não foi possível concluir a operação. Confira a conexão e exporte o diagnóstico.",
        _ => "O aplicativo encontrou um erro. Exporte o diagnóstico para investigação."
    };
    private void SaveSettings()
    {
        try { Directory.CreateDirectory(setup.DataDirectory); File.WriteAllText(settingsPath, JsonSerializer.Serialize(new Preferences(game.Text, nickname.Text, address.Text, (int)port.Value, ((SeatOption)character.SelectedItem!).Value))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath) || new FileInfo(settingsPath).Length > 4096) return;
            if (JsonSerializer.Deserialize<Preferences>(File.ReadAllText(settingsPath)) is not { } p) return;
            if (File.Exists(Path.Combine(p.Game, "CelebrityPoker.exe"))) game.Text = p.Game;
            nickname.Text = p.Name; address.Text = p.Address; port.Value = Math.Clamp(p.Port, (int)port.Minimum, (int)port.Maximum);
            for (int i = 0; i < character.Items.Count; i++) if (character.Items[i] is SeatOption option && option.Value == p.Seat) character.SelectedIndex = i;
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException) { }
    }
}
