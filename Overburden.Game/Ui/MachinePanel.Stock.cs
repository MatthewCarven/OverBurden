using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Game.Rendering;

namespace Overburden.Game.Ui;

/// <summary>
/// The panel's stock section (IDEAL-MACHINES-PLAN decision 21; IM-6's pass, decision 9): on a vault's panel, a crate's,
/// and the quantum backpack's, which opens the whole store on its channel.
/// </summary>
/// <remarks>
/// <para>
/// <b>At the top</b>, a capacity bar in <see cref="VolumeText"/>; the bill in watts with the vault's own minute; and the
/// state — <c>HOLDING</c>, <c>FUSE 12.0 s</c>, <c>FUSE 12.0 s (paused)</c>, <c>DUMPING — owes …</c> or <c>WRECK</c>.
/// <b>Then one row a material</b>: its swatch, name and amounts, and two verbs — <c>→ pocket</c>, and <c>→ vault</c>,
/// <c>→ crate</c> or <c>→ store</c>. A click moves a cubic metre (a piece of a counted item); Shift-click moves all of it.
/// <b>Water</b> is a row with no verbs: the siphon is its door. <b>His warranty line</b> is at the foot of a vault's.
/// </para>
/// <para>
/// <b>Every move goes through Core's <see cref="StoreMoves"/></b> — the one seam M10g's E screen will call too (his
/// design for the screen's boxes, M10-LADDER-PLAN) — so this file moves nothing itself and a refusal says why. A move is
/// not an undo step (decision 9): the set's doors journal nothing, and a later Ctrl+Z short of stock is refused as it is
/// today.
/// </para>
/// <para>
/// <b>The rows are what the box holds and what the pocket could put in it.</b> The pocket's items (the computer, the
/// packs) are not offered, since a vault holds blocks and water (decision 15); a fluid never is, since the pocket refuses
/// it (fact 11).
/// </para>
/// </remarks>
public sealed partial class MachinePanel
{
    /// <summary>His warranty line (2026-09-29, verbatim), at the foot of a vault's panel (decision 21; IM-6's decision 9).</summary>
    public const string WarrantyLine =
        "While all care is taken please understand that warranty in case of battery discharge or negligence, is not implied or otherwise available under these circumstances.";

    private static readonly Color CyanText = new(0.35f, 0.95f, 1.00f);

    /// <summary>What the stock section shows: nothing, one vault or crate, or the backpack's channel's store.</summary>
    private enum StockSubject
    {
        None,
        Vault,
        Crate,
        Store,
    }

    private VBoxContainer _stock = null!;
    private ProgressBar _capacityBar = null!;
    private Label _capacityText = null!;
    private Label _billLine = null!;
    private Label _stateLine = null!;
    private VBoxContainer _stockRows = null!;
    private Label _warranty = null!;
    private Label _moveLine = null!;

    private StoreMoves? _moves;
    private VolumetricInventory? _pocket;
    private MaterialTable? _materials;
    private Overburden.Core.Content.Registry<MaterialDefinition>? _definitions;
    private Func<EquippedPack?>? _wornPack;

    private StockSubject _subject;
    private readonly List<int> _shownMaterials = [];
    private readonly Dictionary<int, Label> _amounts = [];
    private Label? _waterAmount;
    private string? _printedVaultState;

    /// <summary>Stock moves made through the panel's verbs, over the session.</summary>
    public long StockMoves { get; private set; }

    /// <summary>What the stock section's state line reads now, empty while none shows — headless's, for the log.</summary>
    public string StockStateText => _stock is { Visible: true } ? _stateLine.Text : string.Empty;

    /// <summary>
    /// Hands the panel what its stock section moves through (IM-6b): the store's moves, the pocket, the material table and
    /// names, and the worn pack. Called once by Main; without it a vault's panel shows its status line alone.
    /// </summary>
    public void AttachStore(
        StoreMoves moves,
        VolumetricInventory pocket,
        MaterialTable materials,
        Overburden.Core.Content.Registry<MaterialDefinition> definitions,
        Func<EquippedPack?> wornPack)
    {
        _moves = moves;
        _pocket = pocket;
        _materials = materials;
        _definitions = definitions;
        _wornPack = wornPack;
    }

