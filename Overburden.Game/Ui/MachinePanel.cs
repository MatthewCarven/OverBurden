using System;
using System.Collections.Generic;
using Godot;
using Overburden.Core.Building;
using Overburden.Core.Fluid;
using Overburden.Core.Machines;

namespace Overburden.Game.Ui;

/// <summary>
/// The computer's panel (M10b-2): one machine's ports as spin boxes, and what the machine did at
/// the last boundary beside them.
/// </summary>
/// <remarks>
/// <para>
/// <b>It owns nothing but the widgets.</b> The row is the truth (M10a-2) and every change goes
/// through <see cref="MachineSet.SetPort"/> the moment it is made — clamped, counted, and the
/// set's version moved so the tick rebuilds its rings at the next boundary — with the value that
/// landed shown back. A port re-sent unchanged moves nothing (b-1), so the panel writes on every
/// change and never costs a rebuild a boundary for it. The status line reads
/// <see cref="MachineRow.LastStall"/> and <see cref="MachineRow.LastMovedMillilitres"/>, which the
/// tick writes, because the number the player typed is only legible beside what the machine did
/// with it (M10b's pass, decision 6).
/// </para>
/// <para>
/// <b>Built in code, like the HUD, and for the HUD's reasons.</b> The rows are per material — the
/// ports the machine's definition declares — which no scene file can know; and a panel checked
/// into <c>Main.tscn</c> would be one more thing a headless profile has to instantiate.
/// </para>
/// <para>
/// <b>The capture handshake is the camera's, not the panel's</b> (the pass's fact 3). Both input
/// handlers are <em>unhandled</em> input, so this frame's <c>MouseFilter.Stop</c> owns every click
/// on it and a focused field owns its keys; a click <em>outside</em> reaches the camera, which
/// captures the mouse by its own rule, and <c>Main</c> closes the panel on seeing capture return.
/// Esc is <c>Main</c>'s while the panel is open. The panel never touches the mouse mode itself,
/// so there is exactly one owner of "does the mouse belong to the game".
/// </para>
/// <para>
/// <b>Labels are derived, never authored</b> — the material's from its identifier's path
/// (<see cref="Hud.DisplayName"/>), a port's from its name — M6d-3's rule, which
/// <see cref="PortDefinition"/> keeps true by holding names to the identifier charset.
/// </para>
/// </remarks>
public sealed partial class MachinePanel : CanvasLayer
{
    /// <summary>Seconds between status refreshes — the HUD's throttle, for the HUD's reason.</summary>
    private const double RefreshSeconds = 0.1;

    private static readonly Color PanelFill = new(0.08f, 0.08f, 0.10f, 0.92f);
    private static readonly Color Border = new(1.00f, 0.86f, 0.45f);
    private static readonly Color DimText = new(0.68f, 0.70f, 0.74f);
    private static readonly Color StallText = new(1.00f, 0.72f, 0.40f);
    private static readonly Color MovedText = new(0.62f, 0.86f, 0.62f);

    private Control _root = null!;
    private Label _title = null!;
    private Label _where = null!;
    private VBoxContainer _portRows = null!;
    private Label _status = null!;

    private readonly List<PortView> _views = [];

    private sealed record PortView(string Name, SpinBox Spin, Label Bounds);

    private MachineRow? _row;
    private MachineSet? _machines;
    private MachineTick? _tick;
    private MachineKind _kind;
    private string _label = string.Empty;
    private bool _syncing;
    private double _sinceRefresh;
    private PumpStall? _printedStall;

    /// <summary>Whether a machine is open in it.</summary>
    public bool IsOpen => _row is not null;

    /// <summary>The row open in it, or null.</summary>
    public MachineRow? Row => _row;

    /// <summary>Port writes made through the panel, over the session.</summary>
    public long Writes { get; private set; }

    /// <summary>What the status line reads now — the headless oracle's, since nothing can look at it there.</summary>
    public string StatusText => _status.Text;

