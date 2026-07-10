using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using ClickableTransparentOverlay;
using CS2Cheat.Core;
using CS2Cheat.Data.Game;
using CS2Cheat.Features;
using CS2Cheat.Utils;
using ImGuiNET;
using Keys = Process.NET.Native.Types.Keys;

namespace CS2Cheat.Graphics;

public static class VectorExtensions
{
    public static uint ToUint(this Vector4 color)
    {
        return ImGui.ColorConvertFloat4ToU32(color);
    }
}

public class OverlayRenderer : Overlay
{
    private readonly GameProcess _gameProcess;
    private readonly GameData _gameData;
    private ConfigManager _config;
    private bool _showMenu, _menuKeyWasDown, _styleApplied;
    private int _activeTab;
    private string? _waitingForBind;
    private bool _clearBindKeyRelease;
    private readonly BombTimer _bombTimer;
    private readonly VoteTeller _voteTeller;
    private readonly AimBot _aimBot;
    private readonly TriggerBot _triggerBot;
    private float _menuAlpha;
    private DateTime _lastToggleTime = DateTime.MinValue;

    // Премиальная темная палитра интерфейса
    static readonly Vector4 ColBg = new(0.08f, 0.09f, 0.11f, 0.98f);
    static readonly Vector4 ColSidebar = new(0.05f, 0.06f, 0.07f, 1.00f);
    static readonly Vector4 ColAccent = new(0.00f, 0.55f, 1.00f, 1.00f);
    static readonly Vector4 ColAccentDim = new(0.00f, 0.42f, 0.80f, 1.00f);
    static readonly Vector4 ColText = new(0.94f, 0.95f, 0.97f, 1.00f);
    static readonly Vector4 ColTextDim = new(0.50f, 0.52f, 0.58f, 1.00f);
    static readonly Vector4 ColItem = new(0.12f, 0.13f, 0.17f, 1.00f);
    static readonly Vector4 ColItemHover = new(0.18f, 0.20f, 0.25f, 1.00f);
    static readonly Vector4 ColItemActive = new(0.10f, 0.11f, 0.14f, 1.00f);
    static readonly Vector4 ColBorder = new(0.20f, 0.22f, 0.27f, 0.35f);

    static readonly string[] TabNames = { "⚙ Aimbot", "👁 Visuals", "🛠 Misc", "📁 Config" };

    public OverlayRenderer(GameProcess gp, GameData gd) : base(true)
    {
        _gameProcess = gp; _gameData = gd;
        _config = ConfigManager.Load();
        _bombTimer = new BombTimer(gp);
        _voteTeller = new VoteTeller(gp);
        _aimBot = new AimBot(gp, gd);
        _triggerBot = new TriggerBot(gp, gd);
    }

    protected override Task PostInitialized()
    {
        var h = this.window.Handle;
        var ex = User32.GetWindowLong(h, User32.GWL_EXSTYLE);
        User32.SetWindowLong(h, User32.GWL_EXSTYLE, ex | User32.WS_EX_NOACTIVATE | User32.WS_EX_TOOLWINDOW);
        UpdateOverlayGeometry();
        return Task.CompletedTask;
    }

    protected override void Render()
    {
        UpdateOverlayGeometry();
        if (!_gameProcess.IsValid) return;

        var mk = _config.MenuToggleKey;
        var mkd = mk.IsKeyDown();
        if (mkd && !_menuKeyWasDown) _showMenu = !_showMenu;
        _menuKeyWasDown = mkd;

        if (_config.AimLegitToggleKey != Keys.None && _config.AimLegitToggleKey.IsKeyDown())
        {
            if (_waitingForBind == null && (DateTime.Now - _lastToggleTime).TotalMilliseconds > 300)
            {
                _config.AimLegitMode = !_config.AimLegitMode;
                ConfigManager.UpdateCache(_config);
                _lastToggleTime = DateTime.Now;
            }
        }

        _menuAlpha = _showMenu ? Math.Min(_menuAlpha + 0.12f, 1f) : Math.Max(_menuAlpha - 0.12f, 0f);

        if (_menuAlpha > 0.01f)
        {
            var io = ImGui.GetIO();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(io.DisplaySize);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, _menuAlpha * 0.45f));
            ImGui.Begin("##background_dim", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBringToFrontOnFocus |
                ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            RenderMenu();
        }