    private void BuildStock(VBoxContainer column)
    {
        _stock = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _stock.AddThemeConstantOverride("separation", 4);
        column.AddChild(_stock);

        var capacityRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        capacityRow.AddThemeConstantOverride("separation", 10);
        _stock.AddChild(capacityRow);

        _capacityBar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1000,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(180f, 14f),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        capacityRow.AddChild(_capacityBar);

        _capacityText = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        capacityRow.AddChild(_capacityText);

        _billLine = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _billLine.AddThemeColorOverride("font_color", DimText);
        _stock.AddChild(_billLine);

        _stateLine = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _stateLine.AddThemeFontSizeOverride("font_size", 18);
        _stock.AddChild(_stateLine);

        _stock.AddChild(new HSeparator { MouseFilter = Control.MouseFilterEnum.Ignore });

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 60f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _stock.AddChild(scroll);

        _stockRows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _stockRows.AddThemeConstantOverride("separation", 3);
        scroll.AddChild(_stockRows);
        _stockScroll = scroll;

        _moveLine = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
        _moveLine.AddThemeColorOverride("font_color", DimText);
        _stock.AddChild(_moveLine);

        _warranty = new Label
        {
            Text = WarrantyLine,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(520f, 0f),
        };
        _warranty.AddThemeColorOverride("font_color", new Color(DimText, 0.75f));
        _warranty.AddThemeFontSizeOverride("font_size", 11);
        _stock.AddChild(_warranty);
    }

    private ScrollContainer _stockScroll = null!;

    /// <summary>Shows the stock section for what was just opened, or hides it.</summary>
    private void ShowStock(StockSubject subject)
    {
        _subject = _moves is null ? StockSubject.None : subject;
        _shownMaterials.Clear();
        _amounts.Clear();
        _waterAmount = null;
        _printedVaultState = null;
        _moveLine.Text = string.Empty;
        foreach (Node child in _stockRows.GetChildren())
        {
            _stockRows.RemoveChild(child);
            child.QueueFree();
        }

        _stock.Visible = _subject != StockSubject.None;
        _warranty.Visible = _subject == StockSubject.Vault;
        _status.Visible = _subject == StockSubject.None;
        if (_subject != StockSubject.None)
        {
            RefreshStock();
            GD.Print($"M10: panel — {_label}: {StockSummary()}");
        }
    }

    /// <summary>The section's one-line account for the log: the capacity, the bill, the state and each row's amounts.</summary>
    public string StockSummary()
    {
        if (_subject == StockSubject.None)
        {
            return "no stock section";
        }

        string rows = _amounts.Count == 0
            ? "nothing held or carried to move"
            : string.Join(", ", _shownMaterials.Select(id => $"{NameOf(id)} {_amounts[id].Text}"));
        string water = _waterAmount is null ? string.Empty : $"; water {_waterAmount.Text}";
        return $"stock {_capacityText.Text}; {_billLine.Text}; {_stateLine.Text}; {rows}{water}";
    }

