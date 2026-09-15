using System.Collections.Generic;
using Godot;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Game.Player;
using Overburden.Game.Rendering;

namespace Overburden.Game.Ui;

/// <summary>
/// The hotbar, the crosshair and the status line — this project's first user interface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built in code rather than in the scene, and that is a decision rather than laziness.</b>
/// <c>Main.tscn</c> is four nodes and every one of them is load-bearing for a measurement; a
/// dozen <c>Control</c> nodes checked into it would be a dozen more things a headless profile
/// run has to instantiate, and the row's contents are per-material anyway — the number of slots
/// is what the player is carrying, which no scene file can know.
/// </para>
/// <para>
/// <b>It reads state and never owns any.</b> Which material is selected lives on
/// <see cref="PlaceController"/>, which brush on the same, which tier on
/// <see cref="MiningController"/>, and what is carried in <see cref="VolumetricInventory"/>.
/// A HUD that kept its own copy of the selection would be a second answer to "what am I
/// holding", and the two would disagree exactly when it mattered — the frame after a material
/// ran out and the controller re-selected without being asked.
/// </para>
/// <para>
/// <b>Not created during a profile run.</b> A profile reports draw calls and primitives, and
/// M1d's deferred 2×2×2 mesh-block question turns on that counter; a hotbar drawing into the
/// same frame would put UI quads into a number that is supposed to be terrain. It <em>is</em>
/// created in a plain headless run, deliberately, because that is the cheapest thing that
/// executes this whole file and a UI nobody has instantiated headlessly is M5c-2's shape of
/// hole one layer out.
/// </para>
/// </remarks>
public sealed partial class Hud : CanvasLayer
{
    /// <summary>How many slots the row can show, which is how many number keys there are.</summary>
    /// <remarks>
    /// Nine because the keyboard has nine, not because the inventory has a limit — it has none
    /// at M6, deliberately. A tenth material is carried and simply cannot be reached from the
    /// row, which is a real limitation and the honest one to have until containers exist.
    /// </remarks>
    public const int Slots = 9;

    /// <summary>Seconds between text refreshes.</summary>
    /// <remarks>
    /// <b>Throttled because litres change every swing and a label does not need to.</b> Mining
    /// credits the inventory five times a second and a person cannot read a number that moves at
    /// sixty. The selection and the brush are <em>not</em> throttled — those change because a key
    /// was pressed, and a keypress that takes a tenth of a second to show reads as a dropped
    /// input.
    /// </remarks>
    private const double RefreshSeconds = 0.1;

    private readonly List<SlotView> _slots = [];

    private Label _status = null!;
    private double _sinceRefresh;

    /// <summary>What each slot showed last, so a rebuild only happens when the row changes.</summary>
    private readonly List<int> _shownMaterials = [];

    private sealed record SlotView(
        PanelContainer Panel,
        ColorRect Swatch,
        Label Name,
        Label Litres,
        StyleBoxFlat Style);

    private static readonly Color SelectedBorder = new(1.00f, 0.86f, 0.45f);
    private static readonly Color IdleBorder = new(0.28f, 0.29f, 0.32f);
    private static readonly Color PanelFill = new(0.08f, 0.08f, 0.10f, 0.78f);
    private static readonly Color DimText = new(0.68f, 0.70f, 0.74f);

