using System;
using Godot;
using Overburden.Core.Lighting;
using Overburden.Core.Navigation;
using Overburden.Core.Voxels;

namespace Overburden.Game.Ui;

/// <summary>
/// The map: <c>F12</c>, 128 pixels of world facing the way you are.
/// </summary>
/// <remarks>
/// <para>
/// <b>A thin shell over <see cref="MapRenderer"/>, on purpose.</b> Everything that could be subtly
/// wrong — which world coordinate a pixel is, which pixel a machine lands on, which way the light
/// comes from — is in Core with tests on it, because a map rotated the wrong way looks exactly like
/// a map. What is here is a texture, a key, and the rule about when to redraw.
/// </para>
/// <para>
/// <b>Built with the HUD and skipped with it under <c>--profile</c></b>, the way
/// <see cref="MachinePanel"/> is: a measurement reports draw calls and primitives, and a map
/// drawing into the same frame would put its own quads into a number that is about terrain.
/// </para>
/// </remarks>
public sealed partial class MapPanel : Control
{
    /// <summary>Screen pixels per map pixel. Nearest-neighbour, so they stay square.</summary>
    /// <remarks>
    /// 128 real pixels is a postage stamp on a 1152-wide window. Three keeps the pixels crisp and
    /// deliberate rather than turning a pixel map into a blurry one, which is what any filtered
    /// scale would do.
    /// </remarks>
    private const int Scale = 3;

    /// <summary>
    /// How far the player must move, or turn, before the picture is worth building again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A map is 3.5 ms on a shaped world, so this is the rule that keeps it off the frame
    /// budget rather than a tidiness.</b> Slice a measured it and the pass's estimate was three
    /// times under; at 60 Hz an unconditional rebuild would be a fifth of every frame, for a
    /// picture that has usually not changed.
    /// </para>
    /// <para>
    /// <b>Turning is the case the pass did not think about</b>, and it is the expensive one: a
    /// facing-up map is a different picture at every yaw, and yaw moves continuously whenever
    /// somebody looks around. One degree moves the outermost pixel by about one pixel, so that is
    /// the threshold — with <see cref="MinimumIntervalSeconds"/> capping how often the threshold
    /// may fire, which is what bounds the cost while a player spins on the spot.
    /// </para>
    /// </remarks>
    private const float MoveThresholdPixels = 1.0f;

    private const float TurnThresholdDegrees = 1.0f;

    private const double MinimumIntervalSeconds = 1.0 / 15.0;

    /// <summary>
    /// How long an open map may go without being rebuilt, however still the player stands.
    /// </summary>
    /// <remarks>
    /// <b>Thresholds on movement and heading are not enough, because the world changes on its
    /// own.</b> Water rises, a collapse takes a hillside, an autosave adds chunks to the mine --
    /// and standing perfectly still is exactly when somebody is reading the map. It was found the
    /// obvious way: an open map on a played world reported "water not analysed here" forever,
    /// because the watershed window installs a moment after the world opens and the map, having
    /// built once and never moved, never looked again. A second costs 3.5 ms, which is a third of
    /// one percent of the time the map is open.
    /// </remarks>
    private const double MaximumAgeSeconds = 1.0;

    private static readonly Color Frame = new(0.05f, 0.06f, 0.07f, 0.85f);
    private static readonly Color Rim = new(0.68f, 0.70f, 0.74f, 0.9f);
    private static readonly Color NorthTick = new(0.93f, 0.42f, 0.36f);
    private static readonly Color Marker = new(1f, 1f, 1f);
    private static readonly Color MarkerEdge = new(0f, 0f, 0f, 0.85f);

    private ImageTexture? _texture;
    private Image? _image;
    private byte[] _bytes = [];
    private MapColour[] _pixels = [];

    private MapProjection _shown;
    private bool _everBuilt;
    private double _sinceBuild;

    private Label _caption = null!;
    private string _reported = string.Empty;


    /// <summary>
    /// Ground a pixel covers. <c>--mapscale=</c> sweeps it; the default is 16 m, so 128 pixels
    /// is one region.
    /// </summary>
    /// <remarks>
    /// <b>A seam rather than a setting, for now.</b> Slice a measured the cost as flat across
    /// the whole range -- 3.46 ms at 2 km and 2.17 ms at 131 km -- so what the scale should be
    /// is purely a question about what reads well, and that is a question for somebody looking
    /// at it. At 16 m a pixel the terrain's own 62 m hills are four pixels across and the map
    /// is busy; at 64 m it is a picture of regions. The zoom slice c plans replaces this.
    /// </remarks>
    public int VoxelsPerPixel { get; set; } = MapProjection.DefaultVoxelsPerPixel;