    /// <summary>The section's figures and rows, re-read from the rows and the pocket.</summary>
    private void RefreshStock()
    {
        if (_subject == StockSubject.None || _moves is null || _pocket is null || _materials is null)
        {
            return;
        }

        SortedDictionary<int, long> held;
        long used, inside, water;
        bool holdsWater;
        switch (_subject)
        {
            case StockSubject.Store when _pack is not null && _machines is not null:
            {
                int channel = Math.Clamp(_pack.Channel, 0, MachineTick.Channels - 1);
                held = _moves.StockOfStore(channel);
                StoreFill fill = _machines.StoreFillOf(channel);
                (used, inside) = (fill.UsedMillilitres, fill.InsideMillilitres);
                water = _machines.StoredWaterOf(channel);
                holdsWater = true;
                FillStoreHeader(channel);
                break;
            }

            case StockSubject.Vault or StockSubject.Crate when _row is not null && _machines?.VaultDefinitionOf(_row) is { } definition:
            {
                held = _moves.StockOfBox(_row.Cell);
                if (!VaultState.TryRead(_row, out VaultContents contents))
                {
                    _stateLine.Text = "UNREADABLE — its state is from a newer build: kept as it is";
                    return;
                }

                (used, inside) = (contents.UsedMillilitres, definition.InsideMillilitres);
                water = contents.WaterMillilitres;
                holdsWater = definition.HoldsWater;
                FillBoxHeader(definition, contents);
                break;
            }

            default:
                return;
        }

        _capacityBar.Value = inside <= 0 ? 0 : Math.Min(1000.0, 1000.0 * used / inside);
        _capacityText.Text = $"{VolumeText.OfMillilitres(Math.Max(0, used))} of {VolumeText.OfMillilitres(inside)} ({Share(used, inside)})";

        // The rows: what the box holds, and the pocket's blocks and machines it could take; rebuilt only when that set changes.
        List<int> wanted = [.. held.Keys
            .Concat(_pocket.Entries().Select(e => e.MaterialId).Where(id => !_pocket.Refuses(id) && _materials.IsPlaceable(id)))
            .Distinct()
            .OrderBy(id => id)];
        bool showWater = holdsWater;
        if (!wanted.SequenceEqual(_shownMaterials) || (showWater != (_waterAmount is not null)))
        {
            RebuildStockRows(wanted, showWater);
        }

        string here = _subject switch
        {
            StockSubject.Store => "store",
            StockSubject.Crate => "crate",
            _ => "vault",
        };
        foreach (int id in _shownMaterials)
        {
            _amounts[id].Text = $"{here} {Holding(id, held.GetValueOrDefault(id))}   ·   pocket {Holding(id, _pocket.Voxels(id))}";
        }

        if (_waterAmount is not null)
        {
            _waterAmount.Text = $"{here} {VolumeText.OfMillilitres(water)}   ·   the siphon is its door";
        }

        PrintVaultStateChange();
    }

    /// <summary>The vault's or crate's header: its bill and own minute, and its state (decisions 9 and 18).</summary>
    private void FillBoxHeader(MachineDefinition definition, VaultContents contents)
    {
        if (!definition.IsChannelVault || _row is null)
        {
            _billLine.Text = "a crate: no channel, no store, no bill";
            _stateLine.Text = contents.IsEmpty ? "EMPTY" : "HOLDING";
            _stateLine.AddThemeColorOverride("font_color", contents.IsEmpty ? DimText : MovedText);
            return;
        }

        long boundary = _tick?.BoundaryMillis ?? MachineTick.DefaultBoundaryMillis;
        long channel = _row.TryGetPort(MachineTick.ChannelPort, out long c) ? c : 0;
        long bill = MachineTick.BillOf(definition, contents, boundary);
        _billLine.Text = $"bill {PowerText.OfMillijoulesPerBoundary(bill, boundary)} on channel {channel}   ·   "
                         + $"own {ChargeOf(contents.OwnChargeMillijoules, MachineTick.OwnChargeCapacity(definition, contents))}";

        (string state, Color colour) = contents switch
        {
            { Wreck: true } => ($"WRECK — owes {Owed(contents)}", StallText),
            { Phase: VaultPhase.Dumping } => ($"DUMPING — owes {Owed(contents)}", StallText),
            { Phase: VaultPhase.Fusing } when _row.LastHoldPaid =>
                ($"FUSE {contents.FuseMillisLeft / 1000.0:N1} s (paused){(PaidByHeldCells(channel) ? " — paid by cells held off by a signal" : string.Empty)}", CyanText),
            { Phase: VaultPhase.Fusing } => ($"FUSE {contents.FuseMillisLeft / 1000.0:N1} s", CyanText),
            _ when !MachineTick.IsEnabled(_row) => ("HOLDING — closed to moves (enabled 0); it still bills", DimText),
            _ => ("HOLDING", contents.IsEmpty ? DimText : MovedText),
        };
        _stateLine.Text = state;
        _stateLine.AddThemeColorOverride("font_color", colour);
    }

    /// <summary>The backpack's store header: the channel's hold, its vaults, and its bank (decision 8's window).</summary>
    private void FillStoreHeader(int channel)
    {
        if (_machines is null)
        {
            return;
        }

        int open = _machines.StoreOf(channel).Count;
        long hold = BankLine.HoldMilliwattsOf(_machines, channel);
        _billLine.Text = $"hold {PowerText.OfMilliwatts(hold)} on channel {channel}   ·   {open} vault(s) open to the store";
        long bank = _tick?.BankOf(channel) ?? 0;
        _stateLine.Text = open == 0
            ? $"NO STORE on channel {channel} — place a vault on it"
            : $"BANK {EnergyText.Of(bank)} on channel {channel}";
        _stateLine.AddThemeColorOverride("font_color", open == 0 ? StallText : bank > 0 ? MovedText : StallText);
    }