    public override void _Ready()
    {
        Layer = 10;

        var root = new Control
        {
            Name = "HudRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        BuildCrosshair(root);
        BuildRow(root);
    }

    /// <summary>
    /// Two bars rather than a texture, because the crosshair is the one thing here the player
    /// looks at every second and a four-pixel PNG is a binary asset for a plus sign.
    /// </summary>
    /// <remarks>
    /// <b>There was no crosshair at all before M6d-3</b>, in a game whose two verbs are both
    /// "what am I pointing at". The wireframe target box stood in for one, which works while
    /// aiming at a surface and vanishes the moment you point at sky — so the player lost the
    /// centre of the screen exactly when they were trying to find it.
    /// </remarks>
    private static void BuildCrosshair(Control root)
    {
        const float arm = 9f;
        const float thickness = 2f;
        var colour = new Color(1f, 1f, 1f, 0.72f);

        root.AddChild(Bar(colour, arm, thickness));
        root.AddChild(Bar(colour, thickness, arm));
    }

    /// <summary>One centred bar of the crosshair.</summary>
    /// <remarks>
    /// <b>Anchors and offsets set directly, never <c>SetAnchorsPreset</c> plus a <c>Size</c>.</b>
    /// The first version did the latter and the vertical bar rendered as a line most of the
    /// screen tall: a preset writes offsets, the assignment to <c>Size</c> writes them again, and
    /// the next layout pass recomputes the rect from the anchors — so the assignment was
    /// overwritten for one bar and not the other. **A defect no log could show**, which is why
    /// this file was verified with a screenshot rather than a headless run.
    /// </remarks>
    private static ColorRect Bar(Color colour, float width, float height) =>
        new()
        {
            Color = colour,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0.5f,
            AnchorRight = 0.5f,
            AnchorTop = 0.5f,
            AnchorBottom = 0.5f,
            OffsetLeft = -width * 0.5f,
            OffsetRight = width * 0.5f,
            OffsetTop = -height * 0.5f,
            OffsetBottom = height * 0.5f,
        };

    private void BuildRow(Control root)
    {
        var column = new VBoxContainer
        {
            Name = "HudBottom",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        // Anchored to the bottom edge and grown upward, with room to spare. The first version
        // reserved 92 px against a row that needs about 66 plus a status line, and the litres
        // label was clipped off the bottom of the screen — visible in a screenshot and in
        // nothing else.
        column.AnchorLeft = 0f;
        column.AnchorRight = 1f;
        column.AnchorTop = 1f;
        column.AnchorBottom = 1f;
        column.OffsetTop = -150f;
        column.OffsetBottom = -18f;
        column.GrowVertical = Control.GrowDirection.Begin;
        column.AddThemeConstantOverride("separation", 6);
        root.AddChild(column);

        _status = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _status.AddThemeColorOverride("font_color", DimText);
        _status.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _status.AddThemeConstantOverride("outline_size", 4);
        column.AddChild(_status);

        var row = new HBoxContainer
        {
            Name = "Hotbar",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        row.AddThemeConstantOverride("separation", 6);
        column.AddChild(row);

        for (int slot = 0; slot < Slots; slot++)
        {
            _slots.Add(BuildSlot(row, slot));
            _shownMaterials.Add(int.MinValue);
        }
    }

    private static SlotView BuildSlot(HBoxContainer row, int slot)
    {
        var style = new StyleBoxFlat
        {
            BgColor = PanelFill,
            BorderColor = IdleBorder,
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
            ContentMarginLeft = 6,
            ContentMarginRight = 6,
            ContentMarginTop = 4,
            ContentMarginBottom = 4,
        };
        style.SetBorderWidthAll(2);

        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", style);
        panel.CustomMinimumSize = new Vector2(84f, 0f);
        row.AddChild(panel);

        var box = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        panel.AddChild(box);

        var header = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        header.AddThemeConstantOverride("separation", 5);
        box.AddChild(header);

        var swatch = new ColorRect
        {
            CustomMinimumSize = new Vector2(14f, 14f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        header.AddChild(swatch);

        // The slot number is drawn even for an empty slot, so the row reads as nine places one
        // of which is empty rather than as a shorter row that has moved.
        var number = new Label
        {
            Text = (slot + 1).ToString(),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        number.AddThemeColorOverride("font_color", DimText);
        header.AddChild(number);

        var name = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddChild(name);

        var litres = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        litres.AddThemeColorOverride("font_color", DimText);
        box.AddChild(litres);

        return new SlotView(panel, swatch, name, litres, style);
    }

    /// <summary>
    /// Redraws the row from the state the controllers own.
    /// </summary>
    /// <remarks>
    /// <b>Called every frame and cheap on most of them.</b> The selection border and the status
    /// line are set unconditionally because they answer a keypress; the per-slot text is rebuilt
    /// only when the row's materials change or the refresh timer elapses.
    /// </remarks>
    public void Refresh(
        double delta,
        VolumetricInventory inventory,
        MaterialTable materials,
        Registry<MaterialDefinition> definitions,
        PlaceController place,
        MiningController? mining,
        bool precise,
        string? radius = null)
    {
        _sinceRefresh += delta;
        bool refreshText = _sinceRefresh >= RefreshSeconds;
        if (refreshText)
        {
            _sinceRefresh = 0.0;
        }

        for (int slot = 0; slot < _slots.Count; slot++)
        {
            int materialId = inventory.MaterialInSlot(slot);
            SlotView view = _slots[slot];

            bool changed = _shownMaterials[slot] != materialId;
            if (changed || refreshText)
            {
                _shownMaterials[slot] = materialId;
                FillSlot(view, materialId, inventory, materials, definitions);
            }

            // Unthrottled, always: this one is the answer to a key the player just pressed.
            view.Style.BorderColor =
                materialId >= 0 && materialId == place.MaterialId ? SelectedBorder : IdleBorder;
        }

        if (refreshText || _status.Text.Length == 0)
        {
            // The adaptive radius, when it is on, is the play's oracle for the whole feature —
            // fly, sprint, watch the number fall and the world stay drawn; stop, watch it climb —
            // and it costs nothing to show, so it rides on the status line's own refresh.
            string text = StatusText(place, mining, precise);
            _status.Text = radius is null ? text : $"{text}   ·   view {radius}";
        }
    }

    private static void FillSlot(
        SlotView view,
        int materialId,
        VolumetricInventory inventory,
        MaterialTable materials,
        Registry<MaterialDefinition> definitions)
    {
        if (materialId < 0)
        {
            view.Swatch.Color = new Color(0f, 0f, 0f, 0.35f);
            view.Name.Text = "—";
            view.Litres.Text = string.Empty;
            return;
        }

        view.Swatch.Color = VoxelMaterial.ColourForLayer(materials.TextureLayer(materialId));
        view.Name.Text = DisplayName(definitions, materialId);
        view.Litres.Text = Holding(inventory, materials, materialId);
    }

    /// <summary>
    /// A holding in the material's own unit: litres, parts, or bodies — with the fraction shown
    /// when a body's 64 voxels are not all there (half a pump after a fall places nothing).
    /// </summary>
    private static string Holding(VolumetricInventory inventory, MaterialTable materials, int materialId)
    {
        long voxels = inventory.Voxels(materialId);
        switch (materials.Unit(materialId))
        {
            case MaterialUnit.Count:
                return $"× {voxels:N0}";
            case MaterialUnit.Cells:
                const long Cell = BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;
                long whole = voxels / Cell, part = voxels % Cell;
                return part == 0 ? $"× {whole:N0}" : $"× {whole:N0} ({part}/{Cell})";
            default:
                return $"{inventory.Litres(materialId):N0} L";
        }
    }

    /// <summary>
    /// A material's label: its identifier's path, with underscores opened out.
    /// </summary>
    /// <remarks>
    /// <b>Derived rather than added to the content format, and that is the whole decision.</b>
    /// A <c>displayName</c> field in the material JSON is the obvious answer and it is the
    /// expensive one: mods write against the format the moment it exists, and a name that can be
    /// authored is a name that wants translating, which is a localisation system this project
    /// has no other reason to grow. The identifier's path is already a human word — the base
    /// game ships <c>stone</c>, <c>dirt</c>, <c>grass</c>, <c>lamp</c> — and it is guaranteed to
    /// exist for every material including a placeholder standing in for a removed mod.
    /// <b>Revisit trigger: the first material whose ID is not a sensible label, or the first
    /// request to run the game in another language.</b>
    /// </remarks>
    private static string DisplayName(Registry<MaterialDefinition> definitions, int materialId) =>
        definitions.GetIdentifier(materialId).Path.Replace('_', ' ');

    private static string StatusText(PlaceController place, MiningController? mining, bool precise)
    {
        string brush = place.Brush.ToString().ToLowerInvariant();
        string tier = mining is null ? "no tool" : mining.Tool.Id.Path.Replace('_', ' ');

        // A machine material places as one body whatever the brush, so the brush is not what the
        // next click does and the line says so (M10a-2).
        if (place.SelectedIsMachine)
        {
            return $"MACHINE  (one {BrushCell.SizeMetres:F0} m body)   ·   {tier}  (T)   ·   1-9 material, V cycle";
        }

        // The modifier is reported as a state rather than as a hint while it is held, because
        // the outline changes size at the same moment and a player needs to know which of the
        // two things they are looking at.
        if (precise)
        {
            return $"ONE VOXEL  (Alt)   ·   {tier}  (T)   ·   1-9 material, V cycle";
        }

        // Same argument, one shape further along and with more riding on it: the outline has
        // just become an 8 m cube and the next click removes 512 cubic metres. The tier still
        // reads, because the tier still gates what comes away — which is the half of the mode
        // that did not change.
        if (mining?.TakesWholeChunk == true)
        {
            // Both verbs named, because both changed scale at the same keypress and the fill is
            // the one a player would not guess. "dig / fill" rather than "mine / place": the
            // pairing is what makes the mode safe to use, since the bite now has an undo.
            return $"WHOLE CHUNK  (Q)   ·   {tier} gate  (T)   ·   dig / fill   ·   Alt: one voxel";
        }

        // The cube bite (2026-09-14): the dig is one brush cell and the fill is the brush at
        // range, so the line is the ordinary brush line with the mode named — the only thing Q
        // changed that a player cannot see is the reach.
        if (mining?.TakesCube == true)
        {
            return $"{BrushCell.SizeMetres:F0} M CELL  (Q)   ·   {tier} gate  (T)   ·   "
                + $"dig cell / {brush} brush  (B)   ·   1-9 material   ·   Alt: one voxel";
        }

        return $"{brush} brush  (B)   ·   {tier}  (T)   ·   1-9 material, V cycle   ·   Alt: one voxel";
    }
}