        var dl = ImGui.GetBackgroundDrawList();
        RenderVisuals(dl);

        if (!_showMenu)
        {
            var drawList = ImGui.GetForegroundDrawList();
            string modeText = _config.AimLegitMode ? "LEGIT" : "RAGE";
            uint modeColor = _config.AimLegitMode ? Colors.LimeGreen : Colors.OrangeRed;

            drawList.AddRectFilled(new Vector2(10, 10), new Vector2(180, 52), ToColor(12, 14, 18, 230), 5f);
            drawList.AddRect(new Vector2(10, 10), new Vector2(180, 52), ToColor(55, 60, 70, 120), 5f);
            drawList.AddText(new Vector2(20, 16), Colors.White, "Mode:");
            drawList.AddText(new Vector2(65, 16), modeColor, modeText);

            if (_config.TriggerBot)
                drawList.AddText(new Vector2(20, 32), Colors.DeepSkyBlue, "[TriggerBot Active]");
            else
                drawList.AddText(new Vector2(20, 32), ColTextDim.ToUint(), "Trigger Ready");
        }
        dl.AddText(new Vector2(10, ImGui.GetIO().DisplaySize.Y - 25), Colors.WhiteSmoke, $"{ImGui.GetIO().Framerate:0} FPS");
    }

    void ApplyStyle()
    {
        if (_styleApplied) return;
        var s = ImGui.GetStyle();
        s.WindowRounding = 10f;
        s.ChildRounding = 6f;
        s.FrameRounding = 5f;
        s.GrabRounding = 5f;
        s.PopupRounding = 6f;
        s.WindowBorderSize = 1f;
        s.ChildBorderSize = 0f;
        s.FrameBorderSize = 0f;
        s.WindowPadding = new Vector2(18, 18);
        s.FramePadding = new Vector2(10, 6);
        s.ItemSpacing = new Vector2(12, 11);

        s.Colors[(int)ImGuiCol.WindowBg] = ColBg;
        s.Colors[(int)ImGuiCol.Border] = ColBorder;
        s.Colors[(int)ImGuiCol.Text] = ColText;
        s.Colors[(int)ImGuiCol.CheckMark] = ColAccent;
        s.Colors[(int)ImGuiCol.FrameBg] = ColItem;
        s.Colors[(int)ImGuiCol.FrameBgHovered] = ColItemHover;
        s.Colors[(int)ImGuiCol.FrameBgActive] = ColItemActive;
        s.Colors[(int)ImGuiCol.SliderGrab] = ColAccent;
        s.Colors[(int)ImGuiCol.SliderGrabActive] = ColAccentDim;
        s.Colors[(int)ImGuiCol.Button] = ColItem;
        s.Colors[(int)ImGuiCol.ButtonHovered] = ColItemHover;
        s.Colors[(int)ImGuiCol.ButtonActive] = ColItemActive;
        s.Colors[(int)ImGuiCol.Header] = ColItem;
        s.Colors[(int)ImGuiCol.HeaderHovered] = ColItemHover;
        s.Colors[(int)ImGuiCol.HeaderActive] = ColItemActive;
        _styleApplied = true;
    }

    void RenderMenu()
    {
        ApplyStyle();
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, _menuAlpha);

        var menuSize = new Vector2(560, 390);
        var io = ImGui.GetIO();
        var pos = (io.DisplaySize - menuSize) * 0.5f;
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        ImGui.SetNextWindowSize(menuSize, ImGuiCond.Always);

        ImGui.Begin("##main", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, ColSidebar);
        ImGui.BeginChild("##sidebar", new Vector2(150, 0), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);

        ImGui.SetCursorPos(new Vector2(16, 22));
        ImGui.PushStyleColor(ImGuiCol.Text, ColAccent);
        ImGui.Text("CS2 EXTERNAL");
        ImGui.PopStyleColor();

        ImGui.SetCursorPosY(52);
        ImGui.Separator();
        ImGui.SetCursorPosY(68);

        for (int i = 0; i < TabNames.Length; i++)
        {
            ImGui.SetCursorPosX(10);
            bool sel = _activeTab == i;

            if (sel)
                ImGui.PushStyleColor(ImGuiCol.Button, ColItemHover);
            else
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
            if (ImGui.Button(TabNames[i], new Vector2(130, 34)))
                _activeTab = i;
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }

        ImGui.SetCursorPosY(ImGui.GetWindowHeight() - 45);
        ImGui.Separator();
        ImGui.SetCursorPosX(16);
        ImGui.TextColored(ColTextDim, $"{_gameProcess.WindowRectangleClient.Width}x{_gameProcess.WindowRectangleClient.Height}");

        ImGui.EndChild();
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(22, 22));
        ImGui.BeginChild("##content", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);

        switch (_activeTab)
        {
            case 0: TabAimbot(); break;
            case 1: TabVisuals(); break;
            case 2: TabMisc(); break;
            case 3: TabConfig(); break;
        }

        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.End();
        ImGui.PopStyleVar();
    }

    void TabAimbot()
    {
        SectionHeader("🎯 Aimbot Settings");
        var aimBot = _config.AimBot;
        if (Toggle("Enable System", ref aimBot)) _config.AimBot = aimBot;

        var legitMode = _config.AimLegitMode;
        if (Toggle("Visibility Check Only (Legit)", ref legitMode)) _config.AimLegitMode = legitMode;

        var silentFlick = _config.AimSilentFlick;
        if (Toggle("Enable Silent-Like Smart Flick", ref silentFlick)) _config.AimSilentFlick = silentFlick;

        var aimFovCircle = _config.AimFovCircle;
        if (Toggle("Draw FOV Range Indicator", ref aimFovCircle)) _config.AimFovCircle = aimFovCircle;

        ImGui.Spacing();
        var fov = _config.AimFov;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("Field of View Radius", ref fov, 1f, 180f, "%.1f°"))
        { _config.AimFov = fov; ConfigManager.UpdateCache(_config); }

        if (!_config.AimSilentFlick)
        {
            var smooth = _config.AimSmoothing;
            ImGui.SetNextItemWidth(260);
            if (ImGui.SliderFloat("Target Locking Smooth", ref smooth, 0f, 20f, "%.1f"))
            { _config.AimSmoothing = smooth; ConfigManager.UpdateCache(_config); }
        }

        var bone = _config.AimBoneIndex;
        ImGui.SetNextItemWidth(260);
        if (ImGui.Combo("Target Hitbox Bone", ref bone, ConfigManager.BoneDisplayNames, ConfigManager.BoneDisplayNames.Length))
        { _config.AimBoneIndex = bone; ConfigManager.UpdateCache(_config); }

        var autoStop = _config.TriggerAutoStop;
        if (Toggle("Velocity Auto-Stop", ref autoStop)) _config.TriggerAutoStop = autoStop;

        ImGui.Spacing();
        SectionHeader("🔧 Recoil Control System");
        var aimRcs = _config.AimRcs;
        if (Toggle("Enable Standalone RCS Engine", ref aimRcs)) _config.AimRcs = aimRcs;
        if (aimRcs)
        {
            var rcsStrength = _config.AimRcsStrength;
            ImGui.SetNextItemWidth(260);
            if (ImGui.SliderFloat("Compensation Force", ref rcsStrength, 0f, 100f, "%.0f%%"))
            { _config.AimRcsStrength = rcsStrength; ConfigManager.UpdateCache(_config); }
        }

        ImGui.Spacing();
        SectionHeader("⌨ Hotkey Mappings");
        DrawKeyBind("Aimbot Hold Lock Key", "AimBotKey", _config.AimBotKey);
        DrawKeyBind("Toggle Legit/Rage State", "AimLegitToggleKey", _config.AimLegitToggleKey);
    }

    void TabVisuals()
    {
        SectionHeader("👥 Player Overlays (ESP)");
        var espBox = _config.EspBox;
        if (Toggle("Draw Box Matrix (Outlined Corner)", ref espBox)) { _config.EspBox = espBox; }

        var espName = _config.EspName;
        if (Toggle("Render Text Names", ref espName)) { _config.EspName = espName; }

        var espWeapon = _config.EspWeapon;
        if (Toggle("Render Active Weapon Icons", ref espWeapon)) { _config.EspWeapon = espWeapon; }

        var espFlags = _config.EspFlags;
        if (Toggle("Render Advanced Status Flags", ref espFlags)) { _config.EspFlags = espFlags; }

        var espFilled = _config.EspFilledBox;
        if (Toggle("Chams-Like Translucent Fill", ref espFilled)) { _config.EspFilledBox = espFilled; }

        ImGui.Spacing();

        // Высокотехнологичный слайдер ограничения максимальной дистанции рендеринга ESP
        var maxDist = _config.EspMaxDistance;
        ImGui.SetNextItemWidth(260);
        if (ImGui.SliderFloat("ESP Max Render Distance", ref maxDist, 100f, 5000f, "%.0f units"))
        {
            _config.EspMaxDistance = maxDist;
            ConfigManager.UpdateCache(_config);
        }

        ImGui.Spacing();
        var boxColorVec = new Vector4(_config.EspBoxColor[0], _config.EspBoxColor[1], _config.EspBoxColor[2], _config.EspBoxColor[3]);
        ImGui.SetNextItemWidth(260);
        if (ImGui.ColorEdit4("Bounding Box Custom Tint", ref boxColorVec, ImGuiColorEditFlags.NoInputs))
        {
            _config.EspBoxColor[0] = boxColorVec.X; _config.EspBoxColor[1] = boxColorVec.Y;
            _config.EspBoxColor[2] = boxColorVec.Z; _config.EspBoxColor[3] = boxColorVec.W;
            ConfigManager.UpdateCache(_config);
        }

        var skeletonEsp = _config.SkeletonEsp;
        if (Toggle("Draw Joint Bones (Skeleton)", ref skeletonEsp)) _config.SkeletonEsp = skeletonEsp;

        var espAimCrosshair = _config.EspAimCrosshair;
        if (Toggle("Draw Precision Crosshair Override", ref espAimCrosshair)) _config.EspAimCrosshair = espAimCrosshair;

        ImGui.Spacing();
        SectionHeader("🔮 Advanced Effects");

        var bulletTracers = _config.VisualsBulletTracers;
        if (Toggle("Own Bullet Tracers (Fade effect)", ref bulletTracers)) _config.VisualsBulletTracers = bulletTracers;

        var grenadeHelper = _config.VisualsGrenadeHelper;
        if (Toggle("Grenade Path Tracker & Timers", ref grenadeHelper)) _config.VisualsGrenadeHelper = grenadeHelper;

        ImGui.Spacing();
        SectionHeader("🌍 Environment Monitoring");
        var bombTimer = _config.BombTimer;
        if (Toggle("C4 Explosive Tracking Engine", ref bombTimer)) _config.BombTimer = bombTimer;

        var voteTeller = _config.VoteTeller;
        if (Toggle("Server Vote Telemetry", ref voteTeller)) _config.VoteTeller = voteTeller;

        var bombTracer = _config.BombTracer;
        if (Toggle("C4 World Ray Vector Trace", ref bombTracer)) _config.BombTracer = bombTracer;
    }

    void TabMisc()
    {
        SectionHeader("🤖 Combat Automation");
        var triggerBot = _config.TriggerBot;
        if (Toggle("Enable Intelligent TriggerBot", ref triggerBot)) _config.TriggerBot = triggerBot;
        DrawKeyBind("Trigger Execution Key", "TriggerBotKey", _config.TriggerBotKey);

        ImGui.Spacing();
        SectionHeader("🏃 Movement Modification");
        var teamCheck = _config.TeamCheck;
        if (Toggle("Ignore Teammates (Filter)", ref teamCheck)) _config.TeamCheck = teamCheck;

        var bunnyHop = _config.BunnyHop;
        if (Toggle("Perfect Seamless BunnyHop", ref bunnyHop)) _config.BunnyHop = bunnyHop;

        var autoStrafer = _config.AutoStrafer;
        if (Toggle("Directional Auto-Strafer System", ref autoStrafer)) _config.AutoStrafer = autoStrafer;

        ImGui.Spacing();
        SectionHeader("🎛 Overlay Interface");
        DrawKeyBind("Toggle Menu Dashboard UI", "MenuToggleKey", _config.MenuToggleKey);
    }

    void TabConfig()
    {
        SectionHeader("💾 Serialization & Configuration");

        ImGui.PushStyleColor(ImGuiCol.Button, ColAccent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColAccentDim);
        if (ImGui.Button("Save Local Configuration", new Vector2(260, 36)))
            ConfigManager.Save(_config);
        ImGui.PopStyleColor(2);

        ImGui.Spacing();
        if (ImGui.Button("Reload From Local Cache", new Vector2(260, 36)))
        { ConfigManager.Reload(); _config = ConfigManager.Load(); }

        ImGui.Spacing();
        if (ImGui.Button("Reset To Defaults", new Vector2(260, 36)))
        { _config = ConfigManager.Default(); ConfigManager.Save(_config); }
    }

    void SectionHeader(string text)
    {
        ImGui.TextColored(ColAccent, text.ToUpper());
        ImGui.Spacing();
    }

    bool Toggle(string label, ref bool value)
    {
        if (ImGui.Checkbox(label, ref value))
        {
            ConfigManager.UpdateCache(_config);
            return true;
        }
        return false;
    }

    void DrawKeyBind(string label, string bindId, Keys currentKey)
    {
        ImGui.PushID(bindId);
        bool isWaiting = _waitingForBind == bindId;
        string btnText = isWaiting ? "[ ... ]" : $"[ {ConfigManager.GetKeyName(currentKey)} ]";

        ImGui.Text(label);
        ImGui.SameLine(260);

        if (isWaiting)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.85f, 0.20f, 0.20f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.95f, 0.25f, 0.25f, 1f));
        }

        if (ImGui.Button(btnText, new Vector2(140, 0)))
        {
            _waitingForBind = isWaiting ? null : bindId;
            _clearBindKeyRelease = true;
        }

        if (isWaiting)
        {
            ImGui.PopStyleColor(2);

            if (_clearBindKeyRelease)
            {
                if (ScanKey() == Keys.None) _clearBindKeyRelease = false;
            }
            else
            {
                var k = ScanKey();
                if (k != Keys.None)
                {
                    if (k == Keys.Escape) { _waitingForBind = null; }
                    else
                    {
                        switch (bindId)
                        {
                            case "AimBotKey": _config.AimBotKey = k; break;
                            case "TriggerBotKey": _config.TriggerBotKey = k; break;
                            case "MenuToggleKey": _config.MenuToggleKey = k; break;
                            case "AimLegitToggleKey": _config.AimLegitToggleKey = k; break;
                        }
                        ConfigManager.UpdateCache(_config);
                        _waitingForBind = null;
                    }
                }
            }
        }
        ImGui.PopID();
    }

    static Keys ScanKey()
    {
        Keys[] keys = {
            Keys.LButton, Keys.RButton, Keys.MButton, Keys.XButton1, Keys.XButton2,
            Keys.LMenu, Keys.RMenu, Keys.LShiftKey, Keys.RShiftKey, Keys.LControlKey, Keys.RControlKey,
            Keys.Insert, Keys.Delete, Keys.Home, Keys.End, Keys.Capital, Keys.Tab, Keys.Space,
            Keys.F1,Keys.F2,Keys.F3,Keys.F4,Keys.F5,Keys.F6,Keys.F7,Keys.F8,Keys.F9,Keys.F10,Keys.F11,Keys.F12,
            Keys.Q,Keys.W,Keys.E,Keys.R,Keys.T,Keys.Y,Keys.U,Keys.I,Keys.O,Keys.P,
            Keys.A,Keys.S,Keys.D,Keys.F,Keys.G,Keys.H,Keys.J,Keys.K,Keys.L,
            Keys.Z,Keys.X,Keys.C,Keys.V,Keys.B,Keys.N,Keys.M,
            Keys.D0,Keys.D1,Keys.D2,Keys.D3,Keys.D4,Keys.D5,Keys.D6,Keys.D7,Keys.D8,Keys.D9,
            Keys.Escape
        };
        foreach (var k in keys) if (k.IsKeyDown()) return k;
        return Keys.None;
    }

    void UpdateOverlayGeometry()
    {
        var r = _gameProcess.WindowRectangleClient;
        if (r.Width <= 0 || r.Height <= 0) return;
        try
        {
            var ts = new System.Drawing.Size(r.Width, r.Height);
            var tp = new System.Drawing.Point(r.X, r.Y);
            if (this.Size != ts) this.Size = ts;
            if (this.Position != tp) this.Position = tp;
        }
        catch { }
    }

    void RenderVisuals(ImDrawListPtr dl)
    {
        if (_config.EspBox) EspBox.Draw(dl, _gameData, _gameProcess);
        if (_config.SkeletonEsp) SkeletonEsp.Draw(dl, _gameData);
        if (_config.EspAimCrosshair) EspAimCrosshair.Draw(dl, _gameData, _gameProcess);
        if (_config.BombTimer) BombTimer.Draw(dl);
        if (_config.VoteTeller) VoteTeller.Draw(dl);
        if (_config.BombTracer) BombTracer.Draw(dl, _gameData, _gameProcess);

        if (_config.AimFovCircle)
        {
            var io = ImGui.GetIO();
            var center = new Vector2(io.DisplaySize.X / 2, io.DisplaySize.Y / 2);
            var radius = (float)(Math.Tan((_config.AimFov * Math.PI / 180.0) / 2.0) / Math.Tan((90.0 * Math.PI / 180.0) / 2.0) * (io.DisplaySize.X / 2.0));

            dl.AddCircle(center, radius, ToColor(0, 0, 0, 150), 64, 2.5f);
            dl.AddCircle(center, radius, Colors.WhiteSmoke, 64, 1.0f);
        }
    }

    public void StartFeatures() { _bombTimer.Start(); _voteTeller.Start(); _triggerBot.Start(); _aimBot.Start(); }
    public void StopFeatures() { _bombTimer.Dispose(); _voteTeller.Dispose(); _triggerBot.Dispose(); _aimBot.Dispose(); }

    public static uint ToColor(byte r, byte g, byte b, byte a = 255) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, a / 255f));
    public static uint ToColor(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public static class Colors
    {
        public static readonly uint Yellow = ToColor(255, 255, 0, 255);
        public static readonly uint Black = ToColor(0, 0, 0, 255);
        public static readonly uint White = ToColor(255, 255, 255);
        public static readonly uint Red = ToColor(240, 50, 50);
        public static readonly uint DarkRed = ToColor(140, 20, 20);
        public static readonly uint Green = ToColor(46, 213, 115);
        public static readonly uint LimeGreen = ToColor(50, 205, 50);
        public static readonly uint Blue = ToColor(30, 144, 255);
        public static readonly uint OrangeRed = ToColor(255, 69, 0);
        public static readonly uint DeepSkyBlue = ToColor(0, 191, 255);
        public static readonly uint WhiteSmoke = ToColor(245, 245, 245);
    }
}