    /// <summary>Whether a paused fuse's bill is being paid by cells a signal holds off (decision 18).</summary>
    private bool PaidByHeldCells(long channel)
    {
        if (_machines is null || _tick is null)
        {
            return false;
        }

        foreach (MachineRow row in _machines.Rows)
        {
            if (_machines.ChargeOf(row) > 0
                && row.TryGetPort(MachineTick.ChannelPort, out long c) && c == channel
                && MachineTick.IsEnabled(row)
                && _tick.IsSignalledOff(row))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What an owing vault owes: its water and its blocks, each in <see cref="VolumeText"/>.</summary>
    private static string Owed(VaultContents contents) =>
        $"{VolumeText.OfMillilitres(contents.WaterMillilitres)} of water and {VolumeText.OfVoxels(contents.TotalVoxels)} of blocks";

    /// <summary>A vault's state is logged when it changes (fact 9: until IM-6b the panel never logged a vault).</summary>
    private void PrintVaultStateChange()
    {
        if (_subject is not StockSubject.Vault || _row is null)
        {
            return;
        }

        // The phase word alone, so a fuse counting down prints once and not every tenth of a second.
        string key = _stateLine.Text.Split(' ', 2)[0] + (_stateLine.Text.Contains("(paused)", StringComparison.Ordinal) ? " paused" : string.Empty);
        if (key != _printedVaultState)
        {
            _printedVaultState = key;
            GD.Print($"M10: panel — {_label} at {_row.Cell}: {_stateLine.Text}   ·   {_billLine.Text}   ·   {_capacityText.Text}");
        }
    }

    private void RebuildStockRows(List<int> materials, bool showWater)
    {
        foreach (Node child in _stockRows.GetChildren())
        {
            _stockRows.RemoveChild(child);
            child.QueueFree();
        }

        _shownMaterials.Clear();
        _amounts.Clear();
        _waterAmount = null;

        string into = _subject switch
        {
            StockSubject.Store => "→ store",
            StockSubject.Crate => "→ crate",
            _ => "→ vault",
        };

        foreach (int id in materials)
        {
            var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(new ColorRect
            {
                Color = VoxelMaterial.ColourForLayer(_materials!.TextureLayer(id)),
                CustomMinimumSize = new Vector2(14f, 14f),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            line.AddChild(new Label { Text = NameOf(id), CustomMinimumSize = new Vector2(170f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore });

            var amount = new Label { CustomMinimumSize = new Vector2(230f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
            amount.AddThemeColorOverride("font_color", DimText);
            line.AddChild(amount);

            int material = id;
            var toPocket = new Button { Text = "→ pocket", FocusMode = Control.FocusModeEnum.None, TooltipText = "a cubic metre (a piece); Shift-click for all" };
            toPocket.Pressed += () => MoveStock(material, toPocket: true, all: Input.IsKeyPressed(Key.Shift));
            line.AddChild(toPocket);

            var toBox = new Button { Text = into, FocusMode = Control.FocusModeEnum.None, TooltipText = "a cubic metre (a piece); Shift-click for all" };
            toBox.Pressed += () => MoveStock(material, toPocket: false, all: Input.IsKeyPressed(Key.Shift));
            line.AddChild(toBox);

            _stockRows.AddChild(line);
            _shownMaterials.Add(id);
            _amounts[id] = amount;
        }

        if (showWater)
        {
            var line = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            line.AddThemeConstantOverride("separation", 8);
            line.AddChild(new ColorRect
            {
                Color = new Color(0.20f, 0.45f, 0.85f),
                CustomMinimumSize = new Vector2(14f, 14f),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            line.AddChild(new Label { Text = "water", CustomMinimumSize = new Vector2(170f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore });
            _waterAmount = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
            _waterAmount.AddThemeColorOverride("font_color", DimText);
            line.AddChild(_waterAmount);
            _stockRows.AddChild(line);
        }

        // Tall enough for six rows before it scrolls, so a long pocket never pushes the panel off the screen.
        int rows = materials.Count + (showWater ? 1 : 0);
        _stockScroll.CustomMinimumSize = new Vector2(0f, Math.Clamp(rows, 1, 6) * 34f);
    }

    /// <summary>
    /// One verb's click (decision 9): a cubic metre (a piece of a counted item), or all of it with Shift, through
    /// <see cref="StoreMoves"/> — out of the box or the store to the pocket, or from the pocket into it.
    /// </summary>
    private void MoveStock(int materialId, bool toPocket, bool all)
    {
        if (_moves is null || _pocket is null || _materials is null)
        {
            return;
        }

        long source = toPocket
            ? (_subject == StockSubject.Store && _pack is not null
                ? _moves.StockOfStore(_pack.Channel).GetValueOrDefault(materialId)
                : _row is not null ? _moves.StockOfBox(_row.Cell).GetValueOrDefault(materialId) : 0)
            : _pocket.Voxels(materialId);
        long asked = all ? source : Math.Min(source, StoreMoves.ClickVoxels(_materials.Unit(materialId)));

        StoreMove move = (_subject, toPocket) switch
        {
            (StockSubject.Store, true) => _moves.ToPocket(_wornPack?.Invoke(), materialId, asked),
            (StockSubject.Store, false) => _moves.ToStore(_wornPack?.Invoke(), materialId, asked),
            (_, true) when _row is not null => _moves.OutOfBox(_row.Cell, materialId, asked),
            (_, false) when _row is not null => _moves.IntoBox(_row.Cell, materialId, asked),
            _ => new StoreMove(0, StoreRefusal.NotABox),
        };

        StockMoves++;
        string where = _subject == StockSubject.Store ? $"the store on channel {_pack?.Channel}" : $"{_label} at {_row?.Cell}";
        string direction = toPocket ? $"from {where} to the pocket" : $"from the pocket into {where}";
        string text = move.Refused
            ? $"refused — {RefusalText(move.Refusal)}"
            : move.Voxels == asked
                ? $"moved {Holding(materialId, move.Voxels)} of {NameOf(materialId)} {direction}"
                : $"moved {Holding(materialId, move.Voxels)} of {Holding(materialId, asked)} asked of {NameOf(materialId)} {direction} (no more room)";
        _moveLine.Text = text;
        GD.Print($"M10: panel — stock: {text}{(all ? " (Shift: all)" : string.Empty)}; {move.Voxels:N0} voxel(s)");
        RefreshStock();
    }

    /// <summary>A store move's refusal in words (IM-6a's reasons).</summary>
    internal static string RefusalText(StoreRefusal refusal) => refusal switch
    {
        StoreRefusal.PocketRefuses => "the pocket can't hold that",
        StoreRefusal.NoBackpack => "no quantum backpack worn: the store is its channel's",
        StoreRefusal.NotABox => "nothing here is a vault or a crate",
        StoreRefusal.Unreadable => "its state is from a newer build",
        StoreRefusal.Wreck => "a wreck: what it holds is owed to the world",
        StoreRefusal.Dumping => "dumping: what it holds is owed to the world",
        StoreRefusal.SwitchedOff => "switched off (enabled 0)",
        _ => refusal.ToString(),
    };

    private string NameOf(int materialId) =>
        _definitions is null ? materialId.ToString(System.Globalization.CultureInfo.InvariantCulture) : Hud.DisplayName(_definitions, materialId);

    /// <summary>A holding in the material's unit, as the hotbar reads it: pieces, bodies, or a volume.</summary>
    private string Holding(int materialId, long voxels)
    {
        if (_materials is null)
        {
            return $"{voxels:N0}";
        }

        const long Cell = BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;
        return _materials.Unit(materialId) switch
        {
            MaterialUnit.Count => $"× {voxels:N0}",
            MaterialUnit.Cells => voxels % Cell == 0 ? $"× {voxels / Cell:N0}" : $"× {voxels / Cell:N0} ({voxels % Cell}/{Cell})",
            _ => VolumeText.OfVoxels(voxels),
        };
    }
}