    public bool IsOpen { get; private set; }

    /// <summary>How many times the picture has been built this session. The cost's denominator.</summary>
    public int Builds { get; private set; }

    public override void _Ready()
    {
        // Nearest, or a deliberate pixel map renders as a smear at three times its size.
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Ignore;

        SetAnchorsPreset(LayoutPreset.Center);
        CustomMinimumSize = new Vector2(
            (MapProjection.DefaultSize * Scale) + 16,
            (MapProjection.DefaultSize * Scale) + 40);

        Size = CustomMinimumSize;

        // Anchored at the centre and offset by half, so it stays centred at any window size
        // rather than being placed once against the size the window happened to open at.
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -Size.X / 2f;
        OffsetTop = -Size.Y / 2f;
        OffsetRight = Size.X / 2f;
        OffsetBottom = Size.Y / 2f;

        _caption = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _caption.AddThemeColorOverride("font_color", Rim);
        _caption.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        _caption.AddThemeConstantOverride("outline_size", 4);
        _caption.SetAnchorsPreset(LayoutPreset.BottomWide);
        _caption.OffsetTop = -26f;
        AddChild(_caption);

        Visible = false;
    }

    /// <summary>Opens or closes the map. Returns whether it is open afterwards.</summary>
    public bool Toggle()
    {
        IsOpen = !IsOpen;
        Visible = IsOpen;

        // A map opened after walking a mile should not show where you were. Forcing the rebuild
        // rather than trusting the thresholds is what makes "open it and look" always honest.
        _everBuilt = _everBuilt && IsOpen;
        return IsOpen;
    }

    public void Close()
    {
        IsOpen = false;
        Visible = false;
    }

    /// <summary>
    /// Rebuilds the picture if it is worth rebuilding, and draws it.
    /// </summary>
    /// <param name="heights">The world. Answers anywhere; nothing need be loaded.</param>
    /// <param name="centreVoxelX">Where the player is.</param>
    /// <param name="centreVoxelZ">Where the player is.</param>
    /// <param name="bearingDegrees">Which way they face, from <see cref="Compass"/>.</param>
    /// <param name="overlay">Your mine, your machines and the water. Called only when rebuilding.</param>
    /// <param name="delta">Seconds since the last frame, for the rebuild interval.</param>
    public void Refresh(
        ISurfaceHeights heights,
        int centreVoxelX,
        int centreVoxelZ,
        float bearingDegrees,
        Func<MapOverlay> overlay,
        double delta)
    {
        if (!IsOpen)
        {
            return;
        }

        _sinceBuild += delta;

        var wanted = new MapProjection(
            centreVoxelX,
            centreVoxelZ,
            VoxelsPerPixel,
            MapProjection.DefaultSize,
            bearingDegrees);

        if (ShouldRebuild(wanted))
        {
            // The overlay is gathered here and nowhere else, which is the point of taking a
            // factory rather than a value: building it walks every stored chunk into a set and
            // allocates a pin list, and at 60 Hz on a played world that is 4,584 chunks a frame
            // for a picture that changes once a second.
            Build(heights, wanted, overlay());
        }

        QueueRedraw();
    }

    private bool ShouldRebuild(MapProjection wanted)
    {
        if (!_everBuilt)
        {
            return true;
        }

        if (_sinceBuild < MinimumIntervalSeconds)
        {
            return false;
        }

        if (_sinceBuild >= MaximumAgeSeconds)
        {
            return true;
        }

        float movedPixels = MathF.Sqrt(
            (MathF.Pow(wanted.CentreVoxelX - _shown.CentreVoxelX, 2)
                + MathF.Pow(wanted.CentreVoxelZ - _shown.CentreVoxelZ, 2)))
            / wanted.VoxelsPerPixel;

        float turned = MathF.Abs(
            ((wanted.BearingDegrees - _shown.BearingDegrees + 540.0f) % 360.0f) - 180.0f);

        return movedPixels >= MoveThresholdPixels || turned >= TurnThresholdDegrees;
    }