    public override void _Ready()
    {
        // Above the HUD's 10, because the panel is what the player is looking at while it is open.
        Layer = 11;

        // The root ignores the mouse so a click outside the frame falls through to the camera,
        // which is the close (see the class remarks). The frame stops it.
        _root = new Control { Name = "MachinePanelRoot", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var style = new StyleBoxFlat
        {
            BgColor = PanelFill,
            BorderColor = Border,
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        style.SetBorderWidthAll(2);

        // Anchored to the centre and grown both ways from it — anchors and offsets set directly,
        // never a preset plus a Size, for the reason Hud.Bar records. The centre is also where the
        // cursor appears when the mouse is released, so the panel opens under the hand.
        var frame = new PanelContainer { Name = "MachinePanelFrame", MouseFilter = Control.MouseFilterEnum.Stop };
        frame.AddThemeStyleboxOverride("panel", style);
        frame.AnchorLeft = 0.5f;
        frame.AnchorRight = 0.5f;
        frame.AnchorTop = 0.5f;
        frame.AnchorBottom = 0.5f;
        frame.GrowHorizontal = Control.GrowDirection.Both;
        frame.GrowVertical = Control.GrowDirection.Both;
        frame.CustomMinimumSize = new Vector2(360f, 0f);
        _root.AddChild(frame);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 6);
        frame.AddChild(column);

        _title = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _title.AddThemeColorOverride("font_color", Border);
        column.AddChild(_title);

        _where = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _where.AddThemeColorOverride("font_color", DimText);
        column.AddChild(_where);

        column.AddChild(new HSeparator { MouseFilter = Control.MouseFilterEnum.Ignore });

        _portRows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _portRows.AddThemeConstantOverride("separation", 4);
        column.AddChild(_portRows);

        column.AddChild(new HSeparator { MouseFilter = Control.MouseFilterEnum.Ignore });

        _status = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_status);

        var hint = new Label
        {
            Text = "Esc or click outside to close   ·   the tick reads a change at its next boundary",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        hint.AddThemeColorOverride("font_color", DimText);
        column.AddChild(hint);

        _root.Visible = false;
    }

    /// <summary>
    /// Shows a machine: its label and cell, one spin box per port its definition declares, and the
    /// status line. Replaces whatever was open.
    /// </summary>
    /// <param name="definition">The machine block of the row's material — the port declarations and their bounds.</param>
    /// <param name="label">The material's display name, derived by the HUD's rule.</param>
    /// <param name="machines">The set the writes go to.</param>
    /// <param name="tick">The tick, for a converter's generation line and the overcast; null under <c>--nowater</c>.</param>
    public void Open(MachineRow row, MachineDefinition definition, string label, MachineSet machines, MachineTick? tick = null)
    {
        _row = row;
        _machines = machines;
        _tick = tick;
        _kind = definition.Kind;
        _label = label;
        _printedStall = null;
        _sinceRefresh = RefreshSeconds;

        _title.Text = label.ToUpperInvariant();
        (float x, float y, float z) = row.Cell.OriginVoxel.MinCornerMetres;
        _where.Text = $"at ({x:F0}, {y:F0}, {z:F0}) m   ·   facing {Facing(row.Facing)}";

        foreach (Node child in _portRows.GetChildren())
        {
            _portRows.RemoveChild(child);
            child.QueueFree();
        }

        _views.Clear();

        foreach (PortDefinition port in definition.Ports)
        {
            _portRows.AddChild(BuildPortRow(port, row));
        }

        if (definition.Ports.Count == 0)
        {
            var none = new Label { Text = "no ports", MouseFilter = Control.MouseFilterEnum.Ignore };
            none.AddThemeColorOverride("font_color", DimText);
            _portRows.AddChild(none);
        }

        _root.Visible = true;
        RefreshStatus();
    }

    /// <summary>
    /// Puts a value into a port's box as a hand would — through the box, so its range and its
    /// signal run — for <c>--panelset=</c>, the headless twin of typing (M5c-2's rule: the wire
    /// between the widget and <see cref="MachineSet.SetPort"/> wants its own recipe).
    /// </summary>
    /// <returns>False when no open row declares a port of that name.</returns>
    public bool TypeInto(string name, long value)
    {
        foreach (PortView view in _views)
        {
            if (view.Name == name)
            {
                view.Spin.Value = value;
                return true;
            }
        }

        return false;
    }

    /// <summary>Hides the panel and forgets the row. Idempotent.</summary>
    public void Close()
    {
        if (_row is null)
        {
            return;
        }

        // A field left focused would keep the next keypress, with the panel gone and the mouse
        // captured again — the number row selecting nothing, silently.
        GetViewport()?.GuiReleaseFocus();

        _row = null;
        _machines = null;
        _tick = null;
        _root.Visible = false;
    }

    /// <summary>Re-reads the row's stall and move into the status line, throttled.</summary>
    public void Refresh(double delta)
    {
        if (_row is null)
        {
            return;
        }

        _sinceRefresh += delta;
        if (_sinceRefresh < RefreshSeconds)
        {
            return;
        }

        _sinceRefresh = 0.0;
        RefreshStatus();
    }

    private Control BuildPortRow(PortDefinition port, MachineRow row)
    {
        var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 10);

        var name = new Label
        {
            Text = port.Name.Replace('_', ' '),
            CustomMinimumSize = new Vector2(110f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        line.AddChild(name);

        // A SpinBox holds a double, and a port is a long: exact up to 2^53, which every shipped
        // port is inside by a wide margin (channel 0..15, weight 1..100). A port declared wider
        // than that would round in the box before SetPort ever saw it — noted, not handled.
        var spin = new SpinBox
        {
            MinValue = port.Min,
            MaxValue = port.Max,
            Step = 1,
            Rounded = true,
            Alignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(120f, 0f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        spin.Value = row.TryGetPort(port.Name, out long held) ? held : port.Default;
        line.AddChild(spin);

        var bounds = new Label { Text = $"{port.Min} … {port.Max}", MouseFilter = Control.MouseFilterEnum.Ignore };
        bounds.AddThemeColorOverride("font_color", DimText);
        line.AddChild(bounds);

        var view = new PortView(port.Name, spin, bounds);
        _views.Add(view);
        spin.ValueChanged += value => OnValueChanged(view, value);
        return line;
    }

    /// <summary>
    /// One change, one write, at once. The box's own range already clamps a typed value, so a
    /// <c>Clamped</c> here means the declaration and the box disagree — which they cannot, being
    /// built from the same definition — but the landed value is shown back regardless, because the
    /// row is the truth and the box is a view of it.
    /// </summary>
    private void OnValueChanged(PortView view, double value)
    {
        if (_syncing || _row is null || _machines is null)
        {
            return;
        }

        PortWrite write = _machines.SetPort(_row.Cell, view.Name, (long)Math.Round(value));
        Writes++;
        GD.Print($"M10: panel — {_label} at {_row.Cell}: {view.Name} := {(long)Math.Round(value)}: {write}");

        if (write.Landed && (long)Math.Round(view.Spin.Value) != write.Value)
        {
            _syncing = true;
            view.Spin.Value = write.Value;
            _syncing = false;
        }

        // A change is legible at once beside what the machine is doing: a pump moved to an empty
        // channel reads "stalled: no outlet on channel 3" at the next boundary, not this frame,
        // and the line's channel number should already say 3.
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (_row is null)
        {
            return;
        }

        (string text, Color colour, PumpStall? stall) = StatusFor(_row);
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", colour);

        // Printed when the stall kind changes and not when the number does, so a lake pump moving
        // 50 L every boundary prints once and a pump that keeps stalling prints once — the log is
        // the instrument headless, where nobody can read the label.
        if (stall != _printedStall)
        {
            _printedStall = stall;
            GD.Print($"M10: panel — {_label} at {_row.Cell}: {text}");
        }
    }

    /// <summary>The status line for a row: what its kind did at the last boundary.</summary>
    private (string Text, Color Colour, PumpStall? Stall) StatusFor(MachineRow row)
    {
        long channel = row.TryGetPort("channel", out long c) ? c : 0;

        if (_kind == MachineKind.Outlet)
        {
            // An outlet is poured into by the pumps on its channel and records nothing of its own
            // (the tick writes the pump's row); its line says what the ports mean.
            long weight = row.TryGetPort(MachineTick.WeightPort, out long w) ? w : 1;
            return ($"receives from every pump on channel {channel}   ·   weight {weight} in the ring", DimText, null);
        }

        // M10c-2: a cell's charge through the set's own definition of one, a panel's generation
        // through the tick's — never a second copy of either formula.
        if (_kind == MachineKind.Storage)
        {
            long capacity = _machines?.CapacityOf(row) ?? 0;
            long charge = _machines?.ChargeOf(row) ?? 0;
            return ($"charged {Joules(charge)} of {Joules(capacity)}   ·   pooled on channel {channel}", charge > 0 ? MovedText : DimText, null);
        }

        if (_kind == MachineKind.Converter)
        {
            int open = ConverterState.ReadOpenColumns(row);
            long output = _tick?.SolarOutputOf(row) ?? 0;
            byte overcast = _tick?.OvercastByte ?? 0;
            return ($"generating {Joules(output)} a boundary onto channel {channel}   ·   {open}/16 open, overcast {overcast}/256",
                output > 0 ? MovedText : StallText, null);
        }

        return row.LastStall switch
        {
            PumpStall.None when row.LastMovedMillilitres > 0 =>
                ($"moved {Litres(row.LastMovedMillilitres)} at the last boundary", MovedText, PumpStall.None),
            PumpStall.None => ("waiting for the first boundary", DimText, null),
            PumpStall.NoOutlet => ($"stalled: no outlet on channel {channel}", StallText, PumpStall.NoOutlet),
            PumpStall.NoWater => ("stalled: no water at the intake", StallText, PumpStall.NoWater),
            PumpStall.NoRoom => ($"stalled: no room at any outlet on channel {channel}", StallText, PumpStall.NoRoom),
            PumpStall.Unloaded => ("stalled: the intake or an outlet is not loaded", StallText, PumpStall.Unloaded),
            PumpStall.Unanalysed => ("stalled: off the analysed window — no ground to pour on", StallText, PumpStall.Unanalysed),
            PumpStall.Unpowered => ($"stalled: no power on channel {channel}", StallText, PumpStall.Unpowered),
            _ => (row.LastStall.ToString(), StallText, row.LastStall),
        };
    }

    /// <summary>Millijoules in the unit a person reads: mJ under a joule, then J, kJ, MJ, GJ.</summary>
    internal static string Joules(long millijoules)
    {
        long sign = millijoules < 0 ? -1 : 1;
        long mJ = Math.Abs(millijoules);
        string s = sign < 0 ? "−" : string.Empty;
        if (mJ < 1_000L) return $"{s}{mJ} mJ";
        if (mJ < 10_000_000L) return $"{s}{mJ / 1000.0:N0} J";
        if (mJ < 10_000_000_000L) return $"{s}{mJ / 1_000_000.0:N1} kJ";
        if (mJ < 10_000_000_000_000L) return $"{s}{mJ / 1_000_000_000.0:N1} MJ";
        return $"{s}{mJ / 1_000_000_000_000.0:N2} GJ";
    }

    /// <summary>Millilitres as litres, whole when they are whole — the tick's unit shown in the player's.</summary>
    private static string Litres(long millilitres) =>
        millilitres % FluidConstants.MillilitresPerLitre == 0
            ? $"{millilitres / FluidConstants.MillilitresPerLitre:N0} L"
            : $"{millilitres / (double)FluidConstants.MillilitresPerLitre:N3} L";

    private static string Facing(BrushFacing facing) => facing switch
    {
        BrushFacing.PositiveX => "+X",
        BrushFacing.NegativeX => "−X",
        BrushFacing.PositiveZ => "+Z",
        BrushFacing.NegativeZ => "−Z",
        _ => facing.ToString(),
    };
}