    private void Build(ISurfaceHeights heights, MapProjection projection, MapOverlay overlay)
    {
        int size = projection.Size;
        if (_pixels.Length != size * size)
        {
            _pixels = new MapColour[size * size];
            _bytes = new byte[size * size * 3];
            _image = null;
        }

        // Auto, always: a ramp fitted to the world's peak amplitude paints an ordinary patch of
        // a shaped world in one shade of brown -- measured at 32% of the palette, and it is what
        // the first screenshot of this panel showed.
        MapRamp used = MapRenderer.Render(
            heights, projection, MapRamp.Auto, MapPalette.Default, overlay, _pixels);

        for (int i = 0; i < _pixels.Length; i++)
        {
            MapColour colour = _pixels[i];
            _bytes[(i * 3) + 0] = colour.R;
            _bytes[(i * 3) + 1] = colour.G;
            _bytes[(i * 3) + 2] = colour.B;
        }

        if (_image is null)
        {
            _image = Image.CreateFromData(size, size, false, Image.Format.Rgb8, _bytes);
            _texture = ImageTexture.CreateFromImage(_image);
        }
        else
        {
            _image.SetData(size, size, false, Image.Format.Rgb8, _bytes);
            _texture!.Update(_image);
        }

        _shown = projection;
        _everBuilt = true;
        _sinceBuild = 0.0;
        Builds++;

        // The range is printed because an auto ramp means two maps are not comparable by
        // colour, and saying what this one's colours mean is the honest answer to that.
        float low = used.LowVoxels / VoxelConstants.VoxelsPerMetre;
        float high = used.HighVoxels / VoxelConstants.VoxelsPerMetre;

        // On a change, not on the first build: the watershed window installs on a worker a
        // moment after the world opens, so a line printed at the first build reports "not
        // analysed" about a map that has water by its second -- which is what it did, and read
        // exactly like a broken overlay. What the map had to draw with is worth saying; saying it
        // once, early, is worse than not saying it.
        string state =
            $"{overlay.Pins.Count} pin(s), "
            + $"mine {(overlay.IsDug is null ? "not asked" : "drawn")}, "
            + $"water {(overlay.Water is null ? "not asked"
                : overlay.Water.Covers(projection.CentreVoxelX, projection.CentreVoxelZ)
                    ? "analysed here" : "not analysed here")}";

        if (state != _reported)
        {
            _reported = state;
            GD.Print(
                $"M12: map — {state}, range "
                + $"{used.LowVoxels / VoxelConstants.VoxelsPerMetre:0.0}"
                + $"..{used.HighVoxels / VoxelConstants.VoxelsPerMetre:0.0} m");
        }

        _caption.Text =
            $"{_shown.SpanMetres / 1000.0f:0.0} km across   ·   "
            + $"{_shown.VoxelsPerPixel / VoxelConstants.VoxelsPerMetre} m a pixel   ·   "
            + $"{low:0} to {high:0} m";
    }

    public override void _Draw()
    {
        if (_texture is null)
        {
            return;
        }

        float side = _shown.Size * Scale;
        var origin = new Vector2(8f, 8f);

        DrawRect(new Rect2(Vector2.Zero, Size), Frame, filled: true);
        DrawTextureRect(_texture, new Rect2(origin, new Vector2(side, side)), tile: false);
        DrawRect(new Rect2(origin, new Vector2(side, side)), Rim, filled: false, width: 1.0f);

        Vector2 centre = origin + new Vector2(side / 2f, side / 2f);

        // North, on the rim. The map turns with the player, so this is the only thing on screen
        // that says which way the world is -- and it is what keeps a facing-up map readable
        // against the coordinates on the HUD.
        float northRadians = -_shown.BearingDegrees * (MathF.PI / 180.0f);
        var north = new Vector2(MathF.Sin(northRadians), -MathF.Cos(northRadians));
        DrawLine(centre + (north * (side / 2f - 12f)), centre + (north * (side / 2f - 2f)), NorthTick, 3.0f);

        // The player, at the centre pixel, pointing up -- because up is where they are looking.
        Vector2[] arrow =
        [
            centre + new Vector2(0f, -7f),
            centre + new Vector2(-5f, 6f),
            centre + new Vector2(0f, 2f),
            centre + new Vector2(5f, 6f),
        ];

        DrawColoredPolygon(arrow, Marker);
        DrawPolyline([.. arrow, arrow[0]], MarkerEdge, 1.0f);
    }
}